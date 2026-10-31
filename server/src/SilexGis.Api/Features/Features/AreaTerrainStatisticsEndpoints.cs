// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// Where in the ground an area's entrances and sinkholes sit, set against the ground itself: slope
/// classes, the direction the ground faces and whether it is hollow, level or a rise, each beside the
/// same distribution over the area's whole outline. A class where the entrances outnumber the
/// ground's share is where the area's caves prefer to be — the figure that guides prospecting.
/// Derived on every request; nothing is stored.
///
/// <para>
/// <b>Reads only pictures an administrator has already computed.</b> Steepness, facing and position
/// are made by a background job; this route never starts one. A measure whose picture is absent says
/// so (<c>no_layer</c>) and carries no classes, rather than an empty histogram that reads as "no
/// caves on steep ground".
/// </para>
/// <para>
/// <b>Protection.</b> The terrain right is required (a computed picture of the ground is elevation
/// data), then the area gate, then the entrances and sinkholes are put through the placement walk
/// <em>before</em> any cell is read. Every count, share and ratio is taken over what the caller may
/// place, so the answer is identical with or without a withheld feature in the area; the response
/// names no feature and no position. The background is a regular grid over the area's own outline,
/// which the same gate has already shown the caller.
/// </para>
/// <para>
/// <b>The route takes no options.</b> A figure that moves under a parameter is an oracle.
/// </para>
/// </summary>
public static class AreaTerrainStatisticsEndpoints
{
    private const string ForbiddenCode = "access.forbidden";
    private const string RoutePrefix = "terrain_statistics";

    /// <summary>
    /// Grid points per side over the outline's bounding box. A few thousand readings estimate class
    /// shares of the ground to well under a percentage point and keep one request short.
    /// </summary>
    private const int BackgroundGridPerSide = 45;

    /// <summary>Points per call to the reader, so one request never holds its gate for long.</summary>
    private const int Chunk = 500;

    public static RouteGroupBuilder MapAreaTerrainStatisticsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/terrain-statistics", StatisticsAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Slope, aspect and position classes of an area's entrances and sinkholes beside the "
                + "same classes over the area's ground. Requires Read on the Terrain domain; counts "
                + "only what the caller may place.");

        return api;
    }

    private static async Task<Results<Ok<AreaTerrainStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        StatisticsAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            TerrainDerivativeCatalogue catalogue,
            IDerivativeSampleService sampler,
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

        var (outline, refusal) = await StudyAreaGate.OutlineAsync(
            protection, ctx, id, RoutePrefix, "measure the ground of", ct);
        if (refusal is not null || outline is null)
        {
            return refusal ?? ApiProblems.NotFound("area.not_found");
        }

        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        var rows = await visible
            .Where(f => f.Geom != null
                && db.FeatureAncestors.Any(a => a.AncestorId == id && a.FeatureId == f.Id))
            .Where(f => f.Kind == FeatureKind.CaveEntrance
                || (f.Kind == FeatureKind.Generic
                    && db.FeatureTypes.Any(t => t.Id == f.FeatureTypeId && t.Code == FeatureTypeSeeds.Sinkhole)))
            .Select(f => new Row(f.Id, f.Kind == FeatureKind.CaveEntrance, f.IsProtectedEffective, f.Geom!))
            .ToListAsync(ct);

        // The whole gate runs before a single cell is read: a value under a feature is a statement
        // about where it is, and so is a count of features per class.
        var placeableIds = await protection.PlaceableIdsAsync(
            ctx, [.. rows.Select(r => (r.Id, r.IsProtectedEffective))], ct);
        var placeable = rows.Where(r => placeableIds.Contains(r.Id)).OrderBy(r => r.Id).ToList();

        var entrancePoints = placeable.Where(r => r.IsEntrance).Select(Centre).ToList();
        var sinkholePoints = placeable.Where(r => !r.IsEntrance).Select(Centre).ToList();
        var background = BackgroundPoints(outline);

        var layers = await LayersAsync(db, catalogue, ct);

        // Every point the measures need, read once per picture.
        var all = new List<DemSamplePoint>(entrancePoints.Count + sinkholePoints.Count + background.Count);
        all.AddRange(entrancePoints);
        all.AddRange(sinkholePoints);
        all.AddRange(background);

        var slope = layers.Slope is null ? null : await ReadAsync(sampler, layers.Slope, all, ct);
        var aspect = layers.Aspect is null ? null : await ReadAsync(sampler, layers.Aspect, all, ct);
        var position = layers.Position is null ? null : await ReadAsync(sampler, layers.Position, all, ct);

        // Facing is meaningless on flat ground, and the picture writes a bearing of zero there, so
        // each facing reading counts only where the steepness picture says the ground slopes.
        double?[]? sloping = null;
        string? aspectReason = null;
        if (aspect is not null)
        {
            if (slope is null)
            {
                aspectReason = layers.SlopeInPercentOnly ? "slope_unit" : "needs_slope";
            }
            else
            {
                sloping = new double?[all.Count];
                for (var i = 0; i < all.Count; i++)
                {
                    sloping[i] = aspect[i] is { } bearing
                        && slope[i] is { } steepness
                        && steepness >= TerrainClassStatistics.FlatSlopeDegrees
                        ? bearing
                        : null;
                }
            }
        }

        var counts = new Counts(entrancePoints.Count, sinkholePoints.Count, background.Count);

        var measures = new List<TerrainMeasureDto>
        {
            Measure(TerrainMeasure.Slope, "slope", slope,
                slope is null ? (layers.SlopeInPercentOnly ? "slope_unit" : "no_layer") : null, counts),
            Measure(TerrainMeasure.Aspect, "aspect", sloping,
                aspect is null ? "no_layer" : aspectReason, counts),
            Measure(TerrainMeasure.Position, "position", position,
                position is null ? "no_layer" : null, counts),
        };

        return TypedResults.Ok(new AreaTerrainStatisticsDto(
            id,
            layers.HasBuild,
            entrancePoints.Count,
            sinkholePoints.Count,
            background.Count,
            measures));
    }

    private static DemSamplePoint Centre(Row row)
    {
        // A sinkhole may be drawn as an outline, so the place read is the middle of what was drawn;
        // for an entrance, which is a point, it is the point itself.
        var centre = row.Geom.Centroid;
        return new DemSamplePoint(centre.X, centre.Y);
    }

    private static IReadOnlyList<DemSamplePoint> BackgroundPoints(Geometry outline)
    {
        var box = outline.EnvelopeInternal;
        var locator = new IndexedPointInAreaLocator(outline);
        return
        [
            .. TerrainClassStatistics
                .GridInside(box.MinX, box.MinY, box.MaxX, box.MaxY, BackgroundGridPerSide)
                .Where(p => locator.Locate(new Coordinate(p.Longitude, p.Latitude)) != Location.Exterior),
        ];
    }

    private static async Task<double?[]> ReadAsync(
        IDerivativeSampleService sampler,
        IReadOnlyList<PreparedTerrainRaster> layer,
        IReadOnlyList<DemSamplePoint> points,
        CancellationToken ct)
    {
        var values = new double?[points.Count];
        for (var start = 0; start < points.Count; start += Chunk)
        {
            var batch = points.Skip(start).Take(Chunk).ToList();
            var read = await sampler.SampleLayerAsync(layer, batch, ct);
            for (var i = 0; i < batch.Count; i++)
            {
                values[start + i] = read[i].Outcome == DemSampleOutcome.Sampled ? read[i].Value : null;
            }
        }

        return values;
    }

    /// <summary>
    /// A measure's classes. The points are laid out entrances, then sinkholes, then background, so
    /// the three groups are slices of one array.
    /// </summary>
    private static TerrainMeasureDto Measure(
        TerrainMeasure measure, string name, double?[]? values, string? reason, Counts counts)
    {
        var codes = TerrainClassStatistics.Codes(measure);
        if (values is null || reason is not null)
        {
            return new TerrainMeasureDto(name, false, reason ?? "no_layer", 0, 0, 0, []);
        }

        int[] Histogram(int from, int length) => TerrainClassStatistics.Histogram(
            measure, values.Skip(from).Take(length).Where(v => v is not null).Select(v => v!.Value));

        var entrances = Histogram(0, counts.Entrances);
        var sinkholes = Histogram(counts.Entrances, counts.Sinkholes);
        var ground = Histogram(counts.Entrances + counts.Sinkholes, counts.Background);
        int entrancesRead = entrances.Sum(), sinkholesRead = sinkholes.Sum(), groundRead = ground.Sum();

        double? Share(int count, int total) => total > 0 ? (double)count / total : null;

        return new TerrainMeasureDto(
            name,
            true,
            null,
            entrancesRead,
            sinkholesRead,
            groundRead,
            [.. codes.Select((code, i) => new TerrainClassRowDto(
                code,
                entrances[i],
                Share(entrances[i], entrancesRead),
                TerrainClassStatistics.Ratio(entrances[i], entrancesRead, ground[i], groundRead),
                sinkholes[i],
                Share(sinkholes[i], sinkholesRead),
                TerrainClassStatistics.Ratio(sinkholes[i], sinkholesRead, ground[i], groundRead),
                ground[i],
                Share(ground[i], groundRead)))]);
    }

    /// <summary>
    /// The current computed picture of each measure for the active terrain: the newest that is ready
    /// and not out of date. Steepness is used only in degrees, the unit the classes are cut in.
    /// </summary>
    private static async Task<Layers> LayersAsync(
        SilexGisDbContext db, TerrainDerivativeCatalogue catalogue, CancellationToken ct)
    {
        var buildId = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (buildId is not { } build)
        {
            return new Layers(false, null, null, null, false);
        }

        var views = (await catalogue.ForBuildAsync(build, ct))
            .Where(v => v.Status == TerrainDerivativeStatus.Ready && !v.Stale)
            .OrderByDescending(v => v.ComputedAt)
            .ToList();

        var slopes = views.Where(v => v.Derivative == TerrainDerivative.Slope).ToList();
        var inDegrees = slopes.FirstOrDefault(v => UnitOf(v.Settings) == TerrainSlopeUnit.Degrees);

        return new Layers(
            true,
            await RastersAsync(db, inDegrees?.Id, ct),
            await RastersAsync(db, views.FirstOrDefault(v => v.Derivative == TerrainDerivative.Aspect)?.Id, ct),
            await RastersAsync(db, views.FirstOrDefault(v => v.Derivative == TerrainDerivative.PositionIndex)?.Id, ct),
            inDegrees is null && slopes.Count > 0);
    }

    private static TerrainSlopeUnit UnitOf(string settings)
    {
        try
        {
            return JsonSerializer.Deserialize<TerrainDerivativeSettings>(settings, JsonSerializerOptions.Web)
                ?.SlopeUnit ?? TerrainSlopeUnit.Degrees;
        }
        catch (JsonException)
        {
            return TerrainSlopeUnit.Degrees;
        }
    }

    private static async Task<IReadOnlyList<PreparedTerrainRaster>?> RastersAsync(
        SilexGisDbContext db, Guid? layerId, CancellationToken ct)
    {
        if (layerId is not { } id)
        {
            return null;
        }

        var stored = await db.TerrainDerivativeRasters.AsNoTracking()
            .Where(r => r.TerrainDerivativeLayerId == id)
            .ToListAsync(ct);
        if (stored.Count == 0)
        {
            return null;
        }

        return
        [
            .. stored.Select(r =>
            {
                var box = r.Footprint.EnvelopeInternal;
                return new PreparedTerrainRaster(
                    r.Path, r.Width, r.Height, r.PixelSizeDegrees,
                    box.MinX, box.MinY, box.MaxX, box.MaxY, 0, r.SizeBytes);
            }),
        ];
    }

    private sealed record Row(Guid Id, bool IsEntrance, bool IsProtectedEffective, Geometry Geom);

    private sealed record Counts(int Entrances, int Sinkholes, int Background);

    private sealed record Layers(
        bool HasBuild,
        IReadOnlyList<PreparedTerrainRaster>? Slope,
        IReadOnlyList<PreparedTerrainRaster>? Aspect,
        IReadOnlyList<PreparedTerrainRaster>? Position,
        bool SlopeInPercentOnly);
}
