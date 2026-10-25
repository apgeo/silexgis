// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Therion.Build;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Compiles uploaded surveys with a Therion installed on this machine, inside the sandbox the
/// runner applies. Refuses to compile at all when the sandbox program is missing, unless the
/// installation allowed running without it.
/// </summary>
public sealed class LocalSurveyCompiler : ISurveyCompiler
{
    /// <summary>How much of the compiler's printed output is handed back for the status row.</summary>
    public const int OutputTailLength = 4096;

    /// <summary>The most of a log read back; Therion's log of a large cave is a few hundred kilobytes.</summary>
    private const int MaxLogBytes = 8 * 1024 * 1024;

    private readonly SandboxedTherionRunner? runner;
    private readonly Lazy<string?> version;

    public LocalSurveyCompiler(IOptions<SurveyCompilerOptions> options, ILogger<LocalSurveyCompiler> logger)
    {
        var settings = options.Value;
        var therion = ExecutableSearch.Resolve(settings.TherionPath, "therion");
        var bwrap = OperatingSystem.IsLinux() ? ExecutableSearch.Resolve(settings.BwrapPath, "bwrap") : null;

        if (therion is null)
        {
            logger.LogError("The survey compiler is set to local mode, but no Therion program was found; uploads are not compiled");
        }
        else if (bwrap is null && !settings.AllowUnsandboxed)
        {
            logger.LogError(
                "The survey compiler is set to local mode, but the sandbox program (bubblewrap) was not found; "
                + "uploads are not compiled. Install bubblewrap, or allow unsandboxed compiling explicitly");
        }
        else
        {
            if (bwrap is null)
            {
                logger.LogWarning(
                    "The survey compiler runs Therion WITHOUT a sandbox, as this installation allowed: an uploaded "
                    + "survey can make it read any file this server can read");
            }

            runner = new SandboxedTherionRunner(therion, bwrap, settings);
        }

        version = new Lazy<string?>(() => therion is null ? null : SandboxedTherionRunner.ProbeVersion(therion));
    }

    public bool IsAvailable => runner is not null;

    public string? Version => IsAvailable ? version.Value : null;

    public async Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct)
    {
        if (runner is null)
        {
            throw new InvalidOperationException("This installation's local survey compiler cannot run; ask IsAvailable first.");
        }

        var tail = new OutputTail(OutputTailLength);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout);

        TherionRun run;
        try
        {
            run = await runner.RunAsync(
                request.ProjectDirectory,
                request.ConfigFileName,
                request.Timeout,
                line =>
                {
                    tail.Add(line);
                    if (StageOf(line) is { } stage)
                    {
                        progress.Report(stage);
                    }
                },
                deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Therion did not finish within {request.Timeout.TotalSeconds:0} seconds.");
        }

        var log = run.LogPath is null ? "" : await ReadHeadAsync(run.LogPath, ct);
        var written = log.Length == 0
            ? []
            : TherionLogParser.Parse(log).OutputFiles.Where(f => f.Completed).Select(f => Relative(request.ProjectDirectory, f.Path)).ToList();

        return new SurveyCompileResult(run.ExitCode == 0, run.ExitCode, log, written, tail.Text);
    }

    // Therion's own step words, turned into a stage and a share of the compile. The printed line
    // itself is never handed on: it can name stations, files and, in an error, a coordinate.
    private static SurveyCompileProgress? StageOf(string line)
    {
        var text = line.TrimStart();
        return text.StartsWith("reading", StringComparison.Ordinal) ? new SurveyCompileProgress("reading the survey", 10)
            : text.StartsWith("calculating", StringComparison.Ordinal) || text.StartsWith("processing", StringComparison.Ordinal)
                ? new SurveyCompileProgress("calculating the survey", 40)
            : text.StartsWith("writing", StringComparison.Ordinal) || text.StartsWith("making", StringComparison.Ordinal)
                ? new SurveyCompileProgress("writing the outputs", 75)
            : null;
    }

    private static string Relative(string projectDirectory, string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(projectDirectory, path);
        return Path.GetRelativePath(Path.GetFullPath(projectDirectory), Path.GetFullPath(full)).Replace('\\', '/');
    }

    private static async Task<string> ReadHeadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[(int)Math.Min(stream.Length, MaxLogBytes)];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>The last lines printed, up to a length, kept as they arrive from two readers at once.</summary>
    private sealed class OutputTail(int length)
    {
        private readonly Queue<string> lines = new();
        private int size;

        public void Add(string line)
        {
            lock (lines)
            {
                lines.Enqueue(line);
                size += line.Length + 1;
                while (size > length && lines.Count > 1)
                {
                    size -= lines.Dequeue().Length + 1;
                }
            }
        }

        public string Text
        {
            get
            {
                lock (lines)
                {
                    var text = string.Join('\n', lines);
                    return text.Length <= length ? text : text[^length..];
                }
            }
        }
    }
}
