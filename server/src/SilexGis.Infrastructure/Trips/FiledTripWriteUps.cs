// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// What becomes of the write-ups kept on trips when a cave those trips were followed in comes
/// under location protection: they come off the trips.
/// </summary>
/// <remarks>
/// <para>
/// A write-up kept on a trip is a file, and a file does not ask again. Where its layout prints the
/// trip's tracking journal it states the stations and depths any account could be told on the day
/// it was made — and from the moment the cave is protected that is no longer what any account may
/// be told, while everybody who may read the trip can still open the file. Every live surface
/// decides a place on each read for exactly this reason. A kept file cannot, so it is withdrawn
/// instead: the attachment that hangs it on the trip is removed in the same unit of work that
/// switches the protection on, and the trip can be written up again, at which point the journal is
/// read under the cave as it now stands.
/// </para>
/// <para>
/// <b>Taken off the trip, not destroyed</b> — the same thing that happens to a kept write-up when a
/// newer one replaces it. The document stays in the library of whoever made it, and they and an
/// administrator are the only ones who reach it once nothing hangs it on a record others read.
/// </para>
/// <para>
/// <b>Wider than it strictly needs to be, on purpose.</b> Whether a given file's layout printed a
/// journal is not recorded anywhere, and a report since taken off the log may still be in a file
/// made before it was. So every generated write-up of every trip that was ever followed in the
/// cave goes — a deleted trip's and one whose only report there was removed included. One that
/// stated no place costs a press of a button to make again; one that did and stayed is the
/// failure. A report a club wrote and uploaded by hand into the same slot is not the
/// application's to withdraw and stays where it is.
/// </para>
/// </remarks>
public static class FiledTripWriteUps
{
    /// <summary>
    /// Marks for removal the attachments that hang a generated write-up on any trip followed in
    /// one of the given caves. Nothing is saved here: the rows go when the caller's own change is
    /// committed, so a protection that did not land withdraws nothing.
    /// </summary>
    /// <returns>How many attachments were marked.</returns>
    public static async Task<int> TakeOffTripsFollowedInAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(caveIds);
        if (caveIds.Count == 0)
        {
            return 0;
        }

        var caves = caveIds.Distinct().ToList();

        // Both anchors a place can hang on: the cave of the survey the watch was set on, and the
        // cave each report carries for itself. Read past the filters, so that a deleted trip —
        // which can be restored, file and all — and a report taken off the log are counted.
        var followed = await db.TripPositionEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.CaveFeatureId != null && caves.Contains(e.CaveFeatureId.Value))
            .Select(e => e.TripLogId)
            .Union(db.TripTrackings.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.CaveFeatureId != null && caves.Contains(t.CaveFeatureId.Value))
                .Select(t => t.TripLogId))
            .ToListAsync(ct);
        if (followed.Count == 0)
        {
            return 0;
        }

        var filed = await (
            from attachment in db.Attachments
            join file in db.StoredFiles.AsNoTracking() on attachment.FileId equals file.Id
            where attachment.EntityType == AttachedEntityType.TripLog
                && attachment.EntityId != null
                && followed.Contains(attachment.EntityId.Value)
                && attachment.Role == AttachmentRole.Report
            select new { Attachment = attachment, file.OriginalName })
            .ToListAsync(ct);

        // The name is the only mark a generated write-up carries, and it is the trip's own: told
        // by the one definition the replacing and the deleting of a write-up already use.
        var generated = filed
            .Where(row => row.OriginalName.StartsWith(
                TripReportNaming.GeneratedPrefix(row.Attachment.EntityId!.Value), StringComparison.Ordinal))
            .Select(row => row.Attachment)
            .ToList();
        db.Attachments.RemoveRange(generated);
        return generated.Count;
    }
}
