// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.GeoreferencedMaps;

/// <summary>Draw a map sheet again: another page, other layers, another resolution. Absent parts keep what is drawn now.</summary>
public sealed record MapSheetRenderRequest(int? Page, IReadOnlyList<string>? Layers, double? Dpi, bool AllLayers = false);

public sealed class MapSheetRenderRequestValidator : AbstractValidator<MapSheetRenderRequest>
{
    public MapSheetRenderRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).When(x => x.Page is not null);
        RuleFor(x => x.Layers).Must(l => l!.Count <= 500).When(x => x.Layers is not null)
            .WithMessage("At most 500 layers.");
        RuleForEach(x => x.Layers).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Dpi).GreaterThan(0).When(x => x.Dpi is not null);
    }
}

public sealed class MapPlacementDtoValidator : AbstractValidator<MapPlacementDto>
{
    public MapPlacementDtoValidator()
    {
        RuleFor(x => x.Transformation).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Crs).NotEmpty().MaximumLength(40)
            .Must(c => MapPlacementRules.EpsgCodeOf(c) is not null)
            .WithMessage("The coordinate system must be written EPSG:<code>.");
        RuleFor(x => x.Gcps).NotNull();
        RuleForEach(x => x.Gcps).ChildRules(p => p.RuleFor(g => g.Label).MaximumLength(200));
    }
}

/// <summary>A placement tried on the sheet's small picture.</summary>
/// <param name="Image">A PNG picture of the placed sheet, as a data address.</param>
/// <param name="Extent3857">Where the picture goes, in web-mercator metres: west, south, east, north.</param>
/// <param name="Extent4326">The same in degrees.</param>
public sealed record MapPlacementPreviewDto(
    string Image,
    IReadOnlyList<double> Extent3857,
    IReadOnlyList<double> Extent4326,
    int Width,
    int Height,
    double? RmsMetres,
    IReadOnlyList<MapControlPointResidualDto> Residuals);

/// <summary>How far one control point lands from where it was put, in metres on the ground.</summary>
public sealed record MapControlPointResidualDto(int Index, double Metres);

/// <summary>
/// The routes that make a portable document into a placed map: draw it again, place it, take its
/// placement away, and try a placement before keeping it.
/// </summary>
public static class GeoreferencedMapSheetEndpoints
{
    public static RouteGroupBuilder MapGeoreferencedMapSheetEndpoints(this RouteGroupBuilder maps)
    {
        maps.MapPost("/{id:guid}/render", RenderAsync).WithValidation<MapSheetRenderRequest>()
            .WithSummary(
                "Draws a map sheet's document again with another page, other layers or another resolution "
                + "(Write). A hand placement is kept and applied to the new drawing.");
        maps.MapPut("/{id:guid}/placement", PlaceAsync).WithValidation<MapPlacementDto>()
            .WithSummary(
                "Sets where a drawn map sheet lies — control points as picture fractions matched to ground "
                + "coordinates, a transformation and an optional outline (Write; If-Match) — and queues publishing it.");
        maps.MapDelete("/{id:guid}/placement", UnplaceAsync)
            .WithSummary(
                "Removes a hand placement (Write; If-Match). A document that carries its own place goes back "
                + "to it; any other sheet becomes `unplaced`.");
        maps.MapGet("/{id:guid}/placement/annotation", ExportAnnotationAsync)
            .WithSummary(
                "The sheet's placement as a IIIF Georeference Annotation (Read): control points in the drawn "
                + "picture's pixels and in longitude and latitude, the outline as an SVG polygon.");
        maps.MapPut("/{id:guid}/placement/annotation", ImportAnnotationAsync)
            .WithSummary(
                "Places the sheet from a IIIF Georeference Annotation made elsewhere (Write; If-Match), as a "
                + "placement PUT would: points are read as fractions of the size the annotation states.");
        maps.MapPost("/{id:guid}/placement/preview", PreviewAsync).WithValidation<MapPlacementDto>()
            .WithSummary(
                "Tries a placement on the sheet's small picture without keeping it (Read): the placed picture, "
                + "where it goes and how well the points fit.");
        return maps;
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> RenderAsync(
        Guid id,
        MapSheetRenderRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptionsMonitor<GeoreferencedMapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var (map, refusal) = await WritableSheetAsync(id, db, access, protection, ctx, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (map!.Status is RasterStatus.Uploaded or RasterStatus.Processing)
        {
            return Busy();
        }

        if (request.Page is { } page && map.SourcePageCount is { } pages && page > pages)
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.page_out_of_range", $"The document has {pages} page(s).");
        }

        var o = options.CurrentValue;
        if (request.Dpi is { } dpi && (dpi < o.MinDpi || dpi > o.MaxDpi))
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.dpi_out_of_range",
                string.Create(CultureInfo.InvariantCulture, $"The resolution must be from {o.MinDpi} to {o.MaxDpi} dpi."));
        }

        var known = map.SourceLayers is null ? null : JsonSerializer.Deserialize<string[]>(map.SourceLayers);
        if (request.Layers is { } layers && known is not null && (request.Page is null || request.Page == map.SourcePage))
        {
            var unknown = layers.Where(l => !known.Contains(l, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                return ApiProblems.BadRequest(
                    "georeferenced_map.layer_unknown",
                    $"The page has no layer named {string.Join(", ", unknown.Select(u => $"'{u}'"))}.");
            }
        }

        if (request.Page is { } newPage && newPage != map.SourcePage)
        {
            // Another page is another drawing: its layers are its own, and a hand placement made
            // on the old page means nothing on it.
            map.SourcePage = newPage;
            map.RenderLayers = null;
            map.Placement = null;
            map.PlacementSource = null;
        }

        if (request.AllLayers)
        {
            map.RenderLayers = null;
        }
        else if (request.Layers is { } chosen)
        {
            map.RenderLayers = JsonSerializer.Serialize(chosen.Distinct(StringComparer.Ordinal).ToArray());
        }

        map.Status = RasterStatus.Uploaded;
        map.ProcessingError = null;
        var user = await userAccessor.GetAsync(ct);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.MapSheetRender,
            Payload = JsonSerializer.Serialize(
                new MapSheetPayload(map.Id, request.Dpi ?? (double?)map.RenderDpi), JsonSerializerOptions.Web),
            RequestedBy = user?.UserId,
        });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(map.ToDto(tokens));
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> PlaceAsync(
        Guid id,
        MapPlacementDto request,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        ICrsRegistry crs,
        IOptionsMonitor<GeoreferencedMapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var (map, refusal) = await WritableSheetAsync(id, db, access, protection, ctx, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        return await ApplyAsync(map!, request.ToDomain(), http, db, tokens, userAccessor, crs, options.CurrentValue, ct);
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> ApplyAsync(
        GeoreferencedMap map,
        MapPlacement placement,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        ICrsRegistry crs,
        GeoreferencedMapOptions options,
        CancellationToken ct)
    {
        if (Check(placement, crs, options) is { } problem)
        {
            return problem;
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.GeoreferencedMaps, map.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        if (map.Status is RasterStatus.Uploaded or RasterStatus.Processing)
        {
            return Busy();
        }

        if (map.MasterFileId is null)
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.not_rendered", "The sheet has not been drawn yet; wait until it has.");
        }

        map.Placement = placement.ToJson();
        map.PlacementSource = PlacementSource.Manual;
        map.Status = RasterStatus.Processing;
        map.ProcessingError = null;
        var user = await userAccessor.GetAsync(ct);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.MapPlacementWarp,
            Payload = JsonSerializer.Serialize(new MapSheetPayload(map.Id), JsonSerializerOptions.Web),
            RequestedBy = user?.UserId,
        });
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(map.ToDto(tokens));
    }

    private static async Task<Results<Ok<JsonObject>, ProblemHttpResult>> ExportAnnotationAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        MapWarpService warper,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var map = await db.GeoreferencedMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null || ctx is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
            || await protection.ShouldRedactLinkAsync(ctx, map.CaveFeatureId, ct))
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        if (MapPlacement.FromJson(map.Placement) is not { } placement || map.RenderWidth is not { } width
            || map.RenderHeight is not { } height)
        {
            return ApiProblems.NotFound("georeferenced_map.no_placement");
        }

        IReadOnlyList<MapControlPoint> points;
        try
        {
            points = warper.PointsInWgs84(placement);
        }
        catch (VectorIOException e)
        {
            return ApiProblems.BadRequest(MapPlacementRules.CrsUnknown, e.Message);
        }

        return TypedResults.Ok(GeoreferenceAnnotation.Write(
            placement.Transformation, points, placement.Mask, width, height, $"urn:silexgis:georeferenced-map:{map.Id}"));
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> ImportAnnotationAsync(
        Guid id,
        JsonNode? annotation,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        ICrsRegistry crs,
        IOptionsMonitor<GeoreferencedMapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var (map, refusal) = await WritableSheetAsync(id, db, access, protection, ctx, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var (placement, problem) = GeoreferenceAnnotation.Read(annotation, map!.RenderWidth ?? 0, map.RenderHeight ?? 0);
        if (placement is null)
        {
            return ApiProblems.BadRequest(GeoreferenceAnnotation.Malformed, problem);
        }

        return await ApplyAsync(map, placement, http, db, tokens, userAccessor, crs, options.CurrentValue, ct);
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> UnplaceAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var (map, refusal) = await WritableSheetAsync(id, db, access, protection, ctx, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.GeoreferencedMaps, map!.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        if (map.Status is RasterStatus.Uploaded or RasterStatus.Processing)
        {
            return Busy();
        }

        if (map.PlacementSource != PlacementSource.Manual)
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.no_manual_placement", "The sheet has no hand placement to remove.");
        }

        if (map.EmbeddedPlacement is not null && map.MasterFileId is not null)
        {
            map.Placement = map.EmbeddedPlacement;
            map.PlacementSource = PlacementSource.Embedded;
            map.Status = RasterStatus.Processing;
            var user = await userAccessor.GetAsync(ct);
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.MapPlacementWarp,
                Payload = JsonSerializer.Serialize(new MapSheetPayload(map.Id), JsonSerializerOptions.Web),
                RequestedBy = user?.UserId,
            });
        }
        else
        {
            map.Placement = null;
            map.PlacementSource = null;
            MapSheetPublisher.Unpublish(map);
        }

        map.ProcessingError = null;
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(map.ToDto(tokens));
    }

    private static async Task<Results<Ok<MapPlacementPreviewDto>, ProblemHttpResult>> PreviewAsync(
        Guid id,
        MapPlacementDto request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileStore fileStore,
        MapWarpService warper,
        MapSheetPublisher publisher,
        ICrsRegistry crs,
        IOptionsMonitor<GeoreferencedMapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var map = await db.GeoreferencedMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null || ctx is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
            || await protection.ShouldRedactLinkAsync(ctx, map.CaveFeatureId, ct))
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        var o = options.CurrentValue;
        var placement = request.ToDomain();
        if (Check(placement, crs, o) is { } problem)
        {
            return problem;
        }

        var preview = map.PreviewFileId is { } previewId
            ? await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == previewId, ct)
            : null;
        if (preview is null)
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.not_rendered", "The sheet has not been drawn yet; wait until it has.");
        }

        var slots = PreviewSlots.Get(o.PreviewConcurrency);
        if (!await slots.WaitAsync(TimeSpan.Zero, ct))
        {
            return ApiProblems.ServiceUnavailable(
                "georeferenced_map.preview_busy", "Too many previews are being worked out; try again in a moment.");
        }

        var work = publisher.WorkDirectory(0);
        try
        {
            var outcome = warper.Preview(fileStore.GetAbsolutePath(preview.StoragePath), placement, work, o.PreviewMaxPixels);
            return TypedResults.Ok(new MapPlacementPreviewDto(
                "data:image/png;base64," + Convert.ToBase64String(outcome.Png),
                outcome.Extent3857,
                outcome.Extent4326,
                outcome.Width,
                outcome.Height,
                outcome.RmsMetres,
                [.. outcome.Residuals.Select(r => new MapControlPointResidualDto(r.Index, r.Metres))]));
        }
        catch (VectorIOException e)
        {
            return ApiProblems.BadRequest("georeferenced_map.preview_failed", e.Message);
        }
        finally
        {
            MapSheetPublisher.Remove(work);
            slots.Release();
        }
    }

    /// <summary>
    /// A map sheet the caller may change. A map the caller may not read — or may not place exactly,
    /// when it is a cave's — does not exist for them; one they may read but not change is refused.
    /// </summary>
    private static async Task<(GeoreferencedMap? Map, ProblemHttpResult? Refusal)> WritableSheetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext? ctx,
        CancellationToken ct)
    {
        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null || ctx is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
            || await protection.ShouldRedactLinkAsync(ctx, map.CaveFeatureId, ct))
        {
            return (null, ApiProblems.NotFound("georeferenced_map.not_found"));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, map, ct)).Allowed)
        {
            return (null, ApiProblems.Forbidden());
        }

        if (map.SourceFileId is null)
        {
            return (null, ApiProblems.BadRequest(
                "georeferenced_map.not_a_sheet",
                "Only a map uploaded as a portable document can be drawn again or placed by hand."));
        }

        return (map, null);
    }

    private static ProblemHttpResult? Check(MapPlacement placement, ICrsRegistry crs, GeoreferencedMapOptions options)
    {
        var problems = MapPlacementRules.Validate(placement, options.MaxControlPoints);
        if (problems.Count > 0)
        {
            return ApiProblems.BadRequest(problems[0].Code, string.Join(" ", problems.Select(p => p.Message)));
        }

        return placement.EpsgCode is { } code && crs.Proj4(code) is null
            ? ApiProblems.BadRequest(MapPlacementRules.CrsUnknown, $"EPSG:{code} is not a coordinate system this server can define.")
            : null;
    }

    private static ProblemHttpResult Busy() => ApiProblems.Conflict(
        "georeferenced_map.busy", "The sheet is being drawn or placed; wait until that is done.");

    /// <summary>
    /// How many previews may be worked out at once. A preview is a fraction of a second of one
    /// core; a dozen people dragging corners at once should not take every core from everything else.
    /// </summary>
    private static class PreviewSlots
    {
        private static SemaphoreSlim? slots;
        private static int size;
        private static readonly Lock Gate = new();

        public static SemaphoreSlim Get(int wanted)
        {
            lock (Gate)
            {
                if (slots is null || size != wanted)
                {
                    slots = new SemaphoreSlim(wanted, wanted);
                    size = wanted;
                }

                return slots;
            }
        }
    }
}
