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
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Export;

/// <summary>
/// Streaming file exports. Every path is visibility-filtered and every coordinate leaves the
/// server through the exact-location check — an export file is as sensitive as a map
/// endpoint, and unlike a map it is kept.
/// </summary>
public static class ExportEndpoints
{
    public static RouteGroupBuilder MapExportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/export/caves", ExportCavesAsync)
            .WithTags("Export")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Caves (main entrance points) as GeoJSON/GPX/KML/CSV/zipped shapefile. Takes the "
                + "cave list's `filter` or `savedFilterId`, so the file holds the rows the list shows "
                + "(those with a position).");
        api.MapGet("/export/features", ExportFeaturesAsync)
            .WithTags("Export")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Features of any kind as GeoJSON/GPX/KML/CSV/zipped shapefile. Takes the feature "
                + "list's `filter` or `savedFilterId` (and its `search` and `bbox`), so the file holds "
                + "the rows the list shows that have a position the caller may be given.");
        api.MapGet("/geofiles/export", ExportGeofileListAsync)
            .WithTags("Export")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "The imported rows of every geofile the geodata list shows, as one GeoJSON/GPX/KML/CSV/"
                + "zipped shapefile layer, each row naming the file it came from in a `geofile` "
                + "attribute. Takes the list's `filter` or `savedFilterId` and its `search`, so the file "
                + "holds the files the list shows. Refused, not cut short, when the selection is larger "
                + "than one file may hold.");
        api.MapGet("/geofiles/{id:guid}/export", ExportGeofileAsync)
            .WithTags("Export")
            .WithSummary("Imported geofile rows re-exported in the requested format.");
        api.MapKarstLinkExportEndpoints();
        return api;
    }

    private static readonly Dictionary<string, (ExportFormat Format, string ContentType, string Extension)> Formats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["geojson"] = (ExportFormat.GeoJson, "application/geo+json", "geojson"),
            ["gpx"] = (ExportFormat.Gpx, "application/gpx+xml", "gpx"),
            ["kml"] = (ExportFormat.Kml, "application/vnd.google-earth.kml+xml", "kml"),
            ["csv"] = (ExportFormat.Csv, "text/csv", "csv"),
            ["shapefile"] = (ExportFormat.ShapefileZip, "application/zip", "zip"),
        };

    /// <summary>Flat row for the cave export; the geometry is the feature's main-entrance point cache.</summary>
    private sealed record CaveExportRow(
        Guid Id,
        string? Name,
        Geometry Geom,
        string? IdentificationCode,
        long CaveTypeId,
        string? Region,
        decimal? SurveyedLength,
        decimal? Depth,
        decimal? Altitude,
        int EntranceCount);

    /// <summary>Flat row for the generic feature export.</summary>
    private sealed record FeatureExportRow(
        Guid Id,
        FeatureKind Kind,
        string? Name,
        string? Description,
        long? FeatureTypeId,
        Geometry Geom,
        string Properties);

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportCavesAsync(
        string format,
        long? caveTypeId,
        string? region,
        string? search,
        string? bbox,
        string? filter,
        Guid? savedFilterId,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        FeatureProtection protection,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> access,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        // A box that is not a window is refused rather than read as "no window", which would
        // write every readable cave into a file asked for as one part of the map.
        if (WindowProblems.Optional(bbox, out var window) is { } windowProblem)
        {
            return windowProblem;
        }

        var narrowing = await surfaceFilters.ForAsync<Feature>(
            ctx, FeatureFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // The same narrowing the cave list applies, through the same seam and after the same
        // visibility walk, so a file asked for from the list holds exactly the caves the list
        // shows — less those with no position at all, which a vector row cannot carry.
        var query = narrowing.Narrowing!.Apply(
            db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => f.Kind == FeatureKind.Cave && f.Geom != null && f.Cave != null));

        if (caveTypeId is not null)
        {
            query = query.Where(f => f.Cave!.CaveTypeId == caveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(CaveListText.RegionContains(region));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(CaveListText.NameOrToponymContains(search));
        }

        // The list's own window rule, so the file holds the caves the windowed list shows: a cave
        // is in the box when any point of it the caller is shown is. Its row still carries the
        // cave's own point, which may lie outside the box when a secondary entrance put it inside.
        if (window is { } box)
        {
            query = await CaveWindow.NarrowAsync(query, db, ctx, box, access.Value.LocationGridMeters, ct);
        }

        var caveTypes = await db.CaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        var rows = await query
            .OrderBy(f => f.Name)
            .Select(f => new CaveExportRow(
                f.Id,
                f.Name,
                f.Geom!,
                f.Cave!.IdentificationCode,
                f.Cave.CaveTypeId,
                f.Cave.Region,
                f.Cave.SurveyedLength,
                f.Cave.Depth,
                f.Cave.Altitude,
                f.Cave.EntranceCount))
            .ToListAsync(ct);

        var gridMeters = access.Value.LocationGridMeters;
        var exactIds = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(r => r.Id)], ct);

        var features = new List<VectorFeature>(rows.Count);
        var snapped = new List<Guid>();
        var withheld = new List<Guid>();
        foreach (var row in rows)
        {
            var exact = exactIds.Contains(row.Id);
            var geom = row.Geom;

            if (!exact)
            {
                // A cave's geometry is its main-entrance point cache (a POINT by schema
                // constraint), so the grid snap is the normal path here. Anything that is not a
                // point cannot be snapped without leaking shape, and an export must never carry
                // a precise coordinate the caller may not see — so such a row is dropped rather
                // than degraded.
                if (geom is not Point point)
                {
                    withheld.Add(row.Id);
                    continue;
                }

                geom = LocationProtection.Snap(point, gridMeters);
                snapped.Add(row.Id);
            }

            features.Add(new VectorFeature(geom, new Dictionary<string, object?>
            {
                // First, so it is the column a consumer joins on; the record's own id, which is
                // not a position and says nothing the caller could not read from the list.
                ["id"] = row.Id.ToString(),
                ["name"] = row.Name,
                ["code"] = row.IdentificationCode,
                ["cave_type"] = caveTypes.GetValueOrDefault(row.CaveTypeId),
                ["region"] = row.Region,
                ["surveyed_length_m"] = row.SurveyedLength is null ? null : (double)row.SurveyedLength,
                ["depth_m"] = row.Depth is null ? null : (double)row.Depth,
                // An altitude is a coordinate: beside a snapped point it narrows the grid
                // cell to wherever its contour crosses it. It travels only with an exact
                // position, under the same rule the cave's own record applies.
                ["altitude_m"] = exact && row.Altitude is not null ? (double)row.Altitude : null,
                ["entrances"] = row.EntranceCount,
                // Approximate flag travels with obfuscated coordinates so consumers cannot
                // mistake a snapped grid point for a surveyed location.
                ["approximate"] = exact ? null : "yes",
            }));
        }

        await VectorExportAudit.RecordAsync(
            db, currentUser, "caves", format, features.Count, snapped, withheld,
            savedFilterId, !string.IsNullOrWhiteSpace(filter), null, ct);

        return WriteFile(vectorIO, target, "caves", features);
    }

    /// <summary>
    /// Applies a bounding box so that membership follows the point the file will publish.
    ///
    /// <para>
    /// These exports place a feature the caller may not see exactly on the protection grid, so
    /// the honest box test is against that same snapped point: the row is in the file exactly
    /// when the point printed in the file is inside the box. Tested against the stored
    /// coordinate instead, the box becomes a probe — membership flips when a caller-chosen edge
    /// crosses the true position, which bisects a protected cave onto the map to any precision,
    /// while the snapped point moves only in whole grid steps and gives nothing finer away.
    /// Exactly-placeable rows are narrowed by their true geometry, as ever: for them position is
    /// not a secret.
    /// </para>
    /// </summary>
    private static async Task<IQueryable<Feature>> NarrowedToBoxAsync(
        IQueryable<Feature> query,
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        double gridMeters,
        IReadOnlyCollection<FeatureKind>? kinds,
        CancellationToken ct)
    {
        // Which protected rows are inside is the window-membership rule's question, asked of its
        // single home: by the snapped point for a point the caller may not place, nowhere for a
        // line or polygon (it is dropped from the file below anyway), and by the stored geometry
        // for what the caller may place.
        var polygon = box.ToPolygon();
        var protectedMembers = await WindowMembershipSql.ProtectedMembersAsync(
            db, ctx, box.West, box.South, box.East, box.North, gridMeters, kinds, ct);

        return query.Where(f =>
            (!f.IsProtectedEffective && f.Geom!.Intersects(polygon))
            || protectedMembers.Contains(f.Id));
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportFeaturesAsync(
        string format,
        string? search,
        string? bbox,
        string? filter,
        Guid? savedFilterId,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        FeatureProtection protection,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> access,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        var narrowing = await surfaceFilters.ForAsync<Feature>(
            ctx, FeatureFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        if (WindowProblems.Optional(bbox, out var window) is { } windowProblem)
        {
            return windowProblem;
        }

        // The same narrowing the feature list applies, through the same seam and after the same
        // visibility walk, so a file asked for from the list holds the rows the list shows — less
        // those with no position at all, which a vector row cannot carry, and those the protection
        // rule below keeps out of a file.
        var query = narrowing.Narrowing!.Apply(
            db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => f.Geom != null));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(f =>
                f.Name != null && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)));
        }

        if (window is { } box)
        {
            query = await NarrowedToBoxAsync(
                query, db, ctx, box, access.Value.LocationGridMeters, null, ct);
        }

        var featureTypes = await db.FeatureTypes.AsNoTracking()
            .Select(t => new { t.Id, t.Code, t.ProtectedDisplay })
            .ToDictionaryAsync(t => t.Id, t => t, ct);

        var rows = await query
            .OrderBy(f => f.Name)
            .Select(f => new FeatureExportRow(
                f.Id, f.Kind, f.Name, f.Description, f.FeatureTypeId, f.Geom!, f.Properties))
            .ToListAsync(ct);

        var gridMeters = access.Value.LocationGridMeters;
        var exactIds = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(r => r.Id)], ct);

        var features = new List<VectorFeature>(rows.Count);
        var snapped = new List<Guid>();
        var withheld = new List<Guid>();
        foreach (var row in rows)
        {
            var type = row.FeatureTypeId is null ? null : featureTypes.GetValueOrDefault(row.FeatureTypeId.Value);
            var exact = exactIds.Contains(row.Id);
            var geom = row.Geom;

            if (!exact)
            {
                // Kinds configured to disappear under protection are dropped whatever their
                // geometry; everything else may leave only as a snapped point, because lines,
                // polygons and multi-part geometries disclose shape and extent however coarse
                // the grid. Dropping the row is the only safe degradation — an export must
                // never carry a precise coordinate the caller may not see.
                if (FeatureDisplay.PolicyOf(row.Kind, type?.ProtectedDisplay) == ProtectedDisplay.Withhold
                    || geom is not Point point)
                {
                    withheld.Add(row.Id);
                    continue;
                }

                geom = LocationProtection.Snap(point, gridMeters);
                snapped.Add(row.Id);
            }

            var properties = new Dictionary<string, object?>
            {
                ["id"] = row.Id.ToString(),
                ["name"] = row.Name,
                ["kind"] = KindCode(row.Kind),
                ["feature_type"] = type?.Code,
                ["desc"] = row.Description,
            };

            // Typed jsonb properties are flattened next to the fixed columns.
            var extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Properties);
            foreach (var (key, value) in extra ?? [])
            {
                properties.TryAdd(key, value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetDouble(),
                    JsonValueKind.String => value.GetString(),
                    _ => value.ToString(),
                });
            }

            // Assigned after the flattening so a typed property of the same name can never
            // shadow the protection flag, nor the id a consumer joins the file back on.
            properties["id"] = row.Id.ToString();
            properties["approximate"] = exact ? null : "yes";

            features.Add(new VectorFeature(geom, properties));
        }

        await VectorExportAudit.RecordAsync(
            db, currentUser, "features", format, features.Count, snapped, withheld,
            savedFilterId, !string.IsNullOrWhiteSpace(filter), null, ct);

        return WriteFile(vectorIO, target, "features", features);
    }

    /// <summary>Most geofiles one list export may name; a larger selection is refused, never truncated.</summary>
    public const int MaxExportedGeofiles = 200;

    /// <summary>Most imported rows one list export may hold, summed over the selected files.</summary>
    public const int MaxExportedGeofileRows = 200_000;

    public const string TooManyGeofilesCode = "export.too_many_geofiles";

    /// <summary>
    /// The geodata list as a file: the imported rows of the geofiles the list's own filter leaves.
    /// </summary>
    /// <remarks>
    /// The files are chosen exactly as the list chooses them — the visibility walk first, then the
    /// geofile world's narrowing, then the name search — so a file the list withholds from this
    /// caller can neither appear nor be counted. The rows of a chosen file are then exported as the
    /// single-file route exports them, which asks for nothing beyond reading the file.
    /// </remarks>
    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportGeofileListAsync(
        string format,
        string? search,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        CancellationToken ct,
        string? filter = null,
        Guid? savedFilterId = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        var narrowing = await surfaceFilters.ForAsync<Geofile>(ctx, GeofileFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        var files = narrowing.Narrowing!.Apply(db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            files = files.Where(g => EF.Functions.ILike(EF.Functions.Unaccent(g.Name), EF.Functions.Unaccent(pattern)));
        }

        var chosen = await files
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new { g.Id, g.Name, g.FeatureCount })
            .Take(MaxExportedGeofiles + 1)
            .ToListAsync(ct);
        if (chosen.Count > MaxExportedGeofiles || chosen.Sum(g => (long)g.FeatureCount) > MaxExportedGeofileRows)
        {
            return ApiProblems.BadRequest(
                TooManyGeofilesCode,
                "This request selects more geofile rows than one file may hold. Narrow it and export "
                + "in parts; a file that silently stopped at a limit would disagree with the list for "
                + "a reason nothing in it explains.",
                "limit",
                MaxExportedGeofiles);
        }

        var features = new List<VectorFeature>();
        foreach (var file in chosen)
        {
            var rows = await db.GeofileFeatures.AsNoTracking()
                .Where(f => f.GeofileId == file.Id)
                .OrderBy(f => f.Id)
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Properties) ?? [];
                var converted = properties.ToDictionary(
                    p => p.Key,
                    p => (object?)(p.Value.ValueKind switch
                    {
                        JsonValueKind.Number => p.Value.GetDouble(),
                        JsonValueKind.String => p.Value.GetString(),
                        _ => p.Value.ToString(),
                    }));

                // Assigned after the flattening so a column of the same name in the source file can
                // never shadow which file a row came from.
                converted["geofile"] = file.Name;
                features.Add(new VectorFeature(row.Geom, converted));
            }
        }

        // Imported rows carry no protected position, so the audit lists the files taken (ids, in
        // the order the file names them) rather than treatments.
        await VectorExportAudit.RecordAsync(
            db, currentUser, "geofiles", format, features.Count, [], [],
            savedFilterId, !string.IsNullOrWhiteSpace(filter),
            new Dictionary<string, object?>
            {
                ["geofileIds"] = chosen.Select(g => g.Id.ToString()).ToList(),
            },
            ct);

        return WriteFile(vectorIO, target, "geofiles", features);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportGeofileAsync(
        Guid id,
        string format,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !(await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        var rows = await db.GeofileFeatures.AsNoTracking()
            .Where(f => f.GeofileId == id)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);

        var features = rows.Select(row =>
        {
            var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Properties) ?? [];
            var converted = properties.ToDictionary(
                p => p.Key,
                p => (object?)(p.Value.ValueKind switch
                {
                    JsonValueKind.Number => p.Value.GetDouble(),
                    JsonValueKind.String => p.Value.GetString(),
                    _ => p.Value.ToString(),
                }));
            return new VectorFeature(row.Geom, converted);
        }).ToList();

        await VectorExportAudit.RecordAsync(
            db, currentUser, "geofile", format, features.Count, [], [], null, false,
            new Dictionary<string, object?> { ["geofileIds"] = new[] { geofile.Id.ToString() } },
            ct);

        var layerName = string.Concat(geofile.Name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return WriteFile(vectorIO, target, layerName.Length == 0 ? "geofile" : layerName, features);
    }

    /// <summary>Stable attribute value naming a row's kind — the export mixes kinds in one layer.</summary>
    private static string KindCode(FeatureKind kind) => kind switch
    {
        FeatureKind.Cave => "cave",
        FeatureKind.CaveEntrance => "cave_entrance",
        FeatureKind.Centerline => "centerline",
        _ => "generic",
    };

    private static Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult> WriteFile(
        IVectorIO vectorIO,
        (ExportFormat Format, string ContentType, string Extension) target,
        string layerName,
        IReadOnlyList<VectorFeature> features)
    {
        var bytes = vectorIO.Write(target.Format, layerName, features);
        var fileName = $"{layerName}-{DateTime.UtcNow:yyyyMMdd}.{target.Extension}";
        return TypedResults.File(bytes, target.ContentType, fileName);
    }

    private static ProblemHttpResult UnsupportedFormat(string format) =>
        ApiProblems.BadRequest(
            "export.format_unsupported",
            $"Unsupported export format '{format}'. Supported: {string.Join(", ", Formats.Keys)}.");
}
