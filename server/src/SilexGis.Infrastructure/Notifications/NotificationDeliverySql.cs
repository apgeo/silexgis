// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Raw SQL for notification routing, delivery and retention (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// Claiming a delivery is a lease, not a status change: the claim pushes <c>not_before</c> into
/// the future so a process that dies mid-send leaves the row exactly as it was, due again once the
/// lease expires. That is deliberately unlike the processing queue, which marks rows Running and
/// sweeps them back at startup — a sweep like that would re-send every message that was in flight
/// during a restart.
/// </remarks>
public static class NotificationDeliverySql
{
    /// <summary>How long a claimed row is hidden from other workers while its send is attempted.</summary>
    private const int LeaseMinutes = 5;

    /// <summary>
    /// Takes up to <paramref name="batchSize"/> notifications that have not been routed yet and
    /// returns their ids, stamping <c>routed_at</c> so no notification is ever fanned out twice.
    /// </summary>
    /// <remarks>
    /// The stamp is the claim, and it is terminal rather than a lease: a crash between this
    /// statement and the delivery rows being written loses those deliveries, whereas a lease that
    /// expired would create a second set of them. Losing one outbound copy is the cheaper failure,
    /// because the notification itself is already in the recipient's inbox — the row exists and is
    /// readable whether or not anything ever left the system for it.
    /// </remarks>
    public static Task<List<long>> ClaimUnroutedAsync(SilexGisDbContext db, int batchSize, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            """
            UPDATE notifications
            SET routed_at = now()
            WHERE id IN (
                SELECT id FROM notifications
                WHERE routed_at IS NULL
                ORDER BY id
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            ("batch", batchSize),
            ct);

    /// <summary>
    /// Leases up to <paramref name="batchSize"/> immediately-due deliveries and returns their ids.
    /// SKIP LOCKED so two workers never take the same row.
    /// </summary>
    public static Task<List<long>> ClaimDueAsync(SilexGisDbContext db, int batchSize, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_deliveries
            SET attempts = attempts + 1, not_before = now() + make_interval(mins => {LeaseMinutes})
            WHERE id IN (
                SELECT id FROM notification_deliveries
                WHERE status = 0 AND not_before <= now()
                ORDER BY id
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            ("batch", batchSize),
            ct);

    /// <summary>
    /// Leases every due deferred delivery belonging to ONE recipient — the whole of that person's
    /// summary in a single statement — and returns their ids. Empty when nobody's is due.
    /// </summary>
    /// <remarks>
    /// This is what the recipient id on the delivery row is for: gathering one person's batch would
    /// otherwise join back to the parent inside the claim, on every poll.
    /// </remarks>
    public static Task<List<long>> ClaimDueDigestAsync(SilexGisDbContext db, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_deliveries
            SET attempts = attempts + 1, not_before = now() + make_interval(mins => {LeaseMinutes})
            WHERE status = 1 AND not_before <= now() AND recipient_user_id = (
                SELECT recipient_user_id FROM notification_deliveries
                WHERE status = 1 AND not_before <= now()
                ORDER BY id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            ct);

    /// <summary>
    /// Drops notifications old enough to be of no further interest, deliveries going with them by
    /// cascade.
    /// </summary>
    /// <remarks>
    /// One window over the whole table, keyed on the notification's own age and taking read and
    /// unread alike. Deliberately not keyed on a delivery outcome: an inbox lists what happened,
    /// so keeping only what was successfully emailed would delete precisely the events somebody
    /// had switched email off for.
    /// </remarks>
    public static Task<int> PruneAsync(SilexGisDbContext db, int retentionDays, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "DELETE FROM notifications WHERE created_at < now() - make_interval(days => @days)",
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
