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

public static partial class FileEndpoints
{
    private static async Task<Results<Created<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        Guid? cavingGroupId,
        Guid? cabinetId,
        string? relativePath,
        string? attachEntityType,
        Guid? attachEntityId,
        string? attachRole,
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
        PhotoPositionDisclosure photos,
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

        // Text on the wire, parsed by name: binding the enum directly would refuse the camelCase
        // spelling the responses use, and refuse it as a server fault.
        AttachmentRole? role = null;
        if (!string.IsNullOrWhiteSpace(attachRole))
        {
            if (!RouteEnums.TryParse<AttachmentRole>(attachRole, out var parsedRole))
            {
                return RouteEnums.Invalid<AttachmentRole>(nameof(attachRole));
            }

            role = parsedRole;
        }

        var (destination, destinationProblem) = await UploadDestinationBinding.ResolveAsync(
            new UploadDestinationRequest(
                cabinetId, relativePath, attachEntityType, attachEntityId, role, cavingGroupId),
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

        // The bytes are already in the store, and only a committed document references them. So
        // the recording is one transaction, and anything that stops it before the commit — a
        // failed save, a client that hangs up during the duplicate walk — rolls the rows back
        // and takes the bytes out with it, rather than leaving a blob no row and no sweep will
        // ever find.
        IngestOutcome outcome;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                outcome = await ingest.RecordAsync(
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
            }
            catch
            {
                // Uncommitted, so the rows go back with the transaction when it is disposed.
                await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
                throw;
            }

            await transaction.CommitAsync(CancellationToken.None);
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
            outcome.Content.File.ToDto(
                outcome.Content.Version, tokens, await photos.ForUploaderAsync(outcome.Content.File.Id, ct)));
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
        DocumentFile stored;
        try
        {
            stored = await documents.AddVersionAsync(subject.File.DocumentVersionId, content, ctx.UserId, ct);
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
        catch
        {
            // Any other failure of the revision's own transaction leaves no row pointing at the
            // bytes either — a save that broke, a client that hung up — so they go too.
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            throw;
        }

        // A corrected compilation log arrives this way — as a later revision of the archived
        // source, not as a second one. The quality figures read out of the previous revision
        // describe a run this document no longer holds, so the record that carries them is
        // pointed at the new bytes and read again rather than being left to show them under a
        // revision number that no longer matches.
        if (await SurveyCompilationRequeue.ForNewRevisionAsync(
            db, stored.Version.DocumentId, stored.File.Id, stored.Version.VersionNumber, ctx.UserId, ct) > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // A new revision inherits the document's attachments, so unlike a first upload
        // this one can already be placed by something — and being allowed to change a
        // document is not the same as being allowed to know where its subject is.
        var reach = await photos.ReachAsync(ctx, [stored.File.Id], ct);
        return TypedResults.Created(
            $"/api/v1/files/{stored.File.Id}",
            stored.File.ToDto(stored.Version, tokens, reach));
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

        // A point cloud's revisions are each a position when their header names a coordinate
        // system — or until a run has read them — and every row here carries a delivery
        // address, so the history of a scan's document is answered only when every revision
        // may be handed to this caller; asked per revision, as the file route asks per file.
        foreach (var x in versions)
        {
            if (await SilexGis.Infrastructure.Scans.PointCloudFileAccess.WithheldWithoutExactLocationAsync(
                    db, access, ctx, new FileSubject(x.File, x.Version, subject.Document, subject.CurrentFile), ct))
            {
                return ApiProblems.NotFound("file.not_found");
            }
        }

        // Asked about the current file and answered for every revision. What places a photo
        // is what the document hangs on, and only the current file is what anything hangs
        // on — a superseded one is attached to nothing, so asking about it directly would
        // find no protection at all and hand out the very original the head is withholding.
        var reach = await photos.ReachAsync(ctx, [subject.CurrentFile.Id], ct);
        var mayHaveOriginals = reach.PositionDisclosable(subject.CurrentFile.Id);

        IReadOnlyList<FileVersionDto> dtos = [.. versions.Select(x => new FileVersionDto(
            x.File.Id, x.Version.VersionNumber, x.File.OriginalName, x.File.MimeType, x.File.SizeBytes,
            x.Version.UploadedBy,
            x.Version.UploadedBy is { } uploader ? labels.GetValueOrDefault(uploader) : null,
            x.Version.CreatedAt,
            FileMapping.ContentUrl(
                x.File.Id,
                tokens.CreateToken(x.File.Id, FileDeliveries.Of(reach.OriginalOf(x.File, mayHaveOriginals)))),
            x.Version.IsCurrent))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteVersionAsync(
        Guid id,
        SilexGisDbContext db,
        DocumentWriteService documents,
        StoredContentRemover content,
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

        // Bytes and every cached rendering go after the rows, best effort, by the same remover
        // the purge sweep uses: the version is already gone, so a file that will not delete is
        // logged rather than turned into a failure of a delete that happened, and it does not
        // stop the rest from being cleared. A rendering outliving the file it was drawn from
        // would be a copy of deleted content nothing knows how to find.
        await content.DropAsync(removed);

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
        // repeats the upload with allowDuplicate to store it anyway. The identifier travels as a
        // member of its own as well as inside the sentence — the sentence is for a person to
        // read, and a client that reads it back out of the prose breaks the day it is reworded
        // or translated.
        UploadItemReasons.Duplicate => ApiProblems.Conflict(
            DuplicateCode,
            outcome.DuplicateOfDocumentId is { } id
                ? $"This content is already stored as document {id}."
                : "This content is already stored.",
            "duplicateOfDocumentId",
            outcome.DuplicateOfDocumentId),
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
}
