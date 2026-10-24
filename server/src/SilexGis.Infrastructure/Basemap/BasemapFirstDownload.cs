// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>
/// Queues the download of the configured first region on a start that finds no extract and no
/// download ever asked for — and does nothing on any other start.
/// </summary>
/// <remarks>
/// Once, by construction: the job it queues is itself the record that a download was asked for, so
/// the next start finds it and stops, whether that download finished, failed or was called off.
/// Asked by nobody, so it notifies nobody; the administration page shows it like any other.
/// </remarks>
public sealed class BasemapFirstDownload(
    IServiceScopeFactory scopes,
    IOptions<BasemapOptions> options,
    ILogger<BasemapFirstDownload> logger) : IHostedService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var key = options.Value.InitialRegion?.Trim().ToLowerInvariant();
        if (!options.Value.SelfHosted || string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!SelfHostedBasemap.Presets.TryGetValue(key, out var region))
        {
            logger.LogWarning(
                "SILEXGIS__Basemap__InitialRegion names '{Region}', which is none of {Presets}; nothing is downloaded",
                key,
                string.Join(", ", SelfHostedBasemap.Presets.Keys));
            return;
        }

        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<BasemapStore>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        if (store.Current() is not null
            || await db.ProcessingJobs.AnyAsync(j => j.Kind == ProcessingJobKinds.BasemapExtract, cancellationToken))
        {
            return;
        }

        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.BasemapExtract,
            Payload = JsonSerializer.Serialize(
                new BasemapExtractPayload(region.West, region.South, region.East, region.North, SelfHostedBasemap.MaxZoom, null),
                Json),
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("The first basemap download, region {Region}, is queued", key);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
