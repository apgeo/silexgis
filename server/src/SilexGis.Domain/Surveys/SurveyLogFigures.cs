// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Surveys;

/// <summary>
/// The figures a survey compiler printed about one run, as a page of results shows them: how much
/// was surveyed, how deep and how wide it reaches and between which stations, how the loops close,
/// where the survey sits in the coordinate system it was computed in, and how the run ended.
///
/// <para>
/// <b>Everything the log states, positions included.</b> The compiler prints the station at each
/// extreme of the survey with its absolute height, northing or easting, and the stations every loop
/// runs through; they are carried here as the log gives them. Who may be shown them is not decided
/// here but by whoever hands these figures out: a survey upload's own routes give them only to a
/// reader who may already see the cave's exact position, or to the holder of the upload's public
/// link — who has been handed the compiled model and the log themselves, which state the same
/// things.
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

    /// <summary>The coordinate system the compiler wrote its output in, as the log names it.</summary>
    public string? OutputCoordinateSystem { get; init; }

    /// <summary>The meridian convergence the compiler applied, in degrees.</summary>
    public double? MeridianConvergenceDeg { get; init; }

    /// <summary>The magnetic declinations the compiler used, each with the date it was taken for.</summary>
    public IReadOnlyList<SurveyLogDeclination> Declinations { get; init; } = [];

    /// <summary>
    /// True when the survey has no fixed point of its own and the compiler invented one: its
    /// heights and grid positions are then relative to an arbitrary zero, not the world's.
    /// </summary>
    public bool HasNoControlPoints { get; init; }

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

    /// <summary>The height between the survey's highest and lowest station, and which stations they are.</summary>
    public SurveyLogExtent? VerticalRange { get; init; }

    /// <summary>How far the survey reaches north to south, and between which stations.</summary>
    public SurveyLogExtent? NorthSouthRange { get; init; }

    /// <summary>How far the survey reaches east to west, and between which stations.</summary>
    public SurveyLogExtent? EastWestRange { get; init; }

    /// <summary>How many loops the compiler said the survey has.</summary>
    public int? LoopCount { get; init; }

    /// <summary>The average relative loop error, as a percentage.</summary>
    public double? AverageLoopErrorPercent { get; init; }

    /// <summary>Each loop's closure, in the order the compiler listed them.</summary>
    public IReadOnlyList<SurveyLogLoop> Loops { get; init; } = [];
}

/// <summary>
/// One extent of the survey: its length, and the station at each end with its value on that axis
/// — an absolute height for the vertical range, a northing or an easting for the other two, in the
/// coordinate system the compiler worked in.
/// </summary>
/// <param name="LengthM">The extent, in metres.</param>
/// <param name="FromStation">The station at one end, as the compiler names it.</param>
/// <param name="FromValueM">That station's height, northing or easting.</param>
/// <param name="ToStation">The station at the other end.</param>
/// <param name="ToValueM">That station's height, northing or easting.</param>
public sealed record SurveyLogExtent(double LengthM, string FromStation, double FromValueM, string ToStation, double ToValueM);

/// <summary>How one loop closes: its error, its size and the stations it runs through.</summary>
/// <param name="RelativeErrorPercent">The misclosure as a share of the loop's length.</param>
/// <param name="AbsoluteErrorM">The misclosure, in metres.</param>
/// <param name="LengthM">The loop's length, in metres.</param>
/// <param name="StationCount">How many stations it runs through.</param>
/// <param name="ErrorXM">The misclosure's east component, in metres.</param>
/// <param name="ErrorYM">Its north component.</param>
/// <param name="ErrorZM">Its vertical component.</param>
/// <param name="Stations">The stations the loop runs through, as the compiler lists them.</param>
public sealed record SurveyLogLoop(
    double RelativeErrorPercent,
    double AbsoluteErrorM,
    double LengthM,
    int StationCount,
    double ErrorXM,
    double ErrorYM,
    double ErrorZM,
    string Stations);

/// <summary>A magnetic declination the compiler used.</summary>
/// <param name="Date">The date it was taken for, as the log writes it.</param>
/// <param name="Degrees">The declination, in degrees.</param>
public sealed record SurveyLogDeclination(string Date, double Degrees);
