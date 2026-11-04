// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Holds one row locked from another connection, so an upload that reaches it parks half way
/// through its batch with everything it wrote before that row still uncommitted.
/// </summary>
/// <remarks>
/// A race between two uploads is decided by what the second one reads while the first is inside
/// its transaction. Sent one after the other, every arbitration passes; only a test that keeps the
/// first upload inside that window — by making it wait on a row this connection holds — can tell an
/// arbitration that holds from one that merely looks right. Each step waits until the database
/// reports the expected number of statements parked on a lock, so an upload that answered without
/// ever waiting fails the test instead of passing it for the wrong reason.
/// </remarks>
public sealed class RowStall : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    private readonly SilexGisDbContext db;
    private readonly IDbContextTransaction transaction;

    private RowStall(AsyncServiceScope scope, SilexGisDbContext db, IDbContextTransaction transaction)
    {
        this.scope = scope;
        this.db = db;
        this.transaction = transaction;
    }

    /// <summary>Locks the row <paramref name="id"/> of <paramref name="table"/> until released.</summary>
    public static async Task<RowStall> HoldAsync(SilexGisApiFactory factory, string table, Guid id)
    {
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        var sql = table switch
        {
            "features" => "SELECT 1 AS \"Value\" FROM features WHERE id = {0} FOR UPDATE",
            "trip_recordings" => "SELECT 1 AS \"Value\" FROM trip_recordings WHERE id = {0} FOR UPDATE",
            "documents" => "SELECT 1 AS \"Value\" FROM documents WHERE id = {0} FOR UPDATE",
            "raster_maps" => "SELECT 1 AS \"Value\" FROM raster_maps WHERE id = {0} FOR UPDATE",
            "res_links" => "SELECT 1 AS \"Value\" FROM res_links WHERE id = {0} FOR UPDATE",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table a phone edits."),
        };
        (await db.Database.SqlQueryRaw<int>(sql, id).ToListAsync())
            .Count.ShouldBe(1, $"the stall row of {table} must exist and be locked");
        return new RowStall(scope, db, transaction);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> statements of this database are parked on a lock,
    /// failing as soon as <paramref name="pending"/> answers instead.
    /// </summary>
    public async Task WaitForWaitersAsync(int count, Task pending)
    {
        var waiting = 0L;
        for (var attempt = 0; attempt < 400 && waiting < count; attempt++)
        {
            pending.IsCompleted.ShouldBeFalse(
                "the upload answered without waiting on a row another writer holds, so nothing was raced");

            // The activity view is read once per transaction and then answered from that copy,
            // and this connection is inside the transaction that holds the stall row: without
            // dropping the copy every poll after the first would repeat the first.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting < count)
            {
                await Task.Delay(50);
            }
        }

        if (waiting < count)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            var states = await db.Database.SqlQueryRaw<string>(
                "SELECT coalesce(wait_event_type, '-') || '/' || coalesce(wait_event, '-') || '/' || coalesce(state, '-') "
                + "|| ': ' || left(query, 160) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND pid <> pg_backend_pid()").ToListAsync();
            waiting.ShouldBe(count, "each upload in the race must be parked on a lock before the next step; "
                + string.Join(" | ", states));
        }
    }

    /// <summary>Lets the parked upload go on.</summary>
    public Task ReleaseAsync() => transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await scope.DisposeAsync();
    }
}
