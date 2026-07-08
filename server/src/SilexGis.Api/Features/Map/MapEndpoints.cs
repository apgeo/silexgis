// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// GeoJSON layer endpoints for the map workspace. Always
/// visibility-filtered; protected cave locations obfuscated server-side.
/// Below the cluster zoom threshold results are aggregated into cluster features.
/// </summary>
public static class MapEndpoints
{
    /// <summary>Below this zoom the endpoint returns clusters instead of points.</summary>
    public const int ClusterMaxZoom = 11;

    /// <summary>Safety cap for individual point features per request.</summary>
    private const int MaxPoints = 5000;

    public static RouteGroupBuilder MapMapDataEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/cave-entrances", CaveEntrancesAsync)
            .WithTags("Map")
            .WithSummary("Cave entrances as GeoJSON for the given bbox; clustered at low zoom.");
        api.MapGet("/map/surface-features", SurfaceFeaturesAsync)
            .WithTags("Map")
            .WithSummary("Surface features as GeoJSON for the given bbox, optionally filtered by type.");
        api.MapGet("/map/geofiles/{id:guid}/features", GeofileFeaturesAsync)
            .WithTags("Map")
            .WithSummary("Imported geofile rows as GeoJSON for the given bbox.");
        return api;
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> GeofileFeaturesAsync(
        Guid id,
        string bbox,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Rows inherit the geofile's ACL; an unreadable geofile is not disclosed.
        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !PermissionEvaluator.Can(user, geofile, ObjectPermission.Read))
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var polygon = box.ToPolygon();
        var rows = await db.GeofileFeatures.AsNoTracking()
            .Where(f => f.GeofileId == id && f.Geom.Intersects(polygon))
            .Take(MaxPoints)
            .ToListAsync(ct);

        var features = rows.Select(f =>
        {
            var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(f.Properties)
                ?? [];
            properties["id"] = f.Id;
            return GeoFeature.Of(f.Geom, properties);
        }).ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> SurfaceFeaturesAsync(
        string bbox,
        long? featureTypeId,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var polygon = box.ToPolygon();
        var query = db.SurfaceFeatures.AsNoTracking()
            .VisibleTo(user)
            .Where(f => f.Geom.Intersects(polygon));

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        var rows = await query.Take(MaxPoints).ToListAsync(ct);
        var features = rows.Select(f => GeoFeature.Of(f.Geom, new Dictionary<string, object?>
        {
            ["id"] = f.Id,
            ["name"] = f.Name,
            ["featureTypeId"] = f.FeatureTypeId,
            ["caveId"] = f.CaveId,
        })).ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveEntrancesAsync(
        string bbox,
        int? zoom,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var effectiveZoom = Math.Clamp(zoom ?? 14, 0, 24);
        return effectiveZoom < ClusterMaxZoom
            ? TypedResults.Ok(await MapSql.ClustersAsync(db, user, box, effectiveZoom, access.Value.LocationGridMeters, ct))
            : TypedResults.Ok(await PointsAsync(db, user, box, access.Value.LocationGridMeters, ct));
    }

    private static async Task<FeatureCollection> PointsAsync(
        SilexGisDbContext db, UserContext user, Bbox box, double gridMeters, CancellationToken ct)
    {
        var polygon = box.ToPolygon();

        var rows = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.Geom.Intersects(polygon))
            .Join(
                db.Caves.AsNoTracking().VisibleTo(user),
                e => e.CaveId,
                c => c.Id,
                (e, c) => new { Entrance = e, Cave = c })
            .Take(MaxPoints)
            .ToListAsync(ct);

        var features = rows.Select(row =>
        {
            var exact = LocationProtection.CanViewExactLocation(user, row.Cave);
            var geom = exact ? row.Entrance.Geom : LocationProtection.Snap(row.Entrance.Geom, gridMeters);
            return GeoFeature.Of(geom, new Dictionary<string, object?>
            {
                ["id"] = row.Entrance.Id,
                ["caveId"] = row.Cave.Id,
                ["name"] = row.Entrance.Name ?? row.Cave.Name,
                ["caveName"] = row.Cave.Name,
                ["isMain"] = row.Entrance.IsMain,
                ["protected"] = row.Cave.LocationProtected,
                ["approximate"] = !exact,
            });
        }).ToList();

        return FeatureCollection.Of(features);
    }
}
