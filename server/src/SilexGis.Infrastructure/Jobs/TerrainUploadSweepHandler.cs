// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Deletes rasters sent for a terrain build that no build names and that nobody came back for.
/// </summary>
/// <remarks>
/// <para>
/// The build form sends a raster the moment it is chosen, so somebody who picks a few files and
/// then leaves the page has put them on the server without submitting anything. They sit beside
/// the builds rather than inside one, so deleting a build never reaches them either. Without this
/// they are permanent.
/// </para>
/// <para>
/// A raster any build names is kept, whatever that build's state: a finished build's record says
/// what it was made from, and a stopped one can be run again and will read it. Deleting the build
/// is what releases its uploads, and this pass takes them once they have aged past the setting.
/// </para>
/// <para>
/// The list of named rasters is read, and the files removed, while holding the lock every
/// submission takes. A submission checks that the rasters it names are there and then writes the
/// build naming them; without the lock an old upload could pass that check, be removed here, and
/// leave a build naming a file that is gone — which surfaces minutes later as a failed build
/// rather than now as a refused request.
/// </para>
/// </remarks>
public sealed class TerrainUploadSweepHandler(
    SilexGisDbContext db,
    TerrainUploads uploads,
    IOptions<TerrainBuildOptions> options,
    ILogger<TerrainUploadSweepHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.TerrainUploadSweep;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - Retention(options.Value);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TerrainBuildSql.TakeSubmissionLockAsync(db, ct);

        var named = await db.TerrainBuildSources.AsNoTracking()
            .Where(s => s.Kind == TerrainBuildSourceKind.Uploaded)
            .Select(s => s.Reference)
            .Distinct()
            .ToListAsync(ct);

        var removed = uploads.SweepAbandoned(cutoff, named.ToHashSet(StringComparer.Ordinal));
        await transaction.CommitAsync(ct);

        if (removed > 0)
        {
            logger.LogInformation(
                "Removed {Count} terrain upload(s) no build names, sent before {Cutoff}", removed, cutoff);
        }
    }

    /// <summary>The setting, brought inside an hour to a year so that 0 never means "at once".</summary>
    public static TimeSpan Retention(TerrainBuildOptions settings) =>
        TimeSpan.FromHours(Math.Clamp(settings.UploadRetentionHours, 1, 8760));
}

/// <summary>
/// Queues the periodic collection of terrain uploads nobody used.
/// </summary>
/// <remarks>
/// Scheduled rather than triggered, because the event it answers is somebody <em>not</em>
/// submitting a build — and nothing raises an event for that. Hourly, comfortably shorter than the
/// shortest retention the setting allows. Nothing is queued at startup, so a restart loop cannot
/// fill the queue with sweeps.
/// </remarks>
public sealed class TerrainUploadSweepScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<TerrainUploadSweepScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await QueuePassAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A scheduler that dies takes the schedule with it for the process's lifetime.
                logger.LogError(e, "Could not queue the terrain upload sweep; will retry next tick");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task QueuePassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var pending = await db.ProcessingJobs.AnyAsync(
            j => j.Kind == ProcessingJobKinds.TerrainUploadSweep
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running),
            ct);
        if (pending)
        {
            return;
        }

        // No requester: a scheduled sweep notifies nobody.
        db.ProcessingJobs.Add(new ProcessingJob { Kind = ProcessingJobKinds.TerrainUploadSweep });
        await db.SaveChangesAsync(ct);
    }
}
