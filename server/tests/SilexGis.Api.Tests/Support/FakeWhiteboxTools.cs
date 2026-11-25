// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Analysis;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Stands in for WhiteboxTools: holds a request to the same rules, writes each output it is asked
/// for as a text naming the tool and the inputs it read, and remembers what the run directory held.
/// </summary>
internal sealed class FakeWhiteboxTools : IWhiteboxTools
{
    public const string FakeVersion = "WhiteboxTools 0.0-fake";

    /// <summary>What the run directory held when the last run started, by name.</summary>
    public IReadOnlyList<string> LastRunFiles { get; private set; } = [];

    /// <summary>The run directory of the last run.</summary>
    public string? LastRunDirectory { get; private set; }

    /// <summary>When set, each run waits this long first (and can be cancelled while it waits).</summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>When set, each run reports this exit code and writes nothing.</summary>
    public int? FailWith { get; init; }

    public string Mode => WhiteboxOptions.ModeLocal;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public string? Version => FakeVersion;

    public async Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct)
    {
        if (WhiteboxRequestRules.Problem(request.Tool, request.Arguments) is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        LastRunDirectory = request.WorkDirectory;
        LastRunFiles = Directory.EnumerateFiles(request.WorkDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(request.WorkDirectory, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (Delay is { } delay)
        {
            // Held to the run's own limit, as the real program is.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(request.Timeout);
            try
            {
                await Task.Delay(delay, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("The fake run passed its time limit.");
            }
        }

        if (FailWith is { } code)
        {
            return new WhiteboxRunResult(false, code, [], $"fake failure in {request.WorkDirectory}/x.tif");
        }

        var inputs = request.Arguments.Where(a => a.Kind == WhiteboxArgumentKind.Input)
            .Select(a => File.ReadAllText(Path.Combine(request.WorkDirectory, a.Value!)));
        var outputs = request.Arguments.Where(a => a.Kind == WhiteboxArgumentKind.Output).Select(a => a.Value!).ToList();
        foreach (var output in outputs)
        {
            await File.WriteAllTextAsync(
                Path.Combine(request.WorkDirectory, output), $"{request.Tool}({string.Join(",", inputs)})", ct);
        }

        return new WhiteboxRunResult(true, 0, outputs, $"wrote into {request.WorkDirectory}/{outputs[0]}");
    }
}
