// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Import;

/// <summary>What an undo of a phone upload did with one line of the upload.</summary>
/// <param name="ItemId">The line.</param>
/// <param name="FeatureId">The row the line is about; null when the row has since been removed for good.</param>
/// <param name="Change">What the upload did to the row (<see cref="SyncUndoChanges"/>); null when nothing says any more.</param>
/// <param name="Outcome"><see cref="SyncUndoOutcomes"/>.</param>
/// <param name="Reason">Why nothing was taken back (<see cref="SyncUndoReasons"/>), for <c>not_undone</c>.</param>
/// <param name="Applied">The fields put back, for an edit.</param>
/// <param name="Skipped">The fields of an edit left as they are, each with its reason.</param>
/// <param name="DevicePersonId">The phone user the line is about, for a line that sent one; null otherwise.</param>
/// <param name="DocumentId">The photo or document the line is about, for a line that sent one or hung one; null otherwise.</param>
/// <param name="TripRecordingId">The phone recording the line is about, for a recording line.</param>
/// <param name="TripRecordingPointId">The scan the line is about, for a scan line.</param>
/// <param name="TripLogId">For a recording taken back: the trip report that had been made from it.</param>
/// <param name="TripOutcome">What became of that report: <c>undone</c> (deleted with it) or <c>not_undone</c>.</param>
/// <param name="TripReason">Why the report was left (<see cref="SyncUndoReasons.TripChanged"/>, <see cref="SyncUndoReasons.ConvertedElsewhere"/>).</param>
public sealed record SyncUndoItemResult(
    long ItemId,
    Guid? FeatureId,
    string? Change,
    string Outcome,
    string? Reason,
    IReadOnlyList<string> Applied,
    IReadOnlyList<HistoryRestoreSkip> Skipped,
    Guid? DevicePersonId = null,
    Guid? DocumentId = null,
    Guid? TripRecordingId = null,
    Guid? TripRecordingPointId = null,
    Guid? TripLogId = null,
    string? TripOutcome = null,
    string? TripReason = null);

/// <summary>A request to undo an upload that cannot be answered line by line.</summary>
public sealed class SyncUndoException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Takes back everything one upload from a phone did: the rows it created are deleted, the rows it
/// deleted come back, and the fields it changed return to what they held before it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read out of the history, not out of a copy.</b> Every history row an upload wrote names the
/// upload's batch, and an edit's row holds each changed field's value before and after it (a large
/// shape keeps both full shapes beside it). That is everything an undo needs, so the batch keeps no
/// second record of the rows it touched — one record, with one set of protection rules.
/// </para>
/// <para>
/// <b>A later edit wins over the undo.</b> A field goes back only while it still holds what the
/// upload wrote; a field somebody has changed since is left and reported as superseded, and a row
/// somebody deleted again after the upload is left deleted. <c>force</c> puts them back anyway —
/// but never past the protection rules: a position goes back only for a caller who may see it
/// exactly, and a field their history hides is not theirs to have back. Those two are asked before
/// anything compares a value, so a caller who may not see a position learns nothing about whether
/// it moved since.
/// </para>
/// <para>
/// <b>Each value goes back as an ordinary edit</b>, through the same rules a restore from the
/// history uses (<see cref="HistoryRestoreService"/>) and through <see cref="FeatureWriteService"/>,
/// so the cave's main entrance and its point follow and a property document is validated. The undo
/// is a change set of its own, filed under the batch it takes back, so the batch's timeline ends
/// with its undo; every row it changes is written now, so a device reads the undone state through
/// its ordinary cursor.
/// </para>
/// <para>
/// <b>A line at a time, or all of them.</b> Lines named in <c>itemIds</c> are undone and the rest
/// left; a line taken back, wholly or partly, is stamped so a second undo does not compare its row
/// against values it put back itself. The batch counts as reverted once every line is stamped; a
/// line that could not be taken back stays open for another try. A line stands or falls whole: a
/// line refused half-way is rolled back to where it began.
/// </para>
/// <para>
/// <b>The cave's main role goes back where it was.</b> Promoting an entrance takes the role from
/// another, and deleting the main entrance hands it on; neither entrance that lost or gained it
/// has a line of its own, but both have history rows filed under the upload, and those say which
/// entrance held the role before. Once the line that moved the role is taken back, the role
/// returns to that entrance, so the cave plots where it plotted before the upload.
/// </para>
/// </remarks>
public sealed partial class DeviceSyncUndoService(
    SilexGisDbContext db,
    FeatureWriteService writer,
    HistoryRestoreService history,
    IAccessService access,
    IChangeContext changes,
    DocumentWriteService documents,
    TripLogWriteService trips)
{
    public const string ItemsUnknownCode = "import_batch.items_unknown";

    private const string LineSavepoint = "sync_undo_line";
    private const string DeviceEditTime = nameof(Feature.ClientUpdatedAt);
    private const string MainRole = nameof(CaveEntrance.IsMain);
    private static readonly string EntranceType = FeatureAudit.TypeName(FeatureKind.CaveEntrance);
    private const string PersonType = nameof(DevicePerson);

    public async Task<IReadOnlyList<SyncUndoItemResult>> UndoAsync(
        ImportBatch batch,
        AccessContext ctx,
        IReadOnlyCollection<long>? itemIds,
        bool force,
        CancellationToken ct = default)
    {
        var allItems = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == batch.Id)
            .ToListAsync(ct);
        var wanted = itemIds is { Count: > 0 } ? itemIds.ToHashSet() : null;
        if (wanted is not null && wanted.Except(allItems.Select(i => i.Id)).Any())
        {
            throw new SyncUndoException(ItemsUnknownCode, "Some of the lines named are not lines of this upload.");
        }

        changes.Begin(ChangeSource.Undo, batch.Id);

        // Newest first: what the upload did last is taken back first, so a row it created inside
        // a row it also created goes before its container, and a container it deleted comes back
        // before the rows inside it are looked at.
        //
        // A phone recording is the one exception: its lines go before its scans' lines whatever
        // their order, because a scan the upload deleted comes back only inside a recording that is
        // there, and an upload that deleted a recording sent its scans' deletions after it.
        var items = allItems
            .Where(i => wanted is null || wanted.Contains(i.Id))
            .OrderBy(i => i.TripRecordingId is not null ? 0 : 1)
            .ThenByDescending(i => i.Id)
            .ToList();

        // What the upload itself wrote — not what an earlier undo of it wrote, which carries the
        // same batch under its own source.
        var written = await db.AuditEntries.AsNoTracking()
            .Where(a => a.ImportBatchId == batch.Id && a.Source == ChangeSource.DeviceSync && a.EntityId != null)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        // A phone user's history rows are keyed by the phone's identifier for them, which the
        // phone minted as it mints feature identifiers; kept apart so the two can never be read
        // as each other's.
        var byPerson = written.Where(a => a.EntityType == PersonType)
            .GroupBy(a => a.EntityId!)
            .ToDictionary(g => g.Key, g => g.ToList());
        written = [.. written.Where(a => a.EntityType != PersonType)];

        // Likewise a document's: the phone mints its documents' identifiers too, and a document
        // under the same identifier as a feature must never have its rows read as the feature's.
        var byDocument = written.Where(a => IsDocumentWorld(a.EntityType))
            .GroupBy(a => a.EntityId!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var byRow = written.Where(a => !IsDocumentWorld(a.EntityType))
            .GroupBy(a => a.EntityId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The batch's own row is filed under it too, so an upload made since the history began
        // naming its batch always has at least one; an older upload has none at all.
        var attributed = written.Count > 0 || byPerson.Count > 0;

        // Read before anything is taken back: which entrance held each cave's main role before the
        // upload, and whether the one holding it now holds it by the upload's doing.
        var mainRoles = await MainRoleTrailsAsync(written, ct);

        var createdIds = items.Where(i => i.FeatureId is not null && i.UndoneAt is null)
            .Select(i => i.FeatureId!.Value)
            .ToHashSet();
        var cavesOfCreatedEntrances = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => createdIds.Contains(e.Id))
            .Select(e => e.CaveFeatureId)
            .Distinct()
            .ToListAsync(ct);

        var results = new List<SyncUndoItemResult>(items.Count);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var item in items)
        {
            // A line stands or falls whole. Deleting and restoring write straight to the database,
            // ahead of the save, so the tracker alone cannot take a refused line's half back.
            await transaction.CreateSavepointAsync(LineSavepoint, ct);
            SyncUndoItemResult result;
            try
            {
                result = item.DevicePersonId is { } personId
                    ? await UndoPersonLineAsync(item, personId, byPerson.GetValueOrDefault(personId.ToString()) ?? [], force, ct)
                    : IsLinkLine(item)
                        ? await UndoLinkLineAsync(item, byDocument, ctx, force, ct)
                        : item.DocumentId is { } documentId
                            ? await UndoDocumentLineAsync(item, documentId, byDocument, ctx, force, ct)
                            : item.TripRecordingId is not null || item.TripRecordingPointId is not null
                                ? await UndoRecordingLineAsync(item, batch.Id, written, ctx, force, ct)
                                : await UndoLineAsync(item, byRow, attributed, ctx, force, ct);
                await db.SaveChangesAsync(ct);
                await transaction.ReleaseSavepointAsync(LineSavepoint, ct);
            }
            catch (FeatureWriteException e)
            {
                // The line's own half-made change goes; the lines already taken back are saved.
                await transaction.RollbackToSavepointAsync(LineSavepoint, ct);
                db.ChangeTracker.Clear();
                result = NotUndone(item, item.FeatureId ?? item.AttachedToFeatureId, null, e.Code);
            }
            catch (DocumentWriteException)
            {
                // The revision the upload made is no longer the current one: another revision
                // landed between the undo's look and its write.
                await transaction.RollbackToSavepointAsync(LineSavepoint, ct);
                db.ChangeTracker.Clear();
                result = new SyncUndoItemResult(
                    item.Id, null, SyncUndoChanges.Updated, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded,
                    [], [], null, item.DocumentId);
            }
            catch (LineWithdrawnException withdrawn)
            {
                await transaction.RollbackToSavepointAsync(LineSavepoint, ct);
                db.ChangeTracker.Clear();
                result = withdrawn.Result;
            }

            results.Add(result);
        }

        // A cave that only gained an entrance from the upload keeps its other entrances and has
        // its count and point worked out again, as the import revert does.
        foreach (var caveId in cavesOfCreatedEntrances.Where(c => !createdIds.Contains(c)))
        {
            if (await db.Features.AsNoTracking().AnyAsync(f => f.Id == caveId, ct))
            {
                await writer.SyncCaveMirrorAsync(caveId, ct);
            }
        }

        await db.SaveChangesAsync(ct);

        await GiveMainRolesBackAsync(mainRoles, results, ctx, force, ct);

        var now = DateTimeOffset.UtcNow;

        // A line the history cannot speak for — an upload older than the history's record of
        // which rows each upload wrote — is stamped as well: no later undo can do more with it,
        // and the batch then closes once what it created has gone, as reverting such an upload
        // always did. Its answer still says that nothing of it was taken back.
        var takenBack = results
            .Where(r => r.Outcome is SyncUndoOutcomes.Undone or SyncUndoOutcomes.Partly
                || r.Reason is SyncUndoReasons.NoHistory)
            .Select(r => r.ItemId)
            .ToList();
        if (takenBack.Count > 0)
        {
            await db.ImportBatchItems
                .Where(i => takenBack.Contains(i.Id) && i.UndoneAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UndoneAt, now), ct);
        }

        if (!await db.ImportBatchItems.AnyAsync(i => i.ImportBatchId == batch.Id && i.UndoneAt == null, ct))
        {
            var stamped = await db.ImportBatches.FirstAsync(b => b.Id == batch.Id, ct);
            stamped.RevertedAt = now;
            stamped.RevertedByUserId = ctx.UserId;
            batch.RevertedAt = now;
            batch.RevertedByUserId = ctx.UserId;
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return [.. results.OrderBy(r => r.ItemId)];
    }

    private async Task<SyncUndoItemResult> UndoLineAsync(
        ImportBatchItem item,
        Dictionary<string, List<AuditEntry>> byRow,
        bool attributed,
        AccessContext ctx,
        bool force,
        CancellationToken ct)
    {
        if (item.FeatureId is { } createdId)
        {
            if (item.UndoneAt is not null)
            {
                return NotUndone(item, createdId, SyncUndoChanges.Created, SyncUndoReasons.AlreadyUndone);
            }

            // Already deleted is already undone: the row is out of the registry, as it was before
            // the upload. Deleting a cave deletes the entrances inside it, so this is also how an
            // entrance the upload created under a cave it created ends up.
            var alive = await db.Features.AsNoTracking().AnyAsync(f => f.Id == createdId, ct);
            if (alive)
            {
                await writer.SoftDeleteAsync(createdId, ct);
            }

            return Done(item, createdId, SyncUndoChanges.Created);
        }

        if (item.AttachedToFeatureId is not { } rowId)
        {
            // The row the line changed has been removed for good, and its line no longer says which
            // row it was.
            return NotUndone(item, null, null, SyncUndoReasons.Purged);
        }

        var rows = byRow.GetValueOrDefault(rowId.ToString()) ?? [];
        var edits = rows.Where(r => r.Action == AuditActions.Updated).ToList();
        var deletion = rows.LastOrDefault(r => r.Action == AuditActions.Deleted && r.Changes is null);
        var change = edits.Count > 0 ? SyncUndoChanges.Updated : deletion is not null ? SyncUndoChanges.Deleted : null;
        if (item.UndoneAt is not null)
        {
            return NotUndone(item, rowId, change, SyncUndoReasons.AlreadyUndone);
        }

        if (change is null)
        {
            // An upload that names its batch in the history and wrote no row for this one changed
            // nothing in it — a device re-sending a row as it already stood is answered "updated"
            // and leaves a line, and EF writes nothing. There is nothing to take back. An older
            // upload names nothing, and the history cannot say what it did.
            return attributed
                ? Done(item, rowId, SyncUndoChanges.Updated)
                : NotUndone(item, rowId, null, SyncUndoReasons.NoHistory);
        }

        var state = await db.Features.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Id == rowId)
            .Select(f => new { f.DeletedAt })
            .FirstOrDefaultAsync(ct);
        if (state is null)
        {
            return NotUndone(item, rowId, change, SyncUndoReasons.Purged);
        }

        return change == SyncUndoChanges.Deleted
            ? await UndoDeletionAsync(item, rowId, deletion!, state.DeletedAt is not null, ctx, force, ct)
            : await UndoEditAsync(item, rowId, edits, deletion, state.DeletedAt is not null, ctx, force, ct);
    }

    private async Task<SyncUndoItemResult> UndoDeletionAsync(
        ImportBatchItem item, Guid rowId, AuditEntry deletion, bool deleted, AccessContext ctx, bool force, CancellationToken ct)
    {
        if (!deleted)
        {
            return Done(item, rowId, SyncUndoChanges.Deleted);
        }

        // The right that deletes a row is the right that restores it, whoever restores it and by
        // whatever route — asked first, so a caller who may no longer read the row learns nothing
        // of what has happened to it since.
        if (!await FeatureRestoreRights.MayRestoreOneAsync(db, access, ctx, rowId, ct))
        {
            return NotUndone(item, rowId, SyncUndoChanges.Deleted, SyncUndoReasons.Forbidden);
        }

        // Put back and deleted again since: the newer deletion is somebody's decision, and the
        // restore below would undo that one rather than the upload's.
        var key = rowId.ToString();
        var deletedAgain = await db.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.EntityId == key && a.Id > deletion.Id && a.Action == AuditActions.Deleted
                && a.EntityType == deletion.EntityType, ct);
        if (deletedAgain && !force)
        {
            return NotUndone(item, rowId, SyncUndoChanges.Deleted, SyncUndoReasons.Superseded);
        }

        try
        {
            await writer.RestoreAsync(rowId, ct);
        }
        catch (FeatureWriteException e) when (e.Code == FeatureDeletionRules.ContainerDeletedCode)
        {
            return NotUndone(item, rowId, SyncUndoChanges.Deleted, SyncUndoReasons.ContainerDeleted);
        }

        // Asked again once the write is in: somebody deleting a container while the undo runs
        // skips this row, which their snapshot still shows deleted, and the row would come back
        // standing inside a deleted container — reachable by its own address and inheriting its
        // audience from nothing.
        await db.SaveChangesAsync(ct);
        if (await writer.HasDeletedContainerAsync(rowId, ct))
        {
            throw new LineWithdrawnException(
                NotUndone(item, rowId, SyncUndoChanges.Deleted, SyncUndoReasons.ContainerDeleted));
        }

        return Done(item, rowId, SyncUndoChanges.Deleted);
    }

    private async Task<SyncUndoItemResult> UndoEditAsync(
        ImportBatchItem item,
        Guid rowId,
        List<AuditEntry> edits,
        AuditEntry? deletion,
        bool deleted,
        AccessContext ctx,
        bool force,
        CancellationToken ct)
    {
        if (deleted)
        {
            // Edited by the upload and deleted since. Deleted in the same upload, with a container
            // the upload also deleted, it comes back with that container or not at all; deleted by
            // somebody afterwards, the deletion is the newer state and stays.
            return NotUndone(item, rowId, SyncUndoChanges.Updated,
                deletion is not null ? SyncUndoReasons.ContainerDeleted : SyncUndoReasons.Superseded);
        }

        var feature = await db.Features
            .Include(f => f.Cave)
            .Include(f => f.Entrance)
            .FirstAsync(f => f.Id == rowId, ct);

        // An entrance is changed under its cave's permission, as its own edit is.
        var governing = feature.Entrance is { } own
            ? await db.Features.FirstOrDefaultAsync(f => f.Id == own.CaveFeatureId, ct)
            : feature;
        if (governing is null || !(await access.DecideAsync(ctx, AccessAction.Write, governing, ct)).Allowed)
        {
            return NotUndone(item, rowId, SyncUndoChanges.Updated, SyncUndoReasons.Forbidden);
        }

        // Per field, the value before the upload's first edit of it and the value its last edit
        // left. Taken back: the fields a restore may write, and the device's own edit time, which
        // the upload wrote and devices are handed back. The rest of what an edit's row records is
        // bookkeeping the write keeps on its own (the write time, derived ancestry and
        // protection, the schema stamp).
        var span = new Dictionary<string, (AuditEntry First, JsonObject FirstChanges, JsonObject LastChanges)>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            var diff = HistoryRestoreService.ChangesOf(edit);
            foreach (var (field, _) in diff)
            {
                if (!FeatureHistoryFields.IsRestorable(feature.Kind, field) && field != DeviceEditTime)
                {
                    continue;
                }

                span[field] = span.TryGetValue(field, out var seen)
                    ? (seen.First, seen.FirstChanges, diff)
                    : (edit, diff, diff);
            }
        }

        // A field the upload wrote back unchanged — a device sends the whole row, and a shape is
        // written whether or not it moved — was not changed by it, and there is nothing to take back.
        foreach (var field in span.Where(s => JsonNode.DeepEquals(
                     (s.Value.FirstChanges[s.Key] as JsonObject)?["old"],
                     (s.Value.LastChanges[s.Key] as JsonObject)?["new"]))
                 .Select(s => s.Key)
                 .ToList())
        {
            span.Remove(field);
        }

        var applied = new Dictionary<string, object?>(StringComparer.Ordinal);
        var skipped = new List<HistoryRestoreSkip>();
        var views = new Dictionary<long, (bool Exact, HashSet<string> Redacted)>();
        foreach (var (field, (first, firstChanges, lastChanges)) in span)
        {
            if (!views.TryGetValue(first.Id, out var view))
            {
                var (exact, _, redacted) = await history.ViewAsync(ctx, feature, first.EntityType!, firstChanges, ct);
                view = (exact, redacted);
                views[first.Id] = view;
            }

            // The device's edit time is no position and no field of the edit form, so of the
            // restore's checks only what the caller's history hides applies to it.
            var reason = field == DeviceEditTime
                ? view.Redacted.Contains(field) ? HistoryRestoreReasons.Redacted : null
                : history.Gate(feature, firstChanges, field, view.Exact, view.Redacted);
            if (reason is null && !force
                && !HistoryValues.StillRecorded(
                    (lastChanges[field] as JsonObject)?["new"],
                    writer.FieldValue(feature, field),
                    writer.FieldType(feature, field)!,
                    writer.IsDocumentField(feature, field)))
            {
                reason = HistoryRestoreReasons.Superseded;
            }

            reason ??= await history.ReadOldAsync(feature, first, firstChanges, field, applied, ct);
            if (reason is not null)
            {
                skipped.Add(new HistoryRestoreSkip(field, reason));
            }
        }

        if (applied.Count > 0)
        {
            await writer.ApplyHistoricalValuesAsync(feature, applied, ct);
        }

        if (skipped.Count == 0)
        {
            return new SyncUndoItemResult(item.Id, rowId, SyncUndoChanges.Updated, SyncUndoOutcomes.Undone, null,
                [.. applied.Keys], []);
        }

        return applied.Count > 0
            ? new SyncUndoItemResult(item.Id, rowId, SyncUndoChanges.Updated, SyncUndoOutcomes.Partly, null,
                [.. applied.Keys], skipped)
            : new SyncUndoItemResult(item.Id, rowId, SyncUndoChanges.Updated, SyncUndoOutcomes.NotUndone,
                SyncUndoReasons.FieldsLeft, [], skipped);
    }

    // ---------- a cave's main role ----------

    /// <summary>
    /// One cave whose main role the upload took from an entrance — by promoting another, or by
    /// deleting the one that held it, after which the cave's own rule handed the role on.
    /// </summary>
    /// <param name="CaveId">The cave.</param>
    /// <param name="PreviousMain">The entrance that held the role when the upload began.</param>
    /// <param name="MainNow">The entrance holding it as the undo starts; null when none does.</param>
    /// <param name="HeldByTheUpload">
    /// Whether <paramref name="MainNow"/> holds the role by the upload's doing — created by it, or
    /// given the role by it — with nobody having moved the role since.
    /// </param>
    private sealed record MainRoleTrail(Guid CaveId, Guid PreviousMain, Guid? MainNow, bool HeldByTheUpload);

    /// <summary>
    /// Where each cave's main role stood before the upload and where it stands now, read out of
    /// the upload's history before anything is taken back.
    /// </summary>
    /// <remarks>
    /// The entrance that loses the role has no line of its own in the upload: the line is the one
    /// promoted, or the one deleted. Its history row is filed under the upload all the same, and
    /// that row is what says which entrance to give the role back to. Without it, taking the
    /// promotion back would leave the role to the cave's fallback rule — the oldest entrance — and
    /// the cave would plot at an entrance that was never its main one.
    /// </remarks>
    private async Task<List<MainRoleTrail>> MainRoleTrailsAsync(List<AuditEntry> written, CancellationToken ct)
    {
        var rows = written
            .Where(a => a.EntityType == EntranceType && Guid.TryParse(a.EntityId, out _))
            .GroupBy(a => Guid.Parse(a.EntityId!))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).ToList());
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Keys.ToList();
        var entrances = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.CaveFeatureId, e.IsMain })
            .ToDictionaryAsync(e => e.Id, ct);

        var previous = new Dictionary<Guid, (Guid Entrance, long Since)>();
        var madeMain = new HashSet<Guid>();
        var lastRow = new Dictionary<Guid, long>();
        foreach (var (id, history) in rows)
        {
            if (!entrances.TryGetValue(id, out var entrance))
            {
                continue; // removed for good since; there is no role to give it
            }

            lastRow[id] = history[^1].Id;
            var moves = history
                .Where(a => a.Action == AuditActions.Updated)
                .Select(a => (a.Id, Pair: HistoryRestoreService.ChangesOf(a)[MainRole] as JsonObject))
                .Where(m => m.Pair is not null)
                .ToList();
            if (history.Any(a => a.Action == AuditActions.Created) || (moves.Count > 0 && IsTrue(moves[^1].Pair!["new"])))
            {
                madeMain.Add(id);
            }

            // It held the role when the upload began if its first move in the upload took the
            // role away, or if the upload deleted it while it held the role — a soft deletion
            // leaves the flag on the row it hides.
            long? heldUntil = moves.Count > 0
                ? IsTrue(moves[0].Pair!["old"]) ? moves[0].Id : null
                : entrance.IsMain
                  && history.FirstOrDefault(a => a.Action == AuditActions.Deleted && a.Changes is null) is { } gone
                    ? gone.Id
                    : null;
            if (heldUntil is { } at
                && (!previous.TryGetValue(entrance.CaveFeatureId, out var seen) || at < seen.Since))
            {
                previous[entrance.CaveFeatureId] = (id, at);
            }
        }

        if (previous.Count == 0)
        {
            return [];
        }

        var caveIds = previous.Keys.ToList();
        var mainsNow = await db.CaveEntrances.AsNoTracking()
            .Where(e => caveIds.Contains(e.CaveFeatureId) && e.IsMain && db.Features.Any(f => f.Id == e.Id))
            .Select(e => new { e.Id, e.CaveFeatureId })
            .ToListAsync(ct);

        var trails = new List<MainRoleTrail>(previous.Count);
        foreach (var (caveId, (prior, _)) in previous)
        {
            Guid? now = mainsNow.FirstOrDefault(m => m.CaveFeatureId == caveId)?.Id;
            var held = now is not { } holder
                || (madeMain.Contains(holder) && !await RoleMovedSinceAsync(holder, lastRow[holder], ct));
            trails.Add(new MainRoleTrail(caveId, prior, now, held));
        }

        return trails;
    }

    // Whether anything after the upload changed this entrance's main role.
    private async Task<bool> RoleMovedSinceAsync(Guid entranceId, long afterRowId, CancellationToken ct)
    {
        var key = entranceId.ToString();
        var later = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == key && a.Id > afterRowId && a.Action == AuditActions.Updated)
            .ToListAsync(ct);
        return later.Any(a => HistoryRestoreService.ChangesOf(a).ContainsKey(MainRole));
    }

    /// <summary>
    /// Gives each cave's main role back to the entrance that held it before the upload, once this
    /// undo has taken back the line that moved it.
    /// </summary>
    /// <remarks>
    /// Held to the rules of the line it follows from: the caller must still be able to change the
    /// cave, and — because the cave's point moves with the role — see both the cave and the
    /// entrance exactly, asked before anything says whether the role moved since. A role somebody
    /// moved after the upload stays where they put it unless <c>force</c>. A role left as it is
    /// is reported on that line, as a skipped <c>IsMain</c>.
    /// </remarks>
    private async Task GiveMainRolesBackAsync(
        List<MainRoleTrail> trails, List<SyncUndoItemResult> results, AccessContext ctx, bool force, CancellationToken ct)
    {
        foreach (var trail in trails.Where(t => t.MainNow != t.PreviousMain))
        {
            // The line that moved the role, taken back by this undo: the entrance holding it gave
            // it up (its promotion was put back, or the upload created it and it is gone), or the
            // entrance that held it came back from the upload's deletion.
            var index = results.FindIndex(r =>
                r.Outcome is SyncUndoOutcomes.Undone or SyncUndoOutcomes.Partly
                && ((trail.MainNow is { } holder && r.FeatureId == holder
                        && (r.Change == SyncUndoChanges.Created || r.Applied.Contains(MainRole)))
                    || (r.FeatureId == trail.PreviousMain && r.Change == SyncUndoChanges.Deleted)));
            if (index < 0)
            {
                continue;
            }

            // Read afresh: the lines above wrote straight to the database as well as through the
            // tracker, and a tracked copy would still show a restored entrance as deleted.
            db.ChangeTracker.Clear();
            var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == trail.CaveId, ct);
            var waiting = cave is not null && await db.CaveEntrances.AsNoTracking()
                .AnyAsync(e => e.Id == trail.PreviousMain && e.CaveFeatureId == trail.CaveId && !e.IsMain
                    && db.Features.Any(f => f.Id == e.Id), ct);
            if (!waiting)
            {
                continue;
            }

            string? reason = null;
            if (!(await access.DecideAsync(ctx, AccessAction.Write, cave!, ct)).Allowed
                || !await history.SeesExactlyAsync(ctx, [trail.PreviousMain, trail.CaveId], ct))
            {
                reason = HistoryRestoreReasons.Forbidden;
            }
            else if (!force && !trail.HeldByTheUpload)
            {
                reason = HistoryRestoreReasons.Superseded;
            }

            if (reason is null)
            {
                await writer.SetMainEntranceAsync(trail.CaveId, trail.PreviousMain, ct);
                await db.SaveChangesAsync(ct);
                continue;
            }

            var line = results[index];
            results[index] = line with
            {
                Outcome = SyncUndoOutcomes.Partly,
                Skipped = [.. line.Skipped, new HistoryRestoreSkip(MainRole, reason)],
            };
        }
    }

    private static bool IsTrue(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out var yes) && yes;

    /// <summary>A line refused after part of it was written: the line's writes are rolled back and this is its answer.</summary>
    private sealed class LineWithdrawnException(SyncUndoItemResult result) : Exception
    {
        public SyncUndoItemResult Result { get; } = result;
    }

    /// <summary>
    /// Takes back what an upload did to one phone user: the link to a caver it made is removed and
    /// the names it changed go back to what they were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A phone user the upload described for the first time is not removed. That the phone has
    /// such a user is a fact about the phone, which the next upload states again; what the upload
    /// decided is the link, and the link is what goes. Removing the row would also take away the
    /// record later uploads and imports resolve the same phone user through.
    /// </para>
    /// <para>
    /// The rule a later edit wins by is the features' rule. A value goes back only while it still
    /// holds what the upload wrote; one somebody has changed since — a caver linked in the
    /// browser, a name the phone sent again differently — is left and reported as superseded,
    /// unless <c>force</c> asks for it anyway. The link and who made it go back together, as one
    /// field: a caver id restored under the source of somebody else's link would claim an
    /// authority nobody exercised.
    /// </para>
    /// </remarks>
    private async Task<SyncUndoItemResult> UndoPersonLineAsync(
        ImportBatchItem item, Guid personId, List<AuditEntry> history, bool force, CancellationToken ct)
    {
        var created = history.Exists(a => a.Action == AuditActions.Created);
        var change = created ? SyncUndoChanges.Created : SyncUndoChanges.Updated;
        if (item.UndoneAt is not null)
        {
            return PersonResult(item, personId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone, [], []);
        }

        var person = await db.DevicePersons.FirstOrDefaultAsync(p => p.DeviceUserId == personId, ct);
        if (person is null)
        {
            return PersonResult(item, personId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged, [], []);
        }

        // Each field's value before the upload's first change to it and after its last.
        var fields = new Dictionary<string, (string? Old, string? New)>(StringComparer.Ordinal);
        foreach (var entry in history.Where(a => a.Action is AuditActions.Created or AuditActions.Updated))
        {
            if (entry.Changes is null || JsonNode.Parse(entry.Changes) is not JsonObject changed)
            {
                continue;
            }

            foreach (var (name, pair) in changed)
            {
                var newValue = pair?["new"]?.ToString();
                fields[name] = fields.TryGetValue(name, out var seen)
                    ? (seen.Old, newValue)
                    : (entry.Action == AuditActions.Created ? null : pair?["old"]?.ToString(), newValue);
            }
        }

        if (fields.Count == 0)
        {
            // An upload that wrote no history for this phone user changed nothing in it.
            return PersonResult(item, personId, change, SyncUndoOutcomes.Undone, null, [], []);
        }

        var applied = new List<string>();
        var skipped = new List<HistoryRestoreSkip>();

        if (fields.TryGetValue(nameof(DevicePerson.CaverId), out var link))
        {
            // The link holds while both halves are still what the upload left: the caver, and the
            // source the upload linked under. That source is the one the history recorded when the
            // upload changed it; when it did not — a phone moving its own link from one caver to
            // another leaves the source as it was — it is a phone's, because only a phone uploads.
            // Somebody who has since confirmed the same caver in the browser has made the link
            // theirs, and putting the phone's earlier choice back under their authority would make
            // it a link no later phone correction may replace.
            var linkedUnder = fields.TryGetValue(nameof(DevicePerson.LinkSource), out var by)
                ? by.New
                : SourceName(DevicePersonLinkSource.Device);
            var holds = string.Equals(person.CaverId?.ToString(), link.New, StringComparison.OrdinalIgnoreCase)
                && string.Equals(SourceName(person.LinkSource), linkedUnder, StringComparison.Ordinal);
            if (holds || force)
            {
                person.CaverId = Guid.TryParse(link.Old, out var before) ? before : null;

                // Who made the link goes back with it. When the upload changed it, to what it was;
                // when it did not, to a phone's — the caver put back was a phone's choice, whatever
                // source a forced undo finds on the row now.
                person.LinkSource = fields.TryGetValue(nameof(DevicePerson.LinkSource), out var source)
                    && Enum.TryParse<DevicePersonLinkSource>(source.Old, ignoreCase: true, out var sourceBefore)
                        ? sourceBefore
                        : DevicePersonLinkSource.Device;
                applied.Add(nameof(DevicePerson.CaverId));
            }
            else
            {
                skipped.Add(new HistoryRestoreSkip(nameof(DevicePerson.CaverId), SyncUndoReasons.Superseded));
            }
        }

        // A first description has no earlier names to go back to; only an edit's names do.
        if (!created)
        {
            RestoreName(nameof(DevicePerson.Username), person.Username, v => person.Username = v ?? person.Username);
            RestoreName(nameof(DevicePerson.FirstName), person.FirstName, v => person.FirstName = v);
            RestoreName(nameof(DevicePerson.LastName), person.LastName, v => person.LastName = v);
        }

        if (applied.Count == 0 && skipped.Count > 0)
        {
            return PersonResult(item, personId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded, applied, skipped);
        }

        return PersonResult(
            item, personId, change, skipped.Count > 0 ? SyncUndoOutcomes.Partly : SyncUndoOutcomes.Undone, null, applied, skipped);

        void RestoreName(string name, string? current, Action<string?> set)
        {
            if (!fields.TryGetValue(name, out var value))
            {
                return;
            }

            if (string.Equals(current, value.New, StringComparison.Ordinal) || force)
            {
                set(value.Old);
                applied.Add(name);
            }
            else
            {
                skipped.Add(new HistoryRestoreSkip(name, SyncUndoReasons.Superseded));
            }
        }
    }

    // As the history spells an enum value.
    private static string SourceName(DevicePersonLinkSource source) =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(source.ToString());

    private static SyncUndoItemResult PersonResult(
        ImportBatchItem item,
        Guid personId,
        string change,
        string outcome,
        string? reason,
        IReadOnlyList<string> applied,
        IReadOnlyList<HistoryRestoreSkip> skipped) =>
        new(item.Id, null, change, outcome, reason, applied, skipped, personId);

    /// <summary>
    /// A phone recording or scan line. One the upload created is deleted — a deletion, not a
    /// removal, so the phones carrying it are told — and a recording's trip report goes with it when
    /// a conversion of this recording's own uploads made the report and nobody has touched it since.
    /// One the upload changed or deleted gets its fields back, or comes back, out of the history the
    /// upload wrote, as a feature does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Touched" is read from the trip's history: every row on the trip, or on anything of it — its
    /// people, its positions, its tracking, its flag — has to be one the conversion that made it
    /// wrote. Anything else there means somebody worked on the report, and a report somebody worked
    /// on is theirs: it is left, and the answer names it so a person can decide.
    /// </para>
    /// <para>
    /// The conversion that made the report may be that of a later upload than the one that created
    /// the recording — the upload that closed a recording sent while the trip was still under way.
    /// Undoing either takes the untouched report back: the creating upload's undo because the
    /// recording goes, the closing upload's because the recording is open again. A report an archive
    /// import made is not an upload's to take back, and is left the same way.
    /// </para>
    /// <para>
    /// The report is named only to a caller who may read it: which trip it is belongs to the trip's
    /// own audience, as the archive import's refusal has it.
    /// </para>
    /// </remarks>
    private async Task<SyncUndoItemResult> UndoRecordingLineAsync(
        ImportBatchItem item, Guid batchId, List<AuditEntry> written, AccessContext ctx, bool force, CancellationToken ct)
    {
        var recordingLine = item.TripRecordingId is not null;
        var change = item.Action == ImportDecisionAction.Create ? SyncUndoChanges.Created : SyncUndoChanges.Updated;
        SyncUndoItemResult Line(
            string outcome,
            string? reason,
            ReportOutcome? report = null,
            IReadOnlyList<string>? applied = null,
            IReadOnlyList<HistoryRestoreSkip>? skipped = null) =>
            new(item.Id, null, change, outcome, reason, applied ?? [], skipped ?? [], null,
                TripRecordingId: item.TripRecordingId, TripRecordingPointId: item.TripRecordingPointId,
                TripLogId: report?.TripLogId, TripOutcome: report?.Outcome, TripReason: report?.Reason);

        if (item.UndoneAt is not null)
        {
            return Line(SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone);
        }

        if (item.Action != ImportDecisionAction.Create)
        {
            return await UndoRecordingChangeAsync(item, batchId, written, ctx, force, ct);
        }

        var now = DateTimeOffset.UtcNow;
        if (!recordingLine)
        {
            var point = await db.TripRecordingPoints.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == item.TripRecordingPointId, ct);
            if (point is null)
            {
                return Line(SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
            }

            point.DeletedAt ??= now;
            return Line(SyncUndoOutcomes.Undone, null);
        }

        var recording = await db.TripRecordings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == item.TripRecordingId, ct);
        if (recording is null)
        {
            return Line(SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
        }

        recording.DeletedAt ??= now;
        return Line(SyncUndoOutcomes.Undone, null, await TakeBackReportAsync(recording, batchId, creatingLine: true, ctx, ct));
    }

    /// <summary>What became of a recording's trip report; the trip named only to a caller who may read it.</summary>
    private sealed record ReportOutcome(Guid? TripLogId, string Outcome, string? Reason);

    /// <summary>
    /// The report made from <paramref name="recording"/>, taken back when a conversion of one of the
    /// right uploads made it and nobody has touched it since. For the line that created the
    /// recording the right uploads are any of the recording's own uploads; for a line that changed
    /// it, this upload only — and then the recording loses its mark, so the change that undo puts
    /// back can make a report again. Null when there is nothing to say about a report.
    /// </summary>
    private async Task<ReportOutcome?> TakeBackReportAsync(
        TripRecording recording, Guid batchId, bool creatingLine, AccessContext ctx, CancellationToken ct)
    {
        if (recording.ConvertedTripLogId is not { } tripId)
        {
            return null;
        }

        // A report already deleted is not named: nobody may read it any more, which is the question
        // that decides naming.
        var trip = await db.TripLogs.FirstOrDefaultAsync(t => t.Id == tripId, ct);
        var named = trip is not null && (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? tripId
            : (Guid?)null;

        var key = tripId.ToString();
        var ofTheTrip = db.AuditEntries.AsNoTracking().Where(a =>
            (a.EntityType == nameof(TripLog) && a.EntityId == key)
            || (a.RootEntityType == nameof(TripLog) && a.RootEntityId == key));
        var madeBy = await ofTheTrip
            .Where(a => a.EntityType == nameof(TripLog)
                && a.Action == AuditActions.Created
                && a.Source == ChangeSource.TripConversion)
            .Select(a => a.ImportBatchId)
            .FirstOrDefaultAsync(ct);

        bool ours;
        if (madeBy is not { } conversion)
        {
            ours = false;
        }
        else if (creatingLine)
        {
            ours = await db.ImportBatchItems.AsNoTracking()
                .AnyAsync(i => i.ImportBatchId == conversion && i.TripRecordingId == recording.Id, ct);
        }
        else
        {
            ours = conversion == batchId;
        }

        if (!ours)
        {
            // A change line has nothing to say about a report somebody else's work made.
            return creatingLine
                ? trip is null
                    ? new ReportOutcome(named, SyncUndoOutcomes.Undone, null)
                    : new ReportOutcome(named, SyncUndoOutcomes.NotUndone, SyncUndoReasons.ConvertedElsewhere)
                : null;
        }

        if (trip is null)
        {
            // Already out of the registry, which is where the undo would have put it.
            return new ReportOutcome(named, SyncUndoOutcomes.Undone, null);
        }

        var touched = await ofTheTrip.AnyAsync(a => a.Source != ChangeSource.TripConversion || a.ImportBatchId != madeBy, ct);
        if (touched)
        {
            return new ReportOutcome(named, SyncUndoOutcomes.NotUndone, SyncUndoReasons.TripChanged);
        }

        trips.SoftDelete(trip, ctx.UserId);
        if (!creatingLine)
        {
            recording.ConvertedTripLogId = null;
        }

        return new ReportOutcome(named, SyncUndoOutcomes.Undone, null);
    }

    /// <summary>The fields of a recording an undo puts back; the rest is bookkeeping the write keeps.</summary>
    private static readonly string[] RecordingFields =
    [
        nameof(TripRecording.CaveFeatureId), nameof(TripRecording.Title), nameof(TripRecording.Description),
        nameof(TripRecording.Log), nameof(TripRecording.StartedAt), nameof(TripRecording.EndedAt),
        nameof(TripRecording.ClientUpdatedAt),
    ];

    /// <summary>The fields of a scan an undo puts back.</summary>
    private static readonly string[] PointFields =
    [
        nameof(TripRecordingPoint.PlaceFeatureId), nameof(TripRecordingPoint.ScannedAt),
        nameof(TripRecordingPoint.Notes), nameof(TripRecordingPoint.CreatedByDeviceUserId),
    ];

    /// <summary>
    /// A recording or scan the upload changed or deleted, put back out of the history rows the upload
    /// wrote: a deleted one comes back, and each changed field returns to what it held before while
    /// it still holds what the upload wrote. A field changed since is left and named, as a feature's
    /// is; <c>force</c> puts it back anyway.
    /// </summary>
    /// <remarks>
    /// The rights asked are the ones the upload needed: the recording's change right for its fields
    /// and its scans, its delete right to bring it back. A cave or place the recording or scan named
    /// before the upload goes back only for a caller who may read it, so an undo is never the way to
    /// pin a recording on something the caller cannot see. A put-back row has its write time moved,
    /// so the trips stream hands it to the phones again. A recording already made into a report is
    /// left with its report; the report was flagged when the change arrived, and the undo of that
    /// change takes the report back only when this upload's own conversion made it.
    /// </remarks>
    private async Task<SyncUndoItemResult> UndoRecordingChangeAsync(
        ImportBatchItem item, Guid batchId, List<AuditEntry> written, AccessContext ctx, bool force, CancellationToken ct)
    {
        var recordingLine = item.TripRecordingId is not null;
        var rowId = (item.TripRecordingId ?? item.TripRecordingPointId)!.Value;
        var key = rowId.ToString();
        var entityType = recordingLine ? nameof(TripRecording) : nameof(TripRecordingPoint);
        var edits = written.Where(a => a.EntityType == entityType && a.EntityId == key && a.Action == AuditActions.Updated).ToList();

        SyncUndoItemResult Line(
            string change,
            string outcome,
            string? reason,
            ReportOutcome? report = null,
            IReadOnlyList<string>? applied = null,
            IReadOnlyList<HistoryRestoreSkip>? skipped = null) =>
            new(item.Id, null, change, outcome, reason, applied ?? [], skipped ?? [], null,
                TripRecordingId: item.TripRecordingId, TripRecordingPointId: item.TripRecordingPointId,
                TripLogId: report?.TripLogId, TripOutcome: report?.Outcome, TripReason: report?.Reason);

        if (edits.Count == 0)
        {
            return Line(SyncUndoChanges.Updated, SyncUndoOutcomes.NotUndone, SyncUndoReasons.NoHistory);
        }

        // Per field, the value before the upload's first edit of it and the value its last edit left.
        var span = new Dictionary<string, (JsonNode? Old, JsonNode? New)>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            foreach (var (field, pair) in HistoryRestoreService.ChangesOf(edit))
            {
                var old = (pair as JsonObject)?["old"];
                var @new = (pair as JsonObject)?["new"];
                span[field] = span.TryGetValue(field, out var seen) ? (seen.Old, @new) : (old, @new);
            }
        }

        var deletedByUpload = span.TryGetValue(nameof(TripRecording.DeletedAt), out var deletion)
            && deletion.Old is null && deletion.New is not null;
        var change = deletedByUpload ? SyncUndoChanges.Deleted : SyncUndoChanges.Updated;

        TripRecording? recording;
        TripRecordingPoint? point = null;
        if (recordingLine)
        {
            recording = await db.TripRecordings.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == rowId, ct);
        }
        else
        {
            point = await db.TripRecordingPoints.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == rowId, ct);
            recording = point is null
                ? null
                : await db.TripRecordings.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == point.RecordingId, ct);
        }

        if (recording is null || (!recordingLine && point is null))
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
        }

        var needed = recordingLine && deletedByUpload ? AccessAction.Delete : AccessAction.Write;
        if (!(await access.DecideAsync(ctx, needed, recording, ct)).Allowed)
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Forbidden);
        }

        DateTimeOffset? currentDeletedAt = recordingLine ? recording.DeletedAt : point!.DeletedAt;
        if (!recordingLine && recording.DeletedAt is not null)
        {
            // A scan comes back only inside a recording that is there.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.ContainerDeleted);
        }

        if (deletedByUpload)
        {
            if (currentDeletedAt is null)
            {
                // Brought back by somebody since.
                return Line(change, SyncUndoOutcomes.Undone, null);
            }

            if (!force && !SameMoment(deletion.New, currentDeletedAt))
            {
                // Brought back and deleted again since: the newer deletion is somebody's decision.
                return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
            }

            if (recordingLine)
            {
                recording.DeletedAt = null;
            }
            else
            {
                point!.DeletedAt = null;
            }

            return Line(change, SyncUndoOutcomes.Undone, null);
        }

        if (currentDeletedAt is not null)
        {
            // Changed by the upload and deleted since: the deletion is the newer state.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
        }

        var fields = recordingLine ? RecordingFields : PointFields;
        var applied = new List<string>();
        var skipped = new List<HistoryRestoreSkip>();
        foreach (var field in fields)
        {
            if (!span.TryGetValue(field, out var values) || JsonNode.DeepEquals(values.Old, values.New))
            {
                continue;
            }

            var (type, current) = recordingLine ? FieldOf(recording, field) : FieldOf(point!, field);
            if (!force && !SameValue(values.New, current, type))
            {
                skipped.Add(new HistoryRestoreSkip(field, HistoryRestoreReasons.Superseded));
                continue;
            }

            var old = ValueOf(values.Old, type);
            if (old is Guid named && field is nameof(TripRecording.CaveFeatureId) or nameof(TripRecordingPoint.PlaceFeatureId))
            {
                var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == named, ct);
                if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
                {
                    skipped.Add(new HistoryRestoreSkip(field, HistoryRestoreReasons.Redacted));
                    continue;
                }
            }

            if (recordingLine)
            {
                SetField(recording, field, old);
            }
            else
            {
                SetField(point!, field, old);
            }

            applied.Add(field);
        }

        var report = applied.Count > 0 && recordingLine
            ? await TakeBackReportAsync(recording, batchId, creatingLine: false, ctx, ct)
            : null;
        if (applied.Count == 0 && skipped.Count == 0)
        {
            // Sent back unchanged: the upload changed nothing there was to take back.
            return Line(change, SyncUndoOutcomes.Undone, null, report);
        }

        return skipped.Count == 0
            ? Line(change, SyncUndoOutcomes.Undone, null, report, applied)
            : applied.Count > 0
                ? Line(change, SyncUndoOutcomes.Partly, null, report, applied, skipped)
                : Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.FieldsLeft, null, [], skipped);
    }

    private static (Type Type, object? Value) FieldOf(TripRecording r, string field) => field switch
    {
        nameof(TripRecording.CaveFeatureId) => (typeof(Guid), r.CaveFeatureId),
        nameof(TripRecording.Title) => (typeof(string), r.Title),
        nameof(TripRecording.Description) => (typeof(string), r.Description),
        nameof(TripRecording.Log) => (typeof(string), r.Log),
        nameof(TripRecording.StartedAt) => (typeof(DateTimeOffset), r.StartedAt),
        nameof(TripRecording.EndedAt) => (typeof(DateTimeOffset?), r.EndedAt),
        nameof(TripRecording.ClientUpdatedAt) => (typeof(DateTimeOffset?), r.ClientUpdatedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static (Type Type, object? Value) FieldOf(TripRecordingPoint p, string field) => field switch
    {
        nameof(TripRecordingPoint.PlaceFeatureId) => (typeof(Guid?), p.PlaceFeatureId),
        nameof(TripRecordingPoint.ScannedAt) => (typeof(DateTimeOffset), p.ScannedAt),
        nameof(TripRecordingPoint.Notes) => (typeof(string), p.Notes),
        nameof(TripRecordingPoint.CreatedByDeviceUserId) => (typeof(Guid?), p.CreatedByDeviceUserId),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    /// <summary>
    /// A field set back to its value before the upload. A required field whose old value cannot be
    /// read is left as it is rather than emptied.
    /// </summary>
    private static void SetField(TripRecording r, string field, object? value)
    {
        switch (field)
        {
            case nameof(TripRecording.CaveFeatureId) when value is Guid cave:
                r.CaveFeatureId = cave;
                break;
            case nameof(TripRecording.Title) when value is string title:
                r.Title = title;
                break;
            case nameof(TripRecording.Description):
                r.Description = value as string;
                break;
            case nameof(TripRecording.Log):
                r.Log = value as string;
                break;
            case nameof(TripRecording.StartedAt) when value is DateTimeOffset started:
                r.StartedAt = started;
                break;
            case nameof(TripRecording.EndedAt):
                r.EndedAt = value as DateTimeOffset?;
                break;
            case nameof(TripRecording.ClientUpdatedAt):
                r.ClientUpdatedAt = value as DateTimeOffset?;
                break;
        }
    }

    private static void SetField(TripRecordingPoint p, string field, object? value)
    {
        switch (field)
        {
            case nameof(TripRecordingPoint.PlaceFeatureId):
                p.PlaceFeatureId = value as Guid?;
                break;
            case nameof(TripRecordingPoint.ScannedAt) when value is DateTimeOffset scanned:
                p.ScannedAt = scanned;
                break;
            case nameof(TripRecordingPoint.Notes):
                p.Notes = value as string;
                break;
            case nameof(TripRecordingPoint.CreatedByDeviceUserId):
                p.CreatedByDeviceUserId = value as Guid?;
                break;
        }
    }

    /// <summary>A history value read as the property's type; null for a null or unreadable one.</summary>
    private static object? ValueOf(JsonNode? node, Type type)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return value.TryGetValue<string>(out var text) ? text : null;
        }

        if (target == typeof(Guid))
        {
            return value.TryGetValue<Guid>(out var id) ? id : null;
        }

        if (target == typeof(DateTimeOffset))
        {
            return value.TryGetValue<DateTimeOffset>(out var at) ? at : null;
        }

        return null;
    }

    /// <summary>
    /// Whether the row still holds what the upload wrote. Moments are compared to the microsecond the
    /// database keeps, since the history recorded the value as the upload sent it.
    /// </summary>
    private static bool SameValue(JsonNode? recorded, object? current, Type type)
    {
        var written = ValueOf(recorded, type);
        return written switch
        {
            null => current is null,
            DateTimeOffset at => current is DateTimeOffset now && Microseconds(at) == Microseconds(now),
            _ => Equals(written, current),
        };
    }

    private static bool SameMoment(JsonNode? recorded, DateTimeOffset? current) =>
        SameValue(recorded, current, typeof(DateTimeOffset?));

    private static long Microseconds(DateTimeOffset at) => at.UtcTicks / 10;

    private static SyncUndoItemResult Done(ImportBatchItem item, Guid rowId, string change) =>
        new(item.Id, rowId, change, SyncUndoOutcomes.Undone, null, [], []);

    private static SyncUndoItemResult NotUndone(ImportBatchItem item, Guid? rowId, string? change, string reason) =>
        new(item.Id, rowId, change, SyncUndoOutcomes.NotUndone, reason, [], []);
}
