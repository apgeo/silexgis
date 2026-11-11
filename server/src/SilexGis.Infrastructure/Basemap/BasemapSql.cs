// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>Raw SQL of the basemap downloads.</summary>
public static class BasemapSql
{
    private const long SubmissionLockKey = 0x42415345_444C0001;

    /// <summary>
    /// Holds every other download request back until this transaction ends. "Is one queued or
    /// running" followed by "queue one" is a read then a write: asked apart, requests a few
    /// milliseconds apart all find the queue empty and all queue a download. Transaction-scoped,
    /// so a request that fails while holding it cannot leave the next one waiting.
    /// </summary>
    public static Task TakeSubmissionLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [SubmissionLockKey], ct);
}
