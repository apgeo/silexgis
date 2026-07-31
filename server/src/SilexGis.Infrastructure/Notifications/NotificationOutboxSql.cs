// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Raw SQL for the notification outbox (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// Claiming is a lease, not a status change: the claim pushes <c>not_before</c> into the future so
/// a process that dies mid-send leaves the row exactly as it was, due again once the lease
/// expires. That is deliberately unlike the processing queue, which marks rows Running and sweeps
/// them back at startup — a sweep like that would re-send every message that was in flight during
/// a restart.
/// </remarks>
public static class NotificationOutboxSql
{
    /// <summary>How long a claimed row is hidden from other workers while its send is attempted.</summary>
    private const int LeaseMinutes = 5;

    /// <summary>
    /// Leases up to <paramref name="batchSize"/> immediately-due rows and returns their ids.
    /// SKIP LOCKED so two workers never take the same row.
    /// </summary>
    public static Task<List<long>> ClaimDueAsync(SilexGisDbContext db, int batchSize, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_outbox
            SET attempts = attempts + 1, not_before = now() + make_interval(mins => {LeaseMinutes})
            WHERE id IN (
                SELECT id FROM notification_outbox
                WHERE status = 0 AND not_before <= now()
                ORDER BY id
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            ("batch", batchSize),
            ct);

    /// <summary>
    /// Leases every due deferred row belonging to ONE recipient — the whole of that person's
    /// digest in a single statement — and returns their ids. Empty when nobody's digest is due.
    /// </summary>
    public static Task<List<long>> ClaimDueDigestAsync(SilexGisDbContext db, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_outbox
            SET attempts = attempts + 1, not_before = now() + make_interval(mins => {LeaseMinutes})
            WHERE status = 1 AND not_before <= now() AND user_id = (
                SELECT user_id FROM notification_outbox
                WHERE status = 1 AND not_before <= now()
                ORDER BY id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            ct);

    /// <summary>
    /// Drops rows that have run their course. Dead rows are kept: they are the only record an
    /// operator has of a message that never arrived.
    /// </summary>
    public static Task<int> PruneAsync(SilexGisDbContext db, int retentionDays, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "DELETE FROM notification_outbox WHERE status IN (2, 3) AND created_at < now() - make_interval(days => @days)",
            [new Npgsql.NpgsqlParameter("days", retentionDays)],
            ct);

    private static Task<List<long>> QueryIdsAsync(
        SilexGisDbContext db, string sql, CancellationToken ct) =>
        QueryIdsAsync(db, sql, null, ct);

    private static async Task<List<long>> QueryIdsAsync(
        SilexGisDbContext db, string sql, (string Name, int Value)? parameter, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is { } p)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = p.Name;
            dbParameter.Value = p.Value;
            command.Parameters.Add(dbParameter);
        }

        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}
