// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Terrain;

/// <summary>A line drawn by the caller, as the vertices they placed.</summary>
public sealed record TerrainProfileRequest(IReadOnlyList<TerrainProbePointRequest> Points);

/// <summary>
/// The ground along one drawn line.
/// </summary>
/// <param name="DistanceAlongM">Great-circle distance from the start of the line, metres.</param>
/// <param name="Longitude">Where the reading was taken, on the caller's own line.</param>
/// <param name="Latitude">Where the reading was taken, on the caller's own line.</param>
/// <param name="Outcome">Whether a height was read, and if not, why not.</param>
/// <param name="ElevationM">Height in metres, or null for either kind of no answer. Never zero
/// standing in for "not known".</param>
public sealed record TerrainProfileSampleDto(
    double DistanceAlongM, double Longitude, double Latitude, DemSampleOutcome Outcome, double? ElevationM);

/// <summary>
/// A surface profile along a drawn line.
/// </summary>
/// <param name="HasTerrain">Whether this installation serves any elevation at all.</param>
/// <param name="LengthM">Length of the line, metres.</param>
/// <param name="ReadCount">How many stations got a height.</param>
/// <param name="LowestM">Lowest height read.</param>
/// <param name="HighestM">Highest height read.</param>
/// <param name="AscentM">Total rise between neighbouring stations that both got a height.</param>
/// <param name="DescentM">Total fall between neighbouring stations that both got a height.</param>
/// <param name="Samples">Stations, in increasing distance along.</param>
public sealed record TerrainProfileDto(
    bool HasTerrain,
    double LengthM,
    int ReadCount,
    double? LowestM,
    double? HighestM,
    double AscentM,
    double DescentM,
    IReadOnlyList<TerrainProfileSampleDto> Samples);

/// <summary>Refuses a line the elevation reader should not be asked about.</summary>
public sealed class TerrainProfileRequestValidator : AbstractValidator<TerrainProfileRequest>
{
    public TerrainProfileRequestValidator()
    {
        RuleFor(x => x.Points)
            .NotNull()
            .Must(p => p is { Count: >= 2 })
            .WithMessage("A line needs at least two points.")
            .Must(p => p is null || p.Count <= TerrainLineProfile.MaxVertices)
            .WithMessage($"A line may have at most {TerrainLineProfile.MaxVertices} points.");

        RuleForEach(x => x.Points).ChildRules(point =>
        {
            point.RuleFor(p => p.Longitude)
                .Must(double.IsFinite).WithMessage("Longitude must be a number.")
                .InclusiveBetween(-180d, 180d);
            point.RuleFor(p => p.Latitude)
                .Must(double.IsFinite).WithMessage("Latitude must be a number.")
                .InclusiveBetween(-90d, 90d);
        });
    }
}

/// <summary>
/// A surface profile along a line somebody drew, for approach and rigging planning.
///
/// <para>
/// <b>Cave-blind, on purpose.</b> The route reads the ground along coordinates the caller supplied
/// and consults nothing else — no feature is looked up, none annotated, and the line cannot be
/// seeded from a cave's centreline, because a profile that could be would be a way of reading a
/// protected cave's passage as a curve. That is the property that makes the terrain right alone
/// sufficient, exactly as for the free-form probe.
/// </para>
/// <para>
/// The caller's line is resampled on the server to evenly spaced stations (never more than the
/// probe's own bound), so the distance axis is a true ground distance and the shared elevation
/// reader is held for a bounded time.
/// </para>
/// </summary>
public static class TerrainProfileEndpoints
{
    public static RouteGroupBuilder MapTerrainProfileEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/terrain/profile", ProfileAsync)
            .WithValidation<TerrainProfileRequest>()
            .WithTags("Terrain")
            .WithSummary(
                "Ground height along a line the caller draws; requires Read on the Terrain domain "
                + "and carries no cave data at all.");

        return api;
    }

    private static async Task<Results<Ok<TerrainProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ProfileAsync(
            TerrainProfileRequest request,
            IAccessContextAccessor accessAccessor,
            TerrainRasterIndex index,
            IDemSampleService sampler,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(TerrainProbeEndpoints.ForbiddenCode);
        }

        var (stations, lengthM) = TerrainLineProfile.Stations(
            [.. request.Points.Select(p => new DemSamplePoint(p.Longitude, p.Latitude))]);

        var coverage = await index.ActiveCoverageAsync(ct);
        var samples = await sampler.SampleAsync(
            coverage, [.. stations.Select(s => new DemSamplePoint(s.Longitude, s.Latitude))], ct);

        var summary = TerrainLineProfile.Summarise([.. samples.Select(s => s.ElevationM)]);

        return TypedResults.Ok(new TerrainProfileDto(
            coverage.Rasters.Count > 0,
            lengthM,
            summary.ReadCount,
            summary.LowestM,
            summary.HighestM,
            summary.AscentM,
            summary.DescentM,
            [.. stations.Select((s, i) => new TerrainProfileSampleDto(
                s.DistanceAlongM, s.Longitude, s.Latitude, samples[i].Outcome, samples[i].ElevationM))]));
    }
}
