// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Polls the notification outbox and drains it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the processing-job worker because the two queues want opposite things: a job is
/// one heavy task at a time whose failures are final, a notification is one small row per
/// recipient that should be retried. Sharing a worker would make a burst of membership mail stall
/// a raster conversion for minutes.
/// </para>
/// <para>
/// There is deliberately no startup sweep. The claim is a lease, so anything interrupted becomes
/// due again by itself — whereas a sweep like the job queue's would re-send every message that was
/// in flight when the process restarted.
/// </para>
/// </remarks>
public sealed class NotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private DateTimeOffset lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = configuration.GetValue("Notifications:PollSeconds", 15);
        if (pollSeconds <= 0)
        {
            // A real operator switch, and what keeps the test suite deterministic: every test
            // class shares one database, so a background drain would settle another class's rows.
            logger.LogInformation("Notification delivery is switched off (Notifications:PollSeconds <= 0)");
            return;
        }

        // Migrations run before the app serves, but hosted services start alongside them.
        await WaitForQueueAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Notification worker iteration failed; continuing");
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        while (await RunAsync(s => s.RunOnceAsync(ct)) > 0)
        {
        }

        while (await RunAsync(s => s.RunDigestAsync(ct)) > 0)
        {
        }

        if (DateTimeOffset.UtcNow - lastPrune < PruneInterval)
        {
            return;
        }

        lastPrune = DateTimeOffset.UtcNow;
        _ = await RunAsync(async s =>
        {
            await s.PruneAsync(ct);
            return 0;
        });
    }

    /// <summary>One scope per unit of work, so a long drain never holds one context open.</summary>
    private async Task<int> RunAsync(Func<NotificationOutboxService, Task<int>> work)
    {
        using var scope = scopeFactory.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<NotificationOutboxService>());
    }

    private async Task WaitForQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                _ = await db.NotificationOutbox.AnyAsync(ct);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}
