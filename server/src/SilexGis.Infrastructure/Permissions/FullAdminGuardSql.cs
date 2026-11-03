// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>The one lock every "is a full administrator left?" decision takes.</summary>
public static class FullAdminGuardSql
{
    /// <summary>The key of that lock. One decision, one key.</summary>
    private const long LastFullAdminLockKey = 0x46554c4c_41444d01L; // "FULLADM" + 1

    /// <summary>
    /// Holds every other transaction's last-administrator check back until this one ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check runs after the transaction has staged its own removal, and under the default
    /// isolation it cannot see another transaction's removal that has not committed yet. Two
    /// administrators locking each other at the same moment would each see the other still live,
    /// both commit, and leave nobody. Taken just before the check, this lock makes the second
    /// check wait until the first transaction has committed or rolled back; a statement under
    /// the default isolation reads what is committed when it starts, so the second check then
    /// sees the first removal and refuses its own.
    /// </para>
    /// <para>
    /// Taken last, after the transaction's own writes, and held only across a read and the
    /// commit, so a holder never waits on anything another holder could be holding. A
    /// transaction-scoped lock, released by the commit or the rollback, so none can leak.
    /// Outside a transaction it would be released at once, and the check would be as unguarded
    /// as it was without it — every caller runs the check inside the transaction it guards.
    /// </para>
    /// </remarks>
    public static Task TakeLastFullAdminLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [LastFullAdminLockKey], ct);
}
