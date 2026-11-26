// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>
/// Looks after the render nodes' lane, which no in-process worker runs: a render whose node
/// stopped answering goes back to the queue when its lease runs out (or fails, after its last
/// try), one its requester stopped while nobody was there to hear it ends, and a film whose keeping
/// time is over is deleted.
/// </summary>
/// <remarks>
/// The same statement the in-process workers take back their own lanes with, over the nodes'
/// lane: a lane every worker filters by its own predicate would otherwise have nobody to take its
/// jobs back. Switched off with the job workers (<c>Jobs:PollSeconds</c> ≤ 0), so a test host that
/// drives the queue itself has no second hand in it.
/// </remarks>
public sealed class RenderNodeSweeper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RenderNodeSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobs = configuration.GetSection(JobQueueOptions.SectionName).Get<JobQueueOptions>() ?? new JobQueueOptions();
        if (jobs.PollSeconds <= 0)
        {
            return;
        }

        // Every half minute at least: a node's lease is two minutes, so a lost render is queued
        // again within about two and a half.
        var every = TimeSpan.FromSeconds(jobs.SweepSeconds > 0 ? Math.Min(jobs.SweepSeconds, 30) : 30);
        using var timer = new PeriodicTimer(every);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The table may not exist yet while migrations run at start; the next pass tries again.
                logger.LogWarning(e, "The render nodes' sweep failed; it runs again shortly");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass. Public so a test can drive it.</summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var renders = scope.ServiceProvider.GetRequiredService<FlybyRenders>();
        var cancelled = await renders.EndCancelledAsync(ct);
        var failed = await renders.ReturnStaleAsync(ct);
        var purged = await renders.PurgeExpiredAsync(ct);
        if (cancelled + failed + purged > 0)
        {
            logger.LogInformation(
                "Render nodes' sweep: {Cancelled} stopped, {Failed} failed after their last try, {Purged} deleted",
                cancelled, failed, purged);
        }
    }
}
