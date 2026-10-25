// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Surveys;

/// <summary>What a survey compiler is asked to compile.</summary>
/// <param name="ProjectDirectory">The run's own copy of the project: the sources and the configuration written around them.</param>
/// <param name="ConfigFileName">The compiler configuration inside <paramref name="ProjectDirectory"/>.</param>
/// <param name="Timeout">How long the compile may take before it is stopped.</param>
public sealed record SurveyCompileRequest(string ProjectDirectory, string ConfigFileName, TimeSpan Timeout);

/// <summary>What a compile produced.</summary>
/// <param name="Succeeded">Whether the compiler finished and said the survey compiled.</param>
/// <param name="ExitCode">The compiler's exit code.</param>
/// <param name="LogText">The compiler's own log.</param>
/// <param name="OutputFiles">The files the compiler wrote, relative to the project directory.</param>
/// <param name="OutputTail">The end of what the compiler printed, for the readers allowed to see it.</param>
public sealed record SurveyCompileResult(
    bool Succeeded, int ExitCode, string LogText, IReadOnlyList<string> OutputFiles, string OutputTail);

/// <summary>Where a running compile has got to, in the compiler's own words, and how far along.</summary>
public sealed record SurveyCompileProgress(string Stage, int Percent);

/// <summary>
/// What compiles a survey uploaded from a field device into models and drawings.
/// </summary>
/// <remarks>
/// A seam rather than a class because where the compiler runs is the installation's choice: not at
/// all, in a container of its own beside the application, or as a program on the same machine —
/// and a compiler fed a survey a phone sent is a program running over untrusted input, so which of
/// those an installation trusts is not this code's to decide. Every installation that has not
/// chosen gets the one that compiles nothing.
/// </remarks>
public interface ISurveyCompiler
{
    /// <summary>Whether this installation can compile at all.</summary>
    bool IsAvailable { get; }

    /// <summary>The compiler and its version, such as <c>therion 6.3.4</c>, or null when there is none.</summary>
    string? Version { get; }

    Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct);
}

/// <summary>
/// The compiler of an installation that has none: uploads are archived and their archive read, and
/// the run says plainly that nothing was compiled.
/// </summary>
public sealed class NoSurveyCompiler : ISurveyCompiler
{
    public bool IsAvailable => false;

    public string? Version => null;

    public Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct) =>
        throw new InvalidOperationException("This installation has no survey compiler; ask IsAvailable first.");
}
