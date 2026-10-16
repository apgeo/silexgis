// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Raw SQL for the job queue (raw SQL lives only in *Sql.cs files). Claiming uses
/// FOR UPDATE SKIP LOCKED so multiple workers (in-process or future external ones)
/// can poll the same table without double-processing.
/// </summary>
/// <remarks>
/// Every statement here is scoped to one lane, and the lanes divide the table between them, so a
/// worker only ever touches rows meant for it. Locking is not what does that: SKIP LOCKED keeps
/// two workers from taking the same row, but nothing about it stops the wrong worker taking a row
/// meant for the other one. The predicate is the only thing that does.
/// </remarks>
public static class JobsSql
{
    /// <summary>
    /// Atomically claims the next ready job of the lane; returns its id or null. Ready means
    /// queued and not waiting out a retry delay; next means the oldest, with the kinds nobody is
    /// waiting on (<see cref="ProcessingJobKinds.Background"/>) behind every other kind.
    /// </summary>
    public static async Task<long?> ClaimNextAsync(
        SilexGisDbContext db, JobLane lane, CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);

        // The lane predicate sits beside the status inside the sub-select, and it has to. On the
        // outer statement instead, the sub-select would still pick and lock the oldest queued row
        // whatever lane it belonged to, then match nothing — so this worker would be handed no id
        // while the row it had just locked stayed queued. An idle-looking queue that never drains.
        // The retry delay is read against the database's clock, the same clock every claim uses.
        // A claimed job starts its progress afresh: a second attempt counts from nothing.
        command.CommandText = $"""
            UPDATE processing_jobs
            SET status = 1, started_at = now(), attempts = attempts + 1, not_before = NULL,
                progress_done = NULL, progress_total = NULL
            WHERE id = (
                SELECT id FROM processing_jobs
                WHERE status = 0 AND {LanePredicate(lane)}
                  AND (not_before IS NULL OR not_before <= now())
                ORDER BY (kind = ANY(@background)), id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """;
        command.Parameters.Add(KindsParameter(lane));
        command.Parameters.Add(new NpgsqlParameter("background", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = ProcessingJobKinds.Background.ToArray(),
        });

        var result = await command.ExecuteScalarAsync(ct);
        return result is long id ? id : null;
    }

    /// <summary>
    /// Returns jobs of this lane left Running by a previous process (crash/restart) to the
    /// queue. Called once at worker startup, before polling begins.
    /// </summary>
    /// <remarks>
    /// Filtered by the same lane as the claim, and for the same reason. Unfiltered, a worker
    /// starting up would put back whatever the other lane's worker was in the middle of, and that
    /// job would then run a second time alongside the first one — which is the one thing the
    /// queue exists to prevent.
    /// </remarks>
    public static async Task RequeueInterruptedAsync(
        SilexGisDbContext db, JobLane lane, CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = $"""
            UPDATE processing_jobs SET status = 0, started_at = NULL
            WHERE status = 1 AND {LanePredicate(lane)}
            """;
        command.Parameters.Add(KindsParameter(lane));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Puts a job that failed in passing back in the queue, to be taken again no sooner than
    /// <paramref name="delay"/> from now, with the failure kept as its error meanwhile so a screen
    /// waiting on it can say why it is not done yet. Only a job still running is put back.
    /// </summary>
    /// <remarks>
    /// The delay is added to the database's own clock, the clock the claim compares it with, so the
    /// two cannot disagree about when it has passed. The attempt's progress goes with it: a job
    /// waiting for its next turn has done nothing of that turn yet.
    /// </remarks>
    public static async Task PutBackAsync(
        SilexGisDbContext db, long jobId, TimeSpan delay, string error, CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            UPDATE processing_jobs
            SET status = 0, started_at = NULL, not_before = now() + @delay, error = @error,
                progress_done = NULL, progress_total = NULL
            WHERE id = @id AND status = 1
            """;
        command.Parameters.Add(new NpgsqlParameter("id", jobId));
        command.Parameters.Add(new NpgsqlParameter("delay", NpgsqlDbType.Interval) { Value = delay });
        command.Parameters.Add(new NpgsqlParameter("error", error));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Records how far a running job has got. A job no longer running is left alone.</summary>
    public static async Task ReportProgressAsync(
        SilexGisDbContext db, long jobId, int done, int total, CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            UPDATE processing_jobs SET progress_done = @done, progress_total = @total
            WHERE id = @id AND status = 1
            """;
        command.Parameters.Add(new NpgsqlParameter("id", jobId));
        command.Parameters.Add(new NpgsqlParameter("done", done));
        command.Parameters.Add(new NpgsqlParameter("total", total));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// A command on the context's own connection, opened if the context has not opened it yet.
    /// Going through the connection rather than through the raw-SQL helpers keeps the lane
    /// predicate — which is one of two fixed fragments, never anything a caller supplies — out of
    /// the string-concatenation shape those helpers refuse.
    /// </summary>
    private static async Task<DbCommand> CommandAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection.CreateCommand();
    }

    /// <summary>
    /// One list, two halves that cover the table exactly once between them.
    /// </summary>
    /// <remarks>
    /// The general lane is written as an exclusion rather than as a list of its own so that a
    /// kind added later belongs to it without anybody remembering to say so — a whitelist there
    /// would leave new kinds claimed by nobody. <c>kind</c> is NOT NULL, which is what makes the
    /// exclusion correct: against a null the comparison would answer null and claim nothing. An
    /// empty list degrades safely both ways — everything is outside it, nothing is inside it.
    /// </remarks>
    private static string LanePredicate(JobLane lane) =>
        lane.Excluded ? "kind <> ALL(@kinds)" : "kind = ANY(@kinds)";

    private static NpgsqlParameter KindsParameter(JobLane lane) =>
        new("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = lane.Kinds.ToArray() };
}
