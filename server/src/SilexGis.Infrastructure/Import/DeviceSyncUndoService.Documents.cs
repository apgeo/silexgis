// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The inverses of the photos and documents a phone upload sent, and of the links it hung or took
/// down. Read out of the history the upload wrote, by the same rule as a feature row's: a value
/// goes back only while it still holds what the upload left, unless the undo is forced.
/// </summary>
public sealed partial class DeviceSyncUndoService
{
    private const string DocumentType = nameof(Document);
    private const string PhotoDetailsType = nameof(PhotoDetails);
    private const string AttachmentType = nameof(Attachment);
    private const string FileField = "file";

    /// <summary>Whether a history row is one of a document, a photo's details or a link.</summary>
    private static bool IsDocumentWorld(string? entityType) =>
        entityType is DocumentType or PhotoDetailsType or AttachmentType;

    /// <summary>
    /// A line that hung documents on objects or took them down, rather than one that wrote a
    /// document: it names the links, and no feature.
    /// </summary>
    private static bool IsLinkLine(ImportBatchItem item) =>
        item.FeatureId is null
        && item.AttachedToFeatureId is null
        && ImportBatchAttachments.Read(item.AttachmentIds).Count > 0;

    /// <summary>
    /// Takes back what an upload did to one document. A document it created is removed; one it
    /// removed comes back; new bytes it gave a document step back to the revision before them,
    /// which leaves the stepped-back revision in the version list; a title or a caption it changed
    /// goes back to what it was.
    /// </summary>
    private async Task<SyncUndoItemResult> UndoDocumentLineAsync(
        ImportBatchItem item,
        Guid documentId,
        Dictionary<string, List<AuditEntry>> byRow,
        AccessContext ctx,
        bool force,
        CancellationToken ct)
    {
        var entries = byRow.GetValueOrDefault(documentId.ToString()) ?? [];
        var ofDocument = entries.Where(a => a.EntityType == DocumentType).ToList();
        var ofPhoto = entries.Where(a => a.EntityType == PhotoDetailsType).ToList();
        var created = item.Action == ImportDecisionAction.Create;
        var removed = !created && ofDocument.Exists(a => a.Action == AuditActions.Updated && SetsDeletedAt(a));
        var change = created ? SyncUndoChanges.Created : removed ? SyncUndoChanges.Deleted : SyncUndoChanges.Updated;

        if (item.UndoneAt is not null)
        {
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone, [], []);
        }

        var document = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged, [], []);
        }

        if (created)
        {
            // Removed the way any document is removed — stamped, so it can be brought back from
            // the bin, and so its links leave the phones' catalogue as tombstones of a removed
            // document. Already removed is already undone.
            if (document.DeletedAt is null)
            {
                document.DeletedAt = DateTimeOffset.UtcNow;
                document.DeletedByUserId = ctx.UserId;
            }

            return DocumentResult(item, documentId, change, SyncUndoOutcomes.Undone, null, [], []);
        }

        if (removed)
        {
            if (document.DeletedAt is null)
            {
                return DocumentResult(item, documentId, change, SyncUndoOutcomes.Undone, null, [], []);
            }

            // The right that removed it is the right that brings it back.
            if (!(await access.DecideAsync(ctx, AccessAction.Delete, document, ct)).Allowed)
            {
                return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Forbidden, [], []);
            }

            document.DeletedAt = null;
            document.DeletedByUserId = null;
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.Undone, null, [], []);
        }

        if (document.DeletedAt is not null)
        {
            // Changed by the upload and removed by somebody since: the removal is the newer state.
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded, [], []);
        }

        var current = await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, current?.File, ct))
        {
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Forbidden, [], []);
        }

        var applied = new List<string>();
        var skipped = new List<HistoryRestoreSkip>();

        if (item.DocumentVersionId is { } madeId)
        {
            var made = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == madeId, ct);
            var before = made is null
                ? null
                : await db.DocumentVersions.AsNoTracking()
                    .Where(v => v.DocumentId == documentId && v.VersionNumber < made.VersionNumber)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            if (made is null || before is null)
            {
                skipped.Add(new HistoryRestoreSkip(FileField, SyncUndoReasons.Purged));
            }
            else if (!made.IsCurrent)
            {
                // Newer bytes have landed since — somebody's later revision, which stepping back
                // past would throw away. Not even a forced undo does that: the revision the upload
                // made is still in the list, and the document's owner chooses between them.
                skipped.Add(new HistoryRestoreSkip(FileField, SyncUndoReasons.Superseded));
            }
            else
            {
                await documents.RestoreVersionAsync(made.Id, before.Id, ct);
                applied.Add(FileField);
            }
        }

        if (Span(ofDocument, nameof(Document.Title)) is { } title)
        {
            if (title.Old is not null && (string.Equals(document.Title, title.New, StringComparison.Ordinal) || force))
            {
                document.Title = title.Old;
                applied.Add(nameof(Document.Title));
            }
            else if (title.Old is not null)
            {
                skipped.Add(new HistoryRestoreSkip(nameof(Document.Title), SyncUndoReasons.Superseded));
            }
        }

        if (Span(ofPhoto, nameof(PhotoDetails.Caption)) is { } caption)
        {
            var details = await db.PhotoDetails.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
            if (details is not null && (string.Equals(details.Caption, caption.New, StringComparison.Ordinal) || force))
            {
                details.Caption = caption.Old;
                applied.Add(nameof(PhotoDetails.Caption));
            }
            else
            {
                skipped.Add(new HistoryRestoreSkip(nameof(PhotoDetails.Caption), SyncUndoReasons.Superseded));
            }
        }

        if (applied.Count > 0)
        {
            // The document's revision moves, so the phones' catalogue hands it out again as it
            // now stands — the caption and the file live beside the document row, not in it.
            db.Entry(document).Property(d => d.UpdatedAt).IsModified = true;
        }

        if (applied.Count == 0 && skipped.Count > 0)
        {
            return DocumentResult(item, documentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded, applied, skipped);
        }

        return DocumentResult(
            item, documentId, change, skipped.Count > 0 ? SyncUndoOutcomes.Partly : SyncUndoOutcomes.Undone, null, applied, skipped);
    }

    /// <summary>
    /// Takes back the links one line hung or took down. A link the upload hung is taken down; one
    /// it took down is hung again from what the history kept of it, on the document's current file;
    /// one whose role, caption or place it changed gets them back. Each needs Write on the object
    /// the link hangs on, as hanging or taking it down did.
    /// </summary>
    private async Task<SyncUndoItemResult> UndoLinkLineAsync(
        ImportBatchItem item,
        Dictionary<string, List<AuditEntry>> byRow,
        AccessContext ctx,
        bool force,
        CancellationToken ct)
    {
        var ids = ImportBatchAttachments.Read(item.AttachmentIds);
        var histories = ids.ToDictionary(
            id => id,
            id => (byRow.GetValueOrDefault(id.ToString()) ?? []).Where(a => a.EntityType == AttachmentType).ToList());
        var change = histories.Values.Any(h => h.Exists(a => a.Action == AuditActions.Created))
            ? SyncUndoChanges.Created
            : histories.Values.Any(h => h.Exists(a => a.Action == AuditActions.Deleted))
                ? SyncUndoChanges.Deleted
                : SyncUndoChanges.Updated;

        if (item.UndoneAt is not null)
        {
            return DocumentResult(item, item.DocumentId, change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone, [], []);
        }

        var applied = new List<string>();
        var skipped = new List<HistoryRestoreSkip>();
        string? refusal = null;

        foreach (var (id, history) in histories)
        {
            var link = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (history.Exists(a => a.Action == AuditActions.Created))
            {
                // Taken down already is undone already.
                if (link is null)
                {
                    continue;
                }

                if (!await MayChangeLinksOnAsync(link.EntityType, link.FeatureId ?? link.EntityId!.Value, ctx, ct))
                {
                    refusal = SyncUndoReasons.Forbidden;
                    continue;
                }

                // Through the tracker, so the history records it and the removal log hands the
                // phones the tombstone.
                db.Attachments.Remove(link);
                applied.Add(id.ToString());
            }
            else if (history.LastOrDefault(a => a.Action == AuditActions.Deleted) is { } removal)
            {
                var outcome = await HangAgainAsync(removal, ctx, ct);
                if (outcome is null)
                {
                    applied.Add(id.ToString());
                }
                else
                {
                    refusal = outcome;
                }
            }
            else if (link is not null)
            {
                if (!await MayChangeLinksOnAsync(link.EntityType, link.FeatureId ?? link.EntityId!.Value, ctx, ct))
                {
                    refusal = SyncUndoReasons.Forbidden;
                    continue;
                }

                RestoreLinkFields(link, history, force, applied, skipped);
            }
            else
            {
                refusal = SyncUndoReasons.Purged;
            }
        }

        if (applied.Count == 0 && (refusal is not null || skipped.Count > 0))
        {
            return DocumentResult(
                item, item.DocumentId, change, SyncUndoOutcomes.NotUndone, refusal ?? SyncUndoReasons.Superseded, applied, skipped);
        }

        return DocumentResult(
            item,
            item.DocumentId,
            change,
            skipped.Count > 0 || refusal is not null ? SyncUndoOutcomes.Partly : SyncUndoOutcomes.Undone,
            null,
            applied,
            skipped);
    }

    /// <summary>
    /// Hangs a link the upload took down back up, from every member the history kept of it when it
    /// went. Null when it is up again; otherwise why it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hung on the document's current file rather than on the file it hung on before: had the link
    /// stayed up, it would have moved to each new revision as it landed. It is hung under a new
    /// identifier, because the phones have already been handed a tombstone for the old one, and a
    /// tombstone that a link of the same identifier later contradicts is a question a phone should
    /// not have to settle.
    /// </para>
    /// <para>
    /// The primary mark comes back only while the object has no other primary file: somebody who
    /// chose another one since made a later decision.
    /// </para>
    /// </remarks>
    private async Task<string?> HangAgainAsync(AuditEntry removal, AccessContext ctx, CancellationToken ct)
    {
        if (removal.Changes is null || JsonNode.Parse(removal.Changes) is not JsonObject members)
        {
            return SyncUndoReasons.NoHistory;
        }

        var featureId = GuidOf(members, nameof(Attachment.FeatureId));
        var entityId = GuidOf(members, nameof(Attachment.EntityId));
        AttachedEntityType? entityType = Enum.TryParse<AttachedEntityType>(
            Old(members, nameof(Attachment.EntityType)), ignoreCase: true, out var parsedType) ? parsedType : null;
        if (GuidOf(members, nameof(Attachment.FileId)) is not { } fileId || (featureId ?? entityId) is not { } targetId)
        {
            return SyncUndoReasons.NoHistory;
        }

        var documentId = await (from file in db.StoredFiles.AsNoTracking()
                                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                                where file.Id == fileId
                                select (Guid?)version.DocumentId)
            .FirstOrDefaultAsync(ct);
        var current = documentId is { } id
            && await db.Documents.AsNoTracking().AnyAsync(d => d.Id == id, ct)
                ? await DocumentQueries.CurrentFileAsync(db, id, ct)
                : null;
        if (current is null)
        {
            // The document went, or its file did.
            return SyncUndoReasons.Purged;
        }

        // A link hands the document to everyone who can read the object, so hanging it back up
        // needs the document to be one the person undoing may read, as hanging it through the
        // attachment routes does — whoever took it down, read rights may have changed since.
        var document = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId!.Value, ct);
        if (!await DocumentAccessRules.CanReadAsync(db, access, ctx, document, current.File, ct))
        {
            return SyncUndoReasons.Forbidden;
        }

        if (featureId is not null && !await db.Features.AsNoTracking().AnyAsync(f => f.Id == featureId, ct))
        {
            return SyncUndoReasons.ContainerDeleted;
        }

        if (!await MayChangeLinksOnAsync(featureId is null ? entityType : null, targetId, ctx, ct))
        {
            return SyncUndoReasons.Forbidden;
        }

        var primary = string.Equals(Old(members, nameof(Attachment.IsPrimary)), "true", StringComparison.OrdinalIgnoreCase)
            && !await db.Attachments.AsNoTracking().AnyAsync(a => a.IsPrimary
                && (featureId != null
                    ? a.FeatureId == featureId
                    : a.EntityType == entityType && a.EntityId == entityId), ct);

        db.Attachments.Add(new Attachment
        {
            FileId = current.File.Id,
            FeatureId = featureId,
            EntityType = featureId is null ? entityType : null,
            EntityId = featureId is null ? entityId : null,
            Role = Enum.TryParse<AttachmentRole>(Old(members, nameof(Attachment.Role)), ignoreCase: true, out var role)
                ? role
                : AttachmentRole.Other,
            Caption = Old(members, nameof(Attachment.Caption)),
            SortOrder = int.TryParse(Old(members, nameof(Attachment.SortOrder)), out var order) ? order : 0,
            IsPrimary = primary,
            InDeviceDefaultSet = string.Equals(
                Old(members, nameof(Attachment.InDeviceDefaultSet)), "true", StringComparison.OrdinalIgnoreCase),
            AddedBy = GuidOf(members, nameof(Attachment.AddedBy)),
        });
        return null;
    }

    /// <summary>Puts back a link's role, caption and place where they still hold what the upload wrote.</summary>
    private static void RestoreLinkFields(
        Attachment link, List<AuditEntry> history, bool force, List<string> applied, List<HistoryRestoreSkip> skipped)
    {
        Restore(nameof(Attachment.Role), JsonCamel(link.Role.ToString()), value =>
            link.Role = Enum.TryParse<AttachmentRole>(value, ignoreCase: true, out var role) ? role : link.Role);
        Restore(nameof(Attachment.Caption), link.Caption, value => link.Caption = value);
        Restore(nameof(Attachment.SortOrder), link.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value => link.SortOrder = int.TryParse(value, out var order) ? order : link.SortOrder);

        void Restore(string field, string? now, Action<string?> set)
        {
            if (Span(history, field) is not { } span)
            {
                return;
            }

            if (string.Equals(now, span.New, StringComparison.Ordinal) || force)
            {
                set(span.Old);
                applied.Add(field);
            }
            else
            {
                skipped.Add(new HistoryRestoreSkip(field, SyncUndoReasons.Superseded));
            }
        }
    }

    /// <summary>Whether the caller may hang files on the object, or take them down: Write on it.</summary>
    private Task<bool> MayChangeLinksOnAsync(AttachedEntityType? entityType, Guid targetId, AccessContext ctx, CancellationToken ct) =>
        FileAccessRules.CanWriteTargetAsync(db, access, ctx, new AttachmentTarget(entityType, targetId), ct);

    /// <summary>
    /// A field's value before the first of these history rows changed it and after the last; null
    /// when none of them did.
    /// </summary>
    private static (string? Old, string? New)? Span(IEnumerable<AuditEntry> history, string field)
    {
        (string? Old, string? New)? span = null;
        foreach (var entry in history.Where(a => a.Action is AuditActions.Created or AuditActions.Updated))
        {
            if (entry.Changes is null
                || JsonNode.Parse(entry.Changes) is not JsonObject changed
                || changed[field] is not JsonObject pair)
            {
                continue;
            }

            var newValue = Text(pair["new"]);
            span = span is { } seen
                ? (seen.Old, newValue)
                : (entry.Action == AuditActions.Created ? null : Text(pair["old"]), newValue);
        }

        return span;
    }

    /// <summary>Whether a history row of a document is the one that removed it.</summary>
    private static bool SetsDeletedAt(AuditEntry entry) =>
        entry.Changes is not null
        && JsonNode.Parse(entry.Changes) is JsonObject changed
        && changed[nameof(Document.DeletedAt)] is JsonObject pair
        && pair["old"] is null
        && pair["new"] is not null;

    private static string? Old(JsonObject members, string field) =>
        members[field] is JsonObject pair ? Text(pair["old"]) : null;

    private static Guid? GuidOf(JsonObject members, string field) =>
        Guid.TryParse(Old(members, field), out var id) ? id : null;

    // A history value as text: strings as they are, numbers and booleans in their JSON spelling.
    private static string? Text(JsonNode? value) => value switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var text) => text,
        _ => value.ToJsonString(),
    };

    // As the history spells an enum value.
    private static string JsonCamel(string name) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name);

    private static SyncUndoItemResult DocumentResult(
        ImportBatchItem item,
        Guid? documentId,
        string change,
        string outcome,
        string? reason,
        IReadOnlyList<string> applied,
        IReadOnlyList<HistoryRestoreSkip> skipped) =>
        new(item.Id, null, change, outcome, reason, applied, skipped, null, documentId);
}
