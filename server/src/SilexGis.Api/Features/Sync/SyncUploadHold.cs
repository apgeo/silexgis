// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>One proposal an upload under review keeps instead of writing.</summary>
internal sealed class SyncHeldLine
{
    public required string Section { get; init; }

    public required Guid RowId { get; init; }

    public required string Proposal { get; init; }

    public required ImportDecisionAction Action { get; init; }

    public required IReadOnlyList<Guid> Refers { get; init; }

    public required Guid ReviewId { get; init; }

    /// <summary>
    /// The waiting line this proposal repeats exactly, when the phone sent the same content again:
    /// nothing new is kept, and the device is answered with that line's review id.
    /// </summary>
    public long? RepeatsItemId { get; init; }

    /// <summary>The waiting lines for the same row this proposal replaces.</summary>
    public IReadOnlyList<long> Supersedes { get; init; } = [];

    public Guid? HeldUploadSessionId { get; init; }
}

/// <summary>
/// What an upload by an account under review keeps while it is checked, and how that becomes the
/// held batch's lines once the check has been rolled back.
/// </summary>
/// <remarks>
/// <para>
/// The upload is checked by running it — every section's both steps, item by item, inside a
/// savepoint that is rolled back afterwards — because the checks of a later item are database
/// questions about what the earlier items wrote: an entrance sent beside its new cave finds the
/// cave only because the cave was written first. Checking every item against the registry as it
/// stands would refuse each one whose container is new in the same upload. What is kept of each
/// item that would have been written is its first step's proposal, which accepting writes later
/// through the same second step, re-checking that the row has not moved since.
/// </para>
/// <para>
/// The proposals the account already has waiting are part of what a new upload is checked
/// against: their creates are written first inside the same savepoint, so a place sent today
/// under a cave that is still waiting from yesterday finds its cave, and is recorded as depending
/// on it. Updates and deletions waiting are not replayed, because the new upload's revisions were
/// read from the rows as they stand, and writing a waiting edit first would make every one of them
/// a conflict.
/// </para>
/// <para>
/// A phone re-sends every row whose revision did not move, and a held row's revision does not
/// move. So a proposal that repeats one already waiting for the same row keeps nothing new and is
/// answered with the waiting line's review id; one that differs replaces it, and the line it
/// replaces is marked superseded — the phone's latest word on a row is the one reviewed.
/// </para>
/// </remarks>
internal sealed class SyncHoldRecorder(Guid uploaderId)
{
    private readonly List<SyncHeldLine> lines = [];

    public IReadOnlyList<SyncHeldLine> Lines => lines;

    /// <summary>Lines kept that are new to this batch, as opposed to repeats of waiting ones.</summary>
    public int NewLineCount => lines.Count(l => l.RepeatsItemId is null);

    /// <summary>
    /// Writes the creates the account already has waiting, in the order they were kept, so the
    /// check of this upload sees them. Rows the upload sends again are left out: the upload's own
    /// copy is what gets checked, and compared against the waiting one.
    /// </summary>
    public async Task ReplayWaitingCreatesAsync(
        SyncUploadRun run, IReadOnlyList<ISyncUploadSection> sections, SyncUploadRequest request, CancellationToken ct)
    {
        var sent = SyncUploadSections.RowIds(request);
        var waiting = await UploadReviewHold.PendingLinesOf(run.Db, uploaderId, ImportSource.DeviceSync)
            .AsNoTracking()
            .Where(i => i.Action == ImportDecisionAction.Create && i.Proposal != null && i.ProposalSection != null)
            .OrderBy(i => i.Id)
            .Select(i => new { i.ProposalSection, i.ProposalRowId, i.Proposal })
            .ToListAsync(ct);
        if (waiting.Count == 0)
        {
            return;
        }

        foreach (var section in sections)
        {
            foreach (var line in waiting.Where(w => w.ProposalSection == section.Name
                && !(w.ProposalRowId is { } rowId && sent.Contains(rowId))))
            {
                var applied = await section.ApplyHeldAsync(run, line.Proposal!, ct);
                if (SyncUploadTally.IsWritten(applied.Result.Status))
                {
                    await run.Db.SaveChangesAsync(ct);
                }
                else
                {
                    run.Db.ChangeTracker.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Keeps one proposal that would have been written, and gives the answer the device is told.
    /// </summary>
    public async Task<SyncUploadRowResultDto> KeepAsync(
        SyncUploadRun run,
        string section,
        (Guid RowId, Guid AnswerId) identity,
        string proposal,
        IReadOnlyList<Guid> refers,
        DateTimeOffset? standingRevision,
        ImportDecisionAction action,
        Guid? heldUpload,
        CancellationToken ct)
    {
        var waiting = await UploadReviewHold.PendingLinesOf(run.Db, uploaderId, ImportSource.DeviceSync)
            .AsNoTracking()
            .Where(i => i.ProposalSection == section && i.ProposalRowId == identity.RowId)
            .OrderByDescending(i => i.Id)
            .Select(i => new { i.Id, i.ReviewId, i.Proposal })
            .ToListAsync(ct);

        var repeated = waiting.FirstOrDefault(w => w.ReviewId is not null && SameProposal(w.Proposal, proposal));
        var line = repeated is not null
            ? new SyncHeldLine
            {
                Section = section, RowId = identity.RowId, Proposal = proposal, Action = action, Refers = refers,
                ReviewId = repeated.ReviewId!.Value, RepeatsItemId = repeated.Id, HeldUploadSessionId = heldUpload,
            }
            : new SyncHeldLine
            {
                Section = section, RowId = identity.RowId, Proposal = proposal, Action = action, Refers = refers,
                ReviewId = Guid.CreateVersion7(), Supersedes = [.. waiting.Select(w => w.Id)],
                HeldUploadSessionId = heldUpload,
            };
        lines.Add(line);

        return new SyncUploadRowResultDto(identity.AnswerId, SyncRowStatus.Held, standingRevision, null, null, line.ReviewId);
    }

    /// <summary>
    /// Writes the kept proposals as the batch's waiting lines, after the check was rolled back:
    /// marks what they replace as superseded, moves the lines that depended on a replaced one onto
    /// its replacement, and records each new line's dependency on a waiting create.
    /// </summary>
    public async Task PersistAsync(
        SilexGisDbContext db, ImportBatch batch, DateTimeOffset now, int releasedBytesRetentionDays, CancellationToken ct)
    {
        var created = new Dictionary<Guid, ImportBatchItem>();
        var pending = new List<(SyncHeldLine Line, ImportBatchItem Item)>();
        foreach (var line in lines.Where(l => l.RepeatsItemId is null))
        {
            var item = new ImportBatchItem
            {
                ImportBatchId = batch.Id,
                Action = line.Action,
                ReviewDecision = ImportReviewDecision.Pending,
                ReviewId = line.ReviewId,
                Proposal = line.Proposal,
                ProposalSection = line.Section,
                ProposalRowId = line.RowId,
                HeldUploadSessionId = line.HeldUploadSessionId,
            };
            db.ImportBatchItems.Add(item);
            pending.Add((line, item));
            if (line.Action == ImportDecisionAction.Create)
            {
                created[line.RowId] = item;
            }
        }

        await db.SaveChangesAsync(ct);

        // The bytes every waiting line names — new or repeated — are kept until it is decided.
        await HeldUploadBytes.ParkAsync(
            db, uploaderId,
            [.. lines.Where(l => l.HeldUploadSessionId is not null).Select(l => l.HeldUploadSessionId!.Value).Distinct()],
            ct);

        var touched = new HashSet<Guid>();
        var superseded = new List<long>();
        foreach (var (line, item) in pending.Where(p => p.Line.Supersedes.Count > 0))
        {
            var replaced = line.Supersedes.ToArray();
            superseded.AddRange(replaced);
            touched.UnionWith(await db.ImportBatchItems.AsNoTracking()
                .Where(i => replaced.Contains(i.Id))
                .Select(i => i.ImportBatchId)
                .ToListAsync(ct));
            await db.ImportBatchItems
                .Where(i => replaced.Contains(i.Id) && i.ReviewDecision == ImportReviewDecision.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(i => i.ReviewDecision, ImportReviewDecision.Superseded)
                    .SetProperty(i => i.DecidedAt, now), ct);
            // Every waiting line of the account that hung on a replaced line hangs on its
            // replacement now.
            var hanging = await UploadReviewHold.PendingLinesOf(db, uploaderId, ImportSource.DeviceSync)
                .Where(i => i.DependsOnItemIds != null && i.DependsOnItemIds.Any(on => replaced.Contains(on)))
                .Select(i => i.Id)
                .ToListAsync(ct);
            foreach (var dependent in await db.ImportBatchItems.Where(i => hanging.Contains(i.Id)).ToListAsync(ct))
            {
                dependent.DependsOnItemIds =
                    [.. dependent.DependsOnItemIds!.Select(on => replaced.Contains(on) ? item.Id : on).Distinct()];
            }

            await db.SaveChangesAsync(ct);
        }

        // A dependency is on a create that is still waiting — one kept just now, a repeat of one
        // already waiting, or one an earlier upload left waiting. A row that refers to something
        // that exists already depends on nothing held.
        var repeats = lines.Where(l => l.RepeatsItemId is not null && l.Action == ImportDecisionAction.Create)
            .ToDictionary(l => l.RowId, l => l.RepeatsItemId!.Value);
        var unresolved = pending.SelectMany(p => p.Line.Refers)
            .Where(id => !created.ContainsKey(id) && !repeats.ContainsKey(id))
            .Distinct()
            .ToList();
        var earlier = unresolved.Count == 0
            ? []
            : await UploadReviewHold.PendingLinesOf(db, batch.ConfirmedByUserId, ImportSource.DeviceSync)
                .AsNoTracking()
                .Where(i => i.Action == ImportDecisionAction.Create && i.ProposalRowId != null
                    && unresolved.Contains(i.ProposalRowId.Value) && i.ImportBatchId != batch.Id)
                .GroupBy(i => i.ProposalRowId!.Value)
                .Select(g => new { RowId = g.Key, Id = g.Max(i => i.Id) })
                .ToDictionaryAsync(x => x.RowId, x => x.Id, ct);

        // Every held create a line names is a dependency: a link needs its document and the place
        // it hangs on, a scan its recording and its place, and either being rejected leaves the
        // line nothing to be written onto.
        foreach (var (line, item) in pending)
        {
            var on = line.Refers.Where(id => id != line.RowId)
                .Select(refersTo => created.TryGetValue(refersTo, out var local) ? local.Id
                    : repeats.TryGetValue(refersTo, out var repeat) ? repeat
                    : earlier.TryGetValue(refersTo, out var before) ? before
                    : (long?)null)
                .OfType<long>()
                .Distinct()
                .ToList();
            item.DependsOnItemIds = on.Count == 0 ? null : on;
        }

        await db.SaveChangesAsync(ct);

        // The bytes of a replaced line go the way a turned-down line's do, unless the line that
        // replaced it names the same ones.
        await HeldUploadBytes.ReleaseAsync(db, superseded, releasedBytesRetentionDays, now, ct);

        // An earlier batch whose every waiting line this upload replaced has nothing left to
        // decide, and is closed rather than left waiting for nobody.
        touched.Remove(batch.Id);
        if (touched.Count > 0)
        {
            await UploadReviewHold.SettleAsync(db, touched, reviewerId: null, now, ct);
        }
    }

    /// <summary>
    /// Whether two stored proposals say the same thing. Compared as JSON values rather than as
    /// text, because the database keeps a jsonb value in its own member order.
    /// </summary>
    private static bool SameProposal(string? stored, string fresh) =>
        stored is not null && JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(fresh));
}
