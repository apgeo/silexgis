// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
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
    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/files").WithTags("Files");

        // The cap is installation configuration, so the request-size metadata that keeps the
        // web server from cutting a large upload off before any handler runs has to be built
        // from the same value. Read here rather than injected: metadata is fixed when the
        // route is mapped, long before a request exists.
        var maxRequestBodyBytes = ((IEndpointRouteBuilder)api).ServiceProvider
            .GetRequiredService<IOptions<FilesOptions>>().Value.MaxRequestBodyBytes;

        files.MapPost("/", UploadAsync)
            .DisableAntiforgery() // bearer-token API; no cookie-form surface to forge
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(maxRequestBodyBytes))
            .WithSummary("Uploads a file (multipart); attach it to an entity via /attachments.");
        files.MapGet("/config", ConfigAsync)
            .WithSummary("Upload limits this installation applies.");
        files.MapGet("/{id:guid}", GetAsync)
            .WithSummary("File metadata with fresh short-lived delivery URLs.");
        files.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<FileUpdateRequest>()
            .WithSummary("Updates user-set file metadata (document date); requires file-write access.");

        files.MapPost("/{id:guid}/versions", UploadVersionAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(maxRequestBodyBytes))
            .WithSummary("Uploads a new version of a file's document; repoints its attachments to it.");
        files.MapGet("/{id:guid}/versions", ListVersionsAsync)
            .WithSummary("Versions of a file's document (editor-only; superseded versions may hold removed content).");
        files.MapDelete("/{id:guid}", DeleteVersionAsync)
            .WithSummary("Deletes a superseded (non-current) version of a file's document.");

        // Content delivery authenticates via the short-lived token in the URL — browsers
        // load these ambiently (img/src, geotiff.js) and cannot send bearer headers.
        // These two routes are on the documented anonymous allow-list.
        files.MapGet("/{id:guid}/content", ContentAsync).AllowAnonymous()
            .WithSummary("Streams file content (honors Range); token-authenticated.");
        files.MapGet("/{id:guid}/thumbnail", ThumbnailAsync).AllowAnonymous()
            .WithSummary("WebP thumbnail for image files (sizes 160/480/1200); token-authenticated.");

        return api;
    }

    /// <summary>
    /// Limits the client needs to know before it starts an upload. Served rather than
    /// compiled in, so a client build cannot disagree with the server it is talking to and
    /// let a user watch a large file transfer only to be refused at the end.
    /// </summary>
    private static Ok<FileConfigDto> ConfigAsync(IOptions<FilesOptions> options) =>
        TypedResults.Ok(new FileConfigDto(options.Value.MaxUploadBytes));

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IPhotoGeotagReader geotagReader,
        IContentMetadataReader metadataReader,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        IOptions<FilesOptions> filesOptions,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // An upload creates a document — the identity the bytes hang off — so "who may
        // upload" is Create in the documents domain rather than a staff-grade right over
        // the file store. The two are different questions: one is authoring content, the
        // other is administering the store it lands in.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (ValidateSize(file, filesOptions.Value.MaxUploadBytes) is { } sizeProblem)
        {
            return sizeProblem;
        }

        var content = await ReadContentAsync(file, fileStore, geotagReader, metadataReader, ct);
        var stored = documents.Create(content, content.OriginalName, user.UserId, user.UserId);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/files/{stored.File.Id}", stored.File.ToDto(stored.Version, tokens));
    }

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadVersionAsync(
        Guid id,
        IFormFile file,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IPhotoGeotagReader geotagReader,
        IContentMetadataReader metadataReader,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IOptions<FilesOptions> filesOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var head = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (head is null || !await FileAccessRules.CanWriteFileAsync(db, access, ctx, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        if (ValidateSize(file, filesOptions.Value.MaxUploadBytes) is { } sizeProblem)
        {
            return sizeProblem;
        }

        var content = await ReadContentAsync(file, fileStore, geotagReader, metadataReader, ct);
        try
        {
            var stored = await documents.AddVersionAsync(head.DocumentVersionId, content, ctx.UserId, ct);
            return TypedResults.Created($"/api/v1/files/{stored.File.Id}", stored.File.ToDto(stored.Version, tokens));
        }
        catch (DocumentWriteException e)
        {
            // Uploads stream straight into the store, so the bytes land before the write path
            // can decide whether they belong to a version at all. A rejected upload takes them
            // back out; nothing references them, and leaving them would grow the store by one
            // dead blob per conflict.
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return ToProblem(e);
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<FileVersionDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListVersionsAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await DocumentQueries.FileWithVersionAsync(db, id, ct);
        if (subject is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // One row per revision, newest first, each represented by its own oldest file — the
        // one the upload produced, before anything derived from it.
        var rows = await (from version in db.DocumentVersions.AsNoTracking()
                          join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                          where version.DocumentId == subject.Version.DocumentId
                          select new { Version = version, File = file }).ToListAsync(ct);
        var versions = rows
            .GroupBy(r => r.Version.Id)
            .Select(g => new
            {
                Version = g.First().Version,
                File = g.OrderBy(r => r.File.CreatedAt).ThenBy(r => r.File.Id).First().File,
            })
            .OrderByDescending(x => x.Version.VersionNumber)
            .ToList();

        var current = versions.FirstOrDefault(x => x.Version.IsCurrent);
        if (current is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        if (!await FileAccessRules.CanAccessAsync(db, access, ctx, current.File, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed
        }

        if (!await FileAccessRules.CanWriteFileAsync(db, access, ctx, current.File, ct))
        {
            return ApiProblems.Forbidden("file.versions_forbidden"); // old versions are editor-only
        }

        // Resolved rather than joined: the label an uploader may be shown under is a rule with
        // one home, and it is never their address.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, versions.Where(x => x.Version.UploadedBy is not null).Select(x => x.Version.UploadedBy!.Value), ct);

        IReadOnlyList<FileVersionDto> dtos = [.. versions.Select(x => new FileVersionDto(
            x.File.Id, x.Version.VersionNumber, x.File.OriginalName, x.File.MimeType, x.File.SizeBytes,
            x.Version.UploadedBy,
            x.Version.UploadedBy is { } uploader ? labels.GetValueOrDefault(uploader) : null,
            x.Version.CreatedAt,
            FileMapping.ContentUrl(x.File.Id, tokens.CreateToken(x.File.Id)),
            x.Version.IsCurrent))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteVersionAsync(
        Guid id,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        ThumbnailService thumbnails,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        var head = file is null ? null : await DocumentQueries.CurrentFileOfDocumentAsync(db, id, ct);
        if (file is null || head is null || !await FileAccessRules.CanWriteFileAsync(db, access, ctx, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // non-writers: existence not disclosed
        }

        IReadOnlyList<StoredFile> removed;
        try
        {
            removed = await documents.DeleteVersionAsync(file.DocumentVersionId, ct);
        }
        catch (DocumentWriteException e)
        {
            return ToProblem(e);
        }

        // Purge bytes and cached thumbnails after the rows are gone (best-effort; missing files are fine).
        foreach (var purged in removed)
        {
            await fileStore.DeleteAsync(purged.StoragePath, ct);
            thumbnails.Purge(purged.Id);
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult? ValidateSize(IFormFile file, long maxUploadBytes) => file.Length switch
    {
        0 => ApiProblems.BadRequest("file.empty", "The uploaded file is empty."),
        var length when length > maxUploadBytes => ApiProblems.BadRequest(
            "file.too_large", $"Files are limited to {maxUploadBytes / (1024 * 1024)} MB."),
        _ => null,
    };

    /// <summary>A rejected document write, as the Problem Details the caller sees.</summary>
    private static ProblemHttpResult ToProblem(DocumentWriteException e) => e.Code switch
    {
        // The version vanished between the access check and the write: the caller learns
        // nothing about it beyond what they already knew.
        "file.not_found" => ApiProblems.NotFound(e.Code),
        _ => ApiProblems.Conflict(e.Code, e.Message),
    };

    /// <summary>
    /// Writes the upload to the store and describes it. Both the format and the geotag are
    /// content-derived: they are read from the bytes that just landed rather than from what
    /// the upload claimed about itself or carried across from anything.
    /// </summary>
    private static async Task<StoredContent> ReadContentAsync(
        IFormFile file,
        IFileStore fileStore,
        IPhotoGeotagReader geotagReader,
        IContentMetadataReader metadataReader,
        CancellationToken ct)
    {
        var (storagePath, sha256, format) = await SaveContentAsync(file, fileStore, ct);
        var absolutePath = fileStore.GetAbsolutePath(storagePath);
        return new StoredContent(
            storagePath,
            Path.GetFileName(file.FileName),
            format.MimeType,
            file.Length,
            sha256,
            format.Kind,
            // Reading EXIF is gated on the sniffed kind, so a photo uploaded under the wrong
            // media type still has its capture location found — and, more importantly, that
            // location is then protected like any other, instead of quietly going unread.
            format.Kind == FileKind.Image ? geotagReader.TryReadPoint(absolutePath) : null,
            await metadataReader.ReadAsync(absolutePath, format.Kind, ct));
    }

    private static async Task<(string StoragePath, string Sha256, FileFormat Format)> SaveContentAsync(
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

        // Decided from the stored bytes: the browser's media type and the file name are
        // whatever the uploader sent, and text extraction later dispatches on the recorded
        // format — a file filed under the wrong one is handed to a reader that cannot read
        // it and silently produces nothing.
        FileFormat format;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            format = await ContentSniffer.DetectAsync(saved, file.ContentType, file.FileName, ct);
        }

        return (storagePath, sha256, format);
    }

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await DocumentQueries.FileWithVersionAsync(db, id, ct);
        if (subject is null || !await FileAccessRules.CanAccessAsync(db, access, ctx, subject.File, ct))
        {
            // Existence of an inaccessible file is not disclosed.
            return ApiProblems.NotFound("file.not_found");
        }

        return TypedResults.Ok(subject.File.ToDto(subject.Version, tokens));
    }

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FileUpdateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // The file-write rule is evaluated against the file the document currently serves —
        // the row attachments point at.
        var head = await DocumentQueries.CurrentFileOfDocumentAsync(db, id, ct);
        if (head is null || !await FileAccessRules.CanWriteFileAsync(db, access, ctx, head, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        // The date describes the revision, not the bytes: editing it through any of a
        // revision's files sets it once, for that revision.
        var version = await db.DocumentVersions.FirstAsync(v => v.Id == file.DocumentVersionId, ct);
        version.DocumentDate = request.DocumentDate;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(file.ToDto(version, tokens));
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
}
