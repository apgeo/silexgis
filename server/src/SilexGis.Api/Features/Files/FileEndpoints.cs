// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
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

/// <summary>
/// What a resumable upload will be, stated before any of it arrives.
/// </summary>
/// <param name="FileName">The name the finished file takes.</param>
/// <param name="SizeBytes">
/// How large it is. Every limit is judged against this before a byte is accepted, and the
/// completed transfer is measured against it as well — a declaration is a claim, and the
/// bytes are the fact.
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
    Guid? CavingGroupId);

public sealed class UploadSessionOpenRequestValidator : AbstractValidator<UploadSessionOpenRequest>
{
    public UploadSessionOpenRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SizeBytes).GreaterThan(0);
        RuleFor(x => x.RelativePath).MaximumLength(2000);
        RuleFor(x => x.AttachRole).IsInEnum();

        // The pair is all or nothing: a target id with no type names nothing, and a type with
        // no id would attach to whatever the parser happened to produce.
        RuleFor(x => x.AttachEntityId).NotNull()
            .When(x => !string.IsNullOrWhiteSpace(x.AttachEntityType))
            .WithMessage("An attachment target needs both a type and an id.");
    }
}

public static class FileEndpoints
{
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
            .WithSummary("Upload limits this installation applies, and how much room the caller has left.");
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
            .WithSummary("Finishes a resumable upload and files the assembled document.");
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
            ArchiveExpansionRules.Extensions));
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

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        Guid? cavingGroupId,
        Guid? cabinetId,
        string? relativePath,
        string? attachEntityType,
        Guid? attachEntityId,
        AttachmentRole? attachRole,
        Guid? batchId,
        bool? allowDuplicate,
        bool? expandArchive,
        SilexGisDbContext db,
        ContentIntake intake,
        UploadIngestService ingest,
        UploadBatchService batches,
        UploadAllowanceService allowances,
        IFileStore fileStore,
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

        if (await GuardCreationAsync(ctx, db, cavingGroupId, ct) is { } createProblem)
        {
            return createProblem;
        }

        var (destination, destinationProblem) = await UploadDestinationBinding.ResolveAsync(
            new UploadDestinationRequest(
                cabinetId, relativePath, attachEntityType, attachEntityId, attachRole, cavingGroupId),
            ctx,
            db,
            ingest,
            access,
            ct);
        if (destination is null)
        {
            return destinationProblem!;
        }

        if (await GuardBatchAsync(ctx, db, batchId, ct) is { } batchProblem)
        {
            return batchProblem;
        }

        // Every limit, in one place, before a byte is stored. The same rule answers the
        // config route above, so what the client was told and what the server enforces cannot
        // drift apart.
        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        if (UploadLimits.Refuse(allowance, file.FileName, file.Length) is { } refusal)
        {
            return RefusalProblem(refusal, allowance);
        }

        var content = await intake.FromStreamAsync(
            file.OpenReadStream(), file.FileName, file.ContentType, ct);

        var outcome = await ingest.RecordAsync(
            content,
            string.IsNullOrWhiteSpace(relativePath) ? file.FileName : relativePath,
            ctx,
            destination,
            batchId,
            allowDuplicate ?? false,
            ct);

        if (batchId is { } batch)
        {
            await batches.RecordAsync(batch, content.OriginalName, content.SizeBytes, outcome, ct);
        }

        if (outcome.Outcome != UploadItemOutcome.Stored || outcome.Content is null)
        {
            // Bytes land before the write path can decide whether they belong to a document at
            // all. Anything that did not become one takes its bytes back out; nothing
            // references them, and leaving them would grow the store by one dead blob per
            // refusal.
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return OutcomeProblem(outcome);
        }

        if ((expandArchive ?? false) && ArchiveExpansionRules.IsArchive(content.OriginalName))
        {
            await QueueArchiveExpansionAsync(db, batches, ctx.UserId, outcome, destination, ct);
        }

        // Nothing can hang on a row created this instant, so a photo uploaded here places
        // nothing yet and its uploader is holding the file they just sent. Attaching it to a
        // guarded cave is what closes this, and every later read of it asks again.
        return TypedResults.Created(
            $"/api/v1/files/{outcome.Content.File.Id}",
            outcome.Content.File.ToDto(outcome.Content.Version, tokens, mayHaveOriginal: true));
    }

    /// <summary>
    /// Whether this caller may create a document at all, and bind it to the club they named.
    /// </summary>
    /// <remarks>
    /// An upload creates a document — the identity the bytes hang off — so "who may upload" is
    /// Create in the documents domain rather than a staff-grade right over the file store. The
    /// two are different questions: one is authoring content, the other is administering the
    /// store it lands in.
    /// <para>
    /// A club named on the upload is part of that question, exactly as it is for a feature or
    /// a trip: it is what lets a ruleset granting a club's own content admit its members,
    /// rather than requiring an installation-wide right to add anything at all. Binding is
    /// guarded on its own terms — belonging to the club, or holding a rule that names its
    /// documents — so naming a club cannot be a way into one.
    /// </para>
    /// </remarks>
    private static async Task<ProblemHttpResult?> GuardCreationAsync(
        AccessContext ctx, SilexGisDbContext db, Guid? cavingGroupId, CancellationToken ct)
    {
        if (cavingGroupId is { } requestedGroupId)
        {
            if (!await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == requestedGroupId, ct))
            {
                return ApiProblems.BadRequest("document.caving_group_not_found", "The caving group does not exist.");
            }

            if (!CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, requestedGroupId))
            {
                return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
            }
        }

        return CreateRules.MayCreate(ctx, AccessDomain.Documents, cavingGroupId)
            ? null
            : ApiProblems.Forbidden(CreateRules.ForbiddenCode);
    }

    /// <summary>
    /// Whether the caller may count this file into the batch they named — which they may only
    /// do for a batch of their own that is still open.
    /// </summary>
    /// <remarks>
    /// Somebody else's batch is answered as absent rather than forbidden. A batch is a record
    /// of what one person did, and confirming that a given id belongs to somebody would make
    /// the ids an enumerable list of who uploaded when.
    /// </remarks>
    private static async Task<ProblemHttpResult?> GuardBatchAsync(
        AccessContext ctx, SilexGisDbContext db, Guid? batchId, CancellationToken ct)
    {
        if (batchId is not { } id)
        {
            return null;
        }

        var batch = await db.UploadBatches.AsNoTracking()
            .Where(b => b.Id == id && b.StartedByUserId == ctx.UserId)
            .Select(b => new { b.Status })
            .FirstOrDefaultAsync(ct);

        return batch switch
        {
            null => ApiProblems.NotFound(UploadBatchNotFoundCode),
            { Status: not UploadBatchStatus.Open } => ApiProblems.Conflict(
                UploadBatchClosedCode, "The upload batch has been closed."),
            _ => null,
        };
    }

    /// <summary>
    /// Queues the expansion of an uploaded archive into its own batch, and answers the upload
    /// with the archive itself.
    /// </summary>
    /// <remarks>
    /// A batch of its own rather than the one the archive was counted into: the archive is one
    /// file that arrived, and what comes out of it is a separate act with its own report,
    /// possibly hundreds of lines long. Folding them together would make the drop's own
    /// summary — "1 file uploaded" — turn into something else while the person watched.
    /// </remarks>
    private static async Task QueueArchiveExpansionAsync(
        SilexGisDbContext db,
        UploadBatchService batches,
        Guid userId,
        IngestOutcome outcome,
        UploadDestination destination,
        CancellationToken ct)
    {
        var expansion = await batches.OpenAsync(
            userId,
            UploadSource.Archive,
            label: null,
            destination.CabinetId,
            tagId: null,
            sourceDescription: outcome.Content!.File.OriginalName,
            ct);

        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.ArchiveExpansion,
            Payload = JsonSerializer.Serialize(
                new ArchiveExpansionPayload(outcome.Content.File.Id, expansion.Id), JsonSerializerOptions.Web),

            // Named, unlike the readings an upload queues: this one is something a person
            // asked for and is waiting on the result of.
            RequestedBy = userId,
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadVersionAsync(
        Guid id,
        IFormFile file,
        SilexGisDbContext db,
        DocumentWriteService documents,
        ContentIntake intake,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        UploadAllowanceService allowances,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanWriteFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        // A revision is as much stored content as a first upload, so it answers to the same
        // limits. Deliberately not to the duplicate rule: re-uploading a document's own bytes
        // as a new version is a correction somebody meant to make, not an accident.
        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        if (UploadLimits.Refuse(allowance, file.FileName, file.Length) is { } refusal)
        {
            return RefusalProblem(refusal, allowance);
        }

        var content = await intake.FromStreamAsync(file.OpenReadStream(), file.FileName, file.ContentType, ct);
        try
        {
            var stored = await documents.AddVersionAsync(subject.File.DocumentVersionId, content, ctx.UserId, ct);

            // A new revision inherits the document's attachments, so unlike a first upload
            // this one can already be placed by something — and being allowed to change a
            // document is not the same as being allowed to know where its subject is.
            var mayHaveOriginal = (await photos.DisclosableIdsAsync(ctx, [stored.File.Id], ct))
                .Contains(stored.File.Id);
            return TypedResults.Created(
                $"/api/v1/files/{stored.File.Id}",
                stored.File.ToDto(stored.Version, tokens, mayHaveOriginal));
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
        PhotoPositionDisclosure photos,
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

        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject?.CurrentFile is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        if (!await DocumentAccessRules.CanReadAsync(db, access, ctx, subject.Document, subject.CurrentFile, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed
        }

        // Superseded versions are editor-only, and the reason is the content itself: a
        // version is replaced precisely when something in it had to go, so an old version
        // may still hold what the current one no longer says. Reading the document is
        // therefore not enough to read its history — that takes the right to change it.
        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, subject.Document, subject.CurrentFile, ct))
        {
            return ApiProblems.Forbidden("file.versions_forbidden");
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

        // Resolved rather than joined: the label an uploader may be shown under is a rule with
        // one home, and it is never their address.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, versions.Where(x => x.Version.UploadedBy is not null).Select(x => x.Version.UploadedBy!.Value), ct);

        // Asked about the current file and answered for every revision. What places a photo
        // is what the document hangs on, and only the current file is what anything hangs
        // on — a superseded one is attached to nothing, so asking about it directly would
        // find no protection at all and hand out the very original the head is withholding.
        var mayHaveOriginals = (await photos.DisclosableIdsAsync(ctx, [subject.CurrentFile.Id], ct))
            .Contains(subject.CurrentFile.Id);

        IReadOnlyList<FileVersionDto> dtos = [.. versions.Select(x => new FileVersionDto(
            x.File.Id, x.Version.VersionNumber, x.File.OriginalName, x.File.MimeType, x.File.SizeBytes,
            x.Version.UploadedBy,
            x.Version.UploadedBy is { } uploader ? labels.GetValueOrDefault(uploader) : null,
            x.Version.CreatedAt,
            FileMapping.ContentUrl(
                x.File.Id,
                tokens.CreateToken(
                    x.File.Id,
                    x.File.Geom is null || mayHaveOriginals ? FileDelivery.Full : FileDelivery.DerivativesOnly)),
            x.Version.IsCurrent))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteVersionAsync(
        Guid id,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        ThumbnailService thumbnails,
        PageRenderService pages,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject?.CurrentFile is null || !await DocumentAccessRules.CanWriteFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // non-writers: existence not disclosed
        }

        IReadOnlyList<StoredFile> removed;
        try
        {
            removed = await documents.DeleteVersionAsync(subject.File.DocumentVersionId, ct);
        }
        catch (DocumentWriteException e)
        {
            return ToProblem(e);
        }

        // Purge bytes and every cached rendering after the rows are gone (best-effort; missing
        // files are fine). A rendering outliving the file it was drawn from would be a copy of
        // deleted content nothing knows how to find.
        foreach (var purged in removed)
        {
            await fileStore.DeleteAsync(purged.StoragePath, ct);
            thumbnails.Purge(purged.Id);
            pages.Purge(purged.Id);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// A limit refusal, as the Problem Details the caller sees. One mapping, because the same
    /// refusal codes come back from a plain upload, a new revision, the opening of a resumable
    /// session and every line of a bulk import — and a client that had to recognise four
    /// spellings of "too large" would recognise three.
    /// </summary>
    private static ProblemHttpResult RefusalProblem(string code, UploadAllowance allowance) => code switch
    {
        UploadItemReasons.Empty => ApiProblems.BadRequest("file.empty", "The uploaded file is empty."),
        UploadItemReasons.TooLarge => ApiProblems.BadRequest(
            "file.too_large", $"Files are limited to {allowance.MaxUploadBytes / (1024 * 1024)} MB."),
        UploadItemReasons.TypeNotAccepted => ApiProblems.BadRequest(
            "file.type_not_accepted", "This installation does not accept files of this type."),

        // Conflict rather than a bad request: nothing about the request is wrong, and the way
        // out of it is to delete something or be given more room.
        UploadItemReasons.QuotaExceeded => ApiProblems.Conflict(
            "file.quota_exceeded", "There is not enough room left to store this file."),
        _ => ApiProblems.BadRequest(code),
    };

    /// <summary>An ingest outcome that is not a stored file, as the caller sees it.</summary>
    private static ProblemHttpResult OutcomeProblem(IngestOutcome outcome) => outcome.Reason switch
    {
        // The one refusal a client is expected to answer: it names the document already
        // holding these bytes so the warning can say what it collides with, and the caller
        // repeats the upload with allowDuplicate to store it anyway.
        UploadItemReasons.Duplicate => ApiProblems.Conflict(
            DuplicateCode,
            outcome.DuplicateOfDocumentId is { } id
                ? $"This content is already stored as document {id}."
                : "This content is already stored."),
        UploadItemReasons.FilingRefused => ApiProblems.Forbidden(UploadDestinationBinding.FilingForbiddenCode),
        UploadItemReasons.PathRefused => ApiProblems.BadRequest(UploadDestinationBinding.PathRefusedCode),
        null => ApiProblems.BadRequest("file.upload_failed"),
        var reason => ApiProblems.BadRequest(reason),
    };

    /// <summary>Whether a value is a SHA-256 hash and not merely a string somebody sent.</summary>
    private static bool IsHexHash(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    /// <summary>A rejected document write, as the Problem Details the caller sees.</summary>
    private static ProblemHttpResult ToProblem(DocumentWriteException e) => e.Code switch
    {
        // The version vanished between the access check and the write: the caller learns
        // nothing about it beyond what they already knew.
        "file.not_found" => ApiProblems.NotFound(e.Code),
        _ => ApiProblems.Conflict(e.Code, e.Message),
    };

    /// <summary>
    /// Opens a resumable upload: everything is decided here, before a byte moves — who may
    /// create, where it is going, whether it fits, whether the type is accepted.
    /// </summary>
    /// <remarks>
    /// Deciding it all up front is the whole value of the mechanism. The failure it exists to
    /// prevent is a large transfer over a bad link that is refused at the end, and a session
    /// that accepted pieces for ten minutes before checking the quota would reproduce exactly
    /// that failure with more steps.
    /// </remarks>
    private static async Task<Results<Created<UploadSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> OpenUploadAsync(
        UploadSessionOpenRequest request,
        SilexGisDbContext db,
        IFileStore fileStore,
        UploadIngestService ingest,
        UploadAllowanceService allowances,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await GuardCreationAsync(ctx, db, request.CavingGroupId, ct) is { } createProblem)
        {
            return createProblem;
        }

        var (destination, destinationProblem) = await UploadDestinationBinding.ResolveAsync(
            new UploadDestinationRequest(
                request.CabinetId,
                request.RelativePath,
                request.AttachEntityType,
                request.AttachEntityId,
                request.AttachRole,
                request.CavingGroupId),
            ctx,
            db,
            ingest,
            access,
            ct);
        if (destination is null)
        {
            return destinationProblem!;
        }

        if (await GuardBatchAsync(ctx, db, request.BatchId, ct) is { } batchProblem)
        {
            return batchProblem;
        }

        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        if (UploadLimits.Refuse(allowance, request.FileName, request.SizeBytes) is { } refusal)
        {
            return RefusalProblem(refusal, allowance);
        }

        // An empty blob to append into. It is the final resting place of the bytes as well as
        // the partial one: when the last piece lands there is nothing to assemble or move,
        // which is what keeps completing a 400 MB upload from being a 400 MB copy.
        var storagePath = await fileStore.SaveAsync(
            Stream.Null, Path.GetExtension(request.FileName), ct);

        var session = new UploadSession
        {
            UserId = ctx.UserId,
            OriginalName = Path.GetFileName(request.FileName),
            DeclaredSizeBytes = request.SizeBytes,
            StoragePath = storagePath,
            CabinetId = destination.CabinetId,
            UploadBatchId = request.BatchId,
            AttachEntityType = destination.AttachEntityType,
            AttachEntityId = destination.AttachEntityId,
            AttachFeatureId = destination.AttachFeatureId,
            RelativePath = request.RelativePath,
            ExpiresAt = UploadSessionRules.ExpiryFrom(DateTimeOffset.UtcNow),
        };
        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/files/uploads/{session.Id}", ToDto(session));
    }

    private static async Task<Results<Ok<UploadSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadStatusAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await OwnSessionAsync(db, ctx, id, ct);
        return session is null
            ? ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode)
            : TypedResults.Ok(ToDto(session));
    }

    /// <summary>
    /// Appends the next piece. The body is the raw bytes — not multipart — because there is
    /// nothing to describe about a piece except where it goes, and wrapping it would add an
    /// envelope to every one of fifty requests.
    /// </summary>
    /// <remarks>
    /// Answers 200 for a piece that was already held, which is what makes a lost response
    /// survivable: a client that never saw the answer sends the same piece again and is told
    /// the same thing, rather than having its upload ended for repeating itself.
    /// </remarks>
    private static async Task<Results<Ok<UploadSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> AppendChunkAsync(
        Guid id,
        long offset,
        HttpRequest httpRequest,
        SilexGisDbContext db,
        IFileStore fileStore,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await OwnSessionAsync(db, ctx, id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode);
        }

        var length = httpRequest.ContentLength ?? 0;
        switch (UploadSessionRules.Decide(session.ReceivedBytes, offset, length, session.DeclaredSizeBytes))
        {
            case ChunkDisposition.AlreadyHeld:
                return TypedResults.Ok(ToDto(session));

            case ChunkDisposition.OutOfOrder:
                // The response carries how far the file actually got, so the client's next
                // attempt is right rather than another guess.
                return ApiProblems.Conflict(
                    UploadSessionRules.OutOfOrderCode,
                    $"The next piece starts at {session.ReceivedBytes}.");

            case ChunkDisposition.Overflow:
                return ApiProblems.BadRequest(
                    UploadSessionRules.OverflowCode, "The upload is larger than it was declared to be.");

            default:
                break;
        }

        long received;
        try
        {
            received = await fileStore.AppendAsync(session.StoragePath, httpRequest.Body, ct);
        }
        catch (IOException)
        {
            // Two pieces of one upload arriving at once: the store refuses to open the blob
            // twice, and the loser is told where the file ends so it can resend from there.
            return ApiProblems.Conflict(
                UploadSessionRules.OutOfOrderCode, $"The next piece starts at {session.ReceivedBytes}.");
        }

        // Taken from the stored bytes rather than added up here. What actually landed is the
        // only thing a resuming client can safely continue from — a number kept beside the
        // bytes would, on the one occasion it disagreed with them, produce a file with a hole
        // in it that nothing notices until somebody opens it months later.
        session.ReceivedBytes = Math.Min(received, session.DeclaredSizeBytes);
        session.ExpiresAt = UploadSessionRules.ExpiryFrom(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(session));
    }

    /// <summary>
    /// Finishes a resumable upload: the assembled blob is described and filed exactly as a
    /// single-request upload would have been.
    /// </summary>
    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> CompleteUploadAsync(
        Guid id,
        bool? allowDuplicate,
        SilexGisDbContext db,
        ContentIntake intake,
        UploadIngestService ingest,
        UploadBatchService batches,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await OwnSessionAsync(db, ctx, id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode);
        }

        if (!UploadSessionRules.IsComplete(session.ReceivedBytes, session.DeclaredSizeBytes))
        {
            return ApiProblems.Conflict(
                UploadSessionRules.IncompleteCode,
                $"Only {session.ReceivedBytes} of {session.DeclaredSizeBytes} bytes have arrived.");
        }

        // Described now rather than piece by piece: the hash, the format and a photograph's
        // capture facts are all properties of the whole file, and none of them can be read
        // from a fragment.
        var content = await intake.FromStoredAsync(
            session.StoragePath, session.OriginalName, declaredMediaType: null, ct);

        var destination = new UploadDestination(
            session.CabinetId,
            FilingPaths.FolderSegmentsOf(session.RelativePath) ?? [],
            session.AttachEntityType,
            session.AttachEntityId,
            session.AttachFeatureId);

        var outcome = await ingest.RecordAsync(
            content,
            string.IsNullOrWhiteSpace(session.RelativePath) ? session.OriginalName : session.RelativePath,
            ctx,
            destination,
            session.UploadBatchId,
            allowDuplicate ?? false,
            ct);

        if (session.UploadBatchId is { } batch)
        {
            await batches.RecordAsync(batch, content.OriginalName, content.SizeBytes, outcome, ct);
        }

        if (outcome.Outcome != UploadItemOutcome.Stored || outcome.Content is null)
        {
            // The session stays open on a refusal the caller can answer — a duplicate they may
            // choose to store anyway — so the bytes they spent ten minutes sending are still
            // there when they say yes. Anything else is final, and takes them with it.
            if (outcome.Reason == UploadItemReasons.Duplicate)
            {
                return OutcomeProblem(outcome);
            }

            db.UploadSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            await fileStore.DeleteAsync(session.StoragePath, CancellationToken.None);
            return OutcomeProblem(outcome);
        }

        // The blob is now a stored file's own content, so the session must stop claiming it —
        // otherwise the expiry sweep would delete the bytes of a perfectly good document.
        db.UploadSessions.Remove(session);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/files/{outcome.Content.File.Id}",
            outcome.Content.File.ToDto(outcome.Content.Version, tokens, mayHaveOriginal: true));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> AbandonUploadAsync(
        Guid id,
        SilexGisDbContext db,
        IFileStore fileStore,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await OwnSessionAsync(db, ctx, id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode);
        }

        db.UploadSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        await fileStore.DeleteAsync(session.StoragePath, CancellationToken.None);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The caller's own session, or null. Somebody else's is answered as absent rather than
    /// forbidden: a session is a transfer in progress and confirming that an id belongs to
    /// someone would say what they are uploading and when.
    /// </summary>
    private static async Task<UploadSession?> OwnSessionAsync(
        SilexGisDbContext db, AccessContext ctx, Guid id, CancellationToken ct)
    {
        var session = await db.UploadSessions.FirstOrDefaultAsync(
            s => s.Id == id && s.UserId == ctx.UserId, ct);

        // An expired session is answered as absent even before the sweep has collected it. Its
        // bytes are due to go, and letting a client resume onto content that is about to be
        // deleted would produce a document whose bytes vanish an hour later.
        return session is null || session.ExpiresAt <= DateTimeOffset.UtcNow ? null : session;
    }

    private static UploadSessionDto ToDto(UploadSession session) => new(
        session.Id,
        session.ReceivedBytes,
        session.DeclaredSizeBytes,
        UploadSessionRules.SuggestedChunkBytes,
        session.ExpiresAt);

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The same walk the document surface uses, because this response carries a delivery
        // token: a rule written against a document has to bind wherever its bytes are handed
        // out, or it binds nowhere.
        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
        {
            // Existence of an inaccessible file is not disclosed.
            return ApiProblems.NotFound("file.not_found");
        }

        // Reading the picture and placing what it shows are two different rights, so the
        // second is asked for separately before the bytes that answer it are offered.
        var mayHaveOriginal = (await photos.DisclosableIdsAsync(ctx, [subject.File.Id], ct))
            .Contains(subject.File.Id);

        // The copy something made of this file so its pages could be drawn, where one exists.
        // It is looked up here rather than in listings: this is the response a reader's screen
        // is built from, and it is the only one that has to know how to show the document.
        var rendition = await db.StoredFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.ConvertedFromFileId == id, ct);
        return TypedResults.Ok(subject.File.ToDto(subject.Version, tokens, mayHaveOriginal, rendition));
    }

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FileUpdateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The write rule is the document's own, evaluated against the file that document
        // currently serves — the row attachments point at — whichever of its files was named.
        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanWriteFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        // The date describes the revision, not the bytes: editing it through any of a
        // revision's files sets it once, for that revision.
        var version = await db.DocumentVersions.FirstAsync(v => v.Id == subject.File.DocumentVersionId, ct);
        version.DocumentDate = request.DocumentDate;
        await db.SaveChangesAsync(ct);

        // Asked here for the same reason it is asked on the way out of a plain read: being
        // allowed to correct a document's date says nothing about being allowed to know where
        // its subject is, and the two answers must not differ by which verb was used.
        var mayHaveOriginal = (await photos.DisclosableIdsAsync(ctx, [subject.File.Id], ct))
            .Contains(subject.File.Id);
        return TypedResults.Ok(subject.File.ToDto(version, tokens, mayHaveOriginal));
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ContentAsync(
        Guid id,
        string? token,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        FileAccessRecorder accessHistory,
        CancellationToken ct)
    {
        // Nothing here can re-decide anything: a URL that was handed out is a decision
        // already taken, and the token carries no right this route could re-weigh. All it
        // does is honour how far that decision went — a token minted for renderings only
        // opens no original, and it says so the same way an unknown file does, because
        // whether these bytes exist is part of what was being kept back.
        var grant = tokens.Validate(token, id);
        if (grant?.Delivery != FileDelivery.Full)
        {
            return ApiProblems.NotFound("file.not_found"); // expired/tampered tokens don't disclose existence
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // Handing over the stored bytes is the event worth recording: this is the one route
        // that gives out what was uploaded rather than something drawn from it. Recorded
        // after the refusals, so a rejected request leaves no trace of a read that did not
        // happen, and against the person the token was minted for — the only identity a
        // route browsers reach ambiently can have.
        await accessHistory.RecordAsync(id, grant.UserId, ct);

        return TypedResults.PhysicalFile(
            fileStore.GetAbsolutePath(file.StoragePath),
            contentType: file.MimeType,
            fileDownloadName: file.OriginalName,
            enableRangeProcessing: true);
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ThumbnailAsync(
        Guid id,
        string? token,
        int? size,
        SilexGisDbContext db,
        ThumbnailService thumbnails,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        // Either reach opens a rendering: this application produces those bytes and strips
        // every metadata profile out of them, so they show what the picture shows and carry
        // nothing the picture did not.
        if (tokens.Validate(token, id) is null)
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

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> PageRenderAsync(
        Guid id,
        int page,
        string? token,
        int? size,
        SilexGisDbContext db,
        PageRenderService pages,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        // Either reach opens a rendering, exactly as it does for a thumbnail, and for the same
        // reason: these bytes are drawn here, they show what the page shows, and they carry
        // nothing the page did not. That is what makes reading a document in this application
        // safe to offer to someone who may not have the file — a viewer that reached for the
        // stored bytes instead would hand over precisely what was being withheld.
        if (tokens.Validate(token, id) is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || !PageRenderService.CanRender(file.MimeType))
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // Page numbers are the reader's, so they start at one; the upper bound is only checked
        // where the file said how many pages it has, since a renderer asked for a page past the
        // end reports its own failure and there is nothing better to say about it.
        if (page < 1 || (file.PageCount is int count && page > count))
        {
            return ApiProblems.NotFound("file.page_not_found");
        }

        var effectiveSize = size ?? 1200;
        if (!PageRenderService.AllowedSizes.Contains(effectiveSize))
        {
            return ApiProblems.BadRequest(
                "file.render_size_unsupported",
                $"Supported sizes: {string.Join(", ", PageRenderService.AllowedSizes)}.");
        }

        string? path;
        try
        {
            path = await pages.GetOrCreateAsync(file.Id, file.StoragePath, page, effectiveSize, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A page that will not draw is a fact about this file, not a fault in the request.
            return ApiProblems.BadRequest("file.render_failed", "The page could not be drawn.");
        }

        return path is null
            ? ApiProblems.NotFound("file.page_not_found")
            : TypedResults.PhysicalFile(path, contentType: "image/webp");
    }

    /// <summary>
    /// The words of one page, exactly as the reader that produced the search index recorded
    /// them.
    /// </summary>
    /// <remarks>
    /// This is the stream a durable text selection is measured against: a quote captured in a
    /// browser means nothing until it is found in the text the server itself holds, and a
    /// selection that stored a browser's own offsets would break the first time anything
    /// re-read the file. So the offsets are computed against this, and a selection whose words
    /// are not in it is refused rather than stored pointing somewhere else.
    /// <para>
    /// It is guarded exactly as the page picture beside it, and for the same reason: the
    /// picture already shows every one of these words to whoever may open it, so handing them
    /// over as characters discloses nothing the drawing did not. A page nothing has read yet
    /// answers "not found" rather than empty text — the difference between "there are no words
    /// here" and "nobody has looked" is the whole of what a caller needs to know.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PageTextDto>, ProblemHttpResult>> PageTextAsync(
        Guid id,
        int page,
        string? token,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (tokens.Validate(token, id) is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        if (page < 1)
        {
            return ApiProblems.NotFound("file.page_not_found");
        }

        var row = await db.DocumentPages.AsNoTracking()
            .Where(p => p.FileId == id && p.PageNumber == page)
            .Select(p => new { p.Text, p.Extractor })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Extractor is null)
        {
            return ApiProblems.NotFound("file.page_text_not_read");
        }

        return TypedResults.Ok(new PageTextDto(page, row.Text ?? string.Empty));
    }
}
