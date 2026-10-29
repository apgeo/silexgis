// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The server's own row, for a decision that leaves a phone showing something the server does not
/// hold: a turned-down or failed edit or removal, or an edit accepted only in part, of any section
/// that carries one.
/// </summary>
/// <remarks>
/// <para>
/// Each row is shaped by its own stream's shaper and gated by its own stream's rule — a feature
/// as the feature stream hands it, a document as the catalogue's document, a link as a catalogue
/// link, a recording with its scans and a map with its pins as their streams hand them, a scan or
/// a pin as one entry of its parent's list. None is shaped or gated here, so a phone can never be
/// told about a row here what its stream would not tell it, nor told it differently.
/// </para>
/// <para>
/// The streams read against what a set hands a reader; this stream is the account's, its
/// decisions reached through any of the account's sets, so the rows are asked of the same answer
/// with the set taken away — readable, and not withheld — exactly as the feature row's echo always
/// was. A row the account may not be given, or that is gone, has no entry, and the phone re-reads
/// that section's stream instead.
/// </para>
/// </remarks>
internal static class SyncReviewCurrent
{
    /// <summary>The sections a phone can send an edit or a removal in, and so receive the server's row for.</summary>
    private static readonly HashSet<string> Echoed =
    [
        SyncUploadSections.Rows,
        SyncUploadSections.Documents,
        SyncUploadSections.DocumentLinks,
        SyncUploadSections.TripRecordings,
        SyncUploadSections.TripPoints,
        SyncUploadSections.RasterMaps,
        SyncUploadSections.RasterPins,
    ];

    /// <summary>
    /// Whether a decided line is answered with the server's row, when the caller may have it: an
    /// edit or removal turned down or not written, or an edit written only in part.
    /// </summary>
    public static bool Wanted(SyncReviewRow row) =>
        Echoed.Contains(row.Section)
        && row.Action != ImportDecisionAction.Create
        && (row.Decision is ImportReviewDecision.Rejected or ImportReviewDecision.Failed || row.Partial);

    /// <summary>The server's row for each line of <paramref name="rows"/> that wants one and may have it, by review id.</summary>
    public static async Task<Dictionary<Guid, object>> OfAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        PhotoPositionDisclosure photos,
        AccessContext ctx,
        SyncSettings sync,
        IReadOnlyList<SyncReviewRow> rows,
        CancellationToken ct)
    {
        var wanted = rows.Where(Wanted).ToList();
        var found = new Dictionary<Guid, object>();
        if (wanted.Count == 0)
        {
            return found;
        }

        List<Guid> IdsOf(string section) =>
            [.. wanted.Where(r => r.Section == section).Select(r => r.RowId).Distinct()];
        void Answer(string section, Func<SyncReviewRow, object?> of)
        {
            foreach (var row in wanted.Where(r => r.Section == section))
            {
                if (of(row) is { } current)
                {
                    found[row.ReviewId] = current;
                }
            }
        }

        var features = (await SyncFeatureShaping.CurrentAsync(db, protection, ctx, IdsOf(SyncUploadSections.Rows), ct))
            .ToDictionary(f => f.Id);
        Answer(SyncUploadSections.Rows, r => features.GetValueOrDefault(r.RowId));

        var links = wanted.Where(r => r.Section == SyncUploadSections.DocumentLinks && r.Link is not null)
            .Select(r => r.Link!).ToList();
        var documentIds = IdsOf(SyncUploadSections.Documents).Concat(links.Select(l => l.Row.DocumentId)).Distinct().ToList();
        var recordingIds = IdsOf(SyncUploadSections.TripRecordings);
        var pointIds = IdsOf(SyncUploadSections.TripPoints);
        var mapIds = IdsOf(SyncUploadSections.RasterMaps);
        var pinIds = IdsOf(SyncUploadSections.RasterPins);
        if (documentIds.Count + recordingIds.Count + pointIds.Count + mapIds.Count + pinIds.Count == 0)
        {
            return found;
        }

        // The points and pins are read for their parents' sake: a scan is answered as an entry of
        // its recording's scans, a pin as an entry of its map's pins.
        var points = pointIds.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking()
                .Where(p => pointIds.Contains(p.Id) && p.DeletedAt == null)
                .Select(p => new { p.Id, p.RecordingId })
                .ToDictionaryAsync(p => p.Id, p => p.RecordingId, ct);
        var pins = pinIds.Count == 0
            ? []
            : await PlacePointQueries.Rows(db).AsNoTracking()
                .Where(p => pinIds.Contains(p.Id) && p.DeletedAt == null)
                .Select(p => new { p.Id, p.MapId })
                .ToDictionaryAsync(p => p.Id, p => p.MapId, ct);
        var recordingsAsked = recordingIds.Concat(points.Values).Distinct().ToList();
        var mapsAsked = mapIds.Concat(pins.Values).Distinct().ToList();

        var handed = await HandedAsync(db, protection, ctx, documentIds, links, recordingsAsked, mapsAsked, ct);

        if (recordingsAsked.Count > 0)
        {
            var carried = await SyncTripEndpoints.Carried(db, ctx, handed)
                .Where(c => recordingsAsked.Contains(c.Recording.Id))
                .Select(c => c.Recording)
                .ToListAsync(ct);
            var shaped = (await SyncTripEndpoints.ShapeAsync(db, ctx, sync, handed, carried, ct)).ToDictionary(r => r.Id);
            Answer(SyncUploadSections.TripRecordings, r => shaped.GetValueOrDefault(r.RowId));
            var scans = shaped.Values.SelectMany(r => r.Points).ToDictionary(p => p.Id);
            Answer(SyncUploadSections.TripPoints, r => scans.GetValueOrDefault(r.RowId));
        }

        if (mapsAsked.Count > 0)
        {
            var carried = await SyncRasterEndpoints.Carried(db, handed)
                .Where(c => mapsAsked.Contains(c.Map.Id))
                .Select(c => c.Map)
                .ToListAsync(ct);
            var shaped = (await SyncRasterEndpoints.ShapeAsync(db, handed, carried, ct)).ToDictionary(m => m.Id);
            Answer(SyncUploadSections.RasterMaps, r => shaped.GetValueOrDefault(r.RowId));
            var placed = shaped.Values.SelectMany(m => m.Pins).ToDictionary(p => p.Id);
            Answer(SyncUploadSections.RasterPins, r => placed.GetValueOrDefault(r.RowId));
        }

        if (documentIds.Count > 0)
        {
            var (onFeatures, onRecordings, onScans) = await SyncDocumentEndpoints.CarriedAsync(db, ctx, handed, ct);
            var rowsOf = new List<SyncLinkRow>();
            foreach (var carried in new[] { onFeatures, onRecordings, onScans })
            {
                rowsOf.AddRange(await carried.Where(x => documentIds.Contains(x.Document.Id)).ToListAsync(ct));
            }

            var shaped = await SyncDocumentEndpoints.ShapeAsync(db, photos, ctx, rowsOf, ct);

            // A document is the same entry on every link that carries it; the first of them by
            // identifier is taken so the answer does not depend on the order the database chose.
            var documents = shaped.GroupBy(l => l.DocumentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).First().Document);
            Answer(SyncUploadSections.Documents, r => documents.GetValueOrDefault(r.RowId));
            Answer(SyncUploadSections.DocumentLinks, r => r.Link is { } link ? LinkOf(shaped, link) : null);
        }

        return found;
    }

    /// <summary>
    /// The catalogue link a link line was about. A phone names a link by its document and what it
    /// hangs on, and the document can hang there more than once; the one the line itself named is
    /// preferred, then the first by identifier.
    /// </summary>
    private static SyncDocumentLinkDto? LinkOf(IReadOnlyList<SyncDocumentLinkDto> shaped, SyncLinkProposal link)
    {
        var named = link.AttachmentIds.ToList();
        return shaped
            .Where(l => l.DocumentId == link.Row.DocumentId
                && l.Target.Kind == link.Row.Target.Kind
                && l.Target.Id == link.Row.Target.Id)
            .OrderBy(l => named.IndexOf(l.Id) is var at && at >= 0 ? at : int.MaxValue)
            .ThenBy(l => l.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// The features any of the rows asked about hangs on — a cave, a place, an area, the object a
    /// document hangs on — narrowed to the ones this caller may read and is not withheld: the
    /// answer every stream reads against, without the set.
    /// </summary>
    private static async Task<IQueryable<Feature>> HandedAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<Guid> documentIds,
        IReadOnlyList<SyncLinkProposal> links,
        IReadOnlyList<Guid> recordingIds,
        IReadOnlyList<Guid> mapIds,
        CancellationToken ct)
    {
        var features = new HashSet<Guid>();
        var recordings = new HashSet<Guid>(recordingIds);
        var scans = new HashSet<Guid>();

        if (documentIds.Count > 0)
        {
            var hung = await (from link in db.Attachments.AsNoTracking()
                              join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
                              join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                              where documentIds.Contains(version.DocumentId)
                              select new { link.FeatureId, link.EntityType, link.EntityId })
                .ToListAsync(ct);
            foreach (var h in hung)
            {
                if (h.FeatureId is { } feature)
                {
                    features.Add(feature);
                }
                else if (h.EntityType == AttachedEntityType.TripRecording && h.EntityId is { } recording)
                {
                    recordings.Add(recording);
                }
                else if (h.EntityType == AttachedEntityType.TripRecordingPoint && h.EntityId is { } scan)
                {
                    scans.Add(scan);
                }
            }
        }

        foreach (var link in links)
        {
            _ = link.Row.Target.Kind switch
            {
                SyncLinkTargetKind.TripRecording => recordings.Add(link.Row.Target.Id),
                SyncLinkTargetKind.TripPoint => scans.Add(link.Row.Target.Id),
                _ => features.Add(link.Row.Target.Id),
            };
        }

        if (scans.Count > 0)
        {
            foreach (var s in await db.TripRecordingPoints.AsNoTracking()
                         .Where(p => scans.Contains(p.Id))
                         .Select(p => new { p.RecordingId, p.PlaceFeatureId })
                         .ToListAsync(ct))
            {
                recordings.Add(s.RecordingId);
                if (s.PlaceFeatureId is { } place)
                {
                    features.Add(place);
                }
            }
        }

        if (recordings.Count > 0)
        {
            features.UnionWith(await db.TripRecordings.AsNoTracking()
                .Where(r => recordings.Contains(r.Id))
                .Select(r => r.CaveFeatureId)
                .ToListAsync(ct));
            features.UnionWith(await db.TripRecordingPoints.AsNoTracking()
                .Where(p => recordings.Contains(p.RecordingId) && p.DeletedAt == null && p.PlaceFeatureId != null)
                .Select(p => p.PlaceFeatureId!.Value)
                .ToListAsync(ct));
        }

        if (mapIds.Count > 0)
        {
            foreach (var m in await db.RasterMaps.AsNoTracking()
                         .Where(m => mapIds.Contains(m.Id))
                         .Select(m => new { m.CaveFeatureId, m.CaveAreaFeatureId })
                         .ToListAsync(ct))
            {
                features.Add(m.CaveFeatureId);
                if (m.CaveAreaFeatureId is { } area)
                {
                    features.Add(area);
                }
            }

            features.UnionWith(await PlacePointQueries.Rows(db).AsNoTracking()
                .Where(p => mapIds.Contains(p.MapId) && p.DeletedAt == null)
                .Select(p => p.PlaceId)
                .ToListAsync(ct));
        }

        var asked = features.ToArray();
        return await SyncScope.WithoutWithheldAsync(
            db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers).Where(f => asked.Contains(f.Id)),
            protection,
            ctx,
            ct);
    }
}
