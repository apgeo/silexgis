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
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
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
    DateTimeOffset UpdatedAt);

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
    }
}

public static class GeoreferencedMapEndpoints
{
    /// <summary>Rasters are big; scanned map sheets regularly exceed the default body limit.</summary>
    private const long MaxUploadBytes = 512L * 1024 * 1024;

    public static RouteGroupBuilder MapGeoreferencedMapEndpoints(this RouteGroupBuilder api)
    {
        var maps = api.MapGroup("/georeferenced-maps").WithTags("GeoreferencedMaps");

        maps.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadBytes))
            .WithSummary("Uploads a georeferenced raster (GeoTIFF) and queues COG normalization.");
        maps.MapGet("/", ListAsync)
            .WithSummary("Paged catalog; visibility-filtered; protected-cave-linked maps omitted.");
        maps.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single georeferenced map with a fresh COG delivery URL.");
        maps.MapPut("/{id:guid}", UpdateAsync).WithValidation<GeoreferencedMapUpdateRequest>()
            .WithSummary("Metadata update (Write permission).");
        maps.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes the catalog entry (stored files are kept).");

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
        if (extension is not (".tif" or ".tiff"))
        {
            return ApiProblems.BadRequest(
                "georeferenced_map.format_unsupported", "Upload a georeferenced GeoTIFF (.tif/.tiff).");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("georeferenced_map.size_invalid", "The file is empty or exceeds 512 MB.");
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

        var name = Path.GetFileNameWithoutExtension(file.FileName);
        var stored = documents.Create(
            new StoredContent(
                storagePath, Path.GetFileName(file.FileName), "image/tiff", file.Length, sha256, FileKind.Raster),
            name,
            user.UserId,
            user.UserId).File;

        var map = new GeoreferencedMap
        {
            Name = name,
            FileId = stored.Id,
            OwnerUserId = user.UserId,
        };

        db.GeoreferencedMaps.Add(map);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.RasterCog,
            Payload = JsonSerializer.Serialize(new RasterCogPayload(map.Id), JsonSerializerOptions.Web),
            RequestedBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/georeferenced-maps/{map.Id}", map.ToDto(tokens));
    }

    private static async Task<Results<Ok<PagedResult<GeoreferencedMapDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        Guid? caveFeatureId,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.GeoreferencedMaps.AsNoTracking().VisibleTo(ctx, AccessDomain.GeoreferencedMaps);
        if (caveFeatureId is not null)
        {
            query = query.Where(m => m.CaveFeatureId == caveFeatureId);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(m => m.UpdatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        // A cave-linked raster IS the cave's location — omit them entirely for callers
        // without exact view on the cave (unlike point features, no partial redaction).
        var exact = await protection.ExactViewIdsAsync(
            ctx, [.. rows.Where(m => m.CaveFeatureId is not null).Select(m => m.CaveFeatureId!.Value)], ct);
        var items = rows
            .Where(m => m.CaveFeatureId is null || exact.Contains(m.CaveFeatureId.Value))
            .Select(m => m.ToDto(tokens))
            .ToList();

        // totalItems counts pre-omission rows; acceptable page-size jitter for a rare case.
        return TypedResults.Ok(new PagedResult<GeoreferencedMapDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<GeoreferencedMapDto>, ProblemHttpResult>> GetAsync(
        Guid id,
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

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.GeoreferencedMaps, map.Id, ct) is { } stale)
        {
            return stale;
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

        // Stored files (upload + COG) are immutable and stay; only the catalog entry goes.
        db.GeoreferencedMaps.Remove(map);
        await db.SaveChangesAsync(ct);
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

    private static GeoreferencedMapDto ToDto(this GeoreferencedMap m, IFileAccessTokenService tokens) => new(
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
            // whole file serves this at all.
            ? $"/api/v1/files/{m.FileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(m.FileId, FileDelivery.Full))}"
            : null,
        m.CreatedAt,
        m.UpdatedAt);
}
