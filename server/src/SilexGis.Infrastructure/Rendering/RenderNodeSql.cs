// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>Raw SQL of the render-node queue (raw SQL lives only in *Sql.cs files).</summary>
public static class RenderNodeSql
{
    /// <summary>
    /// Ends every render job whose requester asked it to stop and that no node will tell about it
    /// any more: one still queued, and one running whose lease has run out (its node is gone, and
    /// the sweep would otherwise put it back in the queue). Answers the ids ended. Compared with the
    /// database's own clock, the clock that wrote the lease.
    /// </summary>
    public static async Task<IReadOnlyList<long>> EndCancelledAsync(SilexGisDbContext db, string error, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE processing_jobs j
            SET status = 3, completed_at = now(), error = @error, lease_expires_at = NULL
            FROM flyby_renders r
            WHERE r.processing_job_id = j.id AND r.cancel_requested_at IS NOT NULL
              AND j.kind = ANY(@kinds)
              AND (j.status = 0
                   OR (j.status = 1 AND (j.lease_expires_at IS NULL OR j.lease_expires_at < now())))
            RETURNING j.id
            """;
        command.Parameters.Add(new NpgsqlParameter("error", error));
        command.Parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = ProcessingJobKinds.NodeLane.ToArray(),
        });
        var ended = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ended.Add(reader.GetInt64(0));
        }

        return ended;
    }

    private const long StorageLockKey = 0x524e4f44_45535401L; // "RNODEST" + 1

    /// <summary>
    /// Holds every other render's storage and turn decisions back until this transaction ends. "Is
    /// there room / is one of the account's going already" followed by the insert is a read then a
    /// write: asked apart, two uploads (or two starts) a few milliseconds apart would each find the
    /// room the other then takes. Transaction-scoped, so a request that fails while holding it
    /// cannot leave the next one waiting.
    /// </summary>
    public static Task TakeStorageLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [StorageLockKey], ct);

    /// <summary>
    /// Claims the oldest queued render job this node can take — the jobs whose frames it keeps
    /// first — leasing it to the node; null when there is none. A job is one the node can take
    /// when its film's container is one the node writes, and when the node said nothing of its
    /// disk, or said it has at least the scratch space the render is estimated to need (a job
    /// whose frames it keeps needs less, and is not held to the estimate). A job with no render
    /// row is claimable, so the caller can end it.
    /// </summary>
    /// <remarks>
    /// The same update every lane is claimed with (<c>JobsSql.ClaimNextAsync</c>), with the render
    /// joined in for what the node can take. The predicate sits inside the locking sub-select for
    /// the reason given there: outside, the sub-select would lock a row the node cannot take and
    /// the update would then match nothing.
    /// </remarks>
    public static async Task<long?> ClaimNextAsync(
        SilexGisDbContext db,
        TimeSpan lease,
        IReadOnlyCollection<long> preferred,
        IReadOnlyCollection<string> containers,
        long? freeDiskBytes,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            UPDATE processing_jobs
            SET status = 1, started_at = now(), attempts = attempts + 1, not_before = NULL,
                progress_done = NULL, progress_total = NULL, lease_expires_at = now() + @lease
            WHERE id = (
                SELECT j.id FROM processing_jobs j
                LEFT JOIN flyby_renders r ON r.processing_job_id = j.id
                WHERE j.status = 0 AND j.kind = ANY(@kinds)
                  AND (j.not_before IS NULL OR j.not_before <= now())
                  AND (r.id IS NULL
                       OR (r.container = ANY(@containers)
                           AND (@free::bigint IS NULL OR j.id = ANY(@preferred) OR r.scratch_bytes <= @free::bigint)))
                ORDER BY (j.id = ANY(@preferred)) DESC, j.id
                LIMIT 1
                FOR UPDATE OF j SKIP LOCKED)
            RETURNING id
            """;
        AddClaimParameters(command, preferred, containers, freeDiskBytes);
        command.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.Interval) { Value = lease });
        var result = await command.ExecuteScalarAsync(ct);
        return result is long id ? id : null;
    }

    /// <summary>
    /// The queued render jobs, ready to start, that this node could not take: how many for their
    /// container, how many for the scratch space they need, and the most any of those needs.
    /// </summary>
    public static async Task<(int ForContainer, int ForDisk, long? MostNeeded)> SkippedAsync(
        SilexGisDbContext db,
        IReadOnlyCollection<long> preferred,
        IReadOnlyCollection<string> containers,
        long? freeDiskBytes,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            SELECT
                count(*) FILTER (WHERE NOT (r.container = ANY(@containers)))::int,
                count(*) FILTER (WHERE r.container = ANY(@containers)
                                 AND @free::bigint IS NOT NULL AND NOT (j.id = ANY(@preferred))
                                 AND r.scratch_bytes > @free::bigint)::int,
                max(r.scratch_bytes) FILTER (WHERE r.container = ANY(@containers)
                                 AND @free::bigint IS NOT NULL AND NOT (j.id = ANY(@preferred))
                                 AND r.scratch_bytes > @free::bigint)
            FROM processing_jobs j
            JOIN flyby_renders r ON r.processing_job_id = j.id
            WHERE j.status = 0 AND j.kind = ANY(@kinds)
              AND (j.not_before IS NULL OR j.not_before <= now())
              AND r.cancel_requested_at IS NULL
            """;
        AddClaimParameters(command, preferred, containers, freeDiskBytes);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    /// <summary>
    /// Ends the render jobs no node will finish: one whose attempt has run past
    /// <paramref name="runningLimit"/> since it was claimed (a node may keep sending heartbeats
    /// while it renders nothing), and one that has waited in the queue longer than
    /// <paramref name="queuedLimit"/> since it was asked for. Answers the ids ended with each
    /// error. Compared with the database's own clock, the clock that stamped the claim.
    /// </summary>
    public static async Task<IReadOnlyList<long>> EndOverdueAsync(
        SilexGisDbContext db,
        TimeSpan runningLimit,
        string runningError,
        TimeSpan queuedLimit,
        string queuedError,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            UPDATE processing_jobs j
            SET status = 3, completed_at = now(), lease_expires_at = NULL,
                error = CASE WHEN j.status = 1 THEN @runningError ELSE @queuedError END
            FROM flyby_renders r
            WHERE r.processing_job_id = j.id AND j.kind = ANY(@kinds)
              AND ((j.status = 1 AND j.started_at < now() - @running)
                   OR (j.status = 0 AND r.created_at < now() - @queued))
            RETURNING j.id
            """;
        command.Parameters.Add(new NpgsqlParameter("runningError", runningError));
        command.Parameters.Add(new NpgsqlParameter("queuedError", queuedError));
        command.Parameters.Add(new NpgsqlParameter("running", NpgsqlDbType.Interval) { Value = runningLimit });
        command.Parameters.Add(new NpgsqlParameter("queued", NpgsqlDbType.Interval) { Value = queuedLimit });
        command.Parameters.Add(Kinds());
        var ended = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ended.Add(reader.GetInt64(0));
        }

        return ended;
    }

    private static void AddClaimParameters(
        System.Data.Common.DbCommand command,
        IReadOnlyCollection<long> preferred,
        IReadOnlyCollection<string> containers,
        long? freeDiskBytes)
    {
        command.Parameters.Add(Kinds());
        command.Parameters.Add(new NpgsqlParameter("preferred", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = preferred.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("containers", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = containers.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("free", NpgsqlDbType.Bigint) { Value = (object?)freeDiskBytes ?? DBNull.Value });
    }

    private static NpgsqlParameter Kinds() => new("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text)
    {
        Value = ProcessingJobKinds.NodeLane.ToArray(),
    };

    private static async Task<System.Data.Common.DbCommand> CommandAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection.CreateCommand();
    }
}
