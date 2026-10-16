// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>How a running job says how far it has got.</summary>
public interface IJobProgress
{
    /// <summary>
    /// Records that <paramref name="done"/> of <paramref name="total"/> units of a job's work are
    /// done. Cheap to call for every unit: it is written at most once a second per job, and always
    /// for the last unit.
    /// </summary>
    Task ReportAsync(long jobId, int done, int total, CancellationToken ct);
}

/// <summary>
/// Writes progress through a connection of its own, never through the context the handler is
/// working in: that context may be inside the job's transaction and holding the job's half-done
/// changes, and saving it to say "120 of 400" would commit or expose them.
/// </summary>
public sealed class JobProgress(IServiceScopeFactory scopes, TimeProvider clock) : IJobProgress
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<long, DateTimeOffset> lastWritten = new();

    public async Task ReportAsync(long jobId, int done, int total, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var last = done >= total;
        if (!last && lastWritten.TryGetValue(jobId, out var previous) && now - previous < MinimumInterval)
        {
            return;
        }

        if (last)
        {
            lastWritten.TryRemove(jobId, out _);
        }
        else
        {
            lastWritten[jobId] = now;
            // A job that fails before its last unit leaves its entry behind; a few long-gone ones
            // are cleared now and then so the table never grows with the queue's history.
            if (lastWritten.Count > 256)
            {
                foreach (var (id, at) in lastWritten)
                {
                    if (now - at > TimeSpan.FromHours(1))
                    {
                        lastWritten.TryRemove(id, out _);
                    }
                }
            }
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await JobsSql.ReportProgressAsync(db, jobId, done, total, ct);
    }
}
