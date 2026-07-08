// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Files;

public static class FileEndpoints
{
    /// <summary>Upload cap: photos and survey scans, not raster archives (those go elsewhere).</summary>
    private const long MaxUploadBytes = 100 * 1024 * 1024;

    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/files").WithTags("Files");

        files.MapPost("/", UploadAsync)
            .DisableAntiforgery() // bearer-token API; no cookie-form surface to forge
            .WithSummary("Uploads a file (multipart); attach it to an entity via /attachments.");
        files.MapGet("/{id:guid}", GetAsync)
            .WithSummary("File metadata with fresh short-lived delivery URLs.");

        // Content delivery authenticates via the short-lived token in the URL — browsers
        // load these ambiently (img/src, geotiff.js) and cannot send bearer headers.
        // These two routes are on the documented anonymous allow-list.
        files.MapGet("/{id:guid}/content", ContentAsync).AllowAnonymous()
            .WithSummary("Streams file content (honors Range); token-authenticated.");
        files.MapGet("/{id:guid}/thumbnail", ThumbnailAsync).AllowAnonymous()
            .WithSummary("WebP thumbnail for image files (sizes 160/480/1200); token-authenticated.");

        return api;
    }

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.CanCreateContent)
        {
            return ApiProblems.Forbidden("file.upload_requires_editor");
        }

        if (file.Length == 0)
        {
            return ApiProblems.BadRequest("file.empty", "The uploaded file is empty.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("file.too_large", $"Files are limited to {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        string storagePath;
        await using (var content = file.OpenReadStream())
        {
            storagePath = await fileStore.SaveAsync(content, Path.GetExtension(file.FileName), ct);
        }

        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var stored = new StoredFile
        {
            StoragePath = storagePath,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = mimeType,
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedBy = user.UserId,
            Kind = KindFromMime(mimeType),
        };
        db.StoredFiles.Add(stored);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/files/{stored.Id}", stored.ToDto(tokens));
    }

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
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

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || !await FileAccessRules.CanAccessAsync(db, user, file, ct))
        {
            // Existence of an inaccessible file is not disclosed.
            return ApiProblems.NotFound("file.not_found");
        }

        return TypedResults.Ok(file.ToDto(tokens));
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ContentAsync(
        Guid id,
        string token,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (!tokens.ValidateToken(token, id))
        {
            return ApiProblems.NotFound("file.not_found"); // expired/tampered tokens don't disclose existence
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        return TypedResults.PhysicalFile(
            fileStore.GetAbsolutePath(file.StoragePath),
            contentType: file.MimeType,
            fileDownloadName: file.OriginalName,
            enableRangeProcessing: true);
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ThumbnailAsync(
        Guid id,
        string token,
        int? size,
        SilexGisDbContext db,
        ThumbnailService thumbnails,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (!tokens.ValidateToken(token, id))
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Kind != FileKind.Image)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var effectiveSize = size ?? 480;
        if (!ThumbnailService.AllowedSizes.Contains(effectiveSize))
        {
            return ApiProblems.BadRequest(
                "file.thumbnail_size_unsupported",
                $"Supported sizes: {string.Join(", ", ThumbnailService.AllowedSizes)}.");
        }

        string path;
        try
        {
            path = await thumbnails.GetOrCreateAsync(file.Id, file.StoragePath, effectiveSize, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Unreadable/corrupt image — report as unprocessable rather than crash.
            return ApiProblems.BadRequest("file.thumbnail_failed", "The image could not be processed.");
        }

        return TypedResults.PhysicalFile(path, contentType: "image/webp");
    }

    private static FileKind KindFromMime(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        var m when m.StartsWith("image/") => FileKind.Image,
        "application/pdf" => FileKind.Document,
        var m when m.StartsWith("text/") => FileKind.Document,
        var m when m.Contains("word") || m.Contains("opendocument") => FileKind.Document,
        _ => FileKind.Other,
    };
}
