// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Terrain;

/// <summary>One place to read the ground at, in degrees on the world datum.</summary>
public sealed record TerrainProbePointRequest(double Longitude, double Latitude);

/// <summary>Places to read the ground at, named by the caller and by nothing else.</summary>
public sealed record TerrainProbeRequest(IReadOnlyList<TerrainProbePointRequest> Points);

/// <summary>Features to read the ground under, named by identifier.</summary>
public sealed record TerrainFeatureProbeRequest(IReadOnlyList<Guid> FeatureIds);

/// <summary>
/// What the ground was at one place asked about, or why it could not be said.
/// </summary>
/// <param name="ElevationM">
/// Height in metres above sea level, or null for either kind of no answer. Never zero standing in
/// for "not known": a surface at sea level is a plausible reading and nothing downstream could tell
/// it from a real one. Already reconciled with the datum surveyed altitudes are measured from, so
/// it may be compared with a cave's own heights directly and no correction is applied a second time.
/// </param>
public sealed record TerrainProbeSampleDto(
    double Longitude, double Latitude, DemSampleOutcome Outcome, double? ElevationM);

/// <summary>
/// The ground under one feature.
/// </summary>
/// <remarks>
/// The feature's position is deliberately not echoed back. The caller has it already — being
/// entitled to it is what earned this row — and a route that repeated it would become a second way
/// of obtaining a cave position, to be kept correct forever alongside the first.
/// </remarks>
public sealed record TerrainFeatureProbeSampleDto(
    Guid FeatureId, DemSampleOutcome Outcome, double? ElevationM);

/// <summary>
/// Ground heights at the places asked about.
/// </summary>
/// <param name="HasTerrain">
/// Whether this installation currently serves any elevation at all. It separates "nobody has built
/// terrain here yet" from "terrain is built and does not reach this place", which are the same
/// no-answer to a caller and completely different things to whoever administers the installation.
/// </param>
/// <param name="Samples">One row per place asked about, in the order they were asked.</param>
public sealed record TerrainProbeDto(bool HasTerrain, IReadOnlyList<TerrainProbeSampleDto> Samples);

/// <summary>
/// Ground heights under the features asked about, as far as the caller is entitled to have them.
/// </summary>
/// <param name="Samples">
/// One row per feature the caller may both read and place exactly, in the order they were asked
/// about. A feature outside that set is <em>missing</em> from this list, never present with no
/// height: a keyed row carrying a null says "this one exists and you may not have it", which is
/// precisely the fact being protected.
/// </param>
public sealed record TerrainFeatureProbeDto(
    bool HasTerrain, IReadOnlyList<TerrainFeatureProbeSampleDto> Samples);

/// <summary>
/// Whether the caller has named places this application can look at.
/// </summary>
/// <remarks>
/// The bound on how many is the point of it. Every point is a window read out of a raster behind a
/// gate only one caller holds at a time, so an unbounded list is a way for one request to hold the
/// elevation reader for as long as it likes.
/// </remarks>
public sealed class TerrainProbeRequestValidator : AbstractValidator<TerrainProbeRequest>
{
    public TerrainProbeRequestValidator()
    {
        RuleFor(x => x.Points)
            .NotNull()
            .Must(p => p is { Count: > 0 })
            .WithMessage("At least one point is required.")
            .Must(p => p is null || p.Count <= TerrainProbeEndpoints.MaxPoints)
            .WithMessage($"At most {TerrainProbeEndpoints.MaxPoints} points may be probed at once.");

        RuleForEach(x => x.Points).ChildRules(point =>
        {
            // Finiteness is checked before the range, because a comparison against an infinity or a
            // not-a-number answers false without saying why and one that has slipped through would
            // be handed straight to a native library.
            point.RuleFor(p => p.Longitude)
                .Must(double.IsFinite).WithMessage("Longitude must be a number.")
                .InclusiveBetween(-180d, 180d);
            point.RuleFor(p => p.Latitude)
                .Must(double.IsFinite).WithMessage("Latitude must be a number.")
                .InclusiveBetween(-90d, 90d);
        });
    }
}

/// <summary>Whether the caller has named features this application could look for.</summary>
public sealed class TerrainFeatureProbeRequestValidator : AbstractValidator<TerrainFeatureProbeRequest>
{
    public TerrainFeatureProbeRequestValidator()
    {
        RuleFor(x => x.FeatureIds)
            .NotNull()
            .Must(f => f is { Count: > 0 })
            .WithMessage("At least one feature is required.")
            .Must(f => f is null || f.Count <= TerrainProbeEndpoints.MaxPoints)
            .WithMessage($"At most {TerrainProbeEndpoints.MaxPoints} features may be probed at once.");
    }
}

/// <summary>
/// Reading the ground height at places on it.
///
/// <para>
/// <b>There are two routes here and their being two is the whole design.</b> One answers about
/// coordinates the caller supplied and knows nothing about caves; the other answers about features,
/// and is governed by what the caller is allowed to know about <em>those features</em> rather than
/// by the terrain right at all.
/// </para>
/// <para>
/// A single route would be a location oracle. Terrain is an installation-level asset, so the right
/// to read it is held at the installation and says nothing about any cave — and an endpoint that
/// accepted a set of places and annotated the ones sitting on a cave would let anybody holding that
/// right find a protected entrance by watching which points came back answered. No care taken over
/// the response body closes that: the disclosure is in which rows exist, not in what they contain.
/// So the free-form route never learns that a point is a cave, and the feature route puts every
/// feature through the reading rights and then through the placement rights before it reads a pixel.
/// </para>
/// <para>
/// <b>The terrain right is necessary on both and sufficient on neither of the two questions the
/// feature route asks.</b> A caller holding it and nothing else gets an empty list there — not a
/// refusal, because refusing would itself say the identifiers named something.
/// </para>
/// </summary>
public static class TerrainProbeEndpoints
{
    /// <summary>The caller is signed in but holds nothing over the terrain domain.</summary>
    public const string ForbiddenCode = "access.forbidden";

    /// <summary>How many places one request may ask about.</summary>
    /// <remarks>
    /// Generous enough for a line of samples along a passage and far short of what would make one
    /// request able to hold the elevation reader indefinitely. The reader serialises access to the
    /// raster library's handles because two threads touching one dataset fault the whole process,
    /// so the length of a request is the length of somebody else's wait.
    /// </remarks>
    public const int MaxPoints = 500;

    public static RouteGroupBuilder MapTerrainProbeEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/terrain/probe", ProbeAsync)
            .WithValidation<TerrainProbeRequest>()
            .WithTags("Terrain")
            .WithSummary(
                "Ground height at coordinates the caller supplies; requires Read on the Terrain "
                + "domain and carries no cave data at all.");

        api.MapPost("/terrain/probe/features", ProbeFeaturesAsync)
            .WithValidation<TerrainFeatureProbeRequest>()
            .WithTags("Terrain")
            .WithSummary(
                "Ground height under cave entrances and sinkholes; requires Read on the Terrain "
                + "domain and, per feature, the right to read it and to place it exactly.");

        return api;
    }

    /// <summary>
    /// Reads the ground at coordinates the caller named, and at nothing else.
    /// </summary>
    /// <remarks>
    /// No feature is looked up, no row is joined and nothing about what is at these coordinates is
    /// consulted. That is what makes the terrain right sufficient here: the answer is a property of
    /// the elevation data this installation holds, which the right is held over, and of the numbers
    /// the caller already had.
    /// </remarks>
    private static async Task<Results<Ok<TerrainProbeDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ProbeAsync(
            TerrainProbeRequest request,
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
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var coverage = await index.ActiveCoverageAsync(ct);
        var points = request.Points
            .Select(p => new DemSamplePoint(p.Longitude, p.Latitude))
            .ToList();

        var samples = await sampler.SampleAsync(coverage, points, ct);

        return TypedResults.Ok(new TerrainProbeDto(
            coverage.Rasters.Count > 0,
            [.. samples.Select(s => new TerrainProbeSampleDto(
                s.Point.Longitude, s.Point.Latitude, s.Outcome, s.ElevationM))]));
    }

    /// <summary>
    /// Reads the ground under the named features, omitting every one the caller may not both read
    /// and place exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order of the work is deliberate and is the same order the closest-approach measurement
    /// uses: the whole gate runs before a single pixel is read. Reading first and filtering
    /// afterwards would put a protected cave's ground height in memory next to a decision not to
    /// hand it over, which is one careless change away from handing it over.
    /// </para>
    /// <para>
    /// Two filters, because they answer two different questions and neither implies the other.
    /// Visibility says whether the caller may know the feature exists; exact placement says whether
    /// they may know where it is. A ground height under a feature <em>is</em> a statement about
    /// where it is — read one contour at a time it says which hillside a cave sits on — so it is
    /// governed by the second and not only by the first.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TerrainFeatureProbeDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ProbeFeaturesAsync(
            TerrainFeatureProbeRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
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

        // Necessary, and on this route nowhere near sufficient. It gates reading elevation at all;
        // what may be read about any particular feature is decided below, per feature.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var asked = request.FeatureIds.Distinct().ToList();

        // Resolved by code rather than by a stored number, so that renaming the shipped kind fails
        // the build instead of quietly making every sinkhole unprobeable.
        var sinkholeTypeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.Sinkhole)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);

        var rows = await visible
            .Where(f => asked.Contains(f.Id) && f.Geom != null)
            .Where(f => f.Kind == FeatureKind.CaveEntrance
                || (f.Kind == FeatureKind.Generic
                    && sinkholeTypeId != null
                    && f.FeatureTypeId == sinkholeTypeId))
            .Select(f => new Row(f.Id, f.IsProtectedEffective, f.Geom!))
            .ToListAsync(ct);

        // Only the rows the stored flag marks as protected are put to the access walk: the flag is
        // a column and the walk is the expensive half. The narrowing is a cheap column and never
        // the verdict — the re-admission below asks the flag again, not the walk's answer.
        var exact = await protection.ExactViewIdsAsync(
            ctx, [.. rows.Where(r => r.IsProtectedEffective).Select(r => r.FeatureId)], ct);

        var placeable = rows
            .Where(r => !r.IsProtectedEffective || exact.Contains(r.FeatureId))
            .ToDictionary(r => r.FeatureId);

        // Answered in the order the caller asked, so a client can walk its own list. What it cannot
        // do is line the two lists up by position: the answer is shorter whenever something was
        // withheld or was not a kind this route reads, which is why every row names its feature.
        var ordered = asked.Where(placeable.ContainsKey).ToList();

        var coverage = await index.ActiveCoverageAsync(ct);

        // A sinkhole may be drawn as an outline rather than as a point, so the place read is the
        // middle of whatever was drawn. For a feature that is already a point that is the point
        // itself, which is every entrance.
        var points = ordered
            .Select(id => placeable[id].Geom.Centroid)
            .Select(centre => new DemSamplePoint(centre.X, centre.Y))
            .ToList();

        var samples = await sampler.SampleAsync(coverage, points, ct);

        return TypedResults.Ok(new TerrainFeatureProbeDto(
            coverage.Rasters.Count > 0,
            [.. ordered.Select((id, i) => new TerrainFeatureProbeSampleDto(
                id, samples[i].Outcome, samples[i].ElevationM))]));
    }

    private sealed record Row(
        Guid FeatureId, bool IsProtectedEffective, NetTopologySuite.Geometries.Geometry Geom);
}
