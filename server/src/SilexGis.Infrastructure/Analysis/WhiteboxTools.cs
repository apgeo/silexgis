// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>What one argument of a run is.</summary>
public enum WhiteboxArgumentKind
{
    /// <summary>A number, such as a search distance.</summary>
    Value = 0,

    /// <summary>A file the run reads, by its name inside the run's directory.</summary>
    Input = 1,

    /// <summary>A file the run writes, by its name inside the run's directory.</summary>
    Output = 2,

    /// <summary>A switch with no value, such as <c>--forms</c>.</summary>
    Flag = 3,
}

/// <summary>One <c>--name=value</c> argument of a run.</summary>
/// <param name="Name">The program's own parameter name, such as <c>dem</c> or <c>search</c>.</param>
/// <param name="Kind">Whether it is a number, a file read, a file written or a switch.</param>
/// <param name="Value">The number written invariantly, or the file's name; null for a switch.</param>
public sealed record WhiteboxArgument(string Name, WhiteboxArgumentKind Kind, string? Value = null)
{
    public static WhiteboxArgument Number(string name, double value) =>
        new(name, WhiteboxArgumentKind.Value, value.ToString("R", CultureInfo.InvariantCulture));

    public static WhiteboxArgument Number(string name, int value) =>
        new(name, WhiteboxArgumentKind.Value, value.ToString(CultureInfo.InvariantCulture));

    public static WhiteboxArgument Input(string name, string file) => new(name, WhiteboxArgumentKind.Input, file);

    public static WhiteboxArgument Output(string name, string file) => new(name, WhiteboxArgumentKind.Output, file);

    public static WhiteboxArgument Switch(string name) => new(name, WhiteboxArgumentKind.Flag);
}

/// <summary>One run of one tool over files in a directory.</summary>
/// <param name="WorkDirectory">The directory the inputs are in and the outputs are written to; nothing else is read or written.</param>
/// <param name="Tool">The tool, by WhiteboxTools' own name, from the tools the application runs (<see cref="WhiteboxRequestRules.Tools"/>).</param>
/// <param name="Arguments">The tool's arguments; files by their names inside <paramref name="WorkDirectory"/>.</param>
/// <param name="Timeout">How long the run may take before it is stopped.</param>
public sealed record WhiteboxRunRequest(
    string WorkDirectory, string Tool, IReadOnlyList<WhiteboxArgument> Arguments, TimeSpan Timeout);

/// <summary>How a run ended.</summary>
/// <param name="Succeeded">The program exited with success and wrote every output it was asked for.</param>
/// <param name="ExitCode">The program's exit code, through the sandbox, or -1 when it never ran.</param>
/// <param name="OutputFiles">The outputs that exist after the run, by name, each an ordinary file within the limits.</param>
/// <param name="OutputTail">The end of what the program printed, for the administrator's log only: it can name paths.</param>
public sealed record WhiteboxRunResult(bool Succeeded, int ExitCode, IReadOnlyList<string> OutputFiles, string OutputTail);

/// <summary>
/// Runs WhiteboxTools — locally inside a sandbox, through the analysis service, or not at all, as
/// the installation chose. Callers hand it files in a directory and read files back; it never sees
/// the database, and what goes in is elevation rasters only, never a cave's position.
/// </summary>
public interface IWhiteboxTools
{
    /// <summary>The installation's choice: <c>none</c>, <c>local</c> or <c>service</c>.</summary>
    string Mode { get; }

    /// <summary>Whether a run can be attempted at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Why runs cannot be attempted, as a stable code — <c>analysis.not_configured</c>,
    /// <c>analysis.tool_missing</c> or <c>analysis.sandbox_missing</c> — or null when they can.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>The program and its version, such as <c>WhiteboxTools 2.4.0</c>, or null when not known.</summary>
    string? Version { get; }

    /// <summary>
    /// Runs one tool. Throws <see cref="WhiteboxUnavailableException"/> when the program cannot be
    /// run at all, <see cref="TimeoutException"/> when the run passed its time limit, and
    /// <see cref="ArgumentException"/> for a request the rules refuse.
    /// </summary>
    Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct);
}

/// <summary>The program cannot be run: a fact about the installation, not about the input — a retry or a configuration change recovers from it.</summary>
public sealed class WhiteboxUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The stable codes of <see cref="IWhiteboxTools.UnavailableReason"/>.</summary>
public static class WhiteboxUnavailable
{
    public const string NotConfigured = "analysis.not_configured";
    public const string ToolMissing = "analysis.tool_missing";
    public const string SandboxMissing = "analysis.sandbox_missing";
}

/// <summary>The installation chose not to run WhiteboxTools.</summary>
public sealed class NoWhiteboxTools : IWhiteboxTools
{
    public string Mode => WhiteboxOptions.ModeNone;

    public bool IsAvailable => false;

    public string? UnavailableReason => WhiteboxUnavailable.NotConfigured;

    public string? Version => null;

    public Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct) =>
        throw new WhiteboxUnavailableException("This installation does not run WhiteboxTools.");
}

/// <summary>
/// What a run may ask for — checked by the application before a run and again by the analysis
/// service on receipt, because the service must not trust that its caller checked.
/// </summary>
/// <remarks>
/// WhiteboxTools holds several hundred tools and runs any program in its plugins directory by name,
/// two of which download and install software. Only the tools the application has an analysis for
/// may run, each with only its own parameters, of their own kinds: the program reads its global
/// options by prefix (anything beginning <c>--cd</c> or <c>--wd</c> changes its working directory
/// and rewrites its settings file), so a parameter name outside a tool's list could reach one. A
/// file is one plain name inside the run's directory, never a path, so no argument can point the
/// program at anything else on the machine; a value is a number, so no argument can carry a second
/// option inside it.
/// </remarks>
public static partial class WhiteboxRequestRules
{
    /// <summary>
    /// The tools the application runs, by WhiteboxTools' own name, each with its parameters and
    /// what kind each is. A tool is added here together with the analysis that needs it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, WhiteboxArgumentKind>> Tools =
        new Dictionary<string, IReadOnlyDictionary<string, WhiteboxArgumentKind>>(StringComparer.Ordinal)
        {
            // Landform classes (Jasiewicz and Stepinski's geomorphons): the elevation model in,
            // one class per cell out; distances in cells, the flatness threshold in degrees.
            ["Geomorphons"] = new Dictionary<string, WhiteboxArgumentKind>(StringComparer.Ordinal)
            {
                ["dem"] = WhiteboxArgumentKind.Input,
                ["output"] = WhiteboxArgumentKind.Output,
                ["search"] = WhiteboxArgumentKind.Value,
                ["threshold"] = WhiteboxArgumentKind.Value,
                ["fdist"] = WhiteboxArgumentKind.Value,
                ["skip"] = WhiteboxArgumentKind.Value,
                ["forms"] = WhiteboxArgumentKind.Flag,
                ["residuals"] = WhiteboxArgumentKind.Flag,
            },
        };

    /// <summary>The most arguments one run may carry.</summary>
    public const int MaxArguments = 32;

    /// <summary>Why the request is refused, or null when it may run.</summary>
    public static string? Problem(string? tool, IReadOnlyList<WhiteboxArgument>? arguments)
    {
        if (tool is null || !Tools.TryGetValue(tool, out var parameters))
        {
            return $"'{tool}' is not a tool this application runs.";
        }

        if (arguments is null || arguments.Count == 0 || arguments.Count > MaxArguments)
        {
            return $"A run carries between 1 and {MaxArguments} arguments.";
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputs = 0;
        foreach (var argument in arguments)
        {
            if (argument is null || !ParameterName().IsMatch(argument.Name ?? "") || !names.Add(argument.Name!))
            {
                return $"'{argument?.Name}' is not a parameter name, or is given twice.";
            }

            if (!parameters.TryGetValue(argument.Name!, out var kind) || kind != argument.Kind)
            {
                return $"{tool} has no {argument.Kind.ToString().ToLowerInvariant()} parameter '{argument.Name}'.";
            }

            switch (argument.Kind)
            {
                case WhiteboxArgumentKind.Value:
                    if (!NumberText().IsMatch(argument.Value ?? "")
                        || !double.TryParse(argument.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        || !double.IsFinite(number))
                    {
                        return $"The value of '{argument.Name}' is not a number.";
                    }

                    break;
                case WhiteboxArgumentKind.Input:
                case WhiteboxArgumentKind.Output:
                    if (FileNameProblem(argument.Value) is { } fileProblem)
                    {
                        return $"'{argument.Name}': {fileProblem}";
                    }

                    if (!files.Add(argument.Value!))
                    {
                        return $"'{argument.Value}' is named by two arguments.";
                    }

                    outputs += argument.Kind == WhiteboxArgumentKind.Output ? 1 : 0;
                    break;
                case WhiteboxArgumentKind.Flag:
                    if (argument.Value is not null)
                    {
                        return $"The switch '{argument.Name}' takes no value.";
                    }

                    break;
                default:
                    return $"'{argument.Name}' is of no known kind.";
            }
        }

        return outputs == 0 ? "A run writes at least one output." : null;
    }

    /// <summary>Why <paramref name="name"/> is not a plain file name inside a run's directory, or null when it is.</summary>
    public static string? FileNameProblem(string? name) =>
        name is not null && FileName().IsMatch(name) && !name.Contains("..", StringComparison.Ordinal)
            ? null
            : $"'{name}' is not a plain file name.";

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"^[-+]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][-+]?[0-9]+)?$")]
    private static partial Regex NumberText();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex FileName();
}
