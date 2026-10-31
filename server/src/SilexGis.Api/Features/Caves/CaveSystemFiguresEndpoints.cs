// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// The vertical figures of one cave taken across all its entrances: highest and lowest entrance,
/// total depth from the highest entrance to the deepest surveyed point, height above the lowest
/// entrance, and the potential depth down to the level the surrounding area's springs flow at.
/// Derived on every request; nothing is stored.
///
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, answered
/// as "no such cave". Every figure here is an altitude, and an altitude places an entrance on a
/// hillside as surely as a pair of degrees; the survey half is the cave's own line work. Within a
/// cave the caller may place, an entrance whose own position is closed to them contributes nothing —
/// not to an extreme, not to the entrance count — so that the figures are the same with or without
/// it. The resurgence level is gathered under the area view's withhold-entirely rule for the same
/// reason.
/// </para>
/// <para>
/// <b>No elevation model is read</b>, so the Terrain right is not involved: the figures come from
/// altitudes the record and the survey already hold.
/// </para>
/// <para>
/// <b>The route takes no options.</b> A figure that moves under a parameter answers questions about
/// a position that the parameter-free figure does not.
/// </para>
/// </summary>
public static class CaveSystemFiguresEndpoints
{
    public static RouteGroupBuilder MapCaveSystemFiguresEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/caves").WithTags("Caves")
            .MapGet("/{id:guid}/system-figures", SystemFiguresAsync)
            .WithValidation<CaveSystemFiguresRequest>()
            .WithSummary(
                "Highest and lowest entrance, total depth, height and potential depth of one cave. "
                + "Withheld from a caller who may not place the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveSystemFiguresDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SystemFiguresAsync(
            [AsParameters] CaveSystemFiguresRequest request,
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

        var feature = await SurveyModelAccess.MeasurableCaveAsync(
            db, access, protection, ctx, request.Id, ct, withDeclaredFigures: true);
        if (feature?.Cave is not { } cave)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var entrances = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveFeatureId == request.Id)
            .Select(e => new { e.Id, e.Altitude })
            .ToListAsync(ct);

        // The cave is placeable, but protection can be set on an entrance's own branch too, so the
        // entrances are put through the walk one by one and the closed ones are dropped before any
        // arithmetic — they are not counted either.
        var exact = await protection.ExactViewIdsAsync(ctx, [.. entrances.Select(e => e.Id)], ct);
        var placeable = entrances.Where(e => exact.Contains(e.Id)).ToList();

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);
        var indices = CaveSurveyIndices.Compute(SurveySegments.AsPassage(set.Segments));

        // The lowest spring in the area that contains the cave is the level water leaves the massif
        // at. Absent when the cave sits in no area, the area holds no spring, or the springs it
        // holds are ones this caller may not place.
        var springs = await CaveHypsometryEndpoints.SpringReferenceLinesAsync(db, protection, ctx, request.Id, ct);

        var figures = CaveSystemFigures.Compute(
            [.. placeable.Where(e => e.Altitude is not null).Select(e => (double)e.Altitude!.Value)],
            placeable.Count,
            indices.HighestZM,
            indices.LowestZM,
            springs.Count > 0 ? springs[0] : null);

        return TypedResults.Ok(new CaveSystemFiguresDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            indices.HasAltitudes,
            figures,
            indices.HighestZM,
            indices.LowestZM,
            CaveSurveyIndices.CompareDepth(figures.PotentialDepthM, cave.PotentialDepth)));
    }
}
