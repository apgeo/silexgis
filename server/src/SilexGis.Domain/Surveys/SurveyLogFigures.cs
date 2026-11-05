// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Surveys;

/// <summary>
/// The figures a survey compiler printed about one run, as a page of results shows them: how much
/// was surveyed, how deep and how wide it reaches, how the loops close and how the run ended.
///
/// <para>
/// <b>Only sizes, never positions.</b> The compiler also prints where each extreme lies — the
/// station at the top and the bottom of the survey, and the absolute height, northing and easting of
/// each — and the stations every loop runs through. None of that is carried here: a range is kept
/// as its length alone, and a loop as its error and its size. A length and a closure error say how
/// big the cave is and how well it was surveyed; an altitude or a grid coordinate says where it is,
/// which is the one thing a page of results is careful not to say on its own account.
/// </para>
///
/// <para>
/// Every figure is null when the log did not state it: a run that stopped early never reaches the
/// statistics, and an absent figure is unknown, not zero.
/// </para>
/// </summary>
public sealed record SurveyLogFigures
{
    /// <summary>How the run ended, as its log says.</summary>
    public required SurveyCompilationOutcome Outcome { get; init; }

    /// <summary>The compiler's version, as it announced itself.</summary>
    public string? CompilerVersion { get; init; }

    /// <summary>How long the compiler said the run took, in seconds.</summary>
    public int? CompilationSeconds { get; init; }

    /// <summary>How many errors the compiler reported.</summary>
    public int ErrorCount { get; init; }

    /// <summary>How many warnings the compiler reported.</summary>
    public int WarningCount { get; init; }

    /// <summary>Survey stations in the compiled survey.</summary>
    public int? StationCount { get; init; }

    /// <summary>Shots joining them.</summary>
    public int? ShotCount { get; init; }

    /// <summary>Pieces of the survey not joined to each other.</summary>
    public int? ConnectedComponents { get; init; }

    /// <summary>Total length of the survey shots, in metres.</summary>
    public double? TotalLengthM { get; init; }

    /// <summary>The same length after the loops were adjusted, in metres.</summary>
    public double? TotalLengthAdjustedM { get; init; }

    /// <summary>Total length of the shots seen from above, in metres.</summary>
    public double? PlanLengthM { get; init; }

    /// <summary>Total vertical length of the shots, in metres.</summary>
    public double? VerticalLengthM { get; init; }

    /// <summary>The height between the survey's highest and lowest station, in metres.</summary>
    public double? VerticalRangeM { get; init; }

    /// <summary>How far the survey reaches north to south, in metres.</summary>
    public double? NorthSouthRangeM { get; init; }

    /// <summary>How far the survey reaches east to west, in metres.</summary>
    public double? EastWestRangeM { get; init; }

    /// <summary>How many loops the compiler said the survey has.</summary>
    public int? LoopCount { get; init; }

    /// <summary>The average relative loop error, as a percentage.</summary>
    public double? AverageLoopErrorPercent { get; init; }

    /// <summary>Each loop's closure, in the order the compiler listed them.</summary>
    public IReadOnlyList<SurveyLogLoop> Loops { get; init; } = [];
}

/// <summary>How one loop closes: its error and its size, and not the stations it runs through.</summary>
/// <param name="RelativeErrorPercent">The misclosure as a share of the loop's length.</param>
/// <param name="AbsoluteErrorM">The misclosure, in metres.</param>
/// <param name="LengthM">The loop's length, in metres.</param>
/// <param name="StationCount">How many stations it runs through.</param>
public sealed record SurveyLogLoop(double RelativeErrorPercent, double AbsoluteErrorM, double LengthM, int StationCount);
