// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>Allow-listed tables whose rows a phone edits against a revision (never user input).</summary>
internal enum SyncRevisionTable
{
    /// <summary>
    /// The feature supertype row, which every kind's edit also writes: its stamp is the revision a
    /// phone holds for a cave, an entrance or any other feature.
    /// </summary>
    Features,

    /// <summary>A recorded trip.</summary>
    TripRecordings,

    /// <summary>A point scanned during a recorded trip.</summary>
    TripRecordingPoints,

    /// <summary>A document, whose stamp moves with its description and with each new version.</summary>
    Documents,

    /// <summary>A raster map laid over a cave.</summary>
    RasterMaps,

    /// <summary>A link between resources, such as a place pinned to a survey station.</summary>
    ResLinks,
}

/// <summary>
/// Whether a phone's base revision still holds once the row's lock is taken, and the revision
/// that stands — what a refused write answers the phone with. Null when the row is not there.
/// </summary>
internal sealed record SyncRevisionHold(bool Holds, DateTimeOffset? Revision);

/// <summary>A row's revision and removal stamp as they stand once its lock is held.</summary>
internal sealed record SyncLockedRevision(DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt);

/// <summary>
/// The row lock that makes a phone's revision check and its write one act.
/// </summary>
/// <remarks>
/// An upload is one long transaction that writes item by item. Comparing the stored revision with
/// the one a phone sent by an ordinary read and then writing lets two uploads that read the same
/// revision both pass: the second reads the last committed value while the first's write is still
/// uncommitted, its update then waits on the first's row lock, and once the first commits it lands
/// over it — both phones told "updated", one edit silently gone. Taking the row lock before the
/// comparison makes the second upload wait at the comparison instead, and under the default
/// isolation the statement that waited reads the revision the first upload committed, so the second
/// is answered a conflict. The revision is read from this statement, never from an entity the
/// context may already be tracking: a tracked copy keeps the values it was first read with.
/// </remarks>
internal static class SyncRevisionSql
{
    private static readonly IReadOnlyDictionary<SyncRevisionTable, string> Tables =
        new Dictionary<SyncRevisionTable, string>
        {
            [SyncRevisionTable.Features] = "features",
            [SyncRevisionTable.TripRecordings] = "trip_recordings",
            [SyncRevisionTable.TripRecordingPoints] = "trip_recording_points",
            [SyncRevisionTable.Documents] = "documents",
            [SyncRevisionTable.RasterMaps] = "raster_maps",
            [SyncRevisionTable.ResLinks] = "res_links",
        };

    /// <summary>
    /// The row's revision, locking the row until the surrounding transaction ends, or null when
    /// there is no such row. Without a transaction on the context the lock ends with the
    /// statement, and the answer is only as good as an ordinary read.
    /// </summary>
    /// <remarks>
    /// The weaker of the two update locks, which is all the write that follows takes itself: it
    /// still makes every other revision writer wait, but not an insert elsewhere whose foreign key
    /// names this row. The full update lock would make a web user adding an entrance under a cave
    /// an upload had edited wait for the whole upload, and two uploads that each edited a row the
    /// other then refers to would deadlock.
    /// </remarks>
    public static async Task<SyncLockedRevision?> LockAsync(
        SilexGisDbContext db, SyncRevisionTable table, Guid id, CancellationToken ct)
    {
        var row = await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<Stamps>(
            new CommandDefinition(
                $"SELECT updated_at AS UpdatedAt, deleted_at AS DeletedAt FROM {Tables[table]} WHERE id = @id FOR NO KEY UPDATE",
                new { id },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
        return row is null
            ? null
            : new SyncLockedRevision(Utc(row.UpdatedAt), row.DeletedAt is { } deleted ? Utc(deleted) : null);
    }

    /// <summary>
    /// The apply guard every section's write starts with: the row locked, then compared on what the
    /// locking statement read. Another upload that has written the row and not yet committed is
    /// waited for here, and its committed revision is what this one is arbitrated against; a row
    /// gone or moved on since the phone read it does not hold. One home for the check, so a section
    /// added later cannot copy a version of it that compares before it locks. A section whose rows
    /// hang under a parent it must also hold (a pin's map, a mapping's place) takes that lock first.
    /// </summary>
    public static async Task<SyncRevisionHold> HoldAsync(
        SilexGisDbContext db, SyncRevisionTable table, Guid id, DateTimeOffset baseRevision, CancellationToken ct)
    {
        var locked = await LockAsync(db, table, id, ct);
        return new SyncRevisionHold(
            locked is { DeletedAt: null } && SyncRevisions.Same(locked.UpdatedAt, baseRevision),
            locked?.UpdatedAt);
    }

    // timestamptz comes back as a UTC DateTime; the offset form is what the revision is carried as.
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class Stamps
    {
        public DateTime UpdatedAt { get; init; }

        public DateTime? DeletedAt { get; init; }
    }
}
