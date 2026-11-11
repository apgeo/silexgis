// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.GeoreferencedMaps;

public sealed record GeoreferencedMapDto(
    Guid Id,
    string Name,
    string? Description,
    MapKind MapKind,
    Guid FileId,
    RasterStatus Status,
    string? ProcessingError,
    GeoJsonGeometry? Bbox,
    int? MinZoom,
    int? MaxZoom,
    string? Attribution,
    decimal DefaultOpacity,
    /// <summary>Feature id of the linked cave, when this raster is one cave's map.</summary>
    Guid? CaveFeatureId,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    /// <summary>Signed COG URL once Ready — feed it to ol/source/GeoTIFF as-is.</summary>
    string? CogUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>The uploaded portable document a map sheet is drawn from; null for a GeoTIFF upload.</summary>
    Guid? SourceFileId = null,
    /// <summary>The page of the document that is drawn, from 1.</summary>
    int? SourcePage = null,
    int? SourcePageCount = null,
    /// <summary>The page's optional-content layer names; empty when it has none, null before it is drawn.</summary>
    IReadOnlyList<string>? SourceLayers = null,
    /// <summary>The layers drawn; null for all of them.</summary>
    IReadOnlyList<string>? RenderLayers = null,
    decimal? RenderDpi = null,
    int? RenderWidth = null,
    int? RenderHeight = null,
    /// <summary>What the drawing library complained about while still drawing the page whole.</summary>
    string? RenderWarnings = null,
    /// <summary>Where the sheet is placed; null when it is not.</summary>
    MapPlacementDto? Placement = null,
    PlacementSource? PlacementSource = null,
    /// <summary>Whether the document carries a place of its own, which removing a hand placement returns to.</summary>
    bool HasEmbeddedPlacement = false,
    decimal? PlacementRmsMetres = null,
    /// <summary>A short-lived address of the drawn page's picture, for the placement editor.</summary>
    string? PreviewUrl = null);

/// <summary>A sheet's placement as the wire carries it; see the placement rules for what each part means.</summary>
public sealed record MapPlacementDto(
    int Version,
    string Transformation,
    string Crs,
    IReadOnlyList<MapControlPointDto> Gcps,
    IReadOnlyList<IReadOnlyList<double>>? Mask);

/// <summary>A point on the picture (fractions of its width and height) matched to a point on the ground.</summary>
public sealed record MapControlPointDto(double U, double V, double X, double Y, string? Label);

public sealed record GeoreferencedMapUpdateRequest(
    string Name,
    string? Description,
    MapKind MapKind,
    int? MinZoom,
    int? MaxZoom,
    string? Attribution,
    decimal DefaultOpacity,
    Guid? CaveFeatureId,
    Guid? CavingGroupId,
    Visibility Visibility);

public sealed class GeoreferencedMapUpdateRequestValidator : AbstractValidator<GeoreferencedMapUpdateRequest>
{
    public GeoreferencedMapUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Attribution).MaximumLength(300);
        RuleFor(x => x.DefaultOpacity).InclusiveBetween(0, 1);
        RuleFor(x => x.MinZoom).InclusiveBetween(0, 24).When(x => x.MinZoom is not null);
        RuleFor(x => x.MaxZoom).InclusiveBetween(0, 24).When(x => x.MaxZoom is not null);
        RuleFor(x => x.MaxZoom).GreaterThanOrEqualTo(x => x.MinZoom!.Value)
            .When(x => x.MinZoom is not null && x.MaxZoom is not null)
            .WithMessage("The largest zoom cannot be below the smallest.");
        RuleFor(x => x.MapKind).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();

        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be a band that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");
    }
}

public static class GeoreferencedMapEndpoints
{
    /// <summary>Rasters are big; scanned map sheets regularly exceed the default body limit.</summary>
    private const long MaxUploadBytes = RasterUploadLimits.MaxRasterBytes;

    /// <summary>What the stored-file row holds of the uploaded file's own name.</summary>
    private const int MaxOriginalNameLength = 255;

    /// <summary>What the map row holds of its display name; the update rule allows the same.</summary>
    private const int MaxNameLength = 200;

    private static string Clipped(string value, int length) =>
        value.Length <= length ? value : value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)];

    public static RouteGroupBuilder MapGeoreferencedMapEndpoints(this RouteGroupBuilder api)
    {
        var maps = api.MapGroup("/georeferenced-maps").WithTags("GeoreferencedMaps");

        maps.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(RasterUploadLimits.MaxRequestBytes))
            .WithSummary(
                "Uploads a georeferenced raster (GeoTIFF) and queues COG normalization, or a map sheet as a "
                + "portable document (.pdf) and queues drawing it: placed at once when the document carries its "
                + "own georeferencing, otherwise left `unplaced` for a placement.");
        maps.MapGet("/", ListAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Paged catalog; visibility-filtered; protected-cave-linked maps omitted and not counted. "
                + "`filter` (a filter document as a link token) or `savedFilterId` narrows it by the "
                + "georeferenced map world's conditions, applied after visibility and counted over the "
                + "narrowed set.");
        maps.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single georeferenced map with a fresh COG delivery URL.");
        maps.MapPut("/{id:guid}", UpdateAsync).WithValidation<GeoreferencedMapUpdateRequest>()
            .WithSummary("Metadata update (Write permission).");
        maps.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes the catalog entry (stored files are kept).");
        maps.MapGeoreferencedMapSheetEndpoints();

        return api;
    }

    private static async Task<Results<Created<GeoreferencedMapDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.GeoreferencedMaps))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".tif" or ".tiff" or ".pdf" or ".png" or ".jpg" or ".jpeg"))
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.format_unsupported",
                "Upload a GeoTIFF (.tif/.tiff), a map sheet as a portable document (.pdf), or a scan to place by hand (.png/.jpg).");
        }

        // A portable document or a plain picture is a sheet: drawn, then placed. A GeoTIFF goes
        // straight to conversion, and becomes a sheet only if it turns out to carry no place.
        var (isSheet, mimeType, kind) = extension switch
        {
            ".pdf" => (true, "application/pdf", FileKind.Document),
            ".png" => (true, "image/png", FileKind.Image),
            ".jpg" or ".jpeg" => (true, "image/jpeg", FileKind.Image),
            _ => (false, "image/tiff", FileKind.Raster),
        };
        if (isSheet && !await StartsLikeAsync(file, extension, ct))
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.format_unsupported", $"The file is named {extension} but its content is not one.");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("georeferenced_map.size_invalid", "The file is empty or exceeds 512 MB.");
        }

        // Checked before the blob is written: a name the stored-file row cannot hold would
        // otherwise fail the save after the bytes are on disk, answering 500 and leaving a file
        // nothing names.
        if (Path.GetFileName(file.FileName).Length > MaxOriginalNameLength)
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.file_name_too_long",
                $"The file name is longer than {MaxOriginalNameLength} characters; rename the file and upload it again.");
        }

        string storagePath;
        await using (var content = file.OpenReadStream())
        {
            storagePath = await fileStore.SaveAsync(content, extension, ct);
        }

        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        // The display name is only a starting point the owner can edit, so a long one is cut to
        // what the column holds rather than refused.
        var name = Clipped(Path.GetFileNameWithoutExtension(file.FileName), MaxNameLength);
        // A sheet's document is a document like any other — private to its uploader until they
        // share it — and the map is how everybody else sees it: as a picture placed on the map,
        // published only to those the map is published to.
        var stored = documents.Create(
            new StoredContent(
                storagePath,
                Path.GetFileName(file.FileName),
                mimeType,
                file.Length,
                sha256,
                kind),
            name,
            user.UserId,
            user.UserId).File;

        var map = new GeoreferencedMap
        {
            Name = name,
            FileId = stored.Id,
            OwnerUserId = user.UserId,
            MapKind = extension == ".pdf" ? MapKind.CaveMap : MapKind.Other,
            SourceFileId = isSheet ? stored.Id : null,
            SourcePage = isSheet ? 1 : null,
        };

        db.GeoreferencedMaps.Add(map);
        db.ProcessingJobs.Add(isSheet
            ? new ProcessingJob
            {
                Kind = ProcessingJobKinds.MapSheetRender,
                Payload = JsonSerializer.Serialize(new MapSheetPayload(map.Id), JsonSerializerOptions.Web),
                RequestedBy = user.UserId,
            }
            : new ProcessingJob
            {
                Kind = ProcessingJobKinds.RasterCog,
                Payload = JsonSerializer.Serialize(new RasterCogPayload(map.Id), JsonSerializerOptions.Web),
                RequestedBy = user.UserId,
            });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/georeferenced-maps/{map.Id}", map.ToDto(tokens));
    }

    private static async Task<Results<Ok<PagedResult<GeoreferencedMapDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        FeatureProtection protection,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        Guid? caveFeatureId,
        CancellationToken ct,
        string? filter = null,
        Guid? savedFilterId = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var narrowing = await surfaceFilters.ForAsync<GeoreferencedMap>(
            ctx, GeoreferencedMapFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // A cave-linked raster IS the cave's location, so a map on a cave this caller may not place
        // exactly is left out before the count and the page — the total never announces how many
        // were withheld. The filter can only intersect what is left.
        var query = narrowing.Narrowing!.Apply(await GeoreferencedMapReads.ShownAsync(db, protection, ctx, ct));
        if (caveFeatureId is not null)
        {
            query = query.Where(m => m.CaveFeatureId == caveFeatureId);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var result = await query.OrderByDescending(m => m.UpdatedAt).ThenByDescending(m => m.Id)
            .ToPagedAsync(p, size, m => m.ToDto(tokens), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var map = await db.GeoreferencedMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null || !(await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed)
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        // The raster's footprint is the linked cave's location, so there is nothing to
        // partially redact — without exact view on that cave the row does not exist.
        if (await protection.ShouldRedactLinkAsync(ctx, map.CaveFeatureId, ct))
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        // The update answers the version it produced; the read hands out the same one, so that a
        // client re-reading after somebody else's change holds the version its next write needs.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.GeoreferencedMaps, map.Id, ct);
        return TypedResults.Ok(map.ToDto(tokens));
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        GeoreferencedMapUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, map, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("georeferenced_map.not_found");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.GeoreferencedMaps, map.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        // The hold read the row again; who may change it is decided on what it holds now.
        if (!(await access.DecideAsync(ctx, AccessAction.Write, map, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("georeferenced_map.not_found");
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.GeoreferencedMaps, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        // Only a *changed* link is re-validated: a full-replace PUT that leaves the stored
        // link alone must keep working even if the cave has since become unreadable to
        // this caller, otherwise the row would be permanently uneditable.
        if (request.CaveFeatureId is not null && request.CaveFeatureId != map.CaveFeatureId
            && !await CaveIsLinkableAsync(request.CaveFeatureId.Value, db, access, ctx, ct))
        {
            // A cave the caller cannot read is reported as missing: whether one exists
            // must not be disclosed through the link target.
            return ApiProblems.BadRequest("georeferenced_map.cave_not_found", "Linked cave does not exist.");
        }

        map.Name = request.Name;
        map.Description = request.Description;
        map.MapKind = request.MapKind;
        map.MinZoom = request.MinZoom;
        map.MaxZoom = request.MaxZoom;
        map.Attribution = request.Attribution;
        map.DefaultOpacity = request.DefaultOpacity;
        map.CaveFeatureId = request.CaveFeatureId;
        map.CavingGroupId = request.CavingGroupId;
        map.Visibility = request.Visibility;
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(map.ToDto(tokens));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
        {
            return ApiProblems.NotFound("georeferenced_map.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, map, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, map, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("georeferenced_map.not_found");
        }

        // Stored files (upload + COG) are immutable and stay; only the catalog entry goes, with
        // the rows that point at it by kind and id — files attached to it, tags on it, and its
        // place in a relation. Those have no foreign key, so nothing else removes them, and they
        // go in one transaction with the map so a failed save leaves the map whole.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PolymorphicRowCleanup.RemoveRowsPointingAtAsync(db, AttachedEntityType.GeoreferencedMap, [map.Id], ct);
        db.GeoreferencedMaps.Remove(map);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// A raster may be tied to a cave feature the caller can actually read; unreadable and
    /// non-existent caves are indistinguishable to the caller by design.
    /// </summary>
    private static async Task<bool> CaveIsLinkableAsync(
        Guid caveFeatureId,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        CancellationToken ct)
    {
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveFeatureId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed;
    }

    /// <summary>Whether the file starts the way every file of its named format does.</summary>
    private static async Task<bool> StartsLikeAsync(IFormFile file, string extension, CancellationToken ct)
    {
        var head = new byte[1024];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        var bytes = head.AsSpan(0, read);
        return extension switch
        {
            // A portable document may carry a few bytes of junk before its header.
            ".pdf" => bytes.IndexOf("%PDF-"u8) >= 0,
            ".png" => bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            _ => bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        };
    }

    internal static GeoreferencedMapDto ToDto(this GeoreferencedMap m, IFileAccessTokenService tokens) => new(
        m.Id,
        m.Name,
        m.Description,
        m.MapKind,
        m.FileId,
        m.Status,
        m.ProcessingError,
        m.Bbox is null ? null : GeoJsonGeometry.From(m.Bbox),
        m.MinZoom,
        m.MaxZoom,
        m.Attribution,
        m.DefaultOpacity,
        m.CaveFeatureId,
        m.OwnerUserId,
        m.CavingGroupId,
        m.Visibility,
        m.Status == RasterStatus.Ready
            // A tile reader fetches ranges of the raster itself, so nothing less than the
            // whole file serves this at all — and it keeps fetching them for as long as the map
            // is on screen, from the one address it was created with, so the address is minted
            // for a session rather than for a fetch.
            ? $"/api/v1/files/{m.FileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(m.FileId, FileDelivery.Full, FileAccessLifetime.Session))}"
            : null,
        m.CreatedAt,
        m.UpdatedAt,
        m.SourceFileId,
        m.SourcePage,
        m.SourcePageCount,
        Names(m.SourceLayers),
        Names(m.RenderLayers),
        m.RenderDpi,
        m.RenderWidth,
        m.RenderHeight,
        m.RenderWarnings,
        MapPlacement.FromJson(m.Placement) is { } placement ? placement.ToDto() : null,
        m.PlacementSource,
        m.EmbeddedPlacement is not null,
        m.PlacementRmsMetres,
        m.PreviewFileId is { } preview
            // A picture this application drew of the page; the editor shows it once, soon after.
            ? $"/api/v1/files/{preview}/content?token={Uri.EscapeDataString(tokens.CreateToken(preview, FileDelivery.Full))}"
            : null);

    private static IReadOnlyList<string>? Names(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<string[]>(json);

    internal static MapPlacementDto ToDto(this MapPlacement p) => new(
        p.Version,
        p.Transformation,
        p.Crs,
        [.. p.Gcps.Select(g => new MapControlPointDto(g.U, g.V, g.X, g.Y, g.Label))],
        p.Mask);

    internal static MapPlacement ToDomain(this MapPlacementDto p) => new(
        p.Version,
        p.Transformation,
        p.Crs,
        [.. (p.Gcps ?? []).Select(g => new MapControlPoint(g.U, g.V, g.X, g.Y, g.Label))],
        p.Mask);
}
