// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// What a cave's survey measures the cave to be — its orientation, and its shape set beside the
/// shape its record claims. Derived on every request from the line work; nothing here is stored.
///
/// <para>
/// <b>Both routes are withheld entirely from a caller who may read the cave but not place it
/// exactly</b>, and they answer that refusal as "no such cave" rather than as "you may not". A leg
/// of a survey is a cave coordinate as surely as the cave's own point is, and a 403 here would
/// confirm to somebody being kept from a hidden cave both that the cave exists and that it has been
/// surveyed. So a cave nobody may place and a cave that was never created answer identically.
/// </para>
/// <para>
/// <b>Every answer says which body of line work produced it.</b> A cave whose survey file was
/// parsed is measured from the surveyor's own per-leg flags; a cave whose line work only ever
/// existed as a drawing is measured from a shape-based reduction that retains most, not all, of the
/// passage. Those two are within a few percent of each other and are not the same measurement, so
/// the basis travels with every response and a reader that hides it puts two different measurements
/// on one axis. Where a parsed survey answered, the answer also names <i>which</i> uploaded file it
/// was measured over: a cave may hold several, only one of them is measured, and two answers about
/// one cave describe the same passage only if that is the same in both.
/// </para>
/// <para>
/// Neither route is askable. They take no options beyond the cave, because a figure that moves
/// under a query parameter answers questions about caves and positions that the parameter-free
/// figure does not.
/// </para>
/// </summary>
public static class CaveSurveyStatisticsEndpoints
{
    /// <summary>
    /// How many paths the sinuosity breakdown carries, longest first. A large cave reduces to
    /// hundreds of them and the whole list is a distribution nobody reads off a cave page; the
    /// weighted average over all of them is reported separately and is not truncated.
    /// </summary>
    private const int PathRowLimit = 20;

    public static RouteGroupBuilder MapCaveSurveyStatisticsEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/statistics", StatisticsAsync)
            .WithValidation<CaveSurveyStatisticsRequest>()
            .WithSummary(
                "The shape one cave's survey measures, and where that disagrees with its record. "
                + "Withheld from a caller who may not place the cave exactly.");

        caves.MapGet("/{id:guid}/orientation", OrientationAsync)
            .WithValidation<CaveSurveyStatisticsRequest>()
            .WithSummary(
                "Which way and how steeply one cave's passage runs. Withheld from a caller who may "
                + "not place the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        StatisticsAsync(
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

        var feature = await ReadableCaveAsync(db, access, protection, ctx, request.Id, ct);
        if (feature?.Cave is not { } cave)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);
        var passage = SurveySegments.AsPassage(set.Segments);
        var indices = CaveSurveyIndices.Compute(passage);

        // A cave with no line work has an unknown length, not a length of zero — and comparing an
        // unknown against a declared figure must come out "not computed", never "disagrees by the
        // whole of it".
        var measured = set.Segments.Count > 0;
        var length = CaveSurveyIndices.CompareLength(
            measured ? indices.TotalLengthM : null, cave.SurveyedLength);
        var depth = CaveSurveyIndices.CompareDepth(indices.VerticalExtentM, cave.Depth);

        var paths = CaveSurveyIndices.PathSinuosities(passage)
            .OrderByDescending(p => p.LengthM)
            .Take(PathRowLimit)
            .ToList();

        return TypedResults.Ok(new CaveStatisticsDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            indices.HasAltitudes,
            indices,
            paths,
            length,
            depth,
            length.Disagrees || depth.Disagrees));
    }

    private static async Task<Results<Ok<CaveOrientationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        OrientationAsync(
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

        var feature = await ReadableCaveAsync(db, access, protection, ctx, request.Id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);

        // A leg the file marks as passage already surveyed on another trip is the same piece of
        // cave measured a second time. Its length is excluded from every total for that reason, and
        // its bearing is excluded here for the same one: leaving it in would weight one trend twice
        // over because two teams walked it, which is a fact about the surveying and not about
        // the cave.
        var measurable = set.Segments.Where(s => !s.IsDuplicate).ToList();

        var orientation = OrientationStatistics.Summarize(measurable
            .Where(s => s.AzimuthDegrees is not null)
            .Select(s => new OrientationSample(
                s.AzimuthDegrees!.Value, s.SlopeLengthM ?? s.PlanLengthM)));

        // Dip is refused, not reported as level, for line work that carries no altitudes. The
        // reduction that produces a drawn centerline's segments substitutes zero for a missing
        // altitude so the result can be stored and drawn, so summarising it would describe a plan
        // drawing as a cave that is flat everywhere, with nothing in the answer to say otherwise.
        var dip = set.HasZ
            ? DipStatistics.Summarize(measurable
                .Where(s => s.DipDegrees is not null)
                .Select(s => new DipSample(
                    s.DipDegrees!.Value, s.SlopeLengthM ?? s.PlanLengthM)))
            : null;

        return TypedResults.Ok(new CaveOrientationDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            set.HasZ,
            orientation.SampleCount,
            orientation.TotalLengthM,
            orientation.ByCount,
            orientation.ByLength,
            orientation.Bins,
            dip));
    }

    /// <summary>
    /// The cave whose survey may be measured, or null when it may not be — which covers a cave that
    /// does not exist, one the caller may not read, and one the caller may read but not place
    /// exactly. All three are one answer on purpose: the callers turn null into "no such cave", and
    /// distinguishing them would say which caves are being kept from whom.
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
            .Include(f => f.Cave)
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

        return feature is not null
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, feature, ct)
                ? feature
                : null;
    }
}
