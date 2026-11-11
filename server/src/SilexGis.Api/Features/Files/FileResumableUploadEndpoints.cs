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
            AttachRole = destination.AttachRole,
            CavingGroupId = destination.CavingGroupId,
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

        if (httpRequest.ContentLength is not { } length)
        {
            // Sent without a length, a piece cut short by a dropped connection looks exactly like
            // a whole one, so none is taken rather than one guessed at.
            return TypedResults.Problem(
                detail: "A piece has to say how long it is.",
                statusCode: StatusCodes.Status411LengthRequired,
                extensions: new Dictionary<string, object?> { ["code"] = UploadSessionRules.LengthRequiredCode });
        }

        // Judged once on what was read, so a replay or a gap is answered without waiting on a
        // piece that is being written, and again below once the content is held.
        if (Refuse(session, offset, length) is { } early)
        {
            return early;
        }

        IExclusiveContentWriter blob;
        try
        {
            blob = await fileStore.OpenExclusiveAsync(session.StoragePath, ct);
        }
        catch (IOException)
        {
            // Two pieces of one upload arriving at once: the store refuses to open the content
            // twice, and the loser is told where the file ends so it can resend from there.
            return OutOfOrder(session);
        }

        await using (blob)
        {
            // Read again while the content is held. Every writer of this session holds it while
            // it reads the session, writes and records the new length, so what is read here is
            // current: a copy of a piece whose first copy has just been recorded is answered as
            // already held instead of being written a second time.
            await db.Entry(session).ReloadAsync(ct);
            if (db.Entry(session).State == EntityState.Detached)
            {
                return ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode);
            }

            if (blob.Length < session.ReceivedBytes)
            {
                // Fewer bytes are stored than the session says. The bytes are what a client can
                // continue from, so the session is brought back to them and the client told.
                session.ReceivedBytes = blob.Length;
                await db.SaveChangesAsync(ct);
                return OutOfOrder(session);
            }

            if (Refuse(session, offset, length) is { } late)
            {
                return late;
            }

            // Written at the session's length, not wherever the file ends: anything beyond it is
            // the remains of a piece that was never recorded, and is written over. A body that
            // could not be read to its end answers false with the piece already cut back out; a
            // store that cannot write throws, and is answered as the server fault it is.
            if (!await blob.WriteAtAsync(session.ReceivedBytes, httpRequest.Body, length, ct))
            {
                return OutOfOrder(session);
            }

            session.ReceivedBytes = offset + length;
            session.ExpiresAt = UploadSessionRules.ExpiryFrom(DateTimeOffset.UtcNow);

            // Recorded before the content is released. Should this save fail, the stored bytes
            // run past the recorded length, and the next piece is written over them from the
            // recorded length.
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToDto(session));
    }

    /// <summary>The answer to a piece that is not to be written, or null when it is.</summary>
    private static Results<Ok<UploadSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>? Refuse(
        UploadSession session, long offset, long length) =>
        UploadSessionRules.Decide(session.ReceivedBytes, offset, length, session.DeclaredSizeBytes) switch
        {
            ChunkDisposition.AlreadyHeld => TypedResults.Ok(ToDto(session)),
            ChunkDisposition.OutOfOrder => OutOfOrder(session),
            ChunkDisposition.Overflow => ApiProblems.BadRequest(
                UploadSessionRules.OverflowCode, "The upload is larger than it was declared to be."),
            _ => null,
        };

    // The response carries how far the file actually got, so the client's next attempt is
    // right rather than another guess — as a member a client reads, beside the sentence a
    // person does.
    private static ProblemHttpResult OutOfOrder(UploadSession session) =>
        ApiProblems.Conflict(
            UploadSessionRules.OutOfOrderCode,
            $"The next piece starts at {session.ReceivedBytes}.",
            ExpectedOffsetMember,
            session.ReceivedBytes);

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
        UploadAllowanceService allowances,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure photos,
        IAppSettingsService settings,
        IAccessService access,
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

        // Asked before the file is read, because they are cheap and reading 400 MB to then
        // refuse it is not.
        UploadDestination? destination = null;
        if (!session.StagedForSync)
        {
            var (resolved, guardProblem) = await GuardCompletionAsync(
                session, ctx, db, ingest, allowances, access, ct);
            if (resolved is null)
            {
                return guardProblem!;
            }

            destination = resolved;
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

        // The document, its batch line and the end of the session are one unit of work. Saved
        // apart, a request cut off (or a save that failed) between the document and the
        // session's removal left a session still claiming the bytes of a committed document,
        // and the expiry sweep deletes a session's bytes a day later. In one transaction a
        // failure anywhere leaves the session and its bytes exactly as they were, so asking
        // again simply completes it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Taken under the session row's lock and before anything is recorded: a second
        // completion of the same session (a client that timed out on a long hash and asked
        // again) waits here until the first has finished, then finds no session and records
        // nothing — rather than both passing the duplicate check and filing two documents over
        // one blob, which the first deletion of either would take from the other. The sweep
        // claims the same row, so it waits too.
        if (!await ClaimForCompletionAsync(db, session.Id, ct))
        {
            return ApiProblems.NotFound(UploadSessionRules.SessionNotFoundCode);
        }

        var outcome = await ingest.RecordAsync(
            content,
            string.IsNullOrWhiteSpace(session.RelativePath) ? session.OriginalName : session.RelativePath,
            ctx,
            destination!,
            session.UploadBatchId,
            allowDuplicate ?? false,
            ct);

        if (session.UploadBatchId is { } batch)
        {
            await batches.RecordAsync(batch, content.OriginalName, content.SizeBytes, outcome, ct);
        }

        var stored = outcome.Outcome == UploadItemOutcome.Stored && outcome.Content is not null;

        // The session stays open on a refusal the caller can answer — a duplicate they may
        // choose to store anyway — so the bytes they spent ten minutes sending are still there
        // when they say yes. Anything else ends it: a stored document now owns the bytes (and
        // the sweep must not see a session claiming them), and any other refusal is final and
        // takes them with it.
        var ends = stored || outcome.Reason != UploadItemReasons.Duplicate;
        if (ends)
        {
            db.UploadSessions.Remove(session);
            await db.SaveChangesAsync(ct);
        }

        // Not the request's token: past this point the work is done, and a client hanging up
        // now must not turn it into a rollback after the answer was decided.
        await transaction.CommitAsync(CancellationToken.None);

        if (!stored)
        {
            if (ends)
            {
                await fileStore.DeleteAsync(session.StoragePath, CancellationToken.None);
            }

            return OutcomeProblem(outcome);
        }

        var made = outcome.Content!;
        return TypedResults.Created(
            $"/api/v1/files/{made.File.Id}",
            made.File.ToDto(made.Version, tokens, await photos.ForUploaderAsync(made.File.Id, ct)));
    }

    /// <summary>
    /// Takes the session row's lock for the rest of the caller's transaction, answering whether
    /// the session is still there and unexpired to take. The update changes nothing; it is how
    /// the lock is asked for without hand-written SQL, and a row another transaction removed
    /// meanwhile is not updated, so the answer is read after that transaction has finished.
    /// </summary>
    private static async Task<bool> ClaimForCompletionAsync(SilexGisDbContext db, Guid sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.UploadSessions
            .Where(s => s.Id == sessionId && s.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, x => x.ExpiresAt), ct);
        return claimed == 1;
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
    /// The destination a finished upload is filed to, asked again exactly as a single-request
    /// upload asks it: who may create in the club it names, where it is filed and what it hangs
    /// on, the batch it counts into, and whether it still fits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The opening answered all of this once, so an upload that was going to be refused is
    /// refused before its bytes are sent. It is asked again because a transfer can last a
    /// day: a right withdrawn, a batch closed or a quota filled by the caller's other uploads
    /// in the meantime would otherwise be reached through the long route and not the short one.
    /// </para>
    /// <para>
    /// A refusal here leaves the session and its bytes in place. What changed may change back
    /// — a grant restored, room made — and the bytes go with the session when it expires.
    /// </para>
    /// </remarks>
    private static async Task<(UploadDestination? Destination, ProblemHttpResult? Problem)> GuardCompletionAsync(
        UploadSession session,
        AccessContext ctx,
        SilexGisDbContext db,
        UploadIngestService ingest,
        UploadAllowanceService allowances,
        IAccessService access,
        CancellationToken ct)
    {
        if (await GuardCreationAsync(ctx, db, session.CavingGroupId, ct) is { } createProblem)
        {
            return (null, createProblem);
        }

        // The target is put back into the words the opening request used, so it is resolved and
        // guarded by the one rule every upload route shares rather than by a copy of it.
        var hasTarget = session.AttachFeatureId is not null || session.AttachEntityType is not null;
        var (destination, destinationProblem) = await UploadDestinationBinding.ResolveAsync(
            new UploadDestinationRequest(
                session.CabinetId,
                session.RelativePath,
                hasTarget ? AttachmentTargets.NameOf(session.AttachFeatureId, session.AttachEntityType) : null,
                session.AttachFeatureId ?? session.AttachEntityId,
                session.AttachRole,
                session.CavingGroupId),
            ctx,
            db,
            ingest,
            access,
            ct);
        if (destination is null)
        {
            return (null, destinationProblem);
        }

        if (await GuardBatchAsync(ctx, db, session.UploadBatchId, ct) is { } batchProblem)
        {
            return (null, batchProblem);
        }

        // An ordinary transfer is not counted against the quota while it arrives, so the room
        // asked about here excludes this file, as it did at the opening.
        var allowance = await allowances.ForAsync(ctx.UserId, ct);
        if (UploadLimits.Refuse(allowance, session.OriginalName, session.DeclaredSizeBytes) is { } refusal)
        {
            return (null, RefusalProblem(refusal, allowance));
        }

        return (destination, null);
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
