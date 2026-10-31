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

        if (request.StageForSync == true)
        {
            return await OpenStagedUploadAsync(request, ctx, db, fileStore, allowances, ct);
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

    /// <summary>
    /// Opens a transfer of bytes a phone will name in a later sync upload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Who may stage is asked as broadly as it can be answered here: the right to create a
    /// document anywhere — installation-wide, or in one of the caller's own clubs. The real
    /// question, whether this caller may make this document under this sync set's club, belongs
    /// to the upload that adopts the bytes, because only it knows the set. Asking nothing at all
    /// here would let an account that may never add a document fill the disk for a week.
    /// </para>
    /// <para>
    /// The quota is judged exactly as for any upload, before a byte moves, and staged bytes
    /// count against it from this moment.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<UploadSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> OpenStagedUploadAsync(
        UploadSessionOpenRequest request,
        AccessContext ctx,
        SilexGisDbContext db,
        IFileStore fileStore,
        UploadAllowanceService allowances,
        CancellationToken ct)
    {
        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents)
            && CreateRules.CavingGroupsToCreateIn(ctx, AccessDomain.Documents).Count == 0)
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        if (UploadLimits.Refuse(allowance, request.FileName, request.SizeBytes) is { } refusal)
        {
            return RefusalProblem(refusal, allowance);
        }

        var storagePath = await fileStore.SaveAsync(
            Stream.Null, Path.GetExtension(request.FileName), ct);

        // While the bytes are still arriving this is an ordinary transfer, with the ordinary
        // idle lifetime: the staging retention starts when the last piece has landed and the
        // content has been described, not when somebody announced a file.
        var session = new UploadSession
        {
            UserId = ctx.UserId,
            OriginalName = Path.GetFileName(request.FileName),
            DeclaredSizeBytes = request.SizeBytes,
            StoragePath = storagePath,
            StagedForSync = true,
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
                // attempt is right rather than another guess — as a member a client reads, beside
                // the sentence a person does.
                return ApiProblems.Conflict(
                    UploadSessionRules.OutOfOrderCode,
                    $"The next piece starts at {session.ReceivedBytes}.",
                    ExpectedOffsetMember,
                    session.ReceivedBytes);

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
                UploadSessionRules.OutOfOrderCode,
                $"The next piece starts at {session.ReceivedBytes}.",
                ExpectedOffsetMember,
                session.ReceivedBytes);
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
    /// single-request upload would have been — or, for bytes a phone staged for a sync upload,
    /// described and left waiting for the upload that names them.
    /// </summary>
    private static async Task<Results<Created<FileDto>, Ok<StagedUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> CompleteUploadAsync(
        Guid id,
        bool? allowDuplicate,
        SilexGisDbContext db,
        ContentIntake intake,
        UploadIngestService ingest,
        UploadBatchService batches,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure photos,
        IAppSettingsService settings,
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

        // Completing staged bytes twice answers the same thing twice, so a phone that lost the
        // first answer can ask again; it neither re-reads the file nor moves the retention.
        if (session.StagedAt is not null)
        {
            return TypedResults.Ok(ToStagedDto(session));
        }

        // Described now rather than piece by piece: the hash, the format and a photograph's
        // capture facts are all properties of the whole file, and none of them can be read
        // from a fragment.
        var content = await intake.FromStoredAsync(
            session.StoragePath, session.OriginalName, declaredMediaType: null, ct);

        if (session.StagedForSync)
        {
            // No document: the row stays as the staged upload, carrying what the adopting
            // upload checks. The camera facts are not copied onto it — they are read again from
            // these same bytes, by the same reader, when the document is made, so there is one
            // reading of a photograph's metadata rather than a stored copy that could drift.
            var now = DateTimeOffset.UtcNow;
            var sync = await settings.GetSyncAsync(ct);
            session.StagedAt = now;
            session.Sha256 = content.Sha256;
            session.MimeType = content.MimeType;
            session.Kind = content.Kind;
            session.ExpiresAt = UploadSessionRules.StagedExpiryFrom(now, sync.StagedUploadRetentionDays);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToStagedDto(session));
        }

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
            outcome.Content.File.ToDto(
                outcome.Content.Version, tokens, await photos.ForUploaderAsync(outcome.Content.File.Id, ct)));
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
        session.ExpiresAt,
        session.StagedForSync,
        session.StagedAt,
        session.Sha256);

    private static StagedUploadDto ToStagedDto(UploadSession session) => new(
        session.Id,
        session.OriginalName,
        session.DeclaredSizeBytes,
        session.Sha256!,
        session.MimeType!,
        session.Kind!.Value,
        session.StagedAt!.Value,
        session.ExpiresAt);
}
