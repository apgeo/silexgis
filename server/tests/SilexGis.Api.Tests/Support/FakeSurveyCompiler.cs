// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A survey compiler for tests: it says it is there, and when asked to compile writes every output
/// the project's configuration names (except those it is told to leave out) with invented content,
/// and a log shaped as Therion writes one, saying so.
/// </summary>
/// <param name="logSaysFailed">Whether the log it writes reports an error, as a survey Therion cannot compile gets.</param>
/// <param name="leaveOut">Output paths, as the configuration names them, it does not write.</param>
internal sealed partial class FakeSurveyCompiler(bool logSaysFailed = false, params string[] leaveOut) : ISurveyCompiler
{
    public const string FakeVersion = "fake-compiler 1.0";

    private int calls;

    public bool IsAvailable => true;

    public string? Version => FakeVersion;

    /// <summary>
    /// The coordinate system its log says the outputs were written in, as Therion names it; null
    /// for a log of a survey fixed to no position, which names none.
    /// </summary>
    public string? OutputCoordinateSystem { get; init; } = "UTM35";

    /// <summary>The word every output it writes starts with, so two compilers' outputs differ.</summary>
    public string Word { get; init; } = "invented";

    /// <summary>How many compiles were asked of it.</summary>
    public int Calls => calls;

    /// <summary>Every file of the last project it was given, by its path relative to the project, with its text.</summary>
    public IReadOnlyDictionary<string, string> LastProject { get; private set; } = new Dictionary<string, string>();

    public async Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        LastProject = Directory.EnumerateFiles(request.ProjectDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(request.ProjectDirectory, f).Replace('\\', '/'),
                f => File.ReadAllText(f));

        progress.Report(new SurveyCompileProgress("writing the outputs", 75));
        var config = await File.ReadAllTextAsync(Path.Combine(request.ProjectDirectory, request.ConfigFileName), ct);
        var log = new StringBuilder("therion 6.3.4 (2025-04-08)\nreading source files ... done\n");
        if (OutputCoordinateSystem is { } cs)
        {
            log.Append("output coordinate system: ").Append(cs).Append('\n');
        }

        var written = new List<string>();
        foreach (Match output in OutputOption().Matches(config))
        {
            var relative = output.Groups["path"].Value;
            if (leaveOut.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            var path = Path.Combine([request.ProjectDirectory, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"{Word} {relative}", ct);
            log.Append("writing ").Append(relative).Append(" ... done\n");
            written.Add(relative);
        }

        if (logSaysFailed)
        {
            log.Append("error: invented failure -- an invented reason\n");
        }

        log.Append("compilation time: 1 sec\n");
        await File.WriteAllTextAsync(Path.Combine(request.ProjectDirectory, "therion.log"), log.ToString(), ct);
        return new SurveyCompileResult(!logSaysFailed, logSaysFailed ? 1 : 0, log.ToString(), written, "invented tail");
    }

    [GeneratedRegex("""-o\s+"(?<path>[^"]+)"|-o\s+(?<path>\S+)""")]
    private static partial Regex OutputOption();
}
