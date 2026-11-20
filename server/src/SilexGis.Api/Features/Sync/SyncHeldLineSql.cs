// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// Raw SQL behind the lock a phone's resend takes on the waiting lines it repeats or replaces
/// (raw SQL lives only in *Sql.cs files).
/// </summary>
internal static class SyncHeldLineSql
{
    /// <summary>
    /// Locks those of the given lines that still wait for a reviewer, in id order, until the
    /// transaction ends, and returns them; a line decided meanwhile is neither locked nor returned.
    /// </summary>
    /// <remarks>
    /// A reviewer's decision claims its lines with an update, so the lock waits for a decision in
    /// flight and then reads what it decided. Taken before the bytes the lines name are kept, so a
    /// resend and a decision take the line before the stored upload, in the order a decision does.
    /// </remarks>
    public static Task<List<long>> LockPendingAsync(SilexGisDbContext db, IReadOnlyCollection<long> ids, CancellationToken ct) =>
        ids.Count == 0
            ? Task.FromResult(new List<long>())
            : db.Database.SqlQueryRaw<long>(
                    "SELECT id AS \"Value\" FROM import_batch_items WHERE id = ANY({0}) AND review_decision = {1} ORDER BY id FOR UPDATE",
                    ids.Distinct().ToArray(), (short)ImportReviewDecision.Pending)
                .ToListAsync(ct);
}
