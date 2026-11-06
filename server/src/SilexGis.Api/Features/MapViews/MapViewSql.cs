// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapViews;

/// <summary>Raw SQL of the saved map views (raw SQL lives only in *Sql.cs files).</summary>
public static class MapViewSql
{
    // First half of the two-key advisory lock; the second half is the owner. Any other user of
    // two-key advisory locks picks its own first key, so the two never wait on each other.
    private const int HomeLockClass = 0x4D564831;

    /// <summary>
    /// Holds every other home change of this owner back until the current transaction ends.
    /// "Clear the owner's other homes, then save this one" is a read followed by a write: two
    /// saves running at once each clear what the other has not committed yet, and both commit as
    /// home. Taken first, the second save waits, and its clear then sees the first one's home.
    /// Transaction-scoped, so a save that fails while holding it cannot leave the next one
    /// waiting. Two owners whose ids hash alike only wait for each other; they never share a home.
    /// </summary>
    public static Task TakeHomeLockAsync(SilexGisDbContext db, Guid ownerUserId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({HomeLockClass}, hashtext({ownerUserId.ToString("D")}))", ct);
}
