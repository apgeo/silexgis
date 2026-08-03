// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// A GeoJSON FeatureCollection plus the two things the centerline overlay needs to explain
/// itself: how many centerlines the request's limits kept back, and whether what it did return
/// is full detail or the splay-free skeleton. Both are GeoJSON foreign members, so a plain
/// GeoJSON reader still parses the collection.
/// </summary>
public sealed record CenterlineFeatureCollection(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    int WithheldCount,
    bool Detail);

/// <summary>Map rendering limits published to the client.</summary>
public sealed record MapConfigDto(
    int CenterlineDetailZoom,
    int CenterlineMaxPaths,
    int CenterlineMaxPathsLimit,
    int CenterlineGateZoom,
    int ClusterMaxZoom);

/// <summary>
/// GeoJSON layer endpoints for the map workspace. Always visibility-filtered; protected
/// feature locations obfuscated server-side (points snapped to the protection grid,
/// extended geometry withheld). Below the cluster zoom threshold the entrance layer
/// aggregates into cluster features.
/// </summary>
public static class MapEndpoints
{
    /// <summary>Below this zoom the entrance endpoint returns clusters instead of points.</summary>
    public const int ClusterMaxZoom = 11;

    /// <summary>Safety cap for individual point features per request.</summary>
    /// <remarks>
    /// Every query that applies the cap orders by id first. Without an order the database is
    /// free to return any rows it likes once the cap bites, and it need not pick the same ones
    /// twice — so a dense viewport would show a different arbitrary subset on each pan back to
    /// it, and features would appear to flicker in and out of a map that had not changed.
    /// </remarks>
    private const int MaxPoints = 5000;

    public static RouteGroupBuilder MapMapDataEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/cave-entrances", CaveEntrancesAsync)
            .WithTags("Map")
            .WithSummary("Cave entrances as GeoJSON for the given bbox; clustered at low zoom.");
        api.MapGet("/map/features", FeaturesAsync)
            .WithTags("Map")
            .WithSummary("Features as GeoJSON for the given bbox, filtered by kinds/type/category/tag; protected points snapped, other protected geometry omitted.");
        api.MapGet("/map/geofiles/{id:guid}/features", GeofileFeaturesAsync)
            .WithTags("Map")
            .WithSummary("Imported geofile rows as GeoJSON for the given bbox.");
        api.MapGet("/map/trip-logs", TripLogsAsync)
            .WithTags("Map")
            .WithSummary("Trip-log geometries as GeoJSON for the given bbox and date range.");
        api.MapGet("/map/cave-centerlines", CaveCenterlinesAsync)
            .WithTags("Map")
            .WithSummary("Cave centerlines as GeoJSON for the given bbox and zoom; splay-free below the detail zoom, protected caves' lines omitted.");
        api.MapGet("/map/config", MapConfigAsync)
            .WithTags("Map")
            .WithSummary("Client-relevant map rendering limits for this installation.");
        api.MapGet("/map/photos", PhotosAsync)
            .WithTags("Map")
            .WithSummary("Geotagged photos as GeoJSON points for the given bbox; photos touching protected features withheld.");
        return api;
    }

    /// <summary>
    /// The rendering limits the client needs in order to ask for the right thing: which zoom
    /// switches the centerline overlay to full detail, and how much it may request. Serving them
    /// rather than hard-coding them keeps a client build from disagreeing with its server.
    /// </summary>
    private static async Task<Results<Ok<MapConfigDto>, UnauthorizedHttpResult>> MapConfigAsync(
        IUserContextAccessor userAccessor,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        if (await userAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = mapOptions.Value;
        return TypedResults.Ok(new MapConfigDto(
            options.CenterlineDetailZoom,
            options.CenterlineMaxPaths,
            options.CenterlineMaxPathsLimit,
            options.CenterlineGateZoom,
            ClusterMaxZoom));
    }

    /// <summary>
    /// Geotagged image files as points. Two separate conditions, and a photo needs both: it
    /// shows only where the caller can read at least one entity it is attached to, and only
    /// where the caller may place everything that photo hangs on. The second is the shared
    /// rule about a capture point rather than this endpoint's own — the same question decides
    /// whether the stored bytes, which carry that point too, may be handed over. The point
    /// itself is the location, so snapping it is not enough; it is shown or it is not.
    /// </summary>
    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> PhotosAsync(
        string bbox,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        PhotoPositionDisclosure photos,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var polygon = box.ToPolygon();

        // The document each photo belongs to comes along, because a rule written against a
        // document decides this surface too — it publishes an id, a rendering and a delivery
        // token, which is the whole of what such a rule is written to hold back.
        var candidates = await (from file in db.StoredFiles.AsNoTracking()
                                join version in db.DocumentVersions.AsNoTracking()
                                    on file.DocumentVersionId equals version.Id
                                join document in db.Documents.AsNoTracking()
                                    on version.DocumentId equals document.Id
                                where file.Geom != null && file.Kind == FileKind.Image
                                    && file.Geom!.Intersects(polygon)
                                orderby file.Id
                                select new { file.Id, file.Geom, file.OriginalName, Document = document })
            .Take(MaxPoints)
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return TypedResults.Ok(FeatureCollection.Of([]));
        }

        var fileIds = candidates.Select(c => c.Id).ToList();
        var links = await db.Attachments.AsNoTracking()
            .Where(a => fileIds.Contains(a.FileId))
            .Select(a => new { a.FileId, a.FeatureId, a.EntityType, a.EntityId })
            .ToListAsync(ct);

        var attachedFeatureIds = links.Where(l => l.FeatureId != null)
            .Select(l => l.FeatureId!.Value).Distinct().ToList();
        var tripIds = links.Where(l => l.EntityType == AttachedEntityType.TripLog)
            .Select(l => l.EntityId!.Value).Distinct().ToList();
        var geofileIds = links.Where(l => l.EntityType == AttachedEntityType.Geofile)
            .Select(l => l.EntityId!.Value).Distinct().ToList();

        // Whether a photo's point may be shown at all is not this endpoint's rule to keep —
        // the same question is asked wherever the bytes that carry that point could be
        // handed over, and one of those two places working it out for itself is how they
        // would come to disagree about a photo.
        var disclosable = await photos.DisclosableIdsAsync(ctx, fileIds, ct);

        // Readability is separate and stays here: it is about the objects a photo hangs on
        // being readable at all, not about placing them. One pass per target world.
        var readableFeatureIds = await ReadableIdsAsync(
            db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers).Select(f => f.Id),
            attachedFeatureIds, ct);
        var readableTripIds = await ReadableIdsAsync(
            db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs).Select(t => t.Id),
            tripIds, ct);
        var readableGeofileIds = await ReadableIdsAsync(
            db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles).Select(g => g.Id),
            geofileIds, ct);

        var linksByFile = links.GroupBy(l => l.FileId).ToDictionary(g => g.Key, g => g.ToList());
        var features = new List<GeoFeature>();
        foreach (var candidate in candidates)
        {
            if (!linksByFile.TryGetValue(candidate.Id, out var fileLinks))
            {
                continue; // no attachment → no context and no visibility path
            }

            if (!disclosable.Contains(candidate.Id))
            {
                continue; // something it hangs on is guarded, and the point itself is the secret
            }

            var visible = fileLinks.Any(l => l.FeatureId is { } fid
                ? readableFeatureIds.Contains(fid)
                : l.EntityType switch
                {
                    AttachedEntityType.TripLog => readableTripIds.Contains(l.EntityId!.Value),
                    AttachedEntityType.Geofile => readableGeofileIds.Contains(l.EntityId!.Value),
                    AttachedEntityType.CavingGroup => FileAccessRules.CanReadCavingGroupTarget(ctx, l.EntityId!.Value),
                    _ => false,
                });
            if (!visible)
            {
                continue;
            }

            // Reach through a readable attachment is established by the line above, and it is
            // the weakest of the reasons a document can be reached: a rule written against the
            // document itself decides first and a deny among them is final. Asked here so this
            // map cannot publish what the cave's own document list has already withheld — the
            // fact resolved a moment ago is the only thing the rule could not fetch, so the
            // walk costs no query.
            if (!DocumentAccessRules.AllowedByOwnRulesOrAttachment(ctx, candidate.Document, AccessAction.Read))
            {
                continue;
            }

            // Reaching this line means every feature the photo inherits protection from is
            // one the caller may already place exactly — the point is being handed over in
            // the response body — so the bytes that carry the same point are no further
            // disclosure.
            var token = tokens.CreateToken(candidate.Id, FileDelivery.Full);
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

    /// <summary>
    /// Centerlines for the viewport. Which representation a cave gets depends on the zoom: the
    /// stored display skeleton at overview zooms, bbox-clipped full detail once the viewport is
    /// small enough for it to be affordable and small enough for the splays to be worth seeing.
    /// A per-request path budget and a low-zoom size gate cap the cost; whatever they exclude is
    /// reported as a count so the client can offer to zoom in.
    /// <para>
    /// Centerlines carry their cave's visibility. Rows whose ancestry the caller may not view
    /// exactly are omitted entirely and never counted — a centerline IS the cave's exact
    /// location.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<CenterlineFeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveCenterlinesAsync(
        string bbox,
        int? zoom,
        int? detailZoom,
        int? maxPaths,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var options = mapOptions.Value;
        var effectiveZoom = Math.Clamp(zoom ?? options.CenterlineDetailZoom, 0, 24);
        // Per-user overrides: a caver on a fast machine can pull detail in earlier and raise the
        // budget, someone on a phone can push both the other way. Bounded so no request can ask
        // for more work than the installation allows.
        var effectiveDetailZoom = Math.Clamp(detailZoom ?? options.CenterlineDetailZoom, 0, 24);
        var effectiveMaxPaths = Math.Clamp(
            maxPaths ?? options.CenterlineMaxPaths, 0, options.CenterlineMaxPathsLimit);

        // The protection rule is evaluated in Domain, over the centerline features whose
        // bounding box meets the viewport — an index-only lookup, so nothing large is read
        // to decide it. Only the rows that PASSED are eligible for the geometry query, so
        // nothing else has geometry produced for it: a row created or newly protected
        // between these statements is simply absent, rather than served unchecked.
        var idsInView = await CenterlineMapSql.CenterlineIdsInViewAsync(db, ctx, box, ct);
        var exactViewIds = await protection.ExactViewIdsAsync(ctx, idsInView, ct);
        var eligibleIds = idsInView.Where(exactViewIds.Contains).ToList();

        var rows = await CenterlineMapSql.QueryAsync(
            db,
            ctx,
            box,
            detail: effectiveZoom >= effectiveDetailZoom,
            maxPaths: effectiveMaxPaths,
            gateActive: effectiveZoom < options.CenterlineGateZoom,
            gatePaths: options.CenterlineGatePaths,
            simplifyToleranceDegrees: options.SimplifyToleranceDegrees(effectiveZoom),
            exactViewCenterlineIds: eligibleIds,
            ct);

        var features = new List<GeoFeature>();
        foreach (var row in rows)
        {
            if (!row.Included || row.GeoJson is null)
            {
                continue;
            }

            // PostGIS already produced the GeoJSON; only the coordinate array is lifted out of
            // it, so no coordinate is ever materialised as an object on this path.
            using var document = JsonDocument.Parse(row.GeoJson);
            var geometry = new GeoJsonGeometry(
                document.RootElement.GetProperty("type").GetString() ?? "MultiLineString",
                document.RootElement.GetProperty("coordinates").Clone());
            features.Add(new GeoFeature("Feature", geometry, new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["caveId"] = row.CaveId,
                ["name"] = row.Name,
                ["lengthM"] = row.LengthM,
                ["paths"] = row.Paths,
                ["detail"] = row.Detail,
            }));
        }

        // What was actually served, not what was asked for: a cave whose full detail would not
        // fit the budget is sent as its skeleton instead, and saying "full detail" then would be
        // a lie the user can see through.
        return TypedResults.Ok(new CenterlineFeatureCollection(
            "FeatureCollection",
            features,
            rows.Count(r => r.Withheld),
            rows.Any(r => r.Included && r.Detail)));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> TripLogsAsync(
        string bbox,
        DateOnly? from,
        DateOnly? to,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var polygon = box.ToPolygon();
        var query = db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(x => x.Geom != null && x.Geom.Intersects(polygon));

        if (from is not null)
        {
            query = query.Where(x => x.TripDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.TripDate <= to);
        }

        var rows = await query.OrderBy(x => x.Id).Take(MaxPoints).ToListAsync(ct);
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
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Rows inherit the geofile's access; an unreadable geofile is not disclosed.
        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !(await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed)
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
            .OrderBy(f => f.Id)
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

    /// <summary>
    /// The cross-kind feature layer: one bbox query over the feature supertype, serving the
    /// data-driven kinds (and entrances too when <paramref name="kinds"/> asks) with
    /// data-level filters. Centerlines are never served here — their multi-megabyte
    /// geometry has its own budgeted overlay endpoint — and caves' representative points
    /// ride the dedicated entrance layer. Protected rows obfuscate by geometry class:
    /// points snap to the protection grid, anything else keeps its properties but loses
    /// its geometry (extended geometry cannot be safely snapped).
    /// </summary>
    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> FeaturesAsync(
        string bbox,
        string? kinds,
        long? featureTypeId,
        string? category,
        string? tag,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        if (!TryParseKinds(kinds, out var kindFilter))
        {
            return ApiProblems.BadRequest(
                "map.invalid_kinds",
                "kinds accepts a comma-separated subset of 'generic' and 'caveEntrance'.");
        }

        FeatureCategory? categoryFilter = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<FeatureCategory>(category, ignoreCase: true, out var parsedCategory)
                || !Enum.IsDefined(parsedCategory))
            {
                return ApiProblems.BadRequest("map.invalid_category", $"Unknown category '{category}'.");
            }

            categoryFilter = parsedCategory;
        }

        var polygon = box.ToPolygon();
        var query = db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => kindFilter.Contains(f.Kind) && f.Geom != null && f.Geom!.Intersects(polygon));

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        if (categoryFilter is not null)
        {
            query = query.Where(f => f.Category == categoryFilter);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        var rows = await query
            .Select(f => new { f.Id, f.Kind, f.Name, f.Geom, f.FeatureTypeId, f.IsProtectedEffective })
            .OrderBy(f => f.Id)
            .Take(MaxPoints)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return TypedResults.Ok(FeatureCollection.Of([]));
        }

        var typeIds = rows.Where(r => r.FeatureTypeId != null)
            .Select(r => r.FeatureTypeId!.Value).Distinct().ToList();
        var types = typeIds.Count == 0
            ? []
            : await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Code, t.SymbolFile })
                .ToListAsync(ct);
        var typesById = types.ToDictionary(t => t.Id);

        // Only protected rows need the per-row exact-view evaluation; unprotected rows are
        // exact by definition (no protected root exists to veto).
        var exactViewIds = await protection.ExactViewIdsAsync(
            ctx, rows.Where(r => r.IsProtectedEffective).Select(r => r.Id).ToList(), ct);

        var gridMeters = accessOptions.Value.LocationGridMeters;
        var features = new List<GeoFeature>(rows.Count);
        foreach (var row in rows)
        {
            var type = row.FeatureTypeId is { } tid ? typesById.GetValueOrDefault(tid) : null;
            var exact = !row.IsProtectedEffective || exactViewIds.Contains(row.Id);
            var properties = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["name"] = row.Name,
                ["kind"] = KindName(row.Kind),
                ["typeCode"] = type?.Code,
                ["symbol"] = type?.SymbolFile,
                ["protected"] = row.IsProtectedEffective,
                ["approximate"] = !exact,
            };

            if (exact)
            {
                features.Add(GeoFeature.Of(row.Geom!, properties));
            }
            else if (row.Geom is Point point)
            {
                features.Add(GeoFeature.Of(LocationProtection.Snap(point, gridMeters), properties));
            }
            else
            {
                // Lines, polygons and Multi* cannot be safely snapped — the row stays (it is
                // readable) but its geometry is withheld. RFC 7946 allows a null geometry.
                features.Add(new GeoFeature("Feature", null!, properties));
            }
        }

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveEntrancesAsync(
        string bbox,
        int? zoom,
        string? tag,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var effectiveZoom = Math.Clamp(zoom ?? 14, 0, 24);
        return effectiveZoom < ClusterMaxZoom
            ? TypedResults.Ok(await MapSql.ClustersAsync(db, ctx, box, effectiveZoom, accessOptions.Value.LocationGridMeters, tag, ct))
            : TypedResults.Ok(await PointsAsync(db, protection, ctx, box, accessOptions.Value.LocationGridMeters, tag, ct));
    }

    private static async Task<FeatureCollection> PointsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Bbox box,
        double gridMeters,
        string? tag,
        CancellationToken ct)
    {
        var polygon = box.ToPolygon();

        // The entrance's own row decides nothing alone anymore: visibility cascades at
        // read time over the ancestor chain, so a private entrance row under a readable
        // cave is served — the filter's inheritance arm does that work.
        var query = db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.CaveEntrance && f.Geom!.Intersects(polygon));

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        var rows = await query
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.Geom,
                f.IsProtectedEffective,
                CaveId = f.Entrance!.CaveFeatureId,
                f.Entrance!.IsMain,
            })
            .OrderBy(f => f.Id)
            .Take(MaxPoints)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return FeatureCollection.Of([]);
        }

        var exactViewIds = await protection.ExactViewIdsAsync(
            ctx, rows.Where(r => r.IsProtectedEffective).Select(r => r.Id).ToList(), ct);

        // Cave names resolve through the visibility filter: an entrance readable through
        // its own grant must not disclose the name of a cave the caller cannot read.
        var caveIds = rows.Select(r => r.CaveId).Distinct().ToList();
        var caveNames = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => caveIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var features = rows.Select(row =>
        {
            var exact = !row.IsProtectedEffective || exactViewIds.Contains(row.Id);
            var point = (Point)row.Geom!;
            var geom = exact ? point : LocationProtection.Snap(point, gridMeters);
            var caveName = caveNames.GetValueOrDefault(row.CaveId);
            return GeoFeature.Of(geom, new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["caveId"] = row.CaveId,
                ["name"] = row.Name ?? caveName,
                ["caveName"] = caveName,
                ["isMain"] = row.IsMain,
                ["protected"] = row.IsProtectedEffective,
                ["approximate"] = !exact,
            });
        }).ToList();

        return FeatureCollection.Of(features);
    }

    /// <summary>
    /// Parses the cross-kind layer's kind filter. Absent means the data-driven kinds only.
    /// Centerlines are rejected (their geometry needs the budgeted overlay endpoint) and
    /// caves are rejected too (their representative point is the main entrance, which the
    /// entrance layer already serves — accepting them here would duplicate every cave).
    /// </summary>
    private static bool TryParseKinds(string? kinds, out FeatureKind[] parsed)
    {
        if (string.IsNullOrWhiteSpace(kinds))
        {
            parsed = [FeatureKind.Generic];
            return true;
        }

        var result = new List<FeatureKind>();
        foreach (var token in kinds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<FeatureKind>(token, ignoreCase: true, out var kind)
                || kind is not (FeatureKind.Generic or FeatureKind.CaveEntrance))
            {
                parsed = [];
                return false;
            }

            if (!result.Contains(kind))
            {
                result.Add(kind);
            }
        }

        parsed = [.. result];
        return parsed.Length > 0;
    }

    /// <summary>Stable camel-case kind names for map payload properties.</summary>
    private static string KindName(FeatureKind kind) => kind switch
    {
        FeatureKind.Generic => "generic",
        FeatureKind.Cave => "cave",
        FeatureKind.CaveEntrance => "caveEntrance",
        FeatureKind.Centerline => "centerline",
        _ => kind.ToString(),
    };
}
