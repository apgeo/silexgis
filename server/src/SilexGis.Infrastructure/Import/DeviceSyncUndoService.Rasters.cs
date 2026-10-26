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

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The inverses of the raster maps and pins a phone upload wrote. Read out of the history the
/// upload wrote, by the same rule as a recording's: a value goes back only while it still holds what
/// the upload left, unless the undo is forced.
/// </summary>
public sealed partial class DeviceSyncUndoService
{
    /// <summary>The fields of a map an undo puts back; the rest is bookkeeping the write keeps.</summary>
    private static readonly string[] RasterMapFields =
    [
        nameof(RasterMap.CaveAreaFeatureId), nameof(RasterMap.Title), nameof(RasterMap.MapType),
        nameof(RasterMap.SortOrder), nameof(RasterMap.ClientUpdatedAt),
    ];

    /// <summary>The fields of a pin an undo puts back.</summary>
    private static readonly string[] RasterPinFields =
    [
        nameof(RasterPin.PlaceFeatureId), nameof(RasterPin.X), nameof(RasterPin.Y),
    ];

    /// <summary>
    /// Takes back what an upload did to one raster map or one pin. A map it created is removed
    /// together with its picture's document and the link that listed the picture among the cave's
    /// maps; a pin it created is removed. A map or pin it removed comes back, a map's picture with
    /// it; a field it changed goes back to what it held before.
    /// </summary>
    private async Task<SyncUndoItemResult> UndoRasterLineAsync(
        ImportBatchItem item, List<AuditEntry> written, AccessContext ctx, bool force, CancellationToken ct)
    {
        var mapLine = item.RasterMapId is not null;
        SyncUndoItemResult Line(
            string change,
            string outcome,
            string? reason,
            IReadOnlyList<string>? applied = null,
            IReadOnlyList<HistoryRestoreSkip>? skipped = null) =>
            new(item.Id, null, change, outcome, reason, applied ?? [], skipped ?? [], null,
                DocumentId: mapLine ? item.DocumentId : null,
                RasterMapId: item.RasterMapId, RasterPinId: item.RasterPinId);

        if (item.UndoneAt is not null)
        {
            return Line(
                item.Action == ImportDecisionAction.Create ? SyncUndoChanges.Created : SyncUndoChanges.Updated,
                SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone);
        }

        if (item.Action != ImportDecisionAction.Create)
        {
            return await UndoRasterChangeAsync(item, written, ctx, force, ct);
        }

        var now = DateTimeOffset.UtcNow;
        if (!mapLine)
        {
            var pin = await db.RasterPins.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == item.RasterPinId, ct);
            if (pin is null)
            {
                return Line(SyncUndoChanges.Created, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
            }

            pin.DeletedAt ??= now;
            return Line(SyncUndoChanges.Created, SyncUndoOutcomes.Undone, null);
        }

        var map = await db.RasterMaps.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == item.RasterMapId, ct);
        if (map is null)
        {
            return Line(SyncUndoChanges.Created, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
        }

        map.DeletedAt ??= now;

        // The picture was made for this map: removed the way any document is removed — stamped, so
        // it can be brought back from the bin — and the link that listed it among the cave's maps
        // taken down, which the removal log tells the phones of.
        var picture = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == map.DocumentId, ct);
        if (picture is { DeletedAt: null })
        {
            picture.DeletedAt = now;
            picture.DeletedByUserId = ctx.UserId;
        }

        var linkIds = ImportBatchAttachments.Read(item.AttachmentIds);
        if (linkIds.Count > 0)
        {
            db.Attachments.RemoveRange(await db.Attachments.Where(a => linkIds.Contains(a.Id)).ToListAsync(ct));
        }

        return Line(SyncUndoChanges.Created, SyncUndoOutcomes.Undone, null);
    }

    /// <summary>
    /// A map or pin the upload changed or deleted, put back out of the history rows the upload wrote.
    /// </summary>
    /// <remarks>
    /// The right asked is the one the upload needed: the right to change the map's cave. An area or
    /// place the map or pin named before the upload goes back only for a caller who may read it, so
    /// an undo is never the way to pin something the caller cannot see. A pin comes back only inside
    /// a map that is there and only while no other live pin marks its place. A put-back row has its
    /// write time moved, so the rasters stream hands it to the phones again.
    /// </remarks>
    private async Task<SyncUndoItemResult> UndoRasterChangeAsync(
        ImportBatchItem item, List<AuditEntry> written, AccessContext ctx, bool force, CancellationToken ct)
    {
        var mapLine = item.RasterMapId is not null;
        var rowId = (item.RasterMapId ?? item.RasterPinId)!.Value;
        var key = rowId.ToString();
        var entityType = mapLine ? nameof(RasterMap) : nameof(RasterPin);
        var edits = written.Where(a => a.EntityType == entityType && a.EntityId == key && a.Action == AuditActions.Updated).ToList();

        SyncUndoItemResult Line(
            string change,
            string outcome,
            string? reason,
            IReadOnlyList<string>? applied = null,
            IReadOnlyList<HistoryRestoreSkip>? skipped = null) =>
            new(item.Id, null, change, outcome, reason, applied ?? [], skipped ?? [], null,
                DocumentId: mapLine ? item.DocumentId : null,
                RasterMapId: item.RasterMapId, RasterPinId: item.RasterPinId);

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

        var deletedByUpload = span.TryGetValue(nameof(RasterMap.DeletedAt), out var deletion)
            && deletion.Old is null && deletion.New is not null;
        var change = deletedByUpload ? SyncUndoChanges.Deleted : SyncUndoChanges.Updated;

        RasterMap? map;
        RasterPin? pin = null;
        if (mapLine)
        {
            map = await db.RasterMaps.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == rowId, ct);
        }
        else
        {
            pin = await db.RasterPins.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == rowId, ct);
            map = pin is null
                ? null
                : await db.RasterMaps.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == pin.RasterMapId, ct);
        }

        if (map is null || (!mapLine && pin is null))
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
        }

        var cave = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == map.CaveFeatureId, ct);
        if (cave is null)
        {
            // The cave has gone, and a map comes back only inside a cave that is there.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.ContainerDeleted);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Forbidden);
        }

        DateTimeOffset? currentDeletedAt = mapLine ? map.DeletedAt : pin!.DeletedAt;
        if (!mapLine && map.DeletedAt is not null)
        {
            // A pin comes back only inside a map that is there.
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

            if (mapLine)
            {
                map.DeletedAt = null;
                var picture = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == map.DocumentId, ct);
                // Only a picture this very deletion removed, and only for a caller who may bring a
                // document back: one trashed by somebody before the map went stays trashed.
                if (picture is { DeletedAt: not null }
                    && SameMoment(picture.DeletedAt, currentDeletedAt)
                    && (await access.DecideAsync(ctx, AccessAction.Delete, picture, ct)).Allowed)
                {
                    picture.DeletedAt = null;
                    picture.DeletedByUserId = null;
                }
            }
            else
            {
                // The place may have been pinned afresh since; two live pins of one place on a map
                // is a state the database refuses, so the pin stays gone.
                var taken = await db.RasterPins.AsNoTracking()
                    .AnyAsync(p => p.RasterMapId == pin!.RasterMapId && p.PlaceFeatureId == pin.PlaceFeatureId
                        && p.DeletedAt == null && p.Id != pin.Id, ct);
                if (taken)
                {
                    return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
                }

                pin!.DeletedAt = null;
            }

            return Line(change, SyncUndoOutcomes.Undone, null);
        }

        if (currentDeletedAt is not null)
        {
            // Changed by the upload and deleted since: the deletion is the newer state.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
        }

        var fields = mapLine ? RasterMapFields : RasterPinFields;
        var applied = new List<string>();
        var skipped = new List<HistoryRestoreSkip>();
        foreach (var field in fields)
        {
            if (!span.TryGetValue(field, out var values) || JsonNode.DeepEquals(values.Old, values.New))
            {
                continue;
            }

            var (type, current) = mapLine ? FieldOf(map, field) : FieldOf(pin!, field);
            if (!force && !SameValue(values.New, current, type))
            {
                skipped.Add(new HistoryRestoreSkip(field, HistoryRestoreReasons.Superseded));
                continue;
            }

            var old = ValueOf(values.Old, type);
            if (old is Guid named && field is nameof(RasterMap.CaveAreaFeatureId) or nameof(RasterPin.PlaceFeatureId))
            {
                var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == named, ct);
                if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
                {
                    skipped.Add(new HistoryRestoreSkip(field, HistoryRestoreReasons.Redacted));
                    continue;
                }

                if (!mapLine
                    && await db.RasterPins.AsNoTracking().AnyAsync(p => p.RasterMapId == pin!.RasterMapId
                        && p.PlaceFeatureId == named && p.DeletedAt == null && p.Id != pin.Id, ct))
                {
                    skipped.Add(new HistoryRestoreSkip(field, HistoryRestoreReasons.Superseded));
                    continue;
                }
            }

            if (mapLine)
            {
                SetField(map, field, old);
            }
            else
            {
                SetField(pin!, field, old);
            }

            applied.Add(field);
        }

        // A picture the upload replaced steps back to the revision before it, which stays in the
        // document's version list; newer bytes landed since are somebody's revision and stay.
        if (mapLine && item.DocumentVersionId is { } madeId)
        {
            var made = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == madeId, ct);
            var before = made is null
                ? null
                : await db.DocumentVersions.AsNoTracking()
                    .Where(v => v.DocumentId == map.DocumentId && v.VersionNumber < made.VersionNumber)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            if (made is null || before is null)
            {
                skipped.Add(new HistoryRestoreSkip(FileField, SyncUndoReasons.Purged));
            }
            else if (!made.IsCurrent)
            {
                skipped.Add(new HistoryRestoreSkip(FileField, SyncUndoReasons.Superseded));
            }
            else
            {
                await documents.RestoreVersionAsync(made.Id, before.Id, ct);
                applied.Add(FileField);
            }
        }

        if (applied.Count == 0 && skipped.Count == 0)
        {
            // Sent back unchanged: the upload changed nothing there was to take back.
            return Line(change, SyncUndoOutcomes.Undone, null);
        }

        return skipped.Count == 0
            ? Line(change, SyncUndoOutcomes.Undone, null, applied)
            : applied.Count > 0
                ? Line(change, SyncUndoOutcomes.Partly, null, applied, skipped)
                : Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.FieldsLeft, [], skipped);
    }

    private static (Type Type, object? Value) FieldOf(RasterMap m, string field) => field switch
    {
        nameof(RasterMap.CaveAreaFeatureId) => (typeof(Guid?), m.CaveAreaFeatureId),
        nameof(RasterMap.Title) => (typeof(string), m.Title),
        nameof(RasterMap.MapType) => (typeof(RasterMapType), m.MapType),
        nameof(RasterMap.SortOrder) => (typeof(int), m.SortOrder),
        nameof(RasterMap.ClientUpdatedAt) => (typeof(DateTimeOffset?), m.ClientUpdatedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static (Type Type, object? Value) FieldOf(RasterPin p, string field) => field switch
    {
        nameof(RasterPin.PlaceFeatureId) => (typeof(Guid), p.PlaceFeatureId),
        nameof(RasterPin.X) => (typeof(int), p.X),
        nameof(RasterPin.Y) => (typeof(int), p.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    /// <summary>
    /// A field set back to its value before the upload. A required field whose old value cannot be
    /// read is left as it is rather than emptied.
    /// </summary>
    private static void SetField(RasterMap m, string field, object? value)
    {
        switch (field)
        {
            case nameof(RasterMap.CaveAreaFeatureId):
                m.CaveAreaFeatureId = value as Guid?;
                break;
            case nameof(RasterMap.Title) when value is string title:
                m.Title = title;
                break;
            case nameof(RasterMap.MapType) when value is RasterMapType kind:
                m.MapType = kind;
                break;
            case nameof(RasterMap.SortOrder) when value is int order:
                m.SortOrder = order;
                break;
            case nameof(RasterMap.ClientUpdatedAt):
                m.ClientUpdatedAt = value as DateTimeOffset?;
                break;
        }
    }

    private static void SetField(RasterPin p, string field, object? value)
    {
        switch (field)
        {
            case nameof(RasterPin.PlaceFeatureId) when value is Guid place:
                p.PlaceFeatureId = place;
                break;
            case nameof(RasterPin.X) when value is int x:
                p.X = x;
                break;
            case nameof(RasterPin.Y) when value is int y:
                p.Y = y;
                break;
        }
    }
}
