// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Basemap;

namespace SilexGis.Api.Tests;

/// <summary>
/// The extracting program, driven through a stand-in that prints the way the real tool does: a
/// progress figure many times over, far more than a pipe holds.
/// </summary>
public sealed class PmtilesCliExtractorTests : IDisposable
{
    private static readonly BasemapRegion Region = new(22.2, 46.2, 23.4, 47.0);

    private readonly string directory = Path.Combine(
        TestScratch.Root, "pmtiles-stand-in-" + Guid.NewGuid().ToString("N")[..8]);

    public PmtilesCliExtractorTests() => Directory.CreateDirectory(directory);

    /// <summary>
    /// Reporting progress is a database write, and a write can fail. When it does, the tool's output
    /// must still be read to the end — otherwise the tool blocks on a full pipe, never exits, and the
    /// download holds the one-at-a-time lane for good. The extract itself finishes.
    /// </summary>
    [Fact]
    public async Task A_failing_progress_report_does_not_stop_the_output_being_read()
    {
        var extractor = new PmtilesCliExtractor(
            Options.Create(new BasemapOptions { ExtractorPath = StandIn(lines: 20_000) }),
            NullLogger<PmtilesCliExtractor>.Instance);
        var reports = 0;

        var extract = extractor.ExtractAsync(
            "https://example.invalid/20261024.pmtiles",
            Region,
            12,
            Path.Combine(directory, "out.pmtiles"),
            _ =>
            {
                Interlocked.Increment(ref reports);
                throw new InvalidOperationException("the progress write failed");
            },
            TestContext.Current.CancellationToken);

        await Should.NotThrowAsync(() => extract.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        reports.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A program that prints <paramref name="lines"/> progress lines and exits 0 — a shell script
    /// where there is a shell, a batch file on Windows (which starts one without a shell of ours).
    /// </summary>
    private string StandIn(int lines)
    {
        const string padding = "|████████████████████████████████████████| fetching chunks";
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(directory, "pmtiles.cmd");
            File.WriteAllText(
                batch,
                $"@echo off\r\nfor /L %%i in (1,1,{lines}) do echo %%i%% {padding}\r\nexit /b 0\r\n",
                Encoding.ASCII);
            return batch;
        }

        var script = Path.Combine(directory, "pmtiles");
        File.WriteAllText(
            script,
            $"#!/bin/sh\ni=0\nwhile [ $i -lt {lines} ]; do echo \"$((i % 100))% {padding}\"; i=$((i + 1)); done\nexit 0\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
