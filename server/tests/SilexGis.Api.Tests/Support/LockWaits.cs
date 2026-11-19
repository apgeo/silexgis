// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>Waits for a request under test to park on a lock another transaction holds.</summary>
public static class LockWaits
{
    /// <summary>
    /// Returns once a statement of this database waits on a lock, once <paramref name="pending"/>
    /// has answered, or after ten seconds — whichever comes first. The caller lets the holder go
    /// either way and asserts on the outcome: a request that answered without waiting was not
    /// held back by anything, which is what a test of a missing lock is there to show.
    /// </summary>
    public static async Task WaitAsync(SilexGisApiFactory factory, Task pending)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        for (var attempt = 0; attempt < 200 && !pending.IsCompleted; attempt++)
        {
            var waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50);
        }
    }
}
