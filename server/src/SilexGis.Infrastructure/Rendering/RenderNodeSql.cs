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
}
