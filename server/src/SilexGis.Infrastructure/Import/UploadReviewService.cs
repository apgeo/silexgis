// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>What became of one accepted line once its source tried to write it.</summary>
/// <param name="Written">
/// The provenance the write would have recorded had the batch landed at once — what was created,
/// changed or attached — copied onto the held line so that it becomes the line undo works from.
/// Null when the line could not be written, or when it wrote nothing because the row was already
/// as proposed.
/// </param>
public sealed record HeldLineOutcome(
    long ItemId, bool Accepted, ImportBatchItem? Written, string? FailureCode = null, string? FailureDetail = null);

/// <summary>
/// The rows a reviewer accepted that changed on the server since the uploader sent them, so the
/// proposal would be written over somebody else's later edit. Nothing is decided; the reviewer is
/// asked to accept them explicitly over the current row, or to reject them.
/// </summary>
public sealed class HeldChangedSinceException(IReadOnlyList<Guid> reviewIds)
    : Exception("Some rows changed on the server since they were uploaded.")
{
    public IReadOnlyList<Guid> ReviewIds { get; } = reviewIds;
}

/// <summary>
/// What review needs of one kind of held batch that only the code owning that kind knows: how a
/// line's proposal may be shown to somebody, and how accepted lines are written — by the code that
/// would have written them had the batch not been held (for a phone upload each section's second
/// step, for a file import the source's own commit).
/// </summary>
public interface IHeldBatchHandler
{
    ImportSource Source { get; }

    /// <summary>
    /// Each line as <paramref name="viewer"/> may be shown it, keyed by line id: what the line
    /// would write, without any position the viewer may not place, and nothing at all of a line
    /// that changes or names a row the viewer may not read. A held proposal can carry the true
    /// position of a protected row it edits or hangs under, and the content of a row it edits; the
    /// review queue is a path that emits both like any other.
    /// </summary>
    Task<IReadOnlyDictionary<long, HeldLineView>> ShowAsync(
        IReadOnlyList<ImportBatchItem> lines, AccessContext viewer, CancellationToken ct);

    /// <summary>
    /// The fields <paramref name="line"/> may be accepted by on their own; empty when it can only
    /// be accepted whole.
    /// </summary>
    IReadOnlyList<string> FieldsOf(ImportBatchItem line);

    /// <summary>
    /// Whether the accepted lines of one batch are handed over together rather than one at a
    /// time. A file import is written by its source's one commit, run once over every candidate
    /// accepted with the others turned into skips; running it line by line would repeat the
    /// commit's whole-batch work — reading the file, the derived state of the hierarchy — for
    /// every candidate.
    /// </summary>
    bool WritesWhole => false;

    /// <summary>
    /// Writes <paramref name="lines"/> of <paramref name="batch"/>, in the order given (every line
    /// after what it depends on), with the uploader's access decisions. Each line is checked again
    /// just before it is written; one that would now be refused is answered as not accepted, with
    /// the reason. A line whose row changed since it was sent throws
    /// <see cref="HeldChangedSinceException"/> unless <paramref name="overCurrent"/>.
    /// </summary>
    Task<IReadOnlyList<HeldLineOutcome>> ApplyAsync(
        ImportBatch batch,
        IReadOnlyList<ImportBatchItem> lines,
        AccessContext uploader,
        bool overCurrent,
        CancellationToken ct);
}

/// <summary>One held line as a viewer may be shown it.</summary>
/// <param name="Proposal">
/// What the line would write (jsonb text) as the viewer may see it; null when the line changes or
/// names a row the viewer may not read, so that nothing of that row is shown.
/// </param>
/// <param name="ChangedSince">
/// A waiting edit whose row was written on the server since the uploader read it: accepting it
/// means writing over the row as it is now.
/// </param>
public sealed record HeldLineView(string? Proposal, bool ChangedSince);

/// <summary>What a decision did: the lines it decided, and how each batch it touched now stands.</summary>
public sealed record UploadReviewResult(
    IReadOnlyList<ImportBatchItem> Decided, IReadOnlyList<ImportBatch> Batches);

/// <summary>
/// A reviewer's decision on held lines — accepting or rejecting a whole batch, one line, or a
/// line's subset of fields — with what it pulls in and what it cascades to.
/// </summary>
/// <remarks>
/// <para>
/// Accepting writes with the <em>uploader's</em> access decisions, resolved afresh: a review is
/// a second pair of eyes on what the account could already do, never a way to land something the
/// account may not write. A line the uploader has since lost the right to write is decided
/// <see cref="ImportReviewDecision.Failed"/> with the refusal the upload itself would have given.
/// The history the writes leave is filed under a change set of its own, from
/// <see cref="ChangeSource.ReviewDecision"/>, belonging to the held batch, and names the reviewer
/// as the person who made the change.
/// </para>
/// <para>
/// One transaction for the whole decision: either every line it reaches is decided, or — when a
/// row changed under a proposal the reviewer did not say to write over — none is.
/// </para>
/// </remarks>
public sealed class UploadReviewService(
    SilexGisDbContext db, IChangeContext changes, IEnumerable<IHeldBatchHandler> handlers, IAppSettingsService settings)
{
    /// <summary>A line accepted whose own dependency could not be written.</summary>
    public const string DependencyFailedCode = "review.dependency_failed";

    /// <summary>
    /// Accepts the lines <paramref name="chosen"/> of one account's held batches, and the waiting
    /// lines they depend on with them.
    /// </summary>
    /// <param name="fields">For a chosen update, the fields accepted when not all of them.</param>
    /// <param name="reopen">
    /// The reviewer changes their mind about a rejection: a chosen line that was rejected within
    /// the installation's retention of turned-down bytes, and that no later upload replaced, is
    /// accepted after all — with the rejected lines it depends on, on the same terms.
    /// </param>
    /// <exception cref="HeldNotPendingException">
    /// Nothing chosen can be decided any more, or another decision reached one of the lines first.
    /// </exception>
    public async Task<UploadReviewResult> AcceptAsync(
        Guid reviewerId,
        Guid uploaderId,
        ImportSource source,
        IReadOnlyCollection<long> chosen,
        IReadOnlyDictionary<long, IReadOnlyList<string>> fields,
        string? note,
        bool overCurrent,
        bool reopen,
        CancellationToken ct)
    {
        var applier = handlers.FirstOrDefault(a => a.Source == source)
            ?? throw new InvalidOperationException($"No reviewer can apply a held {source} batch.");
        var now = DateTimeOffset.UtcNow;
        var sync = await settings.GetSyncAsync(ct);
        var held = await HeldLinesAsync(uploaderId, source, ct);
        var reopenable = reopen
            ? await ReopenableAsync(uploaderId, source, now.AddDays(-sync.RejectedHeldBytesRetentionDays), ct)
            : null;
        var order = UploadReviewDecisions.AcceptOrder(held, chosen, reopenable);
        if (order.Count == 0)
        {
            throw new HeldNotPendingException();
        }

        var standing = held.Where(h => order.Contains(h.Id)).ToDictionary(h => h.Id, h => h.Decision);
        var uploader = await AccessContextResolver.ResolveAsync(db, uploaderId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ClaimAsync(standing, ct);

        // The field subsets are stamped first, because the applier reads them off the lines it is
        // handed: what the reviewer accepted of a proposal is part of what is written.
        var lines = await db.ImportBatchItems.Where(i => order.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var (id, subset) in fields)
        {
            if (lines.TryGetValue(id, out var line))
            {
                line.AcceptedFields = [.. subset];
            }
        }

        await db.SaveChangesAsync(ct);

        // The bytes of a reopened line were given back to the sweep when it was rejected; they
        // are kept again until the line is written, which adopts them.
        await HeldUploadBytes.ParkAsync(
            db, uploaderId,
            [.. lines.Values.Where(l => standing[l.Id] == ImportReviewDecision.Rejected && l.HeldUploadSessionId is not null)
                .Select(l => l.HeldUploadSessionId!.Value).Distinct()],
            ct);

        var failed = held
            .Where(h => h.Decision == ImportReviewDecision.Failed
                || (h.Decision == ImportReviewDecision.Rejected && !standing.ContainsKey(h.Id)))
            .Select(h => h.Id)
            .ToHashSet();
        var outcomes = new Dictionary<long, HeldLineOutcome>();
        var changedSince = new List<Guid>();

        // Written strictly in the order the rules gave — every line after what it depends on — in
        // runs of consecutive lines of one batch, each run filed under its own batch. A dependency
        // can sit in another batch (a cave an earlier upload left waiting), and grouping by batch
        // would write the place before its cave.
        var ordered = order.Select(id => lines[id]).ToList();
        for (var start = 0; start < ordered.Count;)
        {
            var batchId = ordered[start].ImportBatchId;
            var end = start;
            while (end < ordered.Count && ordered[end].ImportBatchId == batchId)
            {
                end++;
            }

            var batch = await db.ImportBatches.AsNoTracking().FirstAsync(b => b.Id == batchId, ct);
            changes.Begin(ChangeSource.ReviewDecision, batch.Id);

            if (applier.WritesWhole)
            {
                // Candidates of a file import depend only on lines of their own batch, so the run
                // is written by one commit of its source — after the lines whose dependency an
                // earlier decision left unwritten are decided failed with that reason. A scan
                // whose trip failed to be created would otherwise make its own trip, which no
                // line of the batch records and its undo never finds.
                var run = new List<ImportBatchItem>();
                for (var i = start; i < end; i++)
                {
                    var line = ordered[i];
                    if (line.DependsOnItemIds?.Any(failed.Contains) == true)
                    {
                        failed.Add(line.Id);
                        outcomes[line.Id] = new HeldLineOutcome(
                            line.Id, false, null, DependencyFailedCode,
                            "A row this one cannot be written without was not written.");
                        continue;
                    }

                    run.Add(line);
                }

                if (run.Count > 0)
                {
                    foreach (var outcome in await applier.ApplyAsync(batch, run, uploader, overCurrent, ct))
                    {
                        outcomes[outcome.ItemId] = outcome;
                    }
                }

                start = end;
                continue;
            }

            // A line whose dependency is now unwritable cannot be written either; it is decided
            // with the reason rather than handed to the applier to fail in a less telling way.
            // Lines are handed over one at a time, so a line's dependency in the same run has
            // been decided before the line is looked at.
            for (var i = start; i < end; i++)
            {
                var line = ordered[i];
                if (line.DependsOnItemIds?.Any(failed.Contains) == true)
                {
                    failed.Add(line.Id);
                    outcomes[line.Id] = new HeldLineOutcome(
                        line.Id, false, null, DependencyFailedCode,
                        "A row this one cannot be written without was not written.");
                    continue;
                }

                IReadOnlyList<HeldLineOutcome> applied;
                try
                {
                    applied = await applier.ApplyAsync(batch, [line], uploader, overCurrent, ct);
                }
                catch (HeldChangedSinceException e)
                {
                    // Every such row is named in the one answer, so the reviewer decides them
                    // together; the transaction is rolled back whatever else was written.
                    changedSince.AddRange(e.ReviewIds);
                    continue;
                }

                foreach (var outcome in applied)
                {
                    outcomes[outcome.ItemId] = outcome;
                    if (!outcome.Accepted)
                    {
                        failed.Add(outcome.ItemId);
                    }
                }
            }

            start = end;
        }

        if (changedSince.Count > 0)
        {
            throw new HeldChangedSinceException(changedSince);
        }

        // The applier writes item by item and clears what it tracked between them, so the lines
        // are read afresh before their decisions are stamped.
        db.ChangeTracker.Clear();
        var decided = await db.ImportBatchItems.Where(i => order.Contains(i.Id)).ToListAsync(ct);
        var chosenSet = chosen.ToHashSet();
        foreach (var line in decided)
        {
            var outcome = outcomes[line.Id];
            line.ReviewDecision = outcome.Accepted ? ImportReviewDecision.Accepted : ImportReviewDecision.Failed;
            line.DecidedAt = now;
            line.DecidedByUserId = reviewerId;
            line.DecisionNote = chosenSet.Contains(line.Id) ? note : line.DecisionNote;
            line.ReviewFailureCode = outcome.FailureCode;
            line.ReviewFailureDetail = outcome.FailureDetail;
            if (outcome.Accepted)
            {
                line.AppliedAt = now;
                if (outcome.Written is { } written)
                {
                    line.Action = written.Action;
                    line.FeatureId = written.FeatureId;
                    line.AttachedToFeatureId = written.AttachedToFeatureId;
                    line.DevicePersonId = written.DevicePersonId;
                    line.DocumentId = written.DocumentId;
                    line.DocumentVersionId = written.DocumentVersionId;
                    line.TripRecordingId = written.TripRecordingId;
                    line.TripRecordingPointId = written.TripRecordingPointId;
                    line.RasterMapId = written.RasterMapId;
                    line.RasterPinId = written.RasterPinId;
                    line.AttachmentIds = written.AttachmentIds;
                    line.SourceProperties = written.SourceProperties;

                    // What only a file import's line records: the trip it wrote, and the row of
                    // the file and the rule that made the candidate. A phone upload's held line
                    // already carries whatever of these it has, so an empty answer keeps it.
                    line.TripLogId = written.TripLogId ?? line.TripLogId;
                    line.TripPositionEventId = written.TripPositionEventId ?? line.TripPositionEventId;
                    line.SourceFeatureId = written.SourceFeatureId ?? line.SourceFeatureId;
                    line.SourceFileId = written.SourceFileId ?? line.SourceFileId;
                    line.RuleId = written.RuleId ?? line.RuleId;
                    line.RuleName = written.RuleName ?? line.RuleName;
                }
            }
        }

        // The batch's own counters say what it wrote, as they do for a batch that landed at once.
        var wrote = decided.Where(l => l.ReviewDecision == ImportReviewDecision.Accepted && outcomes[l.Id].Written is not null)
            .GroupBy(l => l.ImportBatchId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var counted in await db.ImportBatches.Where(b => wrote.Keys.Contains(b.Id)).ToListAsync(ct))
        {
            counted.CreatedCount += wrote[counted.Id].Count(l => l.Action == ImportDecisionAction.Create);
            counted.AttachedCount += wrote[counted.Id].Count(l => l.Action != ImportDecisionAction.Create);
        }

        await db.SaveChangesAsync(ct);
        var batches = await SettleAsync(decided.Select(l => l.ImportBatchId), reviewerId, now, ct);
        await FinishAsync(decided, batches, sync, now, ct);
        await transaction.CommitAsync(ct);
        return new UploadReviewResult(decided, batches);
    }

    /// <summary>
    /// Takes the lines a decision reaches for this decision alone, inside its transaction: each
    /// must still stand where it stood when the decision was worked out. The update holds the rows
    /// until the transaction ends, so a second decision — another reviewer, a double click — or a
    /// resend replacing one of them waits, and then finds them decided and claims nothing.
    /// </summary>
    private async Task ClaimAsync(IReadOnlyDictionary<long, ImportReviewDecision?> standing, CancellationToken ct)
    {
        var pending = standing.Where(s => s.Value == ImportReviewDecision.Pending).Select(s => s.Key).ToList();
        var rejected = standing.Where(s => s.Value == ImportReviewDecision.Rejected).Select(s => s.Key).ToList();
        var claimed = await db.ImportBatchItems
            .Where(i => (pending.Contains(i.Id) && i.ReviewDecision == ImportReviewDecision.Pending)
                || (rejected.Contains(i.Id) && i.ReviewDecision == ImportReviewDecision.Rejected))
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.ReviewDecision, i => i.ReviewDecision), ct);
        if (claimed != standing.Count)
        {
            throw new HeldNotPendingException();
        }
    }

    /// <summary>
    /// What every decision leaves behind besides its lines: the bytes of the lines it decided
    /// without adopting them given back to the sweep, and the uploader told — in the decision's
    /// own transaction, so a message never reports a decision that did not land.
    /// </summary>
    private async Task FinishAsync(
        IReadOnlyList<ImportBatchItem> decided, IReadOnlyList<ImportBatch> batches, SyncSettings sync,
        DateTimeOffset now, CancellationToken ct)
    {
        await HeldUploadBytes.ReleaseAsync(db, [.. decided.Select(l => l.Id)], sync.RejectedHeldBytesRetentionDays, now, ct);
        UploadReviewNotices.Reviewed(db, decided, batches);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Rejects the waiting lines <paramref name="chosen"/> of one account's held batches, and every
    /// waiting line that depends on them. Nothing is written but the decisions.
    /// </summary>
    /// <exception cref="HeldNotPendingException">
    /// Nothing chosen waits any more, or another decision reached one of the lines first.
    /// </exception>
    public async Task<UploadReviewResult> RejectAsync(
        Guid reviewerId, Guid uploaderId, ImportSource source, IReadOnlyCollection<long> chosen, string? note,
        CancellationToken ct)
    {
        var held = await HeldLinesAsync(uploaderId, source, ct);
        var reach = UploadReviewDecisions.RejectReach(held, chosen);
        if (reach.Count == 0)
        {
            throw new HeldNotPendingException();
        }

        var now = DateTimeOffset.UtcNow;
        var sync = await settings.GetSyncAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ClaimAsync(reach.ToDictionary(id => id, _ => (ImportReviewDecision?)ImportReviewDecision.Pending), ct);
        var decided = await db.ImportBatchItems.Where(i => reach.Contains(i.Id)).ToListAsync(ct);
        var chosenSet = chosen.ToHashSet();
        foreach (var line in decided)
        {
            line.ReviewDecision = ImportReviewDecision.Rejected;
            line.DecidedAt = now;
            line.DecidedByUserId = reviewerId;
            line.DecisionNote = chosenSet.Contains(line.Id) ? note : line.DecisionNote;
        }

        await db.SaveChangesAsync(ct);
        var batches = await SettleAsync(decided.Select(l => l.ImportBatchId), reviewerId, now, ct);
        await FinishAsync(decided, batches, sync, now, ct);
        await transaction.CommitAsync(ct);
        return new UploadReviewResult(decided, batches);
    }

    /// <summary>Restates where each touched batch stands from its lines.</summary>
    public Task<IReadOnlyList<ImportBatch>> SettleAsync(
        IEnumerable<Guid> batchIds, Guid? reviewerId, DateTimeOffset now, CancellationToken ct) =>
        UploadReviewHold.SettleAsync(db, batchIds, reviewerId, now, ct);

    /// <summary>
    /// Every line of the account's held batches from this source that a decision could reach:
    /// the waiting ones, and the decided ones a waiting line depends on.
    /// </summary>
    private async Task<IReadOnlyList<HeldLine>> HeldLinesAsync(Guid uploaderId, ImportSource source, CancellationToken ct) =>
        [.. (await (from item in db.ImportBatchItems.AsNoTracking()
                    join batch in db.ImportBatches on item.ImportBatchId equals batch.Id
                    where batch.ConfirmedByUserId == uploaderId
                        && batch.Source == source
                        && batch.ReviewState != ImportReviewState.NotHeld
                        && item.ReviewDecision != null
                    select new { item.Id, item.DependsOnItemIds, item.ReviewDecision })
                .ToListAsync(ct))
            .Select(l => new HeldLine(l.Id, l.DependsOnItemIds ?? [], l.ReviewDecision))];

    /// <summary>
    /// The account's rejected lines a reviewer may still change their mind about: rejected since
    /// <paramref name="since"/> — inside the retention of turned-down bytes — and not replaced by a
    /// later line for the same row, which is the phone's later word on it.
    /// </summary>
    private async Task<IReadOnlySet<long>> ReopenableAsync(
        Guid uploaderId, ImportSource source, DateTimeOffset since, CancellationToken ct)
    {
        var mine = from item in db.ImportBatchItems.AsNoTracking()
                   join batch in db.ImportBatches on item.ImportBatchId equals batch.Id
                   where batch.ConfirmedByUserId == uploaderId && batch.Source == source && item.ReviewDecision != null
                   select item;
        return (await mine
                .Where(i => i.ReviewDecision == ImportReviewDecision.Rejected && i.DecidedAt >= since
                    // A file import's candidate names no row a later upload could send again.
                    && (i.ProposalRowId == null || !mine.Any(later => later.Id > i.Id
                        && later.ProposalSection == i.ProposalSection
                        && later.ProposalRowId == i.ProposalRowId)))
                .Select(i => i.Id)
                .ToListAsync(ct))
            .ToHashSet();
    }
}

/// <summary>
/// A decision that reaches nothing it can decide: every line it names was decided already, or
/// another decision — another reviewer, a second press, a resend replacing a line — reached one of
/// them first. Nothing was decided.
/// </summary>
public sealed class HeldNotPendingException()
    : Exception("Nothing this decision reaches is waiting for it any more.");

/// <summary>
/// Something a held batch's writing needs is out of reach for now — the third party a catalogue
/// import is read from does not answer. Nothing was decided, and the lines still wait, because a
/// line decided failed for a passing outage could not be accepted again.
/// </summary>
public sealed class HeldWriteUnavailableException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
