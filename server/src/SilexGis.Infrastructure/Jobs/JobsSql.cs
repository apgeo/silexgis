// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Raw SQL for the job queue (raw SQL lives only in *Sql.cs files). Claiming uses
/// FOR UPDATE SKIP LOCKED so multiple workers (in-process or future external ones)
/// can poll the same table without double-processing.
/// </summary>
public static class JobsSql
{
    /// <summary>Atomically claims the oldest queued job; returns its id or null.</summary>
    public static async Task<long?> ClaimNextAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE processing_jobs SET status = 1, started_at = now(), attempts = attempts + 1
            WHERE id = (
                SELECT id FROM processing_jobs
                WHERE status = 0
                ORDER BY id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """;
        var result = await command.ExecuteScalarAsync(ct);
        return result is long id ? id : null;
    }

    /// <summary>
    /// Returns jobs left Running by a previous process (crash/restart) to the queue.
    /// Called once at worker startup, before polling begins.
    /// </summary>
    public static async Task RequeueInterruptedAsync(SilexGisDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE processing_jobs SET status = 0, started_at = NULL WHERE status = 1", ct);
    }
}
