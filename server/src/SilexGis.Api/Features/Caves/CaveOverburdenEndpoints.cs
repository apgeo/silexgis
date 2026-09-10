// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// How much rock is over your head along a cave's passages: the passage set against the ground above
/// it, reading the elevation data this installation has prepared. Derived on every request; nothing
/// here is stored.
///
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, and the
/// refusal is answered as "no such cave" rather than as "you may not". A profile is not a statistic
/// about a cave, it is the cave's own shape and position expressed as a curve: distance along
/// against depth below the surface, with the coordinate of every reading beside it. Somebody holding
/// it can put the passage on a map. So the gate deciding <em>which</em> cave may be profiled is
/// the cave's, not the terrain data's — holding every terrain right in the installation opens
/// nothing here.
/// </para>
///
/// <para>
/// <b>And the right to read elevation at all is still required.</b> Every reading carries the
/// ground height the installation's prepared rasters gave for that place, which is the same number
/// the free-form elevation probe answers with and is read under the same right. An installation
/// that withholds that right has to have it withheld here too, or the profile is the way around
/// it. The two gates are necessary together and neither is sufficient alone.
/// </para>
///
/// <para>
/// <b>The route takes no options.</b> A figure that moves under a query parameter can be asked
/// repeatedly with the parameter varied, and the sequence of answers says things about a position
/// that no single answer does.
/// </para>
///
/// <para>
/// <b>An unread place is a gap and never nought.</b> Elevation exists only inside builds an
/// administrator actually ran, and where none reaches the passage the profile carries no thickness
/// there at all. A zero would draw the passage arriving at the surface, which is plausible, alarming
/// and false, and is indistinguishable downstream from a real reading.
/// </para>
/// </summary>
public static class CaveOverburdenEndpoints
{
    private const string CaveNotFoundCode = "cave.not_found";
    private const string ForbiddenCode = "access.forbidden";

    public static RouteGroupBuilder MapCaveOverburdenEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/overburden", OverburdenAsync)
            .WithValidation<CaveOverburdenRequest>()
            .WithSummary(
                "How much rock lies over one cave's passages, along their length. Requires Read "
                + "on the Terrain domain, and is withheld from a caller who may not place the cave "
                + "exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveOverburdenDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        OverburdenAsync(
            [AsParameters] CaveOverburdenRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            TerrainRasterIndex index,
            IDemSampleService sampler,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Necessary, and nowhere near sufficient. Every reading below carries a ground height
        // read out of the installation's prepared rasters — the same value, under the same right,
        // that the free-form elevation probe answers with. Without this an installation that
        // withholds the right to read elevation hands it out anyway, a few hundred readings at a
        // time, to anybody who may place a cave.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        // The whole gate runs before a single pixel is read. Reading first and filtering afterwards
        // would put a withheld cave's passage positions in memory beside a decision not to hand them
        // over, which is one careless change away from handing them over.
        if (!await CaveCrossSectionEndpoints.ReadableCaveAsync(db, access, protection, ctx, request.Id, ct))
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);
        var (stations, passageLengthM) = OverburdenProfile.Stations(SurveySegments.AsPassage(set.Segments));

        // Resolved even when there is nothing to ask about, because "this installation has built no
        // terrain" and "the cave has no altitudes to measure against terrain" are two different
        // answers and a reader shown the first when the second is true will go looking for an
        // administrator who has nothing to do.
        var coverage = await index.ActiveCoverageAsync(ct);

        var ground = await sampler.SampleAsync(
            coverage,
            [.. stations.Select(s => new DemSamplePoint(s.Longitude, s.Latitude))],
            ct);

        var samples = new List<CaveOverburdenSampleDto>(stations.Count);
        for (var i = 0; i < stations.Count; i++)
        {
            var station = stations[i];
            var read = ground[i];

            // The reader has already reconciled its heights with the survey's vertical datum, so the
            // thickness of rock is a plain subtraction. Applying a geoid correction here as well
            // would be a forty-metre error wearing the clothes of a real altitude.
            var overburdenM = read.ElevationM is { } groundM ? groundM - station.PassageAltitudeM : (double?)null;

            samples.Add(new CaveOverburdenSampleDto(
                station.DistanceAlongM,
                station.Longitude,
                station.Latitude,
                station.PassageAltitudeM,
                read.Outcome,
                read.ElevationM,
                overburdenM,
                station.PathIndex,
                station.SegmentIndex));
        }

        // Summarised over the readings that got a ground height and over nothing else: counting an
        // unread place as nought would report a cave as shallower the less of it terrain covers.
        var covered = samples.Where(s => s.OverburdenM is not null).Select(s => s.OverburdenM!.Value).ToArray();

        return TypedResults.Ok(new CaveOverburdenDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            set.HasZ,
            coverage.Rasters.Count > 0,
            passageLengthM,
            covered.Length,
            covered.Length > 0 ? covered.Min() : null,
            covered.Length > 0 ? covered.Max() : null,
            covered.Length > 0 ? covered.Average() : null,
            samples));
    }
}
