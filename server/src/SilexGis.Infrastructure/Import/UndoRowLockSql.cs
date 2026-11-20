// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>The rows an undo of a phone upload compares and then writes back.</summary>
public enum UndoLockedRow
{
    Feature,
    TripRecording,
    TripRecordingPoint,
    Document,
    DevicePerson,
    RasterMap,
}

/// <summary>
/// Raw SQL behind the row lock an undo line takes before it reads what it compares (raw SQL lives
/// only in *Sql.cs files).
/// </summary>
public static class UndoRowLockSql
{
    /// <summary>
    /// Locks one row until the transaction ends, waiting for any writer that holds it.
    /// </summary>
    /// <remarks>
    /// An undo puts a value back only while the row still holds what the upload wrote, so that a
    /// later edit wins over it. Compared on a plain read, a phone upload that has written the row
    /// and not yet committed is invisible: the undo sees the upload's value, decides to put the
    /// old one back, and its write then waits for the phone and lands over the phone's edit once
    /// it commits. Taken before the read, the lock makes the undo wait first and compare on what
    /// the phone committed. Deleted rows are locked too: an undo brings rows back.
    /// </remarks>
    public static Task LockAsync(SilexGisDbContext db, UndoLockedRow row, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            row switch
            {
                UndoLockedRow.Feature => "SELECT 1 FROM features WHERE id = {0} FOR NO KEY UPDATE",
                UndoLockedRow.TripRecording => "SELECT 1 FROM trip_recordings WHERE id = {0} FOR NO KEY UPDATE",
                UndoLockedRow.TripRecordingPoint => "SELECT 1 FROM trip_recording_points WHERE id = {0} FOR NO KEY UPDATE",
                UndoLockedRow.Document => "SELECT 1 FROM documents WHERE id = {0} FOR NO KEY UPDATE",
                UndoLockedRow.DevicePerson => "SELECT 1 FROM device_persons WHERE device_user_id = {0} FOR NO KEY UPDATE",
                UndoLockedRow.RasterMap => "SELECT 1 FROM raster_maps WHERE id = {0} FOR NO KEY UPDATE",
                _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
            },
            [id],
            ct);
}
