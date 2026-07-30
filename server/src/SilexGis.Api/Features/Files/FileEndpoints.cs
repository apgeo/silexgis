// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Files;

/// <summary>
/// User-settable file metadata. Content stays immutable; only these fields are editable.
/// A full-DTO update (not a partial patch): a null field clears that value.
/// </summary>
public sealed record FileUpdateRequest(DateOnly? DocumentDate);

public sealed class FileUpdateRequestValidator : AbstractValidator<FileUpdateRequest>
{
    public FileUpdateRequestValidator()
    {
        // "When the document/photo is from" cannot be in the future. Allow one day of slack so a
        // caller in a UTC-ahead timezone (the app's own locale is UTC+2/+3) is not rejected when
        // picking their local "today" while the server clock is still on the previous UTC day.
        RuleFor(x => x.DocumentDate)
            .Must(d => d is null || d <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("The document date cannot be in the future.");
    }
}

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
        files.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<FileUpdateRequest>()
            .WithSummary("Updates user-set file metadata (document date); requires file-write access.");

        files.MapPost("/{id:guid}/versions", UploadVersionAsync)
            .DisableAntiforgery()
            .WithSummary("Uploads a new version onto a file's head; repoints its attachments to it.");
        files.MapGet("/{id:guid}/versions", ListVersionsAsync)
            .WithSummary("Version chain of a file (editor-only; superseded versions may hold removed content).");
        files.MapDelete("/{id:guid}", DeleteVersionAsync)
            .WithSummary("Deletes a superseded (non-head) file version.");

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
        IPhotoGeotagReader geotagReader,
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

        if (ValidateSize(file) is { } sizeProblem)
        {
            return sizeProblem;
        }

        var (storagePath, sha256, mimeType) = await SaveContentAsync(file, fileStore, ct);
        var kind = KindFromMime(mimeType);
        var stored = new StoredFile
        {
            StoragePath = storagePath,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = mimeType,
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedBy = user.UserId,
            Kind = kind,
            Geom = kind == FileKind.Image ? geotagReader.TryReadPoint(fileStore.GetAbsolutePath(storagePath)) : null,
        };
        stored.VersionGroupId = stored.Id; // first version in its own chain
        db.StoredFiles.Add(stored);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/files/{stored.Id}", stored.ToDto(tokens));
    }

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadVersionAsync(
        Guid id,
        IFormFile file,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IPhotoGeotagReader geotagReader,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var head = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (head is null || !await FileAccessRules.CanWriteFileAsync(db, user, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        var maxVersion = await db.StoredFiles
            .Where(f => f.VersionGroupId == head.VersionGroupId)
            .MaxAsync(f => f.VersionNumber, ct);
        if (head.VersionNumber != maxVersion)
        {
            return ApiProblems.Conflict("file.not_head", "A newer version already exists; upload onto the current head.");
        }

        if (ValidateSize(file) is { } sizeProblem)
        {
            return sizeProblem;
        }

        var (storagePath, sha256, mimeType) = await SaveContentAsync(file, fileStore, ct);
        var kind = KindFromMime(mimeType);
        var stored = new StoredFile
        {
            StoragePath = storagePath,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = mimeType,
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedBy = user.UserId,
            Kind = kind,
            VersionGroupId = head.VersionGroupId,
            VersionNumber = maxVersion + 1,
            DocumentDate = head.DocumentDate, // the document's date carries across versions
            // Geotag is content-derived, so re-read it from this version's own EXIF.
            Geom = kind == FileKind.Image ? geotagReader.TryReadPoint(fileStore.GetAbsolutePath(storagePath)) : null,
        };
        db.StoredFiles.Add(stored);

        // Repoint attachments from the old head to the new one, tracked so the change is audited
        // — entity timelines get a "document updated" event from the Attachment FileId diff.
        var attachments = await db.Attachments.Where(a => a.FileId == head.Id).ToListAsync(ct);
        foreach (var attachment in attachments)
        {
            attachment.FileId = stored.Id;
        }

        // Tags belong to the document, not a specific version — move file taggings to the new head.
        var taggings = await db.Taggings
            .Where(t => t.EntityType == AttachedEntityType.StoredFile && t.EntityId == head.Id)
            .ToListAsync(ct);
        foreach (var tagging in taggings)
        {
            tagging.EntityId = stored.Id;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two uploads raced onto the same head: both computed the same next version number
            // and the unique (version_group_id, version_number) index rejected the loser. Surface
            // it as the same conflict a sequential not-head upload would get.
            return ApiProblems.Conflict("file.not_head", "A newer version already exists; upload onto the current head.");
        }

        return TypedResults.Created($"/api/v1/files/{stored.Id}", stored.ToDto(tokens));
    }

    private static async Task<Results<Ok<IReadOnlyList<FileVersionDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListVersionsAsync(
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
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var chain = await db.StoredFiles.AsNoTracking()
            .Where(f => f.VersionGroupId == file.VersionGroupId)
            .OrderByDescending(f => f.VersionNumber)
            .ToListAsync(ct);

        var head = chain[0]; // ordered desc → the head is first
        if (!await FileAccessRules.CanAccessAsync(db, user, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed
        }

        if (!await FileAccessRules.CanWriteFileAsync(db, user, head, ct))
        {
            return ApiProblems.Forbidden("file.versions_forbidden"); // old versions are editor-only
        }

        // Resolved rather than joined: the label an uploader may be shown under is a rule with
        // one home, and it is never their address.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, chain.Where(f => f.UploadedBy is not null).Select(f => f.UploadedBy!.Value), ct);

        IReadOnlyList<FileVersionDto> dtos = [.. chain.Select(f => new FileVersionDto(
            f.Id, f.VersionNumber, f.OriginalName, f.MimeType, f.SizeBytes,
            f.UploadedBy, f.UploadedBy is { } uploader ? labels.GetValueOrDefault(uploader) : null, f.CreatedAt,
            FileMapping.ContentUrl(f.Id, tokens.CreateToken(f.Id)), f.Id == head.Id))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteVersionAsync(
        Guid id,
        SilexGisDbContext db,
        IFileStore fileStore,
        ThumbnailService thumbnails,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var head = await db.StoredFiles.AsNoTracking()
            .Where(f => f.VersionGroupId == file.VersionGroupId)
            .OrderByDescending(f => f.VersionNumber)
            .FirstAsync(ct);
        if (!await FileAccessRules.CanWriteFileAsync(db, user, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // non-writers: existence not disclosed
        }

        if (file.Id == head.Id)
        {
            return ApiProblems.Conflict("file.head_undeletable", "The current version cannot be deleted; upload a new version instead.");
        }

        // A version upload repoints attachments to the new head, so a superseded version normally
        // has none. If one still points here (e.g. an attachment created directly against an old
        // id), refuse — the attachments→files FK cascades, so deleting would silently drop it.
        if (await db.Attachments.AsNoTracking().AnyAsync(a => a.FileId == file.Id, ct))
        {
            return ApiProblems.Conflict("file.version_in_use", "This version is still attached to an entity and cannot be deleted.");
        }

        db.StoredFiles.Remove(file);
        await db.SaveChangesAsync(ct);

        // Purge bytes and cached thumbnails after the row is gone (best-effort; missing files are fine).
        await fileStore.DeleteAsync(file.StoragePath, ct);
        thumbnails.Purge(file.Id);

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult? ValidateSize(IFormFile file) => file.Length switch
    {
        0 => ApiProblems.BadRequest("file.empty", "The uploaded file is empty."),
        > MaxUploadBytes => ApiProblems.BadRequest("file.too_large", $"Files are limited to {MaxUploadBytes / (1024 * 1024)} MB."),
        _ => null,
    };

    private static async Task<(string StoragePath, string Sha256, string MimeType)> SaveContentAsync(
        IFormFile file, IFileStore fileStore, CancellationToken ct)
    {
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
        return (storagePath, sha256, mimeType);
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

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FileUpdateRequest request,
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

        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // The file-write rule is evaluated against the chain head (the row attachments point at).
        var head = await db.StoredFiles.AsNoTracking()
            .Where(f => f.VersionGroupId == file.VersionGroupId)
            .OrderByDescending(f => f.VersionNumber)
            .FirstAsync(ct);
        if (!await FileAccessRules.CanWriteFileAsync(db, user, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        file.DocumentDate = request.DocumentDate;
        await db.SaveChangesAsync(ct);
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
