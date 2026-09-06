// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
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
using SilexGis.Domain.Import;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// A GeoJSON FeatureCollection plus the three things the centerline overlay needs to explain
/// itself: how many centerlines the request's limits kept back, whether what it did return is
/// full detail or the splay-free skeleton, and — for a caller that asked for altitudes — how
/// many of the served centerlines could not carry any. All are GeoJSON foreign members, so a
/// plain GeoJSON reader still parses the collection.
/// </summary>
public sealed record CenterlineFeatureCollection(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    int WithheldCount,
    bool Detail,
    int FlatCount);

/// <summary>
/// A GeoJSON FeatureCollection of trip geometries, plus the two things a reader cannot work out
/// from the features alone: whether the request stopped at its cap, and how many trips in the
/// date window have no position at all. Both are GeoJSON foreign members, so a plain GeoJSON
/// reader still parses the collection.
/// </summary>
/// <param name="Truncated">
/// The request reached the per-request row cap and there are trips it did not answer with.
/// Counted in <em>trips</em> and never in features, because one trip states up to two shapes of
/// its own: a client comparing a feature count against the published cap would call a complete
/// answer truncated as soon as a single trip stated both of them.
/// </param>
/// <param name="UnlocatedCount">
/// Trips in the window that state no position of their own and name no cave this caller may both
/// read and place — so they are on no map at any viewport. Reported rather than dropped: a map
/// that silently omits them tells a reader the window holds fewer trips than it does, and the
/// honest answer to "where was this one" is that nobody recorded it. A window figure and not a
/// viewport one, because a trip with no position is in no viewport by definition.
/// </param>
public sealed record TripLogFeatureCollection(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    bool Truncated,
    int UnlocatedCount);

/// <summary>
/// The elevation model the 3D scene should draw its ground from, when this installation has one.
/// Absent — not an empty object — when it does not, which is the shipped state.
/// </summary>
/// <param name="Url">Where the tile pyramid is served from; <c>layer.json</c> sits directly under it.</param>
/// <param name="Attribution">Credit line the elevation data's licence requires, if any.</param>
/// <param name="SurveyHeightOffsetM">
/// Metres to add to a surveyed altitude before drawing it against this source's ground. Resolved
/// from the source's declared vertical datum on the server, so the client is handed an answer
/// rather than a datum it could apply backwards, and so swapping the source cannot leave a stale
/// correction behind. Zero for a source whose heights are already the kind a survey carries.
/// </param>
public sealed record TerrainSourceDto(string Url, string? Attribution, double SurveyHeightOffsetM);

/// <summary>Map rendering limits published to the client.</summary>
/// <param name="MaxPoints">
/// The most features any one map layer request answers with. Published rather than kept to the
/// server because a layer that stopped at a limit and a layer that ended looks identical on
/// screen, and the client is the only place that can say which happened.
/// </param>
/// <param name="TerrainFallback">
/// The elevation model to try when <paramref name="Terrain"/> cannot be drawn, or nothing when
/// there is no second one. Only ever set when the first came from configuration and a checked
/// build exists at a different address; see the resolver for why a second answer is needed at all.
/// </param>
public sealed record MapConfigDto(
    int CenterlineDetailZoom,
    int CenterlineMaxPaths,
    int CenterlineMaxPathsLimit,
    int CenterlineGateZoom,
    int ClusterMaxZoom,
    int MaxPoints,
    TerrainSourceDto? Terrain,
    TerrainSourceDto? TerrainFallback);

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

    // The safety cap for individual point features per request now lives in MapOptions, so that
    // an installation importing a survey of tens of thousands of waypoints can raise it without a
    // rebuild. Every query that applies it orders by id first: without an order the database is
    // free to return any rows it likes once the cap bites, and it need not pick the same ones
    // twice — so a dense viewport would show a different arbitrary subset on each pan back to it,
    // and features would appear to flicker in and out of a map that had not changed.

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
            .WithSummary("Trip-log geometries as GeoJSON for the given bbox, date range and list filters; a trip with no shape of its own is placed at a cave it names when the caller may both read and place it, and counted as unlocated otherwise.");
        api.MapGet("/map/cave-centerlines", CaveCenterlinesAsync)
            .WithTags("Map")
            .WithSummary("Cave centerlines as GeoJSON for the given bbox and zoom; splay-free below the detail zoom, protected caves' lines omitted. z=true opts in to altitudes, which the flat display skeleton cannot carry.");
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
    ///
    /// <para>
    /// The elevation model rides along here for the same reason and one more: the client is built
    /// once and deployed everywhere, so it cannot carry a terrain URL, and this is already the
    /// request the 3D view makes before it draws anything. Which model that is can change while
    /// people are looking at the scene — somebody chooses a different build — so it is resolved
    /// per request rather than settled once at startup.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<MapConfigDto>, UnauthorizedHttpResult>> MapConfigAsync(
        IUserContextAccessor userAccessor,
        IOptions<MapOptions> mapOptions,
        IOptions<TerrainOptions> terrainOptions,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        if (await userAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = mapOptions.Value;
        var (terrain, terrainFallback) = await TerrainSourceResolver.ResolveAsync(terrainOptions.Value, db, ct);
        return TypedResults.Ok(new MapConfigDto(
            options.CenterlineDetailZoom,
            options.CenterlineMaxPaths,
            options.CenterlineMaxPathsLimit,
            options.CenterlineGateZoom,
            ClusterMaxZoom,
            options.MaxPoints,
            terrain,
            terrainFallback));
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
                                select new
                                {
                                    file.Id,
                                    file.Geom,
                                    file.OriginalName,
                                    file.DirectionDegrees,
                                    file.DirectionIsMagnetic,
                                    file.PositionSource,
                                    Document = document,
                                })
            .Take(mapOptions.Value.MaxPoints)
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
        var cabinetReach = await DocumentAccessRules.CabinetReachAsync(
            db, ctx, AccessAction.Read, [.. candidates.Select(c => c.Document.Id)], ct);
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
            if (!DocumentAccessRules.AllowedByOwnRulesOrAttachment(
                    ctx, candidate.Document, AccessAction.Read, cabinetReach))
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
                // Which way the camera looked, so the layer can draw it. Carried on the same
                // terms as the point itself and for the same reason: a bearing without a
                // position says nothing, and this response only exists for a caller who may
                // already have the position.
                ["directionDegrees"] = candidate.DirectionDegrees,
                ["directionIsMagnetic"] = candidate.DirectionIsMagnetic,
                ["positionSource"] = candidate.PositionSource.ToString(),
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
    /// <para>
    /// <c>z=true</c> opts in to altitudes. It is off by default because a flat map does not use
    /// the third ordinate and paying for it would make every pan heavier for no visible gain.
    /// Altitudes are not always available: the stored display skeleton is flat by construction,
    /// so an overview row — including a detail request that fell back to the skeleton on the path
    /// budget — comes back without them. Those rows are still served rather than dropped, each
    /// one carrying <c>hasZ: false</c>, and <c>flatCount</c> totals them for a caller that would
    /// rather zoom in than draw a cave at sea level. A row that does carry altitudes also reports
    /// <c>topAltitudeM</c>, the highest altitude of the whole centerline: the served geometry is
    /// cut to the viewport, so the payload alone cannot say where the cave meets the ground, and a
    /// figure read off it would change every time the viewer panned.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<CenterlineFeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> CaveCenterlinesAsync(
        string bbox,
        int? zoom,
        int? detailZoom,
        int? maxPaths,
        bool? z,
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

        var withZ = z == true;
        var rows = await CenterlineMapSql.QueryAsync(
            db,
            ctx,
            box,
            detail: effectiveZoom >= effectiveDetailZoom,
            withZ: withZ,
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
            var properties = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["caveId"] = row.CaveId,
                ["name"] = row.Name,
                ["lengthM"] = row.LengthM,
                ["paths"] = row.Paths,
                ["detail"] = row.Detail,
            };
            if (withZ)
            {
                // Only when altitudes were asked for. A request that did not ask gets exactly the
                // payload it got before, down to the property set.
                properties["hasZ"] = row.HasZ;
                if (row.HasZ && row.TopZ is { } topZ)
                {
                    // The top of the whole survey, which the served geometry cannot be asked for:
                    // at detail zoom it is cut to the viewport, so its own highest point moves as
                    // the viewer pans. A caller drawing the survey against a surface needs a
                    // figure that holds still, and this is the only place one can come from.
                    properties["topAltitudeM"] = topZ;
                }
            }

            features.Add(new GeoFeature("Feature", geometry, properties));
        }

        // What was actually served, not what was asked for: a cave whose full detail would not
        // fit the budget is sent as its skeleton instead, and saying "full detail" then would be
        // a lie the user can see through. The flat count works the same way, and is zero unless
        // altitudes were requested — without the request every row is flat by design, so counting
        // them would say nothing.
        return TypedResults.Ok(new CenterlineFeatureCollection(
            "FeatureCollection",
            features,
            rows.Count(r => r.Withheld),
            rows.Any(r => r.Included && r.Detail),
            withZ ? rows.Count(r => r.Included && !r.HasZ) : 0));
    }

    /// <summary>
    /// A trip's position is a chain of three, and a trip takes the first link it has: the shape
    /// it drew of itself, else where its party met, else the caves its roles name. The third link
    /// exists because a trip read out of a club's historical spreadsheet has no geometry at all —
    /// the sheet records which cave, not where — so without it the whole of an imported archive is
    /// a map with nothing on it.
    ///
    /// <para>
    /// The first two links are the trip's own statements and are served exactly to exactly the
    /// readers of the trip, which is a decision taken elsewhere and pinned by its own test. The
    /// third is not the trip's to give away: it is a cave's position, so it passes the two gates
    /// that govern a cave named on a trip, in that order — may this caller read the cave at all,
    /// and may this caller place it exactly — and then the same placement question is asked again
    /// of the entrance whose coordinate is actually being handed over. A cave that fails any of
    /// them contributes nothing: no snapped point, no blurred one, no dot at all. Blurring is
    /// wrong here in a way it is not for a feature layer, because the reader is not told the point
    /// is approximate and would read a grid-snapped cave as where the trip went.
    /// </para>
    ///
    /// <para>
    /// A cave the caller may not read is refused in silence — the response says nothing about the
    /// trip having named anything — because an answer that differed from the answer for a cave
    /// that does not exist is an answer somebody can go looking for.
    /// </para>
    ///
    /// <para>
    /// Only caves derive a position, never the areas a trip names. An area is a massif, and the
    /// point at the middle of a massif is not a place anybody went; a map drawing it would show an
    /// invented position that reads exactly like a recorded one. A trip whose only geographic
    /// statement is an area is therefore unlocated, and is counted as such rather than placed.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<TripLogFeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> TripLogsAsync(
        string bbox,
        DateOnly? from,
        DateOnly? to,
        string? types,
        string? states,
        string? visibilities,
        bool? hadIncident,
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

        if (!TryParseTripFilters(types, states, visibilities, out var typeIds, out var stateWords, out var visibilityWords, out var badFilter))
        {
            return badFilter!;
        }

        var polygon = box.ToPolygon();
        var cap = mapOptions.Value.MaxPoints;

        // Every narrowing is composed into the visibility-filtered query rather than applied to
        // its results, so the counts below are counts of what this caller may read.
        var window = db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .OverlappingDays(x => x.TripDate, x => x.TripDateEnd, from, to);

        if (typeIds.Count > 0)
        {
            window = window.Where(x => x.TripTypeId != null && typeIds.Contains(x.TripTypeId.Value));
        }

        if (stateWords.Count > 0)
        {
            window = window.Where(x => stateWords.Contains(x.State));
        }

        if (visibilityWords.Count > 0)
        {
            window = window.Where(x => visibilityWords.Contains(x.Visibility));
        }

        if (hadIncident is { } incident)
        {
            window = window.Where(x => x.HadIncident == incident);
        }

        // Either position puts the trip in the window. A trip that states only where its party
        // meets is a trip somebody looking at a map wants to find — that is what the meeting
        // point is a column for — and a filter that asked about the sketch alone would leave
        // every such trip off the map with nothing to say it had been left off.
        var stated = await window
            .Where(x => (x.Geom != null && x.Geom.Intersects(polygon))
                || (x.MeetingGeom != null && x.MeetingGeom.Intersects(polygon)))
            .OrderBy(x => x.Id)
            .Take(cap)
            .ToListAsync(ct);

        // The trips with nothing of their own, over the whole window and deliberately not over
        // the viewport: a trip that turns out to have no position anywhere is in no viewport, so
        // a viewport-shaped question could never find the ones this response has to report.
        var shapeless = await window
            .Where(x => x.Geom == null && x.MeetingGeom == null)
            .OrderBy(x => x.Id)
            .Select(x => new ShapelessTrip(x.Id, x.Title, x.TripDate))
            .Take(cap)
            .ToListAsync(ct);

        // One feature per shape the trip states, each saying which it is, rather than one feature
        // carrying whichever happened to be there. The two mean different things — where the trip
        // went against where it starts — and a map that drew them as the same thing would put a
        // car park where the reader read a cave. Both are served exactly, to exactly the readers
        // of the trip, which the visibility filter above has already decided.
        //
        // Each shape is measured against the window in its own right, because the row was matched
        // if either shape fell inside it: a trip that meets at a car park in this window and works
        // a cave system a county away would otherwise answer a request for this window with that
        // distant cave system, which the caller would draw as though it were in view. A window
        // asks what is in it, and the answer holds nothing else.
        var features = new List<GeoFeature>(stated.Count);
        foreach (var x in stated)
        {
            if (x.Geom is not null && x.Geom.Intersects(polygon))
            {
                features.Add(GeoFeature.Of(x.Geom, Properties(x.Id, x.Title, x.TripDate, "sketch")));
            }

            if (x.MeetingGeom is not null && x.MeetingGeom.Intersects(polygon))
            {
                features.Add(GeoFeature.Of(x.MeetingGeom, Properties(x.Id, x.Title, x.TripDate, "meeting")));
            }
        }

        var derived = await DerivedTripPointsAsync(db, protection, ctx, [.. shapeless.Select(t => t.Id)], ct);
        var unlocated = 0;
        foreach (var trip in shapeless)
        {
            if (!derived.TryGetValue(trip.Id, out var point))
            {
                unlocated++;
                continue;
            }

            if (!point.Geom.Intersects(polygon))
            {
                continue; // located, just not here — the window's business, not this viewport's
            }

            var props = Properties(trip.Id, trip.Title, trip.TripDate, "cave");
            props["caveId"] = point.CaveId;
            props["caveName"] = point.CaveName;
            features.Add(GeoFeature.Of(point.Geom, props));
        }

        // Truncation is a count of trips and never of features: the cap is applied to rows, and
        // one row answers with up to two shapes of its own.
        var truncated = stated.Count >= cap || shapeless.Count >= cap;
        return TypedResults.Ok(new TripLogFeatureCollection("FeatureCollection", features, truncated, unlocated));

        static Dictionary<string, object?> Properties(Guid id, string title, DateOnly date, string kind) => new()
        {
            ["id"] = id,
            ["title"] = title,
            ["tripDate"] = date.ToString("O"),
            ["kind"] = kind,
        };
    }

    /// <summary>A trip that states no position of its own, and the little a map feature needs of it.</summary>
    private sealed record ShapelessTrip(Guid Id, string Title, DateOnly TripDate);

    /// <summary>Where a trip with no shape of its own sits, and which cave put it there.</summary>
    private sealed record DerivedTripPoint(Geometry Geom, Guid CaveId, string? CaveName);

    /// <summary>
    /// The position each of the given trips inherits from the caves its roles name, for trips
    /// that state none of their own. A trip absent from the result has no position this caller
    /// may be shown — which covers a trip naming no cave, a trip naming one this caller may not
    /// read, a trip naming one this caller may read but not place, and a trip naming one nobody
    /// has ever surveyed an entrance for. Those four are deliberately one answer: telling them
    /// apart is telling a caller which caves a response has refused to name.
    /// </summary>
    private static async Task<Dictionary<Guid, DerivedTripPoint>> DerivedTripPointsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<Guid> tripIds,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        // Role-agnostic, as everywhere else a trip's caves are asked for: the question is which
        // caves the trip named, not what it did in them.
        var pairs = await TripRoleLinks.PairsForAsync(db, tripIds, FeatureKind.Cave, ct);
        if (pairs.Count == 0)
        {
            return [];
        }

        // The two gates, in order and in their one home: readability of the cave, then whether
        // this caller may place it exactly. Asked once for the whole page rather than per trip.
        var disclosable = await TripCaveDisclosure.DisclosableCaveIdsAsync(
            db, protection, ctx, [.. pairs.Select(p => p.FeatureId).Distinct()], ct);
        if (disclosable.Count == 0)
        {
            return [];
        }

        var caveIds = disclosable.ToList();
        var caveNames = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        // A cave's own row carries no position; its entrances do. The entrance is where the
        // coordinate actually comes from, so the placement question is asked of it as well as of
        // the cave above — an entrance can carry protection of its own that its cave does not —
        // and one it refuses is left out entirely rather than blurred.
        var entrances = await db.Features.AsNoTracking()
            .Where(f => f.Kind == FeatureKind.CaveEntrance
                && f.Geom != null
                && caveIds.Contains(f.Entrance!.CaveFeatureId))
            .Select(f => new
            {
                f.Id,
                f.Geom,
                CaveId = f.Entrance!.CaveFeatureId,
                f.Entrance!.IsMain,
            })
            .OrderBy(f => f.Id)
            .ToListAsync(ct);
        if (entrances.Count == 0)
        {
            return [];
        }

        var exactView = await protection.ExactViewIdsAsync(ctx, [.. entrances.Select(e => e.Id)], ct);

        // One point per cave: the main entrance where one is marked, otherwise the lowest id, so
        // the same cave lands in the same place on every request rather than wandering between
        // its entrances as the database feels like ordering them.
        var caveGeom = new Dictionary<Guid, Geometry>();
        foreach (var entrance in entrances.OrderByDescending(e => e.IsMain).ThenBy(e => e.Id))
        {
            if (entrance.Geom is not Point || !exactView.Contains(entrance.Id))
            {
                continue;
            }

            caveGeom.TryAdd(entrance.CaveId, entrance.Geom);
        }

        // One dot per trip, not one per cave it named. A weekend that reached three caves is one
        // trip, and three dots would make a reader counting the map disagree with a reader
        // counting the list; the lowest cave id is picked so the choice is the same every time.
        var byTrip = new Dictionary<Guid, DerivedTripPoint>();
        foreach (var pair in pairs.OrderBy(p => p.FeatureId))
        {
            if (byTrip.ContainsKey(pair.TripId) || !caveGeom.TryGetValue(pair.FeatureId, out var geom))
            {
                continue;
            }

            byTrip[pair.TripId] = new DerivedTripPoint(geom, pair.FeatureId, caveNames.GetValueOrDefault(pair.FeatureId));
        }

        return byTrip;
    }

    /// <summary>
    /// The trip layer's narrowings, in the same comma-separated spelling the trip listing uses so
    /// a filter carried from the list to the map survives the journey unchanged. A word this
    /// application does not have is refused rather than dropped: a layer that quietly ignored half
    /// a filter would draw an answer to a question nobody asked.
    /// </summary>
    private static bool TryParseTripFilters(
        string? types,
        string? states,
        string? visibilities,
        out List<long> typeIds,
        out List<ActivityState> stateWords,
        out List<Visibility> visibilityWords,
        out ProblemHttpResult? problem)
    {
        typeIds = [];
        stateWords = [];
        visibilityWords = [];
        problem = null;

        foreach (var word in SplitFilter(types))
        {
            if (!long.TryParse(word, out var typeId))
            {
                problem = ApiProblems.BadRequest("map.invalid_trip_filter", $"Unknown trip type '{word}'.");
                return false;
            }

            typeIds.Add(typeId);
        }

        foreach (var word in SplitFilter(states))
        {
            if (!Enum.TryParse<ActivityState>(word, ignoreCase: true, out var state)
                || !Enum.IsDefined(state)
                || !ActivityStates.IsTripLogState(state))
            {
                problem = ApiProblems.BadRequest("map.invalid_trip_filter", $"Unknown state '{word}'.");
                return false;
            }

            stateWords.Add(state);
        }

        foreach (var word in SplitFilter(visibilities))
        {
            if (!Enum.TryParse<Visibility>(word, ignoreCase: true, out var visibility) || !Enum.IsDefined(visibility))
            {
                problem = ApiProblems.BadRequest("map.invalid_trip_filter", $"Unknown visibility '{word}'.");
                return false;
            }

            visibilityWords.Add(visibility);
        }

        typeIds = [.. typeIds.Distinct()];
        stateWords = [.. stateWords.Distinct()];
        visibilityWords = [.. visibilityWords.Distinct()];
        return true;
    }

    /// <summary>
    /// One facet's chosen words. Blank entries are dropped rather than refused, because a control
    /// that clears its last choice by leaving a trailing comma has not made a mistake.
    /// </summary>
    private static IEnumerable<string> SplitFilter(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> GeofileFeaturesAsync(
        Guid id,
        string bbox,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IOptions<MapOptions> mapOptions,
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
            .Take(mapOptions.Value.MaxPoints)
            .ToListAsync(ct);

        var features = rows.Select(f =>
        {
            var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(f.Properties)
                ?? [];
            properties["id"] = f.Id;

            // What to call this row on screen, decided HERE rather than in the browser. The rule
            // for which of a source's columns is its name is a domain rule with one home — a GPX
            // writes `name`, a shapefile abbreviates to ten characters, a spreadsheet is whatever
            // somebody typed, and the folded-key search that copes with all three already exists.
            // A second copy of it in the client would drift, and the way it would show is one view
            // labelling a waypoint by its comment while another labels it by its name.
            var attributes = SourceAttributeReader.Read(AsText(properties), new ImportAttributeMapping());
            if (attributes.Name is { } label)
            {
                properties[GeofileLabelProperty] = label;
            }

            if (attributes.Description is { } description)
            {
                properties[GeofileDescriptionProperty] = description;
            }

            if (attributes.Elevation is { } elevation)
            {
                properties[GeofileElevationProperty] = elevation;
            }

            return GeoFeature.Of(f.Geom, properties);
        }).ToList();

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    /// <summary>
    /// Where the resolved name, description and altitude of an imported row are carried, alongside
    /// the row's own untouched columns.
    /// </summary>
    /// <remarks>
    /// Prefixed, because the row's own columns are spread into the same object and a source with a
    /// column called <c>label</c> is not hypothetical. A viewer looking at the popup sees the
    /// original columns exactly as the file had them; these three are what the map itself reads.
    /// </remarks>
    public const string GeofileLabelProperty = "silexgis:label";

    /// <inheritdoc cref="GeofileLabelProperty"/>
    public const string GeofileDescriptionProperty = "silexgis:description";

    /// <inheritdoc cref="GeofileLabelProperty"/>
    public const string GeofileElevationProperty = "silexgis:elevationM";

    /// <summary>
    /// A row's columns as text, which is the form the attribute reader answers about.
    /// </summary>
    /// <remarks>
    /// Numbers and booleans are stringified rather than dropped: an altitude column holding 812
    /// arrives as a JSON number, and a reader that only looked at strings would decide the row has
    /// no altitude at all.
    /// </remarks>
    private static Dictionary<string, string?> AsText(Dictionary<string, object?> properties)
    {
        var text = new Dictionary<string, string?>(properties.Count, StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            text[key] = value switch
            {
                null => null,
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
                JsonElement element => element.ToString(),
                _ => value.ToString(),
            };
        }

        return text;
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
            .Take(mapOptions.Value.MaxPoints)
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

        var effectiveZoom = Math.Clamp(zoom ?? 14, 0, 24);
        return effectiveZoom < ClusterMaxZoom
            ? TypedResults.Ok(await MapSql.ClustersAsync(db, ctx, box, effectiveZoom, accessOptions.Value.LocationGridMeters, tag, ct))
            : TypedResults.Ok(await PointsAsync(
                db, protection, ctx, box, accessOptions.Value.LocationGridMeters, tag, mapOptions.Value.MaxPoints, ct));
    }

    private static async Task<FeatureCollection> PointsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Bbox box,
        double gridMeters,
        string? tag,
        int maxPoints,
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
            .Take(maxPoints)
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
