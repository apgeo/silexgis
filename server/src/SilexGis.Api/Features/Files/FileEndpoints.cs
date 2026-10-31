// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using ImageMagick;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

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

/// <summary>
/// What a resumable upload will be, stated before any of it arrives.
/// </summary>
/// <param name="FileName">The name the finished file takes.</param>
/// <param name="SizeBytes">
/// How large it is. Every limit is judged against this before a byte is accepted, and the
/// completed transfer is measured against it as well — a declaration is a claim, and the
/// bytes are the fact.
/// </param>
/// <param name="StageForSync">
/// A phone sending a file ahead of the sync upload that will name it. Completing such a
/// transfer makes no document: the bytes are described and wait, under the session's id,
/// until an upload adopts them or the staging retention runs out. A staged transfer has no
/// destination of its own — where it ends up is the upload's to say — so it names no
/// cabinet, folder, attachment, batch or club.
/// </param>
public sealed record UploadSessionOpenRequest(
    string FileName,
    long SizeBytes,
    Guid? CabinetId,
    string? RelativePath,
    string? AttachEntityType,
    Guid? AttachEntityId,
    AttachmentRole? AttachRole,
    Guid? BatchId,
    Guid? CavingGroupId,
    bool? StageForSync = null);

public sealed class UploadSessionOpenRequestValidator : AbstractValidator<UploadSessionOpenRequest>
{
    public UploadSessionOpenRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(500);

        // Staged bytes become a stored file under this name when a sync upload names them without
        // a name of its own, and a stored file's name is kept in a narrower column than a
        // transfer's: refused here rather than failing the whole upload that adopts them.
        RuleFor(x => x.FileName).MaximumLength(255).When(x => x.StageForSync == true);
        RuleFor(x => x.SizeBytes).GreaterThan(0);
        RuleFor(x => x.RelativePath).MaximumLength(2000);
        RuleFor(x => x.AttachRole).IsInEnum();

        // The pair is all or nothing: a target id with no type names nothing, and a type with
        // no id would attach to whatever the parser happened to produce.
        RuleFor(x => x.AttachEntityId).NotNull()
            .When(x => !string.IsNullOrWhiteSpace(x.AttachEntityType))
            .WithMessage("An attachment target needs both a type and an id.");

        // Refused rather than ignored: a phone that names a destination for staged bytes
        // believes it will be honoured, and quietly dropping it would file the photograph
        // somewhere other than where its sender thinks it went.
        RuleFor(x => x)
            .Must(x => x.CabinetId is null
                && string.IsNullOrWhiteSpace(x.RelativePath)
                && string.IsNullOrWhiteSpace(x.AttachEntityType)
                && x.AttachEntityId is null
                && x.AttachRole is null
                && x.BatchId is null
                && x.CavingGroupId is null)
            .When(x => x.StageForSync == true)
            .WithName(nameof(UploadSessionOpenRequest.StageForSync))
            .WithMessage(
                "Bytes staged for a sync upload have no destination of their own; the upload that "
                + "names them says where they go.");
    }
}

public static partial class FileEndpoints
{
    /// <summary>
    /// The member of an <c>upload.chunk_out_of_order</c> refusal naming the byte offset the next
    /// piece has to start at: how far the transfer got.
    /// </summary>
    public const string ExpectedOffsetMember = "expectedOffset";

    /// <summary>The content this caller already holds cannot be stored twice unasked.</summary>
    public const string DuplicateCode = "file.duplicate";

    /// <summary>The named upload batch is not this caller's, or is not there.</summary>
    public const string UploadBatchNotFoundCode = "upload_batch.not_found";

    /// <summary>The named upload batch has been closed and takes nothing more.</summary>
    public const string UploadBatchClosedCode = "upload_batch.closed";

    /// <summary>
    /// Above this size a client should open a resumable session rather than send one request.
    /// Below it the extra round trips cost more than the resumption is worth — a 4 MB
    /// photograph that fails is re-sent in seconds, and a 400 MB scan is not.
    /// </summary>
    private const long ResumableThresholdBytes = 32L * 1024 * 1024;

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
            .WithSummary("Uploads a file (multipart), optionally filing it into a cabinet, attaching it to an object, and counting it into an upload batch.");
        files.MapGet("/config", ConfigAsync)
            .WithSummary(
                "Upload limits this installation applies, how much room the caller has left, and "
                + "whether it runs the service that lays office documents out as PDF.");
        files.MapGet("/duplicate-check", DuplicateCheckAsync)
            .WithSummary("Whether a document the caller may read already holds content with this hash.");

        // Resumable uploads. A survey scan is hundreds of megabytes and the connection it
        // travels over is frequently a phone on a hillside; one request that has to succeed
        // whole is, at that size and over that link, a request that often does not.
        var uploads = files.MapGroup("/uploads");
        uploads.MapPost("/", OpenUploadAsync)
            .WithValidation<UploadSessionOpenRequest>()
            .WithSummary("Opens a resumable upload and returns where to send from.");
        uploads.MapGet("/{id:guid}", UploadStatusAsync)
            .WithSummary("How far a resumable upload has got — the offset to resume from.");
        uploads.MapPut("/{id:guid}", AppendChunkAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(maxRequestBodyBytes))
            .WithSummary("Appends the next piece at the given offset.");
        uploads.MapPost("/{id:guid}/complete", CompleteUploadAsync)
            .WithSummary("Finishes a resumable upload and files the assembled document, or stages its described bytes for a sync upload to adopt.");
        uploads.MapDelete("/{id:guid}", AbandonUploadAsync)
            .WithSummary("Abandons a resumable upload and drops its partial content.");
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
        // load these ambiently (img/src, media elements, geotiff.js) and cannot send bearer
        // headers. Every route mapped below is on the documented anonymous allow-list; adding
        // one here without adding it there is what makes the unauthenticated surface larger
        // than the record of it.
        files.MapGet("/{id:guid}/content", ContentAsync).AllowAnonymous()
            .WithSummary("Streams file content (honors Range); token-authenticated.");
        files.MapGet("/{id:guid}/thumbnail", ThumbnailAsync).AllowAnonymous()
            .WithSummary("WebP thumbnail for image files (sizes 160/480/1200); token-authenticated.");
        files.MapGet("/{id:guid}/pages/{page:int}/render", PageRenderAsync).AllowAnonymous()
            .WithSummary("WebP picture of one page of a paged document (sizes 160/480/1200/2400); token-authenticated.");

        files.MapGet("/{id:guid}/pages/{page:int}/text", PageTextAsync).AllowAnonymous()
            .WithSummary("Extracted text of one page, as the search index holds it; token-authenticated.");

        return api;
    }

    /// <summary>
    /// Limits the client needs to know before it starts an upload, and how much room this
    /// caller has left. Served rather than compiled in, so a client build cannot disagree with
    /// the server it is talking to and let a user watch a large file transfer only to be
    /// refused at the end.
    /// </summary>
    private static async Task<Results<Ok<FileConfigDto>, UnauthorizedHttpResult>> ConfigAsync(
        UploadAllowanceService allowances,
        IAccessContextAccessor accessAccessor,
        IDocumentConverter converter,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        return TypedResults.Ok(new FileConfigDto(
            allowance.MaxUploadBytes,
            allowance.RemainingBytes,
            allowance.UserQuotaBytes,
            allowance.UserQuotaBytes is null ? null : allowance.UserUsedBytes,
            allowance.AcceptedExtensions,
            allowance.RefusedExtensions,
            UploadSessionRules.SuggestedChunkBytes,
            ResumableThresholdBytes,
            ArchiveExpansionRules.Extensions,
            converter.IsConfigured));
    }

    /// <summary>
    /// Whether the caller already holds this content. Asked by hash before a transfer, so the
    /// warning arrives instead of the bytes.
    /// </summary>
    /// <remarks>
    /// It is only ever advice, and the refusal that matters is the one the upload itself
    /// makes: a client that skipped this is still refused with the same code, so the warning
    /// cannot be bypassed by not asking for it.
    /// </remarks>
    private static async Task<Results<Ok<DuplicateCheckDto>, UnauthorizedHttpResult, ProblemHttpResult>> DuplicateCheckAsync(
        string sha256,
        SilexGisDbContext db,
        UploadIngestService ingest,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!IsHexHash(sha256))
        {
            return ApiProblems.BadRequest("file.sha256_invalid", "A SHA-256 hash is 64 hex characters.");
        }

        var duplicate = await ingest.VisibleDuplicateAsync(ctx, sha256.ToLowerInvariant(), ct);
        if (duplicate is not { } documentId)
        {
            return TypedResults.Ok(new DuplicateCheckDto(false, null, null));
        }

        var title = await db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => d.Title)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new DuplicateCheckDto(true, documentId, title));
    }
}
