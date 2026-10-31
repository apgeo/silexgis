// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// A cave's descent profile: how far below its highest surveyed point the passage lies, along its
/// length. Derived on every request; nothing is stored.
///
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, answered
/// as "no such cave". An altitude profile is the cave's own shape expressed as a curve, and an
/// altitude on a hillside places a cave as surely as a coordinate does. The refusal is identical for
/// a cave that does not exist.
/// </para>
/// <para>
/// <b>No elevation model is read</b>, so the Terrain right is not involved, and the response carries
/// no horizontal position at all — only distance along, altitude and depth.
/// </para>
/// <para>
/// <b>The route takes no options.</b> A figure that moves under a parameter can be asked repeatedly
/// with the parameter varied.
/// </para>
/// </summary>
public static class CaveDepthProfileEndpoints
{
    public static RouteGroupBuilder MapCaveDepthProfileEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/caves").WithTags("Caves")
            .MapGet("/{id:guid}/depth-profile", DepthProfileAsync)
            .WithValidation<CaveDepthProfileRequest>()
            .WithSummary(
                "Depth below the highest surveyed point against distance along one cave's passages. "
                + "Withheld from a caller who may not place the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveDepthProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        DepthProfileAsync(
            [AsParameters] CaveDepthProfileRequest request,
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

        if (await SurveyModelAccess.MeasurableCaveAsync(db, access, protection, ctx, request.Id, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);
        var profile = CaveDepthProfile.Build(SurveySegments.AsPassage(set.Segments));

        return TypedResults.Ok(new CaveDepthProfileDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            set.HasZ,
            profile.PassageLengthM,
            profile.HighestM,
            profile.LowestM,
            profile.DeepestM,
            [.. profile.Samples.Select(s => new CaveDepthProfileSampleDto(
                s.DistanceAlongM, s.AltitudeM, s.DepthM, s.PathIndex, s.SegmentIndex))]));
    }
}
