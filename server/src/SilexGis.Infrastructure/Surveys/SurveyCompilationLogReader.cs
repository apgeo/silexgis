// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Surveys;
using Therion.Build;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Reads what a survey compiler printed while it ran into this application's own shape.
///
/// <para>
/// Pure by design, in the same way the reader for the compiled formats is: text in, a report out,
/// nothing read from disk and nothing written to the database. Reading the archived bytes and
/// storing the result belong to the job that calls this.
/// </para>
///
/// <para>
/// The log format is not parsed here. It belongs to the library that owns the compiler's other
/// formats, is parsed there against that project's own corpus of real logs, and is consumed here as
/// a dependency — a second parser for one format is two places for one truth to be wrong in, and
/// this project has already paid for that mistake once. What this type owns is the translation:
/// the library's vocabulary is its own, and letting it out of here would make the shape of somebody
/// else's model the shape of this application's records.
/// </para>
///
/// <para>
/// Two qualifications are applied here rather than left to every caller to remember. Text that is
/// not a compilation log at all is refused, because the parser answers an empty string with a
/// report that reads as a failed compilation — and "nobody supplied a log" is a different claim
/// from "the compilation failed". And the step a run died in is reported only for a run that
/// actually failed, because a successful run can leave a step looking unfinished for reasons that
/// are not failures.
/// </para>
/// </summary>
public sealed class SurveyCompilationLogReader
{
    /// <summary>
    /// Whether this text is a compilation log. The file name is a hint and not the answer: a log
    /// announces itself in its first lines, and the name only decides whether the weaker markers
    /// further down are trusted.
    /// </summary>
    public bool LooksLikeCompilationLog(string? fileName, string? text) =>
        TherionLogParser.LooksLikeTherionLog(fileName, text);

    /// <summary>
    /// Reads one compilation log.
    /// </summary>
    /// <exception cref="SurveySourceException">
    /// The text is not a compilation log. Refused rather than reported as a failed compilation:
    /// the parser cannot tell those apart and the difference is the whole point of storing this.
    /// </exception>
    public SurveyCompilationReport Read(string? fileName, string? text)
    {
        if (!LooksLikeCompilationLog(fileName, text))
        {
            throw new SurveySourceException("This file is not a survey compilation log.");
        }

        var summary = TherionLogParser.Parse(text);

        // Mapped member by member rather than cast: a stored outcome means whatever this
        // application's enum meant on the day it was written, and a library that renumbers its own
        // vocabulary must not be able to change the meaning of rows already in the database.
        var outcome = summary.Outcome switch
        {
            TherionLogOutcome.Success => SurveyCompilationOutcome.Succeeded,
            TherionLogOutcome.SuccessWithWarnings => SurveyCompilationOutcome.SucceededWithWarnings,
            _ => SurveyCompilationOutcome.Failed,
        };

        return new SurveyCompilationReport
        {
            Outcome = outcome,
            CompilerVersion = summary.TherionVersion,
            CompilerReleaseDate = summary.TherionReleaseDate,
            IncompleteStage = outcome == SurveyCompilationOutcome.Failed ? summary.IncompleteStage : null,
            CompilationSeconds = summary.CompilationTimeSeconds,
            ErrorCount = summary.Errors.Count(),
            WarningCount = summary.Warnings.Count(),
            LoopCount = summary.LoopCount,
            AverageLoopErrorPercent = summary.AverageLoopErrorPercent,
            TotalLengthM = summary.TotalLength,
            TotalLengthAdjustedM = summary.TotalLengthAdjusted,
            LoopErrors = [.. summary.LoopErrors.Select((row, index) => new SurveyLoopError
            {
                Ordinal = index,
                RelativeErrorPercent = row.RelativeErrorPercent,
                AbsoluteErrorM = row.AbsoluteError,
                TotalLengthM = row.TotalLength,
                StationCount = row.StationCount,
                ErrorXM = row.ErrorX,
                ErrorYM = row.ErrorY,
                ErrorZM = row.ErrorZ,
                Stations = row.Stations.Trim(),
            })],
        };
    }
}
