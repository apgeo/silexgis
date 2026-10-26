// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// The one place a recording's photographs are carried onto the report made from it, shared by the
/// automatic conversion, the conversion by hand and the archive import that claims a recording a
/// phone already sent, so the three ways a recording becomes a trip leave the same trip behind.
/// </summary>
internal static class TripRecordingPhotos
{
    /// <summary>
    /// Hangs on the new trip every file that hangs on the recording or on one of its live scans,
    /// once each, so the report carries the photographs the party took. Done inside the
    /// conversion's claim, so a recording converted twice gets its photographs once.
    /// </summary>
    /// <remarks>
    /// Copies the link, never the file: the bytes, the document and its renditions stay single.
    /// Which position a reader may be told for such a photograph is decided from the trip's own
    /// caves, which is the recording's cave, and the copy keeps no trace of the scan's place. So a
    /// photograph taken at a scan whose place is protected by a root the cave does not share is
    /// not copied: its copy would be judged on the cave alone once the scan's own link went.
    /// A link whose document has been deleted is left behind. A photograph added to the recording after its trip was made
    /// is not carried: the trip is not rewritten.
    /// </remarks>
    internal static async Task CopyToTripAsync(
        SilexGisDbContext db, TripRecording recording, IReadOnlyList<TripRecordingPoint> points, TripLog trip, Feature cave, CancellationToken ct)
    {
        var pointIds = points.Select(p => p.Id).ToList();
        var links = await (
            from link in db.Attachments.AsNoTracking()
            where (link.EntityType == AttachedEntityType.TripRecording && link.EntityId == recording.Id)
                || (link.EntityType == AttachedEntityType.TripRecordingPoint
                    && link.EntityId != null && pointIds.Contains(link.EntityId.Value))
            join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            join document in db.Documents.AsNoTracking() on version.DocumentId equals document.Id
            select new
            {
                link.FileId,
                link.EntityId,
                link.EntityType,
                link.Role,
                link.Caption,
                link.SortOrder,
                link.AddedBy,
                link.CreatedAt,
                link.Id,
            }).ToListAsync(ct);

        // A scan's place that carries a protection root the cave does not share would drop out of
        // the copy's chain: leave such a scan's photographs on the scan.
        var guardedScanIds = await ScansAtOwnProtectedPlacesAsync(db, points, cave, ct);

        // The recording's own photographs lead, then the scans' in the order they were hung.
        var ordered = links
            .Where(l => l.EntityType != AttachedEntityType.TripRecordingPoint
                || l.EntityId is not { } scanId || !guardedScanIds.Contains(scanId))
            .OrderBy(l => l.EntityType == AttachedEntityType.TripRecording ? 0 : 1)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .GroupBy(l => l.FileId)
            .Select(g => g.First());

        // A trip that already carries the file (an existing trip a recording was folded into)
        // keeps its link; the file is hung once.
        var already = (await db.Attachments.AsNoTracking()
                .Where(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == trip.Id)
                .Select(a => new { a.FileId, a.SortOrder })
                .ToListAsync(ct));
        var hung = already.Select(a => a.FileId).ToHashSet();
        var order = already.Count == 0 ? 0 : already.Max(a => a.SortOrder) + 1;
        foreach (var link in ordered.Where(l => !hung.Contains(l.FileId)))
        {
            db.Attachments.Add(new Attachment
            {
                FileId = link.FileId,
                EntityType = AttachedEntityType.TripLog,
                EntityId = trip.Id,
                Role = link.Role,
                Caption = link.Caption,
                SortOrder = order++,
                AddedBy = link.AddedBy ?? recording.OwnerUserId,
            });
        }
    }

    /// <summary>
    /// The scans whose place is protected by a root that the recording's cave does not also sit
    /// under, asked of the stored protection roots (own flag and ancestry) rather than by a second
    /// copy of the rule: those are the places a cave-only chain would not cover.
    /// </summary>
    private static async Task<HashSet<Guid>> ScansAtOwnProtectedPlacesAsync(
        SilexGisDbContext db, IReadOnlyList<TripRecordingPoint> points, Feature cave, CancellationToken ct)
    {
        var placeIds = points.Where(p => p.PlaceFeatureId is not null).Select(p => p.PlaceFeatureId!.Value)
            .Distinct().ToList();
        if (placeIds.Count == 0)
        {
            return [];
        }

        var places = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => placeIds.Contains(f.Id))
            .Select(f => new { f.Id, f.LocationProtected, f.AncestorIds })
            .ToListAsync(ct);
        var caveRow = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.Id == cave.Id)
            .Select(f => new { f.Id, f.AncestorIds })
            .FirstAsync(ct);

        var ancestorIds = places.SelectMany(p => p.AncestorIds).Append(cave.Id).Concat(caveRow.AncestorIds).ToHashSet();
        var protectedRoots = (await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ancestorIds.Contains(f.Id) && f.LocationProtected)
            .Select(f => f.Id)
            .ToListAsync(ct)).ToHashSet();
        var caveCovers = caveRow.AncestorIds.Append(caveRow.Id).Where(protectedRoots.Contains).ToHashSet();

        var guardedPlaces = places
            .Where(p => p.AncestorIds.Append(p.Id).Where(id => protectedRoots.Contains(id) || (id == p.Id && p.LocationProtected))
                .Any(id => !caveCovers.Contains(id)))
            .Select(p => p.Id)
            .ToHashSet();
        return [.. points.Where(p => p.PlaceFeatureId is { } id && guardedPlaces.Contains(id)).Select(p => p.Id)];
    }
}
