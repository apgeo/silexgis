// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Waits until the database reports requests parked on a lock that a test's own transaction
/// holds, and fails the test when they never are.
/// </summary>
/// <remarks>
/// A race test holds a row (or an advisory lock) in a transaction of its own, sends the request,
/// and lets go only once the request is waiting on it. Waiting a fixed time instead — or polling
/// and falling through when nothing is seen — lets a request that arrived late, or answered before
/// it ever reached the lock, run after the holder committed: it then meets the finished write by
/// an ordinary sequential check and the test passes with no serialisation in the code at all.
/// The wait has to be seen, and asserted, before the holder commits.
/// </remarks>
public static class LockWaits
{
    /// <summary>
    /// Polls until <paramref name="count"/> other sessions of this database are waiting on a lock.
    /// Fails as soon as <paramref name="pending"/> completes first (the request answered without
    /// waiting), or when the deadline passes with fewer waiters than asked for.
    /// </summary>
    /// <param name="db">A context inside the transaction that holds the lock.</param>
    /// <param name="count">How many sessions must be parked at once.</param>
    /// <param name="pending">The request(s) in flight; for several, pass <c>Task.WhenAny</c> of them.</param>
    /// <param name="what">Names what was expected to wait, for the failure message.</param>
    public static async Task UntilParkedAsync(
        SilexGisDbContext db, int count, Task pending, string what = "the request")
    {
        // Short of the request's own 30-second command timeout, so a request that sits on the lock
        // is reported as unseen by this wait rather than as a 500 from its own timeout.
        var deadline = DateTime.UtcNow.AddSeconds(25);
        var waiting = 0L;
        while (true)
        {
            if (pending.IsCompleted)
            {
                var answered = pending is Task<HttpResponseMessage> { IsCompletedSuccessfully: true } response
                    ? $" with {(int)(await response).StatusCode}"
                    : string.Empty;
                waiting.ShouldBe(count,
                    $"{what} answered{answered} before it was seen waiting on the lock this test holds, so nothing was raced");
            }

            // pg_stat_activity is read once per transaction and answered from that copy until it
            // ends, and this connection is inside the transaction holding the lock: without dropping
            // the copy every poll would repeat the first picture, taken before the request arrived.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting >= count || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(50);
        }

        if (waiting < count)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            var states = await db.Database.SqlQueryRaw<string>(
                "SELECT coalesce(wait_event_type, '-') || '/' || coalesce(wait_event, '-') || '/' || coalesce(state, '-') "
                + "|| ': ' || left(query, 160) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND pid <> pg_backend_pid()").ToListAsync();
            waiting.ShouldBe(count,
                $"{what} must be parked on the lock this test holds before it lets go; sessions: "
                + string.Join(" | ", states));
        }
    }

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
