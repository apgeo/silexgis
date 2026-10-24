// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Basemap;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Downloads a region of a planet build as the installation's own basemap: works out its size,
/// checks it fits, cuts it, and makes it the one served.
/// </summary>
/// <remarks>
/// <para>
/// A job on the long-work lane, because a country is minutes of downloading and the general queue
/// would wait behind it. One runs at a time: the route that asks for one refuses while another is
/// queued or running, and the lane takes one job at a time whatever is queued.
/// </para>
/// <para>
/// Nothing is served until the download has finished: it is written under a name the tile server
/// does not read and renamed when complete. A download that fails or is called off leaves the
/// extract that was being served exactly as it was.
/// </para>
/// <para>
/// Not retried: a download someone called off must stay called off, and one that failed because
/// it did not fit would fail again.
/// </para>
/// </remarks>
public sealed class BasemapExtractHandler(
    BasemapStore store,
    IBasemapExtractor extractor,
    IBasemapBuilds builds,
    BasemapCancellation cancellation,
    IJobProgress progress,
    IOptions<BasemapOptions> options,
    TimeProvider clock,
    ILogger<BasemapExtractHandler> logger) : IProcessingJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// What the disk must have free beyond the extract itself: the archive's directories and the
    /// tool's own working room, and a margin so that a full disk is not the way a download ends.
    /// </summary>
    private const long Headroom = 256L * 1024 * 1024;

    public string Kind => ProcessingJobKinds.BasemapExtract;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BasemapExtractPayload>(job.Payload, Json)
            ?? throw new BasemapException(SelfHostedBasemap.Codes.RegionInvalid, "the download names no region");
        if (!store.SelfHosted)
        {
            throw new BasemapException(SelfHostedBasemap.Codes.SelfHostingOff, "this installation does not serve a basemap of its own");
        }

        if (SelfHostedBasemap.RegionProblem(payload.Region) is { } problem)
        {
            throw new BasemapException(SelfHostedBasemap.Codes.RegionInvalid, problem);
        }

        if (!SelfHostedBasemap.IsZoom(payload.MaxZoom))
        {
            throw new BasemapException(SelfHostedBasemap.Codes.ZoomInvalid, "the last level is outside what a build holds");
        }

        var calledOff = cancellation.Register(job.Id);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, calledOff);
        string? partial = null;
        try
        {
            var build = payload.Build ?? await LatestAsync(linked.Token);
            if (!SelfHostedBasemap.IsBuild(build))
            {
                throw new BasemapException(SelfHostedBasemap.Codes.BuildInvalid, "the build is not written as its date");
            }

            // Recorded as soon as it is known, so the page says which build "the latest" was.
            job.Payload = JsonSerializer.Serialize(payload with { Build = build }, Json);
            var source = options.Value.SourceUrlTemplate.Replace("{build}", build, StringComparison.Ordinal);

            var estimate = await extractor.EstimateAsync(source, payload.Region, payload.MaxZoom, linked.Token);
            if (estimate > options.Value.MaxBytes)
            {
                throw new BasemapException(
                    SelfHostedBasemap.Codes.TooLarge,
                    $"the extract would be {estimate} bytes and this installation accepts at most {options.Value.MaxBytes}");
            }

            if (store.FreeBytes() is { } free && free < estimate + Headroom)
            {
                throw new BasemapException(
                    SelfHostedBasemap.Codes.DiskFull,
                    $"the extract would be {estimate} bytes and the disk has {free} free");
            }

            var extractedAt = clock.GetUtcNow();
            var fileName = SelfHostedBasemap.FileName(build, extractedAt);
            partial = store.PartialPath(fileName);
            await progress.ReportAsync(job.Id, 0, 100, ct);
            // Never 100 before the file is in place: the page reads 100 as done.
            await extractor.ExtractAsync(
                source,
                payload.Region,
                payload.MaxZoom,
                partial,
                percent => progress.ReportAsync(job.Id, Math.Min(percent, 99), 100, CancellationToken.None),
                linked.Token);

            var size = new FileInfo(partial).Length;
            store.Publish(
                partial,
                fileName,
                new BasemapExtractRecord(build, extractedAt, payload.Region, payload.MaxZoom, size, job.RequestedBy));
            partial = null;
            await progress.ReportAsync(job.Id, 100, 100, ct);
            logger.LogInformation(
                "Basemap extract {File} ({Size} bytes) of build {Build} is now served", fileName, size, build);
        }
        catch (OperationCanceledException) when (calledOff.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            BasemapStore.Discard(partial);
            throw new BasemapException(SelfHostedBasemap.Codes.Cancelled, "the download was called off");
        }
        catch
        {
            BasemapStore.Discard(partial);
            throw;
        }
        finally
        {
            cancellation.Forget(job.Id);
        }
    }

    private async Task<string> LatestAsync(CancellationToken ct) =>
        SelfHostedBasemap.Latest((await builds.ListAsync(ct)).Select(b => b.Build))
        ?? throw new BasemapException(SelfHostedBasemap.Codes.BuildNotFound, "the source lists no build");
}
