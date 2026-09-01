// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Where a cave's passage sits vertically, where the entrances of an area sit, and what levels
/// somebody decided either of those shows.
///
/// <para>
/// <b>Every height here is a coordinate.</b> A passage elevation places a gallery inside a hill and
/// an entrance altitude places a hole on its side; a distribution of them is observable one bin at
/// a time, so it is not enough to keep a protected cave out of the map and let it into the
/// histogram. The two views take that seriously in two different ways, because they gather from
/// two different places:
/// </para>
/// <list type="bullet">
/// <item><description>The per-cave view is withheld <i>whole</i> from a caller who may read the
/// cave but not place it exactly, and refuses as "no such cave" rather than as "you may not" — a
/// refusal that named itself would confirm both that the cave exists and that it has been
/// surveyed.</description></item>
/// <item><description>The area view answers, but computes over the caller's exact-view set alone:
/// an entrance whose position is closed to them contributes to no bin, no band, no count and no
/// total. That is what makes the same question, asked with and without a protected entrance in the
/// area, give an identical answer.</description></item>
/// </list>
/// <para>
/// <b>The machine proposes, a person decides.</b> The levels on the two read routes are recomputed
/// from the measurements on every request and are a reading of a histogram, never an assertion
/// about a cave. What somebody concluded is a separate, stored thing, and the routes that carry it
/// say who concluded it and when.
/// </para>
/// <para>
/// The read routes take no options beyond the subject. A figure that moves under a query parameter
/// can be asked repeatedly with the parameter varied, and the sequence of answers says things about
/// positions that no single answer does.
/// </para>
/// </summary>
public static class CaveHypsometryEndpoints
{
    private const string CaveNotFoundCode = "cave.not_found";
    private const string AreaNotFoundCode = "feature.not_found";
    private const string ConcurrentSaveCode = "cave.level_bands.concurrent_save";

    /// <summary>
    /// The partial unique index that allows one current reading per cave. Named here so the race
    /// that is answered as a conflict is exactly that one and not any other key violation.
    /// </summary>
    private const string CurrentReadingIndex = "ux_cave_level_bands_current";

    public static RouteGroupBuilder MapCaveHypsometryEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/hypsometry", CaveHypsometryAsync)
            .WithValidation<CaveSurveyStatisticsRequest>()
            .WithSummary(
                "How one cave's passage length is distributed by height, and the levels it appears "
                + "to be cut at. Withheld from a caller who may not place the cave exactly.");

        caves.MapGet("/{id:guid}/level-bands", ReadLevelBandsAsync)
            .WithSummary(
                "The levels somebody recorded for this cave, or the fact that nobody has. "
                + "Withheld from a caller who may not place the cave exactly.");

        caves.MapPut("/{id:guid}/level-bands", SaveLevelBandsAsync)
            .WithValidation<SaveCaveLevelBandsRequest>()
            .WithSummary("Record a reading of this cave's levels, superseding any earlier one.");

        caves.MapDelete("/{id:guid}/level-bands", ClearLevelBandsAsync)
            .WithSummary("Withdraw the recorded reading of this cave's levels.");

        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/entrance-hypsometry", AreaHypsometryAsync)
            .WithSummary(
                "How the altitudes of the cave entrances under one area are distributed, with the "
                + "spring altitudes among them. Counts only entrances this caller may place.");

        return api;
    }

    private static async Task<Results<Ok<CaveHypsometryDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        CaveHypsometryAsync(
            [AsParameters] CaveSurveyStatisticsRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await ReadableCaveAsync(db, access, protection, ctx, request.Id, ct) is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);

        // A leg the file marks as already surveyed on another trip is the same piece of cave
        // measured twice. Counting its metres a second time would put a level where two teams
        // happened to overlap rather than where the cave has one.
        var measurable = set.Segments.Where(s => !s.IsDuplicate).ToList();

        // Refused, not reported as a cave lying flat at height zero. The reduction that produces a
        // drawn centerline's segments writes zero where the drawing recorded no depth, so
        // summarising it would answer "one level, at sea level" for a plan drawing of a shaft.
        var proposal = set.HasZ
            ? ElevationBands.Summarize(measurable
                .Where(s => s.MidZM is not null)
                .Select(s => new ElevationSample(
                    s.MidZM!.Value, s.SlopeLengthM ?? s.PlanLengthM)))
            : null;

        return TypedResults.Ok(new CaveHypsometryDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            set.HasZ,
            proposal,
            await SpringReferenceLinesAsync(db, protection, ctx, request.Id, ct)));
    }

    private static async Task<Results<Ok<AreaHypsometryDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaHypsometryAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var altitudes = await AreaElevations.ForAreaAsync(db, protection, ctx, id, ct);
        if (altitudes is null)
        {
            return ApiProblems.NotFound(AreaNotFoundCode);
        }

        // Every entrance weighs one: this counts holes in a hillside, where the per-cave view
        // measures metres of passage, and weighting entrances by anything would be inventing an
        // importance the record does not carry.
        var proposal = ElevationBands.Summarize(
            altitudes.Altitudes.Select(a => new ElevationSample(a, 1d)));

        return TypedResults.Ok(new AreaHypsometryDto(
            id, altitudes.EntranceCount, altitudes.SpringAltitudes, proposal));
    }

    private static async Task<Results<Ok<CaveLevelBandsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ReadLevelBandsAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await ReadableCaveAsync(db, access, protection, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        var current = await CurrentAsync(db, id, ct);
        return TypedResults.Ok(ToDto(id, current));
    }

    private static async Task<Results<Ok<CaveLevelBandsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SaveLevelBandsAsync(
            Guid id,
            SaveCaveLevelBandsRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            TimeProvider clock,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cave = await ReadableCaveAsync(db, access, protection, ctx, id, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var now = clock.GetUtcNow();
        var previous = await db.CaveLevelBands
            .FirstOrDefaultAsync(b => b.CaveFeatureId == id && b.SupersededAt == null, ct);
        if (previous is not null)
        {
            // Stamped rather than deleted: a reading is somebody's conclusion about a cave and a
            // later one does not make the earlier one never have happened.
            previous.SupersededAt = now;
        }

        var saved = new CaveLevelBands
        {
            CaveFeatureId = id,
            ConfirmedBy = ctx.UserId,
            Bands = JsonSerializer.Serialize(request.Bands ?? []),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };
        db.CaveLevelBands.Add(saved);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsConcurrentReadingRace(e))
        {
            // Two people recording a reading of the same cave at the same moment both read the
            // same current row, both stamp it and both insert; the partial unique index refuses
            // the second, which is the invariant holding rather than a fault. It is answered as a
            // conflict the caller can retry into, and deliberately not by silently accepting one
            // of the two readings: they are different conclusions about the cave and choosing
            // between them is not this route's decision.
            return ApiProblems.Conflict(ConcurrentSaveCode);
        }

        return TypedResults.Ok(ToDto(id, saved));
    }

    private static async Task<Results<Ok<CaveLevelBandsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ClearLevelBandsAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            TimeProvider clock,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cave = await ReadableCaveAsync(db, access, protection, ctx, id, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var current = await db.CaveLevelBands
            .FirstOrDefaultAsync(b => b.CaveFeatureId == id && b.SupersededAt == null, ct);
        if (current is not null)
        {
            current.SupersededAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        // Withdrawing what was never recorded is the state the caller asked for, so it is answered
        // rather than refused: the route is about what is current, and nothing current is a state.
        return TypedResults.Ok(ToDto(id, null));
    }

    /// <summary>
    /// Whether a failed write is a second reading of the same cave arriving at the same moment —
    /// the state the partial unique index over the current row exists to refuse.
    /// </summary>
    private static bool IsConcurrentReadingRace(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CurrentReadingIndex,
        };

    /// <summary>
    /// The spring altitudes worth drawing across one cave's histogram: those of the area that
    /// contains it, through the primary containment edge.
    /// </summary>
    /// <remarks>
    /// The containing area rather than a distance, because this route deliberately takes no
    /// options: a reference line whose set moves under a radius parameter could be asked for
    /// repeatedly with the radius varied, and the sequence of answers would bracket where each
    /// spring is. The primary edge rather than every ancestor, because a cave's ancestors run all
    /// the way up to whatever the largest region in the installation is, and "the springs of the
    /// region" is not a base level anybody is reading against. Gathering goes through the area
    /// reader, so the caller's own visibility and exact-placement set decide what is in it.
    /// </remarks>
    private static async Task<IReadOnlyList<double>> SpringReferenceLinesAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Guid caveId,
        CancellationToken ct)
    {
        var areaId = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.ChildId == caveId && e.IsPrimary)
            .Select(e => (Guid?)e.ParentId)
            .FirstOrDefaultAsync(ct);

        if (areaId is null)
        {
            return [];
        }

        var altitudes = await AreaElevations.ForAreaAsync(db, protection, ctx, areaId.Value, ct);
        return altitudes?.SpringAltitudes ?? [];
    }

    private static Task<CaveLevelBands?> CurrentAsync(SilexGisDbContext db, Guid caveId, CancellationToken ct) =>
        db.CaveLevelBands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CaveFeatureId == caveId && b.SupersededAt == null, ct);

    private static CaveLevelBandsDto ToDto(Guid caveId, CaveLevelBands? row)
    {
        if (row is null)
        {
            return new CaveLevelBandsDto(caveId, false, [], null, null, null);
        }

        var bands = JsonSerializer.Deserialize<List<SavedElevationBand>>(row.Bands) ?? [];
        return new CaveLevelBandsDto(
            caveId, true, bands, row.Note, row.ConfirmedBy, row.CreatedAt);
    }

    /// <summary>
    /// The cave whose heights may be read, or null when they may not be — which covers a cave that
    /// does not exist, one the caller may not read, and one they may read but not place exactly.
    /// All three are one answer on purpose: distinguishing them would say which caves are being
    /// kept from whom, and the answer would say it to the person being kept out.
    /// </summary>
    private static async Task<Feature?> ReadableCaveAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

        return feature is not null
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, feature, ct)
                ? feature
                : null;
    }
}
