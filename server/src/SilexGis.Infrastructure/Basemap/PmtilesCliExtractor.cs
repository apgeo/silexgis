// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Map;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>
/// Cuts a region out of a planet build with the <c>pmtiles</c> command-line tool, which reads only
/// the byte ranges the region needs: a country is a few hundred requests, not a planet's download.
/// </summary>
/// <remarks>
/// <para>
/// Started with an argument list and never through a shell, and with nothing in it a person typed:
/// the source is the configured address with a date put in, the box four numbers and the zoom one,
/// all checked before they get here, and the output a path this installation chose. The tool is a
/// single static program pinned by version and checksum in the image that runs it.
/// </para>
/// <para>
/// Its progress is read from what it prints while it works, because the archive it writes is laid
/// out at its full size from the start and its size says nothing about how far it has got.
/// </para>
/// </remarks>
public sealed class PmtilesCliExtractor(IOptions<BasemapOptions> options, ILogger<PmtilesCliExtractor> logger)
    : IBasemapExtractor
{
    /// <summary>How long working out a size may take: it reads the build's directories, not its tiles.</summary>
    private static readonly TimeSpan EstimateLimit = TimeSpan.FromMinutes(2);

    /// <summary>How much of the tool's output is kept to report a failure with, from its end.</summary>
    private const int KeptOutput = 8192;

    public async Task<long> EstimateAsync(string source, BasemapRegion region, int maxZoom, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(EstimateLimit);
        string output;
        try
        {
            output = await RunAsync(source, region, maxZoom, "--dry-run", null, _ => Task.CompletedTask, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BasemapException(SelfHostedBasemap.Codes.SourceUnreachable, "the source took too long to answer");
        }

        return SelfHostedBasemap.ArchiveSize(output)
            ?? throw new BasemapException(SelfHostedBasemap.Codes.ExtractFailed, "the extractor reported no size");
    }

    public async Task ExtractAsync(
        string source, BasemapRegion region, int maxZoom, string outputPath, Func<int, Task> progress, CancellationToken ct)
    {
        await RunAsync(source, region, maxZoom, null, outputPath, progress, ct);
    }

    private async Task<string> RunAsync(
        string source,
        BasemapRegion region,
        int maxZoom,
        string? dryRun,
        string? outputPath,
        Func<int, Task> progress,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo(options.Value.ExtractorPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("extract");
        start.ArgumentList.Add(source);
        // A dry run writes nothing; the tool still wants a name for what it would write.
        start.ArgumentList.Add(outputPath ?? Path.Combine(Path.GetTempPath(), "silexgis-basemap-estimate.pmtiles"));
        start.ArgumentList.Add(string.Create(
            CultureInfo.InvariantCulture, $"--bbox={region.West},{region.South},{region.East},{region.North}"));
        start.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"--maxzoom={maxZoom}"));
        if (dryRun is not null)
        {
            start.ArgumentList.Add(dryRun);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new BasemapException(
                SelfHostedBasemap.Codes.ExtractFailed, $"the extractor '{start.FileName}' could not be started: {e.Message}");
        }

        var kept = new StringBuilder();
        var lastPercent = -1;
        // Both streams are kept for a failure's report; progress is read from the output stream
        // alone, where the tool writes its log and its bar, so two readers never report at once.
        async Task Read(StreamReader reader, bool reportsProgress)
        {
            var buffer = new char[1024];
            int read;
            while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
            {
                var chunk = new string(buffer, 0, read);
                lock (kept)
                {
                    kept.Append(chunk);
                    if (kept.Length > KeptOutput * 2)
                    {
                        kept.Remove(0, kept.Length - KeptOutput);
                    }
                }

                if (reportsProgress && SelfHostedBasemap.ProgressPercent(chunk) is { } percent && percent != lastPercent)
                {
                    lastPercent = percent;
                    await progress(percent);
                }
            }
        }

        var reading = Task.WhenAll(Read(process.StandardOutput, reportsProgress: true), Read(process.StandardError, reportsProgress: false));
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            throw;
        }

        await reading;
        var output = kept.ToString();
        if (process.ExitCode != 0)
        {
            var lastLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            logger.LogWarning("Basemap extractor exited with {ExitCode}: {Output}", process.ExitCode, lastLine);
            throw new BasemapException(
                SelfHostedBasemap.Codes.ExtractFailed,
                $"the extractor stopped with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {lastLine}");
        }

        return output;
    }
}
