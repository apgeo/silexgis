// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Cavers;

/// <summary>Raw SQL of the person roster (raw SQL lives only in *Sql.cs files).</summary>
public static class CaverSql
{
    /// <summary>
    /// Takes the row locks of the given roster entries for the rest of the surrounding transaction,
    /// always in id order so two requests locking the same pair cannot deadlock.
    /// </summary>
    /// <remarks>
    /// Any row that names a person through a foreign key — a roster place, an answer, a membership —
    /// takes a key-share lock on that person's row when it is written, which this lock conflicts
    /// with. So a writer naming the person either finished before this lock was granted, and is
    /// read by whatever follows, or waits until the surrounding transaction ends and then sees what
    /// it did: a merge or a delete never acts on a list of dependent rows that is already stale.
    /// </remarks>
    public static Task LockAsync(SilexGisDbContext db, Guid[] caverIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM cavers WHERE id = ANY({caverIds}) ORDER BY id FOR UPDATE", ct);
    }
}
