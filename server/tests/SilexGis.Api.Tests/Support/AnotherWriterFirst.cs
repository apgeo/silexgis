// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Puts a second writer inside a request's read-modify-write window, deterministically.
/// </summary>
/// <remarks>
/// A collision needs two writers who have both read a row before either saves, and a handler that
/// reads and saves inside one request leaves no seam to stand in. This makes one: another
/// connection changes the row and holds its transaction open, the request under test then reads the
/// row as it was (a read waits for nobody) and blocks on its own update, and once the database
/// reports a statement waiting on a lock the other writer commits. The request's update is then
/// evaluated against a row that is no longer the one it read, which is exactly the lost-update
/// case and not an approximation of it.
/// </remarks>
public static class AnotherWriterFirst
{
    public static async Task<HttpResponseMessage> SendAsync(
        SilexGisApiFactory factory, string otherWritersStatement, Func<Task<HttpResponseMessage>> request)
    {
        // The other writer is an ordinary context of the application's own, so it authenticates
        // exactly as the application does; the connection string a context hands back has its
        // password removed.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await db.Database.ExecuteSqlRawAsync(otherWritersStatement))
            .ShouldBe(1, "the other writer must change exactly the row under test");

        var pending = request();

        var waiting = 0L;
        for (var attempt = 0; attempt < 300 && waiting == 0; attempt++)
        {
            waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting == 0)
            {
                await Task.Delay(50);
            }
        }

        // A request parked behind the other writer is not always reported by the database as a
        // lock wait (one that waits on the trip's row lock before its first write is not, in some
        // hosts). A request that has not answered after the whole polling window while the other
        // writer holds its row is parked behind it; one that has answered never waited, and
        // nothing was raced.
        if (waiting == 0)
        {
            pending.IsCompleted.ShouldBeFalse(
                "the request answered without waiting for the other writer, so nothing was raced");
        }

        await transaction.CommitAsync();
        return await pending;
    }
}
