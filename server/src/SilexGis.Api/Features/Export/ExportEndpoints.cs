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
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Export;

/// <summary>
/// Streaming file exports. Every path is visibility-filtered and protected cave
/// locations leave the server obfuscated — exports are as sensitive as map endpoints.
/// </summary>
public static class ExportEndpoints
{
    public static RouteGroupBuilder MapExportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/export/caves", ExportCavesAsync)
            .WithTags("Export")
            .WithSummary("Caves (main entrance points) as GeoJSON/GPX/KML/CSV/zipped shapefile.");
        api.MapGet("/export/surface-features", ExportSurfaceFeaturesAsync)
            .WithTags("Export")
            .WithSummary("Surface features as GeoJSON/GPX/KML/CSV/zipped shapefile.");
        api.MapGet("/geofiles/{id:guid}/export", ExportGeofileAsync)
            .WithTags("Export")
            .WithSummary("Imported geofile rows re-exported in the requested format.");
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

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportCavesAsync(
        string format,
        long? caveTypeId,
        string? region,
        string? search,
        string? bbox,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        AclPermissionService permissions,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        // Same filter surface as the caves list; only caves with a main entrance
        // geometry can be exported as vector rows.
        var query = db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave).Where(c => c.MainGeom != null);

        if (caveTypeId is not null)
        {
            query = query.Where(c => c.CaveTypeId == caveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(c => c.Region == region);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.Name), EF.Functions.Unaccent(pattern)));
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(c => c.MainGeom!.Intersects(polygon));
        }

        var caveTypes = await db.CaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        var rows = await query.OrderBy(c => c.Name).ToListAsync(ct);

        var gridMeters = access.Value.LocationGridMeters;
        var exactGrants = await permissions.CaveExactLocationGrantsAsync(user, ct);
        var features = rows.Select(cave =>
        {
            var exact = LocationProtection.CanViewExactLocation(
                user, cave, exactGrants.Contains(cave.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None);
            var geom = exact ? cave.MainGeom! : LocationProtection.Snap(cave.MainGeom!, gridMeters);
            return new VectorFeature(geom, new Dictionary<string, object?>
            {
                ["name"] = cave.Name,
                ["code"] = cave.IdentificationCode,
                ["cave_type"] = caveTypes.GetValueOrDefault(cave.CaveTypeId),
                ["region"] = cave.Region,
                ["surveyed_length_m"] = cave.SurveyedLength is null ? null : (double)cave.SurveyedLength,
                ["depth_m"] = cave.Depth is null ? null : (double)cave.Depth,
                ["altitude_m"] = cave.Altitude is null ? null : (double)cave.Altitude,
                ["entrances"] = cave.EntranceCount,
                // Approximate flag travels with obfuscated coordinates so consumers
                // cannot mistake a snapped grid point for a surveyed location.
                ["approximate"] = exact ? null : "yes",
            });
        }).ToList();

        return WriteFile(vectorIO, target, "caves", features);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportSurfaceFeaturesAsync(
        string format,
        long? featureTypeId,
        Guid? caveId,
        string? search,
        string? bbox,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        var query = db.SurfaceFeatures.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.SurfaceFeature);

        if (featureTypeId is not null)
        {
            query = query.Where(f => f.FeatureTypeId == featureTypeId);
        }

        if (caveId is not null)
        {
            query = query.Where(f => f.CaveId == caveId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(f =>
                f.Name != null && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)));
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(f => f.Geom.Intersects(polygon));
        }

        var featureTypes = await db.FeatureTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        var rows = await query.OrderBy(f => f.Name).ToListAsync(ct);

        var features = rows.Select(row =>
        {
            var properties = new Dictionary<string, object?>
            {
                ["name"] = row.Name,
                ["feature_type"] = featureTypes.GetValueOrDefault(row.FeatureTypeId),
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

            return new VectorFeature(row.Geom, properties);
        }).ToList();

        return WriteFile(vectorIO, target, "surface_features", features);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportGeofileAsync(
        Guid id,
        string format,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Formats.TryGetValue(format, out var target))
        {
            return UnsupportedFormat(format);
        }

        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !await permissions.CanAsync(user, geofile, ObjectPermission.Read, ct))
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

        var layerName = string.Concat(geofile.Name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return WriteFile(vectorIO, target, layerName.Length == 0 ? "geofile" : layerName, features);
    }

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
