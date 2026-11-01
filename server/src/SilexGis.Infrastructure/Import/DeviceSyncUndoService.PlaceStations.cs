// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.PlaceStations;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The inverses of the places a phone upload mapped to survey stations. Read out of the history the
/// upload wrote, by the same rule as a pin's: a value goes back only while it still holds what the
/// upload left, unless the undo is forced.
/// </summary>
public sealed partial class DeviceSyncUndoService
{
    private PlaceStationStore PlaceStations => placeStationStore ??= new PlaceStationStore(db);

    private PlaceStationStore? placeStationStore;

    /// <summary>
    /// A mapping the upload created is removed — stamped, so the phones are told. One it removed comes
    /// back while nothing has mapped its place in that survey since; a station it changed goes back.
    /// The right asked for the last two is the one the upload needed: the right to change the place.
    /// </summary>
    private async Task<SyncUndoItemResult> UndoPlaceStationLineAsync(
        ImportBatchItem item, List<AuditEntry> written, AccessContext ctx, bool force, CancellationToken ct)
    {
        SyncUndoItemResult Line(string change, string outcome, string? reason, IReadOnlyList<string>? applied = null) =>
            new(item.Id, null, change, outcome, reason, applied ?? [], [], PlaceStationId: item.PlaceStationId);

        var created = item.Action == ImportDecisionAction.Create;
        if (item.UndoneAt is not null)
        {
            return Line(created ? SyncUndoChanges.Created : SyncUndoChanges.Updated,
                SyncUndoOutcomes.NotUndone, SyncUndoReasons.AlreadyUndone);
        }

        var mapping = await PlaceStations.FindAsync(item.PlaceStationId!.Value, ct);
        if (created)
        {
            if (mapping is null)
            {
                return Line(SyncUndoChanges.Created, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
            }

            PlaceStations.Remove(mapping, DateTimeOffset.UtcNow);
            return Line(SyncUndoChanges.Created, SyncUndoOutcomes.Undone, null);
        }

        // Per field, the value before the upload's first edit of it and the value its last edit left:
        // the removal on the link's own row, the station on its survey member's.
        var key = item.PlaceStationId.Value.ToString();
        var surveyKey = mapping?.Survey.Id.ToString();
        JsonNode? deletedOld = null, deletedNew = null, anchorOld = null, anchorNew = null;
        bool deletion = false, anchor = false;
        foreach (var edit in written.Where(a => a.Action == AuditActions.Updated
                     && ((a.EntityType == nameof(ResLink) && a.EntityId == key)
                         || (a.EntityType == nameof(ResLinkMember) && surveyKey is not null && a.EntityId == surveyKey))))
        {
            foreach (var (field, pair) in HistoryRestoreService.ChangesOf(edit))
            {
                var old = (pair as JsonObject)?["old"];
                var @new = (pair as JsonObject)?["new"];
                if (field == nameof(ResLink.DeletedAt) && edit.EntityType == nameof(ResLink))
                {
                    deletedOld = deletion ? deletedOld : old?.DeepClone();
                    deletedNew = @new?.DeepClone();
                    deletion = true;
                }
                else if (field == nameof(ResLinkMember.Anchor))
                {
                    anchorOld = anchor ? anchorOld : old?.DeepClone();
                    anchorNew = @new?.DeepClone();
                    anchor = true;
                }
            }
        }

        var deletedByUpload = deletion && deletedOld is null && deletedNew is not null;
        var change = deletedByUpload ? SyncUndoChanges.Deleted : SyncUndoChanges.Updated;
        if (!deletion && !anchor)
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.NoHistory);
        }

        if (mapping is null)
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Purged);
        }

        var place = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == mapping.PlaceId, ct);
        if (place is null)
        {
            // A mapping comes back only for a place that is there.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.ContainerDeleted);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, place, ct)).Allowed)
        {
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Forbidden);
        }

        await PlaceStations.LockAsync(mapping.PlaceId, ct);
        if (deletedByUpload)
        {
            if (mapping.Link.DeletedAt is null)
            {
                // Brought back by somebody since.
                return Line(change, SyncUndoOutcomes.Undone, null);
            }

            // Brought back and removed again since, or the place mapped afresh in that survey: the
            // newer decision stands.
            if ((!force && !SameMoment(deletedNew, mapping.Link.DeletedAt))
                || await PlaceStations.LiveMappingOfAsync(
                    mapping.PlaceId,
                    mapping.Survey.EntityType == AttachedEntityType.SurveyModel ? mapping.Survey.EntityId : null,
                    mapping.Survey.EntityType == AttachedEntityType.Document ? mapping.Survey.EntityId : null,
                    mapping.Link.Id,
                    ct))
            {
                return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
            }

            PlaceStations.Restore(mapping);
            return Line(change, SyncUndoOutcomes.Undone, null);
        }

        var before = PlaceStationAnchor.Read(Text(anchorOld));
        if (mapping.Link.DeletedAt is not null
            || before is null
            || (!force && !SameStation(mapping.Station, PlaceStationAnchor.Read(Text(anchorNew)))))
        {
            // Removed since, changed again since, or the earlier station cannot be read back.
            return Line(change, SyncUndoOutcomes.NotUndone, SyncUndoReasons.Superseded);
        }

        PlaceStations.SetStation(mapping, before.Value.Station, before.Value.Splay);
        return Line(change, SyncUndoOutcomes.Undone, null, [PlaceStationAnchor.StationField]);

        // Compared as values: the stored payload is read back as the database normalised it.
        static bool SameStation(PlaceStationAnchor.Parsed? a, PlaceStationAnchor.Parsed? b) =>
            a is { } x && b is { } y && x.Station == y.Station && PlaceStationAnchor.SameSplay(x.Splay, y.Splay);

        static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
