// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The latest change time a download page may hand over without risking a skip.
/// </summary>
/// <remarks>
/// <para>
/// Every stream orders on a change time the application stamps while it writes, and the stamp is
/// taken before the write commits: an upload batch writes item by item and commits after the
/// last, an import commits after its last row. A page that hands a phone a position past a row
/// some unfinished transaction has stamped leaves that row behind the position for good once the
/// transaction commits, because the next page starts strictly after it.
/// </para>
/// <para>
/// So a page stops short of the start of the oldest transaction on this database that has written
/// something and not yet finished: any row such a transaction stamped carries a time at or after
/// that start, and nothing at or after it is handed over until the transaction is gone. Only
/// transactions that hold a transaction id count — a reader holds back nothing, and counting
/// readers would let any long report or idle session freeze every phone's download. The stamp is
/// taken an instant before a transaction's first write gives it an id, and before it begins at all
/// for a single-statement save; that gap is invisible to the database, and the installation's
/// settle interval is what covers it. It is subtracted from the earlier of the two bounds, not
/// from the clock alone: a transaction that stamped a row a moment before it began and then stays
/// open long is still covered for that moment, which it would not be if the interval only moved
/// the clock bound. The stamps come from the application's clock and the bounds from the
/// database's, so the interval also has to cover any difference between the two hosts' clocks.
/// </para>
/// <para>
/// The activity view only shows another session's transaction to the same database role (or to a
/// role allowed to read every session's statistics); the application connects as one role, so its
/// own writers are always seen. The view is read once per transaction and answered from that copy
/// afterwards, so it is read first, before any row of the page, and its copy is dropped first in
/// case the caller is already inside a transaction that read it earlier.
/// </para>
/// </remarks>
internal static class SyncHorizonSql
{
    private const string Sql = """
        SELECT pg_stat_clear_snapshot();
        SELECT least(
            clock_timestamp(),
            (SELECT min(xact_start) FROM pg_stat_activity
             WHERE datname = current_database()
               AND pid <> pg_backend_pid()
               AND backend_type = 'client backend'
               AND backend_xid IS NOT NULL
               AND xact_start IS NOT NULL)) - make_interval(secs => @settleSeconds) AS horizon;
        """;

    /// <summary>
    /// The change time a page must stay strictly below: the start of the oldest unfinished
    /// writing transaction, or now, whichever is earlier, less the settle interval.
    /// </summary>
    public static async Task<DateTimeOffset> ReadAsync(
        SilexGisDbContext db, TimeSpan settle, CancellationToken ct)
    {
        using var results = await db.Database.GetDbConnection().QueryMultipleAsync(
            new CommandDefinition(
                Sql,
                new { settleSeconds = settle.TotalSeconds },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
        _ = await results.ReadAsync();
        var horizon = await results.ReadSingleAsync<DateTime>();
        return new DateTimeOffset(DateTime.SpecifyKind(horizon, DateTimeKind.Utc));
    }
}
