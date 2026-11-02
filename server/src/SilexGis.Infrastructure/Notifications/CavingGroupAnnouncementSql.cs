// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Raw SQL for sending announcements to caving groups (raw SQL lives only in *Sql.cs files).
/// </summary>
public static class CavingGroupAnnouncementSql
{
    /// <summary>The key every announcement send takes. One decision, one key.</summary>
    private const long SendLockKey = 0x414e4e4f_554e4301L; // "ANNOUNC" + 1

    /// <summary>
    /// Holds every other announcement send back until this transaction ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions are asked before an announcement is written — has this sender written too
    /// recently, and would it take the day past what the installation will spend — and both are a
    /// read followed by a write. Asked apart, two sends a few milliseconds apart (a double-click, a
    /// script) both find the cooldown open and both read the same committed figure, so every member
    /// is told twice and the ceiling is passed by however much the second one adds.
    /// </para>
    /// <para>
    /// One key for the whole installation rather than one per sender, because the ceiling is a
    /// total over everybody's announcements: a lock per sender would still let two senders read the
    /// same headroom. Announcements are rare and the work behind the lock is a few reads and one
    /// save, so serialising all of them costs nothing worth measuring. Transaction-scoped, so a
    /// request that fails while holding it cannot leave the next one waiting.
    /// </para>
    /// </remarks>
    public static Task TakeSendLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [SendLockKey], ct);
}
