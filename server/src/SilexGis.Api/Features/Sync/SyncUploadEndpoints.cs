// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The write half of the mobile contract: one batch of rows, applied in the order they were
/// given, arbitrated row by row against what this server already holds.
/// </summary>
/// <remarks>
/// <para>
/// Three properties hold this endpoint together, and each of them exists because of a way an
/// offline device breaks. A batch carries an identifier the device minted, so an answer lost on
/// the way back costs a resend rather than a duplicate registry. A row carries the identifier the
/// device gave it, adopted verbatim, so neither side ever has to translate the other's names. And
/// a row carries the server revision the device last saw, which is the only thing compared when
/// deciding whether its edit still applies — never the device's own clock, which is unsynchronised,
/// resettable by whoever holds the phone, and routinely wrong by hours.
/// </para>
/// <para>
/// Every upload is recorded as an import batch. That is not bookkeeping: it is what lets a caver
/// look at what a phone put into the registry and take all of it back in one act, through the same
/// screen and the same button that undoes a bad file.
/// </para>
/// </remarks>
public static class SyncUploadEndpoints
{
    /// <summary>
    /// How many neighbours one created row is told about. A caver deciding whether they have just
    /// re-entered a cave the club already holds needs the nearest few, not a census of the massif.
    /// </summary>
    private const int DuplicatesPerRow = 5;

    /// <summary>An account under review sent an upload without saying it understands held rows.</summary>
    public const string ClientUpdateRequiredCode = "sync.client_update_required";

    private const string HeldCheckSavepoint = "held_upload_check";

    public static void MapSyncUploadEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapPost("/{id:guid}/upload", UploadAsync)
            .WithValidation<SyncUploadRequest>()
            .WithName("syncUpload")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Applies one batch of device rows, arbitrated row by row; resends are answered, not re-applied.");
    }

    private static async Task<Results<Ok<SyncUploadResultDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        UploadAsync(
            Guid id,
            SyncUploadRequest request,
            SilexGisDbContext db,
            FeatureWriteService writer,
            FeatureProtection protection,
            VisibleProximitySearch proximity,
            IAccessService access,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            IAppSettingsService settings,
            IChangeContext changes,
            DocumentWriteService documents,
            UploadIngestService ingest,
            ContentIntake intake,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Asked before the rows are looked at, not after: a device speaking a protocol this build
        // does not implement must be told so while its data is still on the phone.
        if (request.ContractVersion != SyncEndpoints.ContractVersion)
        {
            return ApiProblems.Conflict(
                "sync.contract_unsupported",
                $"This server speaks contract version {SyncEndpoints.ContractVersion}.");
        }

        // Counted over every section together: the ceiling bounds the work of one transaction,
        // which is the same whichever array an item arrives in.
        if (SyncUploadSections.ItemCount(request) > options.Value.ResolvedUploadRowsMax)
        {
            return ApiProblems.BadRequest(
                "sync.batch_too_large",
                $"An upload carries at most {options.Value.ResolvedUploadRowsMax} items, counted over all its sections together.");
        }

        // Resolved by owner, deliberately and unconditionally, for the same reason the download
        // is: a phone authenticates as one account, and somebody else's set is answered like one
        // that is not there whatever that account is otherwise allowed. Nothing an installation
        // can configure widens a write of a set — only reading one by its own address.
        var set = await db.SyncSets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ctx.UserId, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("sync.set_not_found");
        }

        var replayed = await ReplayAsync(
            db, protection, proximity, ctx, request.BatchId, options.Value.ResolvedDuplicateRadiusMeters, ct);
        if (replayed is not null)
        {
            return TypedResults.Ok(replayed);
        }

        // An account under review has what it uploads kept as proposals for a reviewer, and the
        // device is answered that each such row is held. A build that never learnt that answer would
        // read a held row as lost or as written, so it is refused whole, before anything is looked
        // at, and the caver is told to update the app. After the resend check, for the reason the
        // club check below gives: a replay writes nothing.
        var underReview = await UploadReviewHold.AppliesToAsync(db, ctx.UserId, ct);
        if (underReview
            && !(request.ClientFeatures?.Contains(UploadReviewHold.ClientFeature, StringComparer.Ordinal) ?? false))
        {
            return ApiProblems.Conflict(
                ClientUpdateRequiredCode,
                "This account's uploads wait for a reviewer, and this app cannot be told so. Update the app before sending.");
        }

        // Asked here and not only when the set was written. Every row this batch creates is bound
        // to the set's club, and binding content to a club hands that club's members whatever
        // their rulesets grant over its content — so the question is asked at the moment the
        // binding is actually made. A membership withdrawn since the set was created would
        // otherwise keep pushing rows into a club the caver has left. After the resend check on
        // purpose: a replay writes nothing, so refusing it would only withhold from a device the
        // answer to a batch this server had already accepted.
        if (set.CavingGroupId is { } boundGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, boundGroupId))
        {
            return ApiProblems.Forbidden(
                "sync.caving_group_forbidden", "You are not entitled to add rows to that caving group.");
        }

        // The fixed order every upload is applied in, which is the order its parts depend on.
        var sections = SyncUploadSections.InApplyOrder(
            writer, protection, access, await settings.GetSyncAsync(ct), documents, ingest, intake);

        var batch = new ImportBatch
        {
            Source = ImportSource.DeviceSync,
            SyncBatchId = request.BatchId,
            ConfirmedByUserId = ctx.UserId,
            // Nobody went through these one at a time; a device pushed them. Recorded rather than
            // inferred, because it is the first thing worth knowing about a batch that went wrong.
            Mode = ImportBatchMode.AutoCreated,
            Options = BatchOptions(set, request),
            ReviewState = underReview ? ImportReviewState.Pending : ImportReviewState.NotHeld,
            HeldAt = underReview ? DateTimeOffset.UtcNow : null,
        };

        // Everything this upload writes — the batch's own row, every row it creates, edits or
        // deletes — is one change set filed under the batch, so the history says it came from a
        // phone and an undo of the upload can find exactly what it did.
        changes.Begin(ChangeSource.DeviceSync, batch.Id);

        var outcomes = new List<(string Section, IReadOnlyList<SyncUploadRowResultDto> Results)>(sections.Length);
        var items = new List<ImportBatchItem>();

        // One transaction for the whole batch. Items are arbitrated individually and a refused one
        // does not take the others down with it, but the record of what was written and the items
        // themselves have to land or not land together — a batch whose provenance line is missing
        // is a batch nobody can take back.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // The identifier is claimed before a single row is written. Two copies of the same
            // batch in flight at once both get past the replay check above, and if the rows went
            // in first the loser would fail on a feature's own primary key — a violation this
            // handler cannot tell from any other, and so a 500 rather than the documented "that
            // batch is already being applied". Claiming first puts the collision on the index
            // that means exactly that.
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);

            // Under review, the upload is checked by running it inside a savepoint and rolling the
            // savepoint back: each item is checked against what the items before it would have
            // written, which is the only way an entrance sent beside its new cave can be checked at
            // all, and what is kept of each is the proposal accepting will write later. The batch's
            // own row was claimed before the savepoint, so it survives the roll-back.
            var hold = underReview ? new SyncHoldRecorder(ctx.UserId) : null;
            var run = new SyncUploadRun { Db = db, Access = ctx, Set = set, Batch = batch, Hold = hold };
            if (hold is not null)
            {
                await transaction.CreateSavepointAsync(HeldCheckSavepoint, ct);
                await hold.ReplayWaitingCreatesAsync(run, sections, request, ct);
            }

            foreach (var section in sections)
            {
                var outcome = await section.RunAsync(run, request, ct);
                outcomes.Add((section.Name, outcome.Results));
                items.AddRange(outcome.Items);
            }

            if (hold is not null)
            {
                await transaction.RollbackToSavepointAsync(HeldCheckSavepoint, ct);
                db.ChangeTracker.Clear();

                // A held upload in which nothing would have been written — every row a repeat of
                // one already waiting, unchanged or refused — holds nothing, and waits for nobody.
                if (hold.NewLineCount == 0)
                {
                    batch.ReviewState = ImportReviewState.NotHeld;
                    batch.HeldAt = null;
                }
            }

            var answer = await AnswerAsync(
                db, protection, proximity, ctx, request.BatchId, batch.Id, replayed: false,
                outcomes.ToDictionary(o => o.Section, o => o.Results, StringComparer.Ordinal),
                options.Value.ResolvedDuplicateRadiusMeters, ct);

            // Recorded before the transaction closes, so the decision and the record of it are
            // one write and a resend cannot find a batch that never learnt what it answered.
            // Decisions only: positions are read out of the rows when a resend arrives rather
            // than kept here, because an account's right to see a position can be taken away and
            // a stored coordinate would be handed back regardless.
            batch.SyncResult = SyncUploadRecord.Write(outcomes);

            // Re-attached rather than added: the row that claimed the identifier above was
            // written at the start, and a refused item in between clears the tracker, so by now
            // this instance may be tracked by nothing.
            db.ImportBatches.Update(batch);
            items.ForEach(i => i.ImportBatchId = batch.Id);
            db.ImportBatchItems.AddRange(items);

            // Recordings this upload wrote or changed are made into trips by a job, queued in this
            // same save: it exists exactly when the upload does, and the worker can only ever see
            // it beside what the upload committed. Once per upload, never per recording. A held
            // upload wrote no recording, so it queues nothing; accepting one is what does.
            if (hold is null
                && items.Any(i => i.TripRecordingId is not null || i.TripRecordingPointId is not null))
            {
                db.ProcessingJobs.Add(new ProcessingJob
                {
                    Kind = ProcessingJobKinds.TripRecordingConvert,
                    Payload = JsonSerializer.Serialize(
                        new Infrastructure.Jobs.TripRecordingConvertPayload(batch.Id), JsonSerializerOptions.Web),
                    // Nobody asked for a message saying the conversion ran; what it makes is told
                    // through the trip and, when something needs looking at, through its own notice.
                    RequestedBy = null,
                });
            }

            await db.SaveChangesAsync(ct);
            if (hold is not null)
            {
                var sync = await settings.GetSyncAsync(ct);
                await hold.PersistAsync(db, batch, DateTimeOffset.UtcNow, sync.RejectedHeldBytesRetentionDays, ct);

                // Told in the transaction that holds the batch, so a reviewer is never told of a
                // batch that is not there; and only when something waits, since a batch of repeats
                // holds nothing new.
                if (batch.ReviewState == ImportReviewState.Pending && hold.NewLineCount > 0)
                {
                    await UploadReviewNotices.AwaitingAsync(db, batch, hold.NewLineCount, ct);
                    await db.SaveChangesAsync(ct);
                }
            }

            await transaction.CommitAsync(ct);

            return TypedResults.Ok(answer);
        }
        catch (Exception e) when (IsBatchCollision(e))
        {
            // The same batch arrived twice at once — a phone that retried while the first attempt
            // was still in flight. The loser writes nothing and answers with what the winner
            // recorded, which is the same answer a later resend would get.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var stored = await ReplayAsync(
                db, protection, proximity, ctx, request.BatchId,
                options.Value.ResolvedDuplicateRadiusMeters, ct);
            return stored is not null
                ? TypedResults.Ok(stored)
                : ApiProblems.Conflict("sync.batch_conflict", "That batch is already being applied. Send it again.");
        }
        catch (FeatureWriteException e)
        {
            // An item-level refusal is answered per item; reaching here means the write service
            // refused something the loop could not attribute to one item, so nothing is kept.
            await transaction.RollbackAsync(ct);
            return ApiProblems.BadRequest(e.Code, string.Join("; ", e.Errors));
        }
    }

    /// <summary>
    /// How the upload was asked for, kept on its batch: the set it went into, and the protocol
    /// behaviours the device said it understands.
    /// </summary>
    /// <remarks>
    /// The device's list is kept as it was sent (each value once, in its first position), not
    /// narrowed to the values this build knows. It answers a later question — what did the phone
    /// that sent this batch say it could handle — and a value this build does not use yet is part
    /// of that answer. A request without the member records none, which reads as "understands
    /// nothing beyond the first contract".
    /// </remarks>
    private static string BatchOptions(SyncSet set, SyncUploadRequest request)
    {
        var clientFeatures = request.ClientFeatures?.Distinct(StringComparer.Ordinal).ToList();
        return clientFeatures is { Count: > 0 }
            ? JsonSerializer.Serialize(new { syncSetId = set.Id, clientFeatures })
            : JsonSerializer.Serialize(new { syncSetId = set.Id });
    }

    /// <summary>
    /// The answer to an upload, from the decisions its sections made: the live answer and a
    /// resend's answer are built here alike, so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// The conflict echo and the duplicate report are worked out here every time rather than read
    /// back with the decisions. What the account was allowed to see on the day the batch was
    /// applied is not what it is allowed to see on the day it retries, and a resend is a fresh
    /// question with a fresh answer.
    /// </remarks>
    private static async Task<SyncUploadResultDto> AnswerAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        VisibleProximitySearch proximity,
        AccessContext ctx,
        Guid batchId,
        Guid importBatchId,
        bool replayed,
        IReadOnlyDictionary<string, IReadOnlyList<SyncUploadRowResultDto>> sections,
        double duplicateRadiusMeters,
        CancellationToken ct)
    {
        var rows = SyncUploadRecord.Of(sections, SyncUploadSections.Rows);
        var all = sections.Values.SelectMany(results => results).ToList();
        return new SyncUploadResultDto(
            batchId,
            importBatchId,
            replayed,
            Written: all.Count(r => SyncUploadTally.IsWritten(r.Status)),
            Refused: all.Count(r => SyncUploadTally.IsRefused(r.Status)),
            rows,
            await ConflictEchoAsync(db, protection, ctx, rows, ct),
            await DuplicatesAsync(db, proximity, ctx, rows, duplicateRadiusMeters, ct),
            // Each absent rather than empty when the upload carried none of that section's items,
            // so an upload of rows alone is answered byte for byte as it was before sections.
            Present(sections, SyncUploadSections.People),
            Present(sections, SyncUploadSections.Documents),
            Present(sections, SyncUploadSections.DocumentLinks),
            Present(sections, SyncUploadSections.TripRecordings),
            Present(sections, SyncUploadSections.TripPoints),
            Present(sections, SyncUploadSections.RasterMaps),
            Present(sections, SyncUploadSections.RasterPins));
    }

    /// <summary>A section's answers, or null when the upload carried none of its items.</summary>
    private static IReadOnlyList<SyncUploadRowResultDto>? Present(
        IReadOnlyDictionary<string, IReadOnlyList<SyncUploadRowResultDto>> sections, string section) =>
        sections.TryGetValue(section, out var results) && results.Count > 0 ? results : null;

    /// <summary>
    /// The answer an earlier request with this batch identifier recorded, or null if there was
    /// none. Scoped to the account, because a batch identifier only means anything inside the
    /// account that minted it.
    /// </summary>
    private static async Task<SyncUploadResultDto?> ReplayAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        VisibleProximitySearch proximity,
        AccessContext ctx,
        Guid batchId,
        double duplicateRadiusMeters,
        CancellationToken ct)
    {
        var prior = await db.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ConfirmedByUserId == ctx.UserId && b.SyncBatchId == batchId, ct);
        if (prior?.SyncResult is not { } stored)
        {
            return null;
        }

        return await AnswerAsync(
            db, protection, proximity, ctx, batchId, prior.Id, replayed: true,
            SyncUploadRecord.Read(stored), duplicateRadiusMeters, ct);
    }

    /// <summary>
    /// This server's own version of every row that lost a conflict, shaped exactly as a download
    /// would have delivered it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second place in this slice where a position travels from the server to a
    /// device, and it is on a route where none of the download's filtering runs — so it asks the
    /// one place that decides who may be given a position, gets the same answer, and does the same
    /// thing with it. A row this caller may not place is absent from the echo rather than blurred
    /// in it: a device told its edit lost, and given nothing to compare against, still knows to
    /// re-read the row, while a device handed a snapped point would write it to cleartext storage
    /// and pass it on as though it were the cave.
    /// </para>
    /// <para>
    /// Readability is asked here too, and not inherited from the arbitration that produced these
    /// decisions. A resend is answered from this method alone and may arrive long afterwards, by
    /// which time a grant the first attempt relied on can have been withdrawn.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<SyncFeatureDto>> ConflictEchoAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<SyncUploadRowResultDto> rows,
        CancellationToken ct)
    {
        var lost = rows.Where(r => r.Status is SyncRowStatus.Conflict).Select(r => r.Id).ToList();
        if (lost.Count == 0)
        {
            return [];
        }

        return await SyncFeatureShaping.CurrentAsync(db, protection, ctx, lost, ct);
    }

    /// <summary>
    /// What was already here, near a row this batch created. A report and never a verdict: the
    /// row is written either way and its status never mentions this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A device is offline when it decides to add a cave, so it cannot ask first — and a caver
    /// who surveyed a shaft forty metres from one a clubmate entered last week has no way of
    /// knowing. Refusing the row is the wrong answer, because the device would have nowhere to
    /// put the work and the caver would be arguing with a phone in a car park; so the row lands
    /// and the answer says what it landed next to.
    /// </para>
    /// <para>
    /// Who may be in the comparison at all is decided by the search itself and is not re-derived
    /// here: "there is already something within fifty metres of this point" is a position, so the
    /// pool is what this caller may read <em>and</em> may place exactly. The cost is real and is
    /// accepted — a row pushed beside a protected entrance this caller may not place is reported
    /// as near nothing — because the alternative is answering a position to somebody entitled to
    /// no position.
    /// </para>
    /// <para>
    /// Rows this batch wrote are never each other's duplicates. A device sending a cave and the
    /// three places inside it sends four points a few metres apart by construction, and reporting
    /// those against each other would bury the one hit that means anything.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<SyncDuplicateDto>> DuplicatesAsync(
        SilexGisDbContext db,
        VisibleProximitySearch proximity,
        AccessContext ctx,
        IReadOnlyList<SyncUploadRowResultDto> rows,
        double radiusMeters,
        CancellationToken ct)
    {
        var written = rows
            .Where(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated)
            .Select(r => r.Id)
            .ToHashSet();
        var created = rows.Where(r => r.Status is SyncRowStatus.Created).Select(r => r.Id).ToList();
        if (created.Count == 0)
        {
            return [];
        }

        // Read back rather than taken from the request, so a resend answers from the same place
        // the first attempt did — and so a coordinate the arbitration declined to write is never
        // the one compared against.
        var placed = (await db.Features.AsNoTracking()
                .Where(f => created.Contains(f.Id) && f.Geom != null)
                .Select(f => new { f.Id, f.Geom })
                .ToListAsync(ct))
            .Where(f => f.Geom is Point)
            .ToDictionary(f => f.Id, f => (Point)f.Geom!);
        if (placed.Count == 0)
        {
            return [];
        }

        var nearby = await proximity.NearAsync(placed.Values, radiusMeters, ctx, ct);
        var report = new List<SyncDuplicateDto>();
        foreach (var (id, point) in placed)
        {
            var near = nearby
                .Where(hit => !written.Contains(hit.FeatureId))
                .Select(hit => (Hit: hit, Distance: Geodesy.DistanceMeters(point.Coordinate, hit.Geom.Coordinate)))
                .Where(x => x.Distance <= radiusMeters)
                .OrderBy(x => x.Distance)
                .Take(DuplicatesPerRow)
                .Select(x => new SyncDuplicateCandidateDto(
                    x.Hit.FeatureId,
                    x.Hit.Name,
                    x.Hit.Kind,
                    Math.Round(x.Distance, 1),
                    x.Hit.CaveFeatureId))
                .ToList();
            if (near.Count > 0)
            {
                report.Add(new SyncDuplicateDto(id, near));
            }
        }

        return report;
    }
    /// <summary>
    /// Whether a failed save is the same batch arriving twice, rather than any other constraint.
    /// Matched on the index by name so a different violation is not reported as a resend.
    /// </summary>
    private static bool IsBatchCollision(Exception e) =>
        e is DbUpdateException { InnerException: PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_import_batches_user_sync_batch",
        } };
}

/// <summary>
/// How an upload's recorded answer is written to and read back from the batch it belongs to. The
/// same options in both directions, stated once, so a resend is answered with what was recorded
/// rather than with a differently-spelled version of it.
/// </summary>
internal static class SyncUploadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
