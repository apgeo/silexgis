// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// Takes the write-ups filed on trips off those trips when a cave the trips were followed in
/// comes under location protection. <b>Nothing calls this.</b> It is kept, with the gap it was
/// written to close stated below, because the decision to stop calling it is to be looked at
/// again.
/// </summary>
/// <remarks>
/// <para>
/// <b>The danger this leaves open, in one sentence.</b> A write-up filed before a cave was
/// protected can still name that cave's stations to everybody who may read the trip; protecting
/// the cave does not take such a copy back, and the way to remove it is to delete the filed copy.
/// </para>
/// <para>
/// Why that happens. A write-up filed on a trip is a file, and a file does not ask again. Where
/// its layout printed the trip's tracking journal, or the survey's station list, it states the
/// stations and depths any account could be told on the day it was made — and from the moment the
/// cave is protected that is no longer what any account may be told, while everybody who may read
/// the trip can still open the file. Every live surface decides a place on each read for exactly
/// this reason; a filed file cannot, and a write-up filed after the protection withholds the
/// place as it should.
/// </para>
/// <para>
/// <b>Why it is not closed.</b> Withdrawing the files was built and then judged more machinery
/// than it buys. The protection can arrive by the cave's own flag or by one set anywhere above
/// it, so "newly protected" has to be recognised inside the ancestry recompute; a filed file
/// records nothing about whether its layout ever printed a place, and a report since taken off a
/// log may still be named in a file made before it was, so the sweep had to run wide — every
/// generated write-up of every trip ever followed in the cave, a deleted trip's included — and so
/// it withdrew files that stated no place at all. The judgement was that the logic costs more
/// than the hole it fills. <b>It is to be looked at again</b>, which is why the routine below is
/// kept rather than deleted: it is exercised by a test, so that it still works the day somebody
/// decides to call it.
/// </para>
/// <para>
/// What it does when it is called. <b>Taken off the trip, not destroyed</b> — the same thing that
/// happens to a filed write-up when a newer one replaces it. The document stays in the library of
/// whoever made it, and they and an administrator are the only ones who reach it once nothing
/// hangs it on a record others read. It is wider than it strictly needs to be, for the reasons
/// above: every generated write-up of every trip that was ever followed in the cave goes. A
/// report a club wrote and uploaded by hand into the same slot is not the application's to
/// withdraw and stays where it is.
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
