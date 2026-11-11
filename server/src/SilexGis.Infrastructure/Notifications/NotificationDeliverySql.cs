// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Raw SQL for notification routing, delivery and retention (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// <para>
/// Claiming a delivery is a lease, not a status change: the claim pushes <c>not_before</c> into
/// the future so a process that dies mid-send leaves the row exactly as it was, due again once the
/// lease expires. That is deliberately unlike the processing queue, which marks rows Running and
/// sweeps them back — a sweep like that would re-send every message that was in flight during a
/// restart. An attempt is counted when a send starts rather than by the claim of an immediate row,
/// so a row whose sends never record an outcome still reaches the attempt ceiling while the rows a
/// dead pass never reached keep theirs; a summary's rows share one send, and their claim counts it.
/// </para>
/// <para>
/// Whether a row is due is judged against <c>@now</c>, the application's clock, not the
/// database's <c>now()</c>. Every <c>not_before</c> is stamped by the application — a backoff step,
/// the end of somebody's night, the hour of the daily summary — so judging it on another clock
/// stretches or collapses every step by however far the two disagree. With the application a
/// minute behind the database, the first one-minute backoff was due the moment it was written.
/// </para>
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
            [Parameter("batch", batchSize)],
            ct);

    /// <summary>
    /// Leases up to <paramref name="batchSize"/> immediately-due deliveries and returns their ids.
    /// SKIP LOCKED so two workers never take the same row. The lease alone: the sender counts the
    /// attempt on each row as it starts that row's send.
    /// </summary>
    public static Task<List<long>> ClaimDueAsync(
        SilexGisDbContext db, DateTimeOffset now, int batchSize, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_deliveries
            SET not_before = @now + make_interval(mins => {LeaseMinutes})
            WHERE id IN (
                SELECT id FROM notification_deliveries
                WHERE status = 0 AND not_before <= @now
                ORDER BY id
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """,
            [Parameter("now", now), Parameter("batch", batchSize)],
            ct);

    /// <summary>
    /// Leases every due deferred delivery belonging to ONE recipient — the whole of that person's
    /// summary in a single statement — and returns their ids. Empty when nobody's is due.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the recipient id on the delivery row is for: gathering one person's batch would
    /// otherwise join back to the parent inside the claim, on every poll.
    /// </para>
    /// <para>
    /// Who is next is decided by each recipient's <b>first</b> due row alone, and only that row is
    /// locked to decide it; the rest of that person's rows are then locked in id order. Picking by
    /// any due row deadlocked two passes running at once: one locked a recipient's first row and
    /// went on to take the others, while the second, skipping that first row, picked the same
    /// recipient by their second and went on to want the first. With the pick confined to first
    /// rows, a second pass that finds one locked moves on to somebody else and never holds a row of
    /// the first pass's recipient.
    /// </para>
    /// </remarks>
    public static Task<List<long>> ClaimDueDigestAsync(SilexGisDbContext db, DateTimeOffset now, CancellationToken ct) =>
        QueryIdsAsync(
            db,
            $"""
            UPDATE notification_deliveries d
            SET attempts = d.attempts + 1, not_before = @now + make_interval(mins => {LeaseMinutes})
            FROM (
                SELECT id FROM notification_deliveries
                WHERE status = 1 AND not_before <= @now AND recipient_user_id = (
                    SELECT pick.recipient_user_id FROM notification_deliveries pick
                    WHERE pick.status = 1 AND pick.not_before <= @now
                      AND NOT EXISTS (
                          SELECT 1 FROM notification_deliveries earlier
                          WHERE earlier.recipient_user_id = pick.recipient_user_id
                            AND earlier.status = 1 AND earlier.not_before <= @now
                            AND earlier.id < pick.id)
                    ORDER BY pick.id
                    LIMIT 1
                    FOR UPDATE OF pick SKIP LOCKED)
                ORDER BY id
                FOR UPDATE) mine
            WHERE d.id = mine.id
            RETURNING d.id
            """,
            [Parameter("now", now)],
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

    private static Npgsql.NpgsqlParameter Parameter(string name, int value) => new(name, value);

    /// <summary>
    /// Sent as <c>timestamptz</c> in UTC, which is how every <c>not_before</c> is stored, so the
    /// comparison is between instants whatever zone the session or the application runs in.
    /// </summary>
    private static Npgsql.NpgsqlParameter Parameter(string name, DateTimeOffset value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = value.ToUniversalTime() };

    private static async Task<List<long>> QueryIdsAsync(
        SilexGisDbContext db, string sql, IReadOnlyList<Npgsql.NpgsqlParameter> parameters, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
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
