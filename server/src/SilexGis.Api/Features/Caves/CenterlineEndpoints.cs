// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

public sealed record CenterlineDto(
    Guid Id,
    Guid CaveId,
    Guid? SurveyModelId,
    string Name,
    string? Description,
    GeoJsonGeometry Geom,
    decimal? LengthM,
    int PathCount,
    /// <summary>The cave's current shape — exactly one centerline per cave carries it.</summary>
    bool IsDefault,
    CenterlineSource Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CenterlineUpdateRequest(
    string Name,
    string? Description,
    Guid? SurveyModelId,
    bool IsDefault);

public sealed class CenterlineUpdateRequestValidator : AbstractValidator<CenterlineUpdateRequest>
{
    public CenterlineUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

/// <summary>
/// Cave centerlines: the surveyed line work of a cave, uploaded as GeoJSON/GPX/KML
/// (extraction from survey models is a future processing job). A centerline is a feature —
/// a child of its cave, whose visibility cascades down to it — so it is readable exactly when
/// its cave is. Because a centerline traces the cave's exact position, it is location data:
/// under location protection every read path withholds it entirely (extended geometry cannot
/// be snapped to a grid the way a point can) from callers without the exact-location
/// permission, and unreadable never means 403 — it means 404.
/// </summary>
public static class CenterlineEndpoints
{
    /// <summary>Centerline uploads are small line files; whole systems stay well under this.</summary>
    private const long MaxUploadBytes = 20L * 1024 * 1024;

    /// <summary>Bound of the feature name column; an over-long upload file name is cut, not rejected.</summary>
    private const int MaxNameLength = 255;

    public static RouteGroupBuilder MapCenterlineEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/centerlines", ListAsync)
            .WithTags("Centerlines")
            .WithSummary("Centerlines of a cave; withheld without the exact-location permission.");
        api.MapPost("/caves/{caveId:guid}/centerlines", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadBytes))
            .WithTags("Centerlines")
            .WithSummary("Uploads a GeoJSON/GPX/KML centerline (Write on the cave).");
        api.MapPut("/centerlines/{id:guid}", UpdateAsync)
            .WithValidation<CenterlineUpdateRequest>()
            .WithTags("Centerlines")
            .WithSummary("Metadata update, including which centerline is the cave's shape (Write on the cave).");
        api.MapDelete("/centerlines/{id:guid}", DeleteAsync)
            .WithTags("Centerlines")
            .WithSummary("Deletes the centerline (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<CenterlineDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // The cave's visibility cascades to its centerlines at read time, so the row-local
        // filter is the whole read rule — no join back to the cave.
        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Centerline && f.Centerline!.CaveFeatureId == caveId)
            .Include(f => f.Centerline)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);

        // The cave stays readable, but its centerlines trace its exact position: whoever may
        // not see that sees no centerlines at all, and is told nothing about how many exist.
        var exact = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(f => f.Id)], ct);
        return TypedResults.Ok(rows
            .Where(f => exact.Contains(f.Id))
            .Select(f => ToDto(f, f.Centerline!))
            .ToList());
    }

    private static async Task<Results<Created<CenterlineDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        Guid caveId,
        IFormFile file,
        SilexGisDbContext db,
        IVectorIO vectorIO,
        FeatureWriteService writes,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var caveFeature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (caveFeature is null || !(await access.DecideAsync(ctx, AccessAction.Read, caveFeature, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, caveFeature, ct)).Allowed)
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

        // The display skeleton is built once, on the way in, from the geometry as surveyed: a
        // survey export is mostly splays, and drawing one canvas path per splay is what makes the
        // map overlay unusable. Rebuilding it later from an existing skeleton would keep pruning,
        // so an uploaded centerline's skeleton is computed here and nowhere else. A centerline
        // read out of a compiled survey file is built by that extraction instead, from the splay
        // flag the file states, and never reaches this path.
        var skeleton = CenterlineSkeleton.Build(geom);
        var storeSkeleton = CenterlineSkeleton.IsWorthStoring(geom, skeleton);
        var pathCount = CenterlineSkeleton.PathCount(geom);

        var name = Path.GetFileNameWithoutExtension(file.FileName);
        var feature = new Feature
        {
            // Owner and caving-group binding default from the cave in the write service, and
            // read visibility cascades from the cave: a centerline has no access control of its own.
            Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
            Geom = geom,
        };
        var centerline = new Centerline
        {
            CaveFeatureId = caveId,
            Skeleton = storeSkeleton ? skeleton : null,
            PathCount = pathCount,
            SkeletonPathCount = storeSkeleton ? CenterlineSkeleton.PathCount(skeleton) : pathCount,
            LengthM = await CenterlineSql.GeodesicLengthMetersAsync(db, geom, ct),
        };

        try
        {
            await writes.CreateCenterlineAsync(feature, centerline, ct);
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/caves/{caveId}/centerlines", ToDto(feature, centerline));
    }

    private static async Task<Results<Ok<CenterlineDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CenterlineUpdateRequest request,
        SilexGisDbContext db,
        FeatureWriteService writes,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await ResolveAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound("centerline.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, found.CaveFeature, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (request.SurveyModelId is Guid surveyModelId
            && !await db.SurveyModels.AnyAsync(
                m => m.Id == surveyModelId && m.CaveFeatureId == found.Centerline.CaveFeatureId, ct))
        {
            return ApiProblems.BadRequest(
                "centerline.survey_model_invalid", "The survey model does not belong to this cave.");
        }

        // The flag is set by promotion, never by clearing: a cave with centerlines has one of
        // them as its shape, and dropping the last default would leave the map nothing to draw.
        if (!request.IsDefault && found.Centerline.IsDefault)
        {
            return ApiProblems.BadRequest(
                "centerline.default_required",
                "Make another centerline the default instead of clearing this one.");
        }

        found.Feature.Name = request.Name;
        found.Feature.Description = request.Description;
        found.Centerline.SurveyModelId = request.SurveyModelId;
        if (request.IsDefault && !found.Centerline.IsDefault)
        {
            await writes.SetDefaultCenterlineAsync(id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(found.Feature, found.Centerline));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        FeatureWriteService writes,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await ResolveAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound("centerline.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, found.CaveFeature, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        await writes.DeleteCenterlineAsync(id, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Loads a centerline for a write path, tracked, together with its cave's feature row —
    /// or null when the caller may not see it at all (unreadable, or withheld because it is
    /// under location protection the caller does not hold). Callers turn null into a 404: a
    /// 403 would confirm that a hidden cave has a survey.
    /// </summary>
    private static async Task<CenterlineContext?> ResolveAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct)
    {
        var centerline = await db.Centerlines
            .Include(c => c.Feature)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (centerline is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, centerline.Feature, ct)).Allowed)
        {
            return null;
        }

        var exact = await protection.ExactViewIdsAsync(ctx, [id], ct);
        if (!exact.Contains(id))
        {
            return null;
        }

        var caveFeature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == centerline.CaveFeatureId, ct);
        return caveFeature is null ? null : new CenterlineContext(centerline.Feature, centerline, caveFeature);
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

    // A centerline feature always carries its geometry (database CHECK on the kind).
    private static CenterlineDto ToDto(Feature feature, Centerline centerline) => new(
        feature.Id,
        centerline.CaveFeatureId,
        centerline.SurveyModelId,
        feature.Name ?? string.Empty,
        feature.Description,
        GeoJsonGeometry.From(feature.Geom!),
        centerline.LengthM,
        centerline.PathCount,
        centerline.IsDefault,
        centerline.Source,
        feature.CreatedAt,
        feature.UpdatedAt);

    /// <summary>A centerline as it is written: both its rows plus the cave that governs access.</summary>
    private sealed record CenterlineContext(Feature Feature, Centerline Centerline, Feature CaveFeature);
}
