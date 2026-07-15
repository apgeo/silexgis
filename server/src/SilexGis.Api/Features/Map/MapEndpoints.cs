// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
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
        api.MapGet("/map/trip-logs", TripLogsAsync)
            .WithTags("Map")
            .WithSummary("Trip-log geometries as GeoJSON for the given bbox and date range.");
        api.MapGet("/map/cave-centerlines", CaveCenterlinesAsync)
            .WithTags("Map")
            .WithSummary("Cave centerlines as GeoJSON for the given bbox; protected caves' lines omitted.");
        api.MapGet("/map/photos", PhotosAsync)
            .WithTags("Map")
            .WithSummary("Geotagged photos as GeoJSON points for the given bbox; protected-cave photos withheld.");
        return api;
    }

    /// <summary>
    /// Geotagged image files as points. A photo shows only where the caller can read at least
    /// one entity it is attached to; a photo attached to a protected cave (directly or via one
    /// of its entrances) is withheld entirely from callers without the exact-location permission —
    /// the EXIF point itself is the location, so snapping it is not enough.
    /// </summary>
    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> PhotosAsync(
        string bbox,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
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
        var candidates = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Geom != null && f.Kind == FileKind.Image && f.Geom!.Intersects(polygon))
            .Select(f => new { f.Id, f.Geom, f.OriginalName })
            .Take(MaxPoints)
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return TypedResults.Ok(FeatureCollection.Of([]));
        }

        var fileIds = candidates.Select(c => c.Id).ToList();
        var links = await db.Attachments.AsNoTracking()
            .Where(a => fileIds.Contains(a.FileId))
            .Select(a => new { a.FileId, a.EntityType, a.EntityId })
            .ToListAsync(ct);

        Guid[] IdsOf(AttachedEntityType type) =>
            [.. links.Where(l => l.EntityType == type).Select(l => l.EntityId).Distinct()];

        // Entrance visibility + protection follow the entrance's cave.
        var entranceIds = IdsOf(AttachedEntityType.CaveEntrance);
        var entranceCaves = entranceIds.Length == 0
            ? []
            : await db.CaveEntrances.AsNoTracking()
                .Where(e => entranceIds.Contains(e.Id))
                .Select(e => new { e.Id, e.CaveId })
                .ToListAsync(ct);
        var entranceToCave = entranceCaves.ToDictionary(e => e.Id, e => e.CaveId);

        // A trip's cave links and a surface feature's linked cave reveal that cave's location too,
        // so a photo attached to one is subject to the same protection as a direct cave attachment.
        var tripIds = IdsOf(AttachedEntityType.TripLog);
        var tripCaves = tripIds.Length == 0
            ? []
            : await db.TripLogCaves.AsNoTracking()
                .Where(x => tripIds.Contains(x.TripLogId))
                .Select(x => new { x.TripLogId, x.CaveId })
                .ToListAsync(ct);
        var tripToCaves = tripCaves.GroupBy(x => x.TripLogId).ToDictionary(g => g.Key, g => g.Select(x => x.CaveId).ToList());

        var featureIds = IdsOf(AttachedEntityType.SurfaceFeature);
        var featureCaves = featureIds.Length == 0
            ? []
            : await db.SurfaceFeatures.AsNoTracking()
                .Where(f => featureIds.Contains(f.Id) && f.CaveId != null)
                .Select(f => new { f.Id, CaveId = f.CaveId!.Value })
                .ToListAsync(ct);
        var featureToCave = featureCaves.ToDictionary(f => f.Id, f => f.CaveId);

        // Every cave a photo touches (direct, via an entrance, via a trip cave-link, or via a
        // surface feature's cave) — drives readability and protection.
        var allCaveIds = IdsOf(AttachedEntityType.Cave)
            .Concat(entranceCaves.Select(e => e.CaveId))
            .Concat(tripCaves.Select(x => x.CaveId))
            .Concat(featureCaves.Select(f => f.CaveId))
            .Distinct().ToList();

        var readableCaveIds = await ReadableIdsAsync(
            db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave).Select(c => c.Id), allCaveIds, ct);
        var readableFeatureIds = await ReadableIdsAsync(
            db.SurfaceFeatures.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.SurfaceFeature).Select(f => f.Id),
            featureIds, ct);
        var readableTripIds = await ReadableIdsAsync(
            db.TripLogs.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog).Select(t => t.Id),
            tripIds, ct);
        var readableGeofileIds = await ReadableIdsAsync(
            db.Geofiles.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Geofile).Select(g => g.Id),
            IdsOf(AttachedEntityType.Geofile), ct);

        // Caves whose exact location the caller may not see → any photo touching one is withheld.
        var exactGrants = await new AclPermissionService(db).CaveExactLocationGrantsAsync(user, ct);
        var referencedCaves = allCaveIds.Count == 0
            ? []
            : await db.Caves.AsNoTracking().Where(c => allCaveIds.Contains(c.Id)).ToListAsync(ct);
        var hiddenCaveIds = referencedCaves
            .Where(c => !LocationProtection.CanViewExactLocation(
                user, c, exactGrants.Contains(c.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None))
            .Select(c => c.Id)
            .ToHashSet();

        var linksByFile = links.GroupBy(l => l.FileId).ToDictionary(g => g.Key, g => g.ToList());
        var features = new List<GeoFeature>();
        foreach (var candidate in candidates)
        {
            if (!linksByFile.TryGetValue(candidate.Id, out var fileLinks))
            {
                continue; // no attachment → no context and no visibility path
            }

            var photoCaveIds = fileLinks
                .Where(l => l.EntityType == AttachedEntityType.Cave).Select(l => l.EntityId)
                .Concat(fileLinks
                    .Where(l => l.EntityType == AttachedEntityType.CaveEntrance)
                    .Select(l => entranceToCave.TryGetValue(l.EntityId, out var caveId) ? caveId : (Guid?)null)
                    .Where(caveId => caveId is not null)
                    .Select(caveId => caveId!.Value))
                .Concat(fileLinks
                    .Where(l => l.EntityType == AttachedEntityType.TripLog)
                    .SelectMany(l => tripToCaves.GetValueOrDefault(l.EntityId) ?? []))
                .Concat(fileLinks
                    .Where(l => l.EntityType == AttachedEntityType.SurfaceFeature)
                    .Select(l => featureToCave.TryGetValue(l.EntityId, out var caveId) ? caveId : (Guid?)null)
                    .Where(caveId => caveId is not null)
                    .Select(caveId => caveId!.Value));
            if (photoCaveIds.Any(hiddenCaveIds.Contains))
            {
                continue; // protected-cave location — the point itself is sensitive
            }

            var visible = fileLinks.Any(l => l.EntityType switch
            {
                AttachedEntityType.Cave => readableCaveIds.Contains(l.EntityId),
                AttachedEntityType.CaveEntrance => entranceToCave.TryGetValue(l.EntityId, out var caveId) && readableCaveIds.Contains(caveId),
                AttachedEntityType.SurfaceFeature => readableFeatureIds.Contains(l.EntityId),
                AttachedEntityType.TripLog => readableTripIds.Contains(l.EntityId),
                AttachedEntityType.Geofile => readableGeofileIds.Contains(l.EntityId),
                AttachedEntityType.Team => user.IsMemberOf(l.EntityId),
                _ => false,
            });
            if (!visible)
            {
                continue;
            }

            var token = tokens.CreateToken(candidate.Id);
            features.Add(GeoFeature.Of(candidate.Geom!, new Dictionary<string, object?>
            {
                ["id"] = candidate.Id,
                ["name"] = candidate.OriginalName,
                ["thumbnailUrl"] = $"/api/v1/files/{candidate.Id}/thumbnail?size=160&token={Uri.EscapeDataString(token)}",
                ["contentUrl"] = $"/api/v1/files/{candidate.Id}/content?token={Uri.EscapeDataString(token)}",
            }));
        }

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    /// <summary>Intersects a set of candidate ids with a visibility-filtered id query (empty → empty, no round trip).</summary>
    private static async Task<HashSet<Guid>> ReadableIdsAsync(
        IQueryable<Guid> visibleIds, IReadOnlyList<Guid> candidates, CancellationToken ct) =>
        candidates.Count == 0
            ? []
            : [.. await visibleIds.Where(id => candidates.Contains(id)).ToListAsync(ct)];

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveCenterlinesAsync(
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

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        // Centerlines inherit the cave's visibility; the join also applies the caves'
        // soft-delete filter. Lines of location-protected caves are omitted entirely —
        // a centerline IS the cave's exact location.
        var polygon = box.ToPolygon();
        var visibleCaves = db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave);
        var rows = await db.CaveCenterlines.AsNoTracking()
            .Where(cl => cl.Geom.Intersects(polygon))
            .Join(visibleCaves, cl => cl.CaveId, c => c.Id, (cl, c) => cl)
            .Take(MaxPoints)
            .ToListAsync(ct);

        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(db, user, rows.Select(r => r.CaveId), ct);
        var features = rows
            .Where(r => !redacted.Contains(r.CaveId))
            .Select(r => GeoFeature.Of(r.Geom, new Dictionary<string, object?>
            {
                ["id"] = r.Id,
                ["caveId"] = r.CaveId,
                ["name"] = r.Name,
                ["lengthM"] = r.LengthM,
            }))
            .ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> TripLogsAsync(
        string bbox,
        DateOnly? from,
        DateOnly? to,
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
        var query = db.TripLogs.AsNoTracking()
            .VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog)
            .Where(x => x.Geom != null && x.Geom.Intersects(polygon));

        if (from is not null)
        {
            query = query.Where(x => x.TripDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.TripDate <= to);
        }

        var rows = await query.Take(MaxPoints).ToListAsync(ct);
        var features = rows.Select(x => GeoFeature.Of(x.Geom!, new Dictionary<string, object?>
        {
            ["id"] = x.Id,
            ["title"] = x.Title,
            ["tripDate"] = x.TripDate.ToString("O"),
        })).ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> GeofileFeaturesAsync(
        Guid id,
        string bbox,
        SilexGisDbContext db,
        IPermissionService permissions,
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
        if (geofile is null || !await permissions.CanAsync(user, geofile, ObjectPermission.Read, ct))
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
        string? tag,
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
            .VisibleTo(user, db.ObjectAcls, AttachedEntityType.SurfaceFeature)
            .Where(f => f.Geom.Intersects(polygon));

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.EntityType == AttachedEntityType.SurfaceFeature && tg.EntityId == f.Id
                && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        var rows = await query.Take(MaxPoints).ToListAsync(ct);

        // A cave link next to exact feature coordinates would disclose a protected
        // cave's location — hide the link where the caller lacks the permission.
        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(
            db, user, rows.Where(f => f.CaveId is not null).Select(f => f.CaveId!.Value), ct);

        var features = rows.Select(f => GeoFeature.Of(f.Geom, new Dictionary<string, object?>
        {
            ["id"] = f.Id,
            ["name"] = f.Name,
            ["featureTypeId"] = f.FeatureTypeId,
            ["caveId"] = f.CaveId is not null && redacted.Contains(f.CaveId.Value) ? null : f.CaveId,
        })).ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveEntrancesAsync(
        string bbox,
        int? zoom,
        string? tag,
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
            ? TypedResults.Ok(await MapSql.ClustersAsync(db, user, box, effectiveZoom, access.Value.LocationGridMeters, tag, ct))
            : TypedResults.Ok(await PointsAsync(db, user, box, access.Value.LocationGridMeters, tag, ct));
    }

    private static async Task<FeatureCollection> PointsAsync(
        SilexGisDbContext db, UserContext user, Bbox box, double gridMeters, string? tag, CancellationToken ct)
    {
        var exactGrants = await new AclPermissionService(db).CaveExactLocationGrantsAsync(user, ct);
        var polygon = box.ToPolygon();

        var caves = db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            caves = caves.Where(c => db.Taggings.Any(tg =>
                tg.EntityType == AttachedEntityType.Cave && tg.EntityId == c.Id
                && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        var rows = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.Geom.Intersects(polygon))
            .Join(
                caves,
                e => e.CaveId,
                c => c.Id,
                (e, c) => new { Entrance = e, Cave = c })
            .Take(MaxPoints)
            .ToListAsync(ct);

        var features = rows.Select(row =>
        {
            var exact = LocationProtection.CanViewExactLocation(
                user, row.Cave, exactGrants.Contains(row.Cave.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None);
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
