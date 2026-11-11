// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Storage;

/// <summary>The row locks a confirmed deletion of scan products takes before it decides what to delete.</summary>
public static class StorageReportSql
{
    /// <summary>
    /// Locks the uploads whose products are about to be deleted and every point-cloud model of
    /// their caves, until the transaction ends.
    /// </summary>
    /// <remarks>
    /// Between the group's query and its deletion, an upload could be queued to run again (which
    /// would take up the very products being deleted) or another scan could become the cave's
    /// current one (which would change what is superseded). Both are writes to these rows, so with
    /// them locked such a write waits until the deletion has committed and then sees its result,
    /// and the group is computed again after the lock so the deletion sees any such write that
    /// committed first. Rows are taken in id order so two deletions cannot wait on each other.
    /// </remarks>
    public static async Task LockScanRowsAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> uploadIds, IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        if (uploadIds.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM survey_uploads WHERE id = ANY({0}) ORDER BY id FOR UPDATE",
                [uploadIds.ToArray()], ct);
        }

        if (caveIds.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM survey_models WHERE cave_feature_id = ANY({0}) ORDER BY id FOR UPDATE",
                [caveIds.ToArray()], ct);
        }
    }
}
