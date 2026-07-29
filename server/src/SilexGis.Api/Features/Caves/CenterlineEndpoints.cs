// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

public sealed record CenterlineDto(
    Guid Id,
    Guid CaveId,
    Guid? SurveyModelId,
    string Name,
    GeoJsonGeometry Geom,
    decimal? LengthM,
    CenterlineSource Source,
    DateTimeOffset CreatedAt);

/// <summary>
/// Cave centerlines: uploaded GeoJSON/GPX line work displayed over surface maps
/// (extraction from survey files is a future processing job). They inherit the cave's
/// access control, and because a centerline traces the cave's exact position they are
/// location data: for a location-protected cave every read path withholds them entirely
/// from callers without the exact-location permission.
/// </summary>
public static class CenterlineEndpoints
{
    /// <summary>Centerline uploads are small line files; whole systems stay well under this.</summary>
    private const long MaxUploadBytes = 20L * 1024 * 1024;

    public static RouteGroupBuilder MapCenterlineEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/centerlines", ListAsync)
            .WithTags("Centerlines")
            .WithSummary("Centerlines of a cave; withheld without the exact-location permission.");
        api.MapPost("/caves/{caveId:guid}/centerlines", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadBytes))
            .WithTags("Centerlines")
            .WithSummary("Uploads a GeoJSON/GPX centerline (Write on the cave).");
        api.MapDelete("/cave-centerlines/{id:guid}", DeleteAsync)
            .WithTags("Centerlines")
            .WithSummary("Deletes the centerline (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<CenterlineDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caveId, ct);
        if (cave is null || !await permissions.CanAsync(user, cave, ObjectPermission.Read, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // The cave stays readable, but its centerlines trace its exact location.
        if (await CaveLinkRedaction.ShouldRedactAsync(db, user, caveId, ct))
        {
            return TypedResults.Ok(new List<CenterlineDto>());
        }

        var centerlines = await db.CaveCenterlines.AsNoTracking()
            .Where(c => c.CaveId == caveId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(centerlines.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<CenterlineDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        Guid caveId,
        IFormFile file,
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

        var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caveId, ct);
        if (cave is null || !await permissions.CanAsync(user, cave, ObjectPermission.Read, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!await permissions.CanAsync(user, cave, ObjectPermission.Write, ct))
        {
            return ApiProblems.Forbidden();
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var format = extension switch
        {
            ".gpx" => GeofileFormat.Gpx,
            ".geojson" or ".json" => GeofileFormat.GeoJson,
            // Survey tools export centerlines as KML more often than anything else.
            ".kml" or ".kmz" => GeofileFormat.Kml,
            _ => (GeofileFormat?)null,
        };
        if (format is null)
        {
            return ApiProblems.BadRequest(
                "centerline.format_unsupported", "Upload a GeoJSON, GPX or KML centerline file.");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("centerline.size_invalid", "The file is empty or exceeds 20 MB.");
        }

        // The upload is parsed and discarded — the geometry itself is the record.
        MultiLineString geom;
        var tempPath = Path.Combine(Path.GetTempPath(), $"silexgis-centerline-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var content = File.Create(tempPath))
            {
                await file.CopyToAsync(content, ct);
            }

            var dataset = vectorIO.Read(tempPath, format.Value);
            geom = MergeLines(dataset.Features.Select(f => f.Geom));
        }
        catch (VectorIOException ex)
        {
            return ApiProblems.BadRequest("centerline.unreadable", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }

        if (geom.IsEmpty)
        {
            return ApiProblems.BadRequest(
                "centerline.no_lines", "The file contains no line geometries.");
        }

        // The display skeleton is built once, here, from the geometry as surveyed: a survey
        // export is mostly splays, and drawing one canvas path per splay is what makes the map
        // overlay unusable. Rebuilding it later from an existing skeleton would keep pruning,
        // so this is the only place it is computed.
        var skeleton = CenterlineSkeleton.Build(geom);
        var storeSkeleton = CenterlineSkeleton.IsWorthStoring(geom, skeleton);
        var pathCount = CenterlineSkeleton.PathCount(geom);

        var centerline = new CaveCenterline
        {
            CaveId = caveId,
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            Geom = geom,
            Skeleton = storeSkeleton ? skeleton : null,
            PathCount = pathCount,
            SkeletonPathCount = storeSkeleton ? CenterlineSkeleton.PathCount(skeleton) : pathCount,
            LengthM = await CenterlineSql.GeodesicLengthMetersAsync(db, geom, ct),
        };

        db.CaveCenterlines.Add(centerline);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/caves/{caveId}/centerlines", ToDto(centerline));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var centerline = await db.CaveCenterlines.FirstOrDefaultAsync(c => c.Id == id, ct);
        var cave = centerline is null
            ? null
            : await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == centerline.CaveId, ct);
        if (centerline is null || cave is null
            || !await permissions.CanAsync(user, cave, ObjectPermission.Read, ct)
            || await CaveLinkRedaction.ShouldRedactAsync(db, user, cave.Id, ct))
        {
            return ApiProblems.NotFound("centerline.not_found");
        }

        if (!await permissions.CanAsync(user, cave, ObjectPermission.Write, ct))
        {
            return ApiProblems.Forbidden();
        }

        db.CaveCenterlines.Remove(centerline);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Collects every LineString from the parsed features into one MultiLineStringZ.
    /// The column is Z-typed, so 2D sources get Z=0 rather than a mixed-dimension insert.
    /// Zero-length components are dropped: survey exports contain them (a station written
    /// twice) and PostGIS reports a geometry carrying them as invalid.
    /// </summary>
    private static MultiLineString MergeLines(IEnumerable<Geometry> geometries)
    {
        var lines = new List<LineString>();
        foreach (var geometry in geometries)
        {
            switch (geometry)
            {
                case LineString line:
                    Add(line);
                    break;
                case MultiLineString multi:
                    foreach (var component in multi.Geometries.Cast<LineString>())
                    {
                        Add(component);
                    }

                    break;
            }
        }

        return new MultiLineString([.. lines]) { SRID = 4326 };

        void Add(LineString line)
        {
            if (HasLength(line))
            {
                lines.Add(ForceZ(line));
            }
        }
    }

    /// <summary>True when a component spans at least two distinct positions (NaN Z reads as 0).</summary>
    private static bool HasLength(LineString line)
    {
        if (line.Coordinates.Length < 2)
        {
            return false;
        }

        var first = line.Coordinates[0];
        var firstZ = double.IsNaN(first.Z) ? 0 : first.Z;
        return line.Coordinates.Any(c =>
            c.X != first.X || c.Y != first.Y || (double.IsNaN(c.Z) ? 0 : c.Z) != firstZ);
    }

    private static LineString ForceZ(LineString line)
    {
        var coordinates = line.Coordinates
            .Select(c => new CoordinateZ(c.X, c.Y, double.IsNaN(c.Z) ? 0 : c.Z))
            .Cast<Coordinate>()
            .ToArray();
        return new LineString(coordinates) { SRID = 4326 };
    }

    private static CenterlineDto ToDto(CaveCenterline c) => new(
        c.Id,
        c.CaveId,
        c.SurveyModelId,
        c.Name,
        GeoJsonGeometry.From(c.Geom),
        c.LengthM,
        c.Source,
        c.CreatedAt);
}
