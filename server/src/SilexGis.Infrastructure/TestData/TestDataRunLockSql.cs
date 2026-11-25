// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>Raw SQL of starting and removing test-data runs.</summary>
public static class TestDataRunLockSql
{
    private const long RunLockKey = 0x54455354_44415401L; // "TESTDAT" + 1

    /// <summary>
    /// Holds every other start or removal request back until this transaction ends. "Is a run
    /// going" followed by "queue one" is a read then a write: two requests a moment apart both
    /// find nothing going and both queue a run, which is exactly what "one run at a time" exists
    /// to prevent. Transaction-scoped, so a request that fails while holding it frees it.
    /// </summary>
    public static Task TakeAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [RunLockKey], ct);
}
