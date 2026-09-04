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
/// How big a cave's passages are: the widths and heights measured at its stations, how those are
/// distributed through the cave and up its vertical range, and how much space the passage encloses.
/// Derived on every request from the stored readings; nothing here is stored.
///
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, and the
/// refusal is answered as "no such cave" rather than as "you may not". A wall distance is a
/// measurement taken at a cave coordinate as surely as the cave's own point is, and a 403 would
/// confirm to somebody being kept from a hidden cave both that it exists and that it was surveyed.
/// </para>
/// <para>
/// <b>Every figure is paired with the count it was computed over.</b> A station whose surveyor never
/// reached one wall leaves the volume rather than contributing a flattened sliver of it, so the
/// answer says how many stations produced a cross-section and how many metres of passage the volume
/// describes. An estimate over a tenth of a cave and an estimate over all of it are the same number
/// with two entirely different meanings, and nothing but that count separates them.
/// </para>
/// </summary>
public static class CaveCrossSectionEndpoints
{
    public static RouteGroupBuilder MapCaveCrossSectionEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/cross-section", CrossSectionAsync)
            .WithValidation<CaveCrossSectionRequest>()
            .WithSummary(
                "How big one cave's passages are, and over how much of the cave that was worked "
                + "out. Withheld from a caller who may not place the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveCrossSectionDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        CrossSectionAsync(
            [AsParameters] CaveCrossSectionRequest request,
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

        if (!await ReadableCaveAsync(db, access, protection, ctx, request.Id, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);
        var readings = await SurveyLrudSql.ForCaveAsync(db, ctx, request.Id, ct);

        var summary = readings.Count > 0
            ? CrossSectionMorphometry.Summarize(readings, LegsOf(set))
            : null;

        return TypedResults.Ok(new CaveCrossSectionDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            readings.Count > 0,
            summary));
    }

    /// <summary>
    /// The cave's legs as the volume estimate sees them.
    /// </summary>
    /// <remarks>
    /// The real length along the passage is used, not its shadow on the map: a prism between two
    /// stations is as long as the passage between them, and using the plan length would shrink every
    /// steep piece of cave towards nothing. A leg with no slope length has no altitudes behind it and
    /// the two lengths are the same figure.
    /// </remarks>
    internal static IReadOnlyList<CrossSectionLeg> LegsOf(SurveySegmentSet set) =>
    [
        .. set.Segments.Select(s => new CrossSectionLeg(
            s.FromStationName, s.ToStationName, s.SlopeLengthM ?? s.PlanLengthM, s.IsDuplicate)),
    ];

    /// <summary>
    /// Whether this cave's survey may be measured at all — false for a cave that does not exist, one
    /// the caller may not read, and one the caller may read but not place exactly. All three are one
    /// answer on purpose: distinguishing them would say which caves are being kept from whom.
    /// </summary>
    internal static async Task<bool> ReadableCaveAsync(
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
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, feature, ct);
    }
}
