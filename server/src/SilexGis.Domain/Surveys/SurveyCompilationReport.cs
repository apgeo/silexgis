// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Surveys;

/// <summary>
/// How a compilation of a survey ended. Stored as a smallint and never renumbered: the value is
/// part of the schema contract, so a new member takes the next free number and nothing is reused.
/// </summary>
public enum SurveyCompilationOutcome : short
{
    /// <summary>The compiler ran to the end and said nothing about the survey.</summary>
    Succeeded = 0,

    /// <summary>The compiler ran to the end and had remarks about the survey.</summary>
    SucceededWithWarnings = 1,

    /// <summary>
    /// The compiler stopped, or reported an error, or never got as far as saying how long it took.
    /// A failed compilation is a quality signal of its own: it is not a survey with no loops, and
    /// the two must never be recorded the same way.
    /// </summary>
    Failed = 2,
}

/// <summary>
/// One row of the compiler's own <c>loop errors</c> table: how far a closed loop failed to close.
///
/// <para>
/// The two error measures answer different questions and neither substitutes for the other.
/// <see cref="AbsoluteErrorM"/> is a <em>distance</em> — how far apart the two arrivals at the
/// closing station are. <see cref="RelativeErrorPercent"/> is a <em>ratio</em> — that distance as a
/// percentage of the loop's own length, which is what says whether the distance is large for a loop
/// of this size. A short loop can be much the worse ratio while being much the smaller distance, so
/// a reader shown one of them alone is being told the wrong thing about half the loops. Both are
/// carried, and nothing ranks by one while labelling it the other.
/// </para>
///
/// <para>
/// The vocabulary is the compiler's, deliberately: its table prints REL-ERR, ABS-ERR, TOTAL-L, STS
/// and the three axis errors, so a figure here and a figure in the operator's own log are
/// recognisably the same number. Lengths are metres; the relative error is a percentage, so 71.92
/// means 71.92 %, not 0.7192.
/// </para>
/// </summary>
public sealed record SurveyLoopError
{
    /// <summary>
    /// The loop's position in the compiler's own table, from zero. The table's rows carry no
    /// identity of their own — two loops can agree in every printed number and differ only in which
    /// stations they run through — so the order the compiler printed them in is what is preserved.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>REL-ERR: the closing distance as a percentage of the loop's length.</summary>
    public required double RelativeErrorPercent { get; init; }

    /// <summary>ABS-ERR: the closing distance itself, in metres.</summary>
    public required double AbsoluteErrorM { get; init; }

    /// <summary>TOTAL-L: the length of the loop, in metres.</summary>
    public required double TotalLengthM { get; init; }

    /// <summary>STS: how many stations the loop runs through.</summary>
    public required int StationCount { get; init; }

    /// <summary>X-ERROR: the east–west component of the closing distance, in metres.</summary>
    public required double ErrorXM { get; init; }

    /// <summary>Y-ERROR: the north–south component of the closing distance, in metres.</summary>
    public required double ErrorYM { get; init; }

    /// <summary>Z-ERROR: the vertical component of the closing distance, in metres.</summary>
    public required double ErrorZM { get; init; }

    /// <summary>
    /// The loop's station sequence exactly as the compiler printed it between its brackets — a
    /// chain of survey-qualified station names joined by " - ". Kept as one string rather than split
    /// because it is the compiler's own text and nothing here can re-derive it; it names stations
    /// inside a survey, so it travels only where the survey itself travels.
    /// </summary>
    public required string Stations { get; init; }
}

/// <summary>
/// What one compilation of a survey reported: how it ended, what it ran as, and how well each of
/// the survey's loops closed.
///
/// <para>
/// This is evidence of a single run, not a property of the survey. The same survey compiled again
/// tomorrow — at a different compiler version, after a correction — produces different figures, and
/// the run they came from has to stay attached to them or a reader cannot tell which compilation
/// they are looking at.
/// </para>
/// </summary>
public sealed record SurveyCompilationReport
{
    /// <summary>How the run ended.</summary>
    public required SurveyCompilationOutcome Outcome { get; init; }

    /// <summary>The compiler's version string, as it announced itself.</summary>
    public string? CompilerVersion { get; init; }

    /// <summary>
    /// The release date the compiler announced for itself. This is not when the run happened — a log
    /// carries no run timestamp at all, so when a compilation took place can only come from when its
    /// log was archived here.
    /// </summary>
    public string? CompilerReleaseDate { get; init; }

    /// <summary>
    /// The step the run died in, and null unless <see cref="Outcome"/> is
    /// <see cref="SurveyCompilationOutcome.Failed"/>. A completed run can leave a step looking
    /// unfinished for reasons that are not failures, so the qualification is applied here rather
    /// than left for every reader to remember.
    /// </summary>
    public string? IncompleteStage { get; init; }

    /// <summary>How long the compiler said the run took, in seconds; null when it never said.</summary>
    public int? CompilationSeconds { get; init; }

    /// <summary>How many errors the compiler reported.</summary>
    public int ErrorCount { get; init; }

    /// <summary>How many warnings the compiler reported.</summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// How many loops the compiler said the survey has. This is not the same number as the length of
    /// <see cref="LoopErrors"/>: the count comes from the survey statistics and the table from a
    /// later block, so a run that stopped in between reports the count and no rows.
    /// </summary>
    public int? LoopCount { get; init; }

    /// <summary>The average relative loop error the compiler reported, as a percentage.</summary>
    public double? AverageLoopErrorPercent { get; init; }

    /// <summary>The total surveyed length the compiler reported, in metres.</summary>
    public double? TotalLengthM { get; init; }

    /// <summary>
    /// The total length after the compiler distributed the loop errors around the network, in
    /// metres. Its difference from <see cref="TotalLengthM"/> is how much the adjustment moved.
    /// </summary>
    public double? TotalLengthAdjustedM { get; init; }

    /// <summary>The loop-error table, in the order the compiler printed it.</summary>
    public IReadOnlyList<SurveyLoopError> LoopErrors { get; init; } = [];
}
