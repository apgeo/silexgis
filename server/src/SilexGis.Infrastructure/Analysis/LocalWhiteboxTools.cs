// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// Runs WhiteboxTools as a child of this process, inside the sandbox <see cref="SandboxedWhiteboxRunner"/>
/// draws. The program and bubblewrap are looked for once, at start; when either is missing the
/// installation's log says which, and runs are refused as "not available" rather than attempted.
/// The analysis service runs this same class inside its container.
/// </summary>
public sealed class LocalWhiteboxTools : IWhiteboxTools
{
    private readonly WhiteboxOptions settings;
    private readonly SandboxedWhiteboxRunner? runner;
    private readonly Lazy<string?> version;

    public LocalWhiteboxTools(IOptions<WhiteboxOptions> options, ILogger<LocalWhiteboxTools> logger)
    {
        settings = options.Value;
        var program = ExecutableSearch.Resolve(settings.WhiteboxPath, SandboxedWhiteboxRunner.ProgramName);
        var bwrap = OperatingSystem.IsLinux() ? ExecutableSearch.Resolve(settings.BwrapPath, "bwrap") : null;

        if (program is null)
        {
            UnavailableReason = WhiteboxUnavailable.ToolMissing;
            logger.LogError(
                "WhiteboxTools was not found ({Where}); the analyses that need it are not available.",
                string.IsNullOrWhiteSpace(settings.WhiteboxPath) ? "no whitebox_tools on the search path" : "the configured path does not exist");
        }
        else if (bwrap is null && !settings.AllowUnsandboxed)
        {
            UnavailableReason = WhiteboxUnavailable.SandboxMissing;
            logger.LogError(
                "WhiteboxTools is installed but bubblewrap is not, and running it without a sandbox was not allowed "
                + "(SILEXGIS__Analysis__Whitebox__AllowUnsandboxed); the analyses that need it are not available.");
        }
        else
        {
            if (bwrap is null)
            {
                logger.LogWarning("WhiteboxTools runs without a sandbox: only its limits and an emptied environment bound it.");
            }

            runner = new SandboxedWhiteboxRunner(program, bwrap, settings);
        }

        version = new Lazy<string?>(() => program is null ? null : SandboxedWhiteboxRunner.ProbeVersion(program));
    }

    public string Mode => WhiteboxOptions.ModeLocal;

    public bool IsAvailable => runner is not null;

    public string? UnavailableReason { get; }

    public string? Version => IsAvailable ? version.Value : null;

    public async Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct)
    {
        if (runner is null)
        {
            throw new WhiteboxUnavailableException("WhiteboxTools is not available on this server.");
        }

        if (WhiteboxRequestRules.Problem(request.Tool, request.Arguments) is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        var run = Path.GetFullPath(request.WorkDirectory);
        if (!Directory.Exists(run))
        {
            throw new ArgumentException("The run directory does not exist.", nameof(request));
        }

        foreach (var argument in request.Arguments)
        {
            var file = argument.Kind is WhiteboxArgumentKind.Input or WhiteboxArgumentKind.Output
                ? new FileInfo(Path.Combine(run, argument.Value!))
                : null;
            if (argument.Kind == WhiteboxArgumentKind.Input && (file is not { Exists: true } || file.LinkTarget is not null))
            {
                throw new ArgumentException($"The input '{argument.Value}' is not an ordinary file in the run directory.", nameof(request));
            }

            if (argument.Kind == WhiteboxArgumentKind.Output && (file!.Exists || Directory.Exists(file.FullName)))
            {
                throw new ArgumentException($"The output '{argument.Value}' is already there.", nameof(request));
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout);
        int exitCode;
        string tail;
        try
        {
            (exitCode, tail) = await runner.RunAsync(run, request.Tool, request.Arguments, request.Timeout, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"WhiteboxTools did not finish within {request.Timeout.TotalSeconds:0} seconds and was stopped.");
        }

        var outputs = Outputs(run, request.Arguments, settings);
        var asked = request.Arguments.Count(a => a.Kind == WhiteboxArgumentKind.Output);
        return new WhiteboxRunResult(exitCode == 0 && outputs.Count == asked, exitCode, outputs, tail);
    }

    /// <summary>
    /// The outputs a run was asked for that it left as ordinary files within the limits. Anything
    /// else written under an output's name — a link, a directory, a file over the limit, or more than
    /// the run's whole budget — is read as the work of a program that misbehaved: removed and not
    /// reported, so nothing downstream reads it.
    /// </summary>
    internal static IReadOnlyList<string> Outputs(string run, IReadOnlyList<WhiteboxArgument> arguments, WhiteboxOptions settings)
    {
        var kept = new List<string>();
        long total = 0;
        foreach (var name in arguments.Where(a => a.Kind == WhiteboxArgumentKind.Output).Select(a => a.Value!))
        {
            var path = Path.Combine(run, name);
            var file = new FileInfo(path);
            if (file.LinkTarget is not null || Directory.Exists(path))
            {
                Remove(path);
                continue;
            }

            if (!file.Exists)
            {
                continue;
            }

            total += file.Length;
            if (file.Length > settings.OutputFileCap || total > settings.OutputCap)
            {
                Remove(path);
                continue;
            }

            kept.Add(name);
        }

        return kept;
    }

    private static void Remove(string path)
    {
        try
        {
            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The caller discards the whole run directory; what could not be removed here goes then.
        }
    }
}
