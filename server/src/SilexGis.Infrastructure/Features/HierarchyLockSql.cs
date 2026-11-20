// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// Raw SQL behind the one lock every derivation of hierarchy state takes (raw SQL lives only in
/// *Sql.cs files).
/// </summary>
public static class HierarchyLockSql
{
    // "HIERARC" + 1. A single 64-bit key: PostgreSQL keeps it apart from the two-integer keys the
    // other per-row locks of this database use.
    private const long HierarchyLockKey = 0x48494552_41524301L;

    /// <summary>
    /// Waits for, and holds until the surrounding transaction ends, the lock that serialises every
    /// reading of the containment edges and protection flags that a stored ancestor list, closure
    /// row or effective-protection flag is worked out from. Without it two writers each read the
    /// other's input before it committed — a protection switched on beside a feature created under
    /// it, or two moves that each make the other a cycle — and both commit a state neither would
    /// have stored alone. Taken before the first such read, so that at READ COMMITTED every later
    /// statement sees what the previous holder committed. Outside a transaction it is released when
    /// the statement ends and protects nothing, so a caller that needs it opens one.
    /// </summary>
    public static Task TakeAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [HierarchyLockKey], ct);

    /// <summary>
    /// Gives the surrounding transaction its transaction id now, before its first write would.
    /// </summary>
    /// <remarks>
    /// A phone's download stops short of the start of the oldest unfinished transaction that holds
    /// an id, and no earlier. A transaction that stamps a change time and only then gets its id —
    /// at its first write, after a long read — leaves a window in which an edit committing beside
    /// it can hand a phone a position past that stamp, and the rows stamped with it then sort
    /// behind the phone's position for good. Holding an id from before the stamp is taken closes
    /// that window.
    /// </remarks>
    public static Task ClaimTransactionIdAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_current_xact_id()", ct);
}
