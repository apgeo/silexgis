// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// The vertical figures of one cave taken across all its entrances.
/// </summary>
/// <param name="EntranceCount">How many entrances contributed, with or without an altitude. This
/// counts what the caller may place, never what exists.</param>
/// <param name="EntrancesWithAltitude">How many of those carried a recorded altitude.</param>
/// <param name="HighestEntranceM">Altitude of the highest entrance, metres.</param>
/// <param name="LowestEntranceM">Altitude of the lowest entrance, metres.</param>
/// <param name="EntranceSpreadM">Highest entrance minus lowest — how much of the cave's relief is
/// already spent between its own doors.</param>
/// <param name="TotalDepthM">From the highest entrance down to the lowest point the survey reaches.
/// Zero when the survey never goes below that entrance; null when there is no survey with altitudes
/// or no entrance altitude to measure from.</param>
/// <param name="HeightAboveLowestEntranceM">From the lowest entrance up to the highest point the
/// survey reaches. Zero when the survey never rises above it; null on the same conditions.</param>
/// <param name="ResurgenceLevelM">Altitude water leaves the surrounding massif at, metres.</param>
/// <param name="PotentialDepthM">From the highest entrance down to the resurgence level: how deep
/// the cave could go if it were followed all the way to base level. Null when the resurgence level
/// is unknown or does not lie below the highest entrance.</param>
public sealed record CaveSystemFiguresSummary(
    int EntranceCount,
    int EntrancesWithAltitude,
    double? HighestEntranceM,
    double? LowestEntranceM,
    double? EntranceSpreadM,
    double? TotalDepthM,
    double? HeightAboveLowestEntranceM,
    double? ResurgenceLevelM,
    double? PotentialDepthM);

/// <summary>
/// Depth and height of a cave measured from its entrances and its survey, rather than from a typed
/// field. Survey altitudes and entrance altitudes are in one datum (the survey's), so the two can be
/// subtracted directly.
///
/// <para>
/// Pure arithmetic over numbers the caller has already gated: an altitude is a position, so the
/// caller passes only entrances the reader may place exactly, and counts nothing else.
/// </para>
/// </summary>
public static class CaveSystemFigures
{
    /// <param name="entranceAltitudesM">One altitude per placeable entrance that has one.</param>
    /// <param name="placeableEntranceCount">All placeable entrances, altitude or not.</param>
    /// <param name="surveyHighestZM">Highest point of the survey, or null with no altitudes.</param>
    /// <param name="surveyLowestZM">Lowest point of the survey, or null with no altitudes.</param>
    /// <param name="resurgenceLevelM">Lowest spring altitude of the area, or null when unknown.</param>
    public static CaveSystemFiguresSummary Compute(
        IReadOnlyCollection<double> entranceAltitudesM,
        int placeableEntranceCount,
        double? surveyHighestZM,
        double? surveyLowestZM,
        double? resurgenceLevelM)
    {
        ArgumentNullException.ThrowIfNull(entranceAltitudesM);

        var finite = entranceAltitudesM.Where(double.IsFinite).ToList();
        double? highest = finite.Count > 0 ? finite.Max() : null;
        double? lowest = finite.Count > 0 ? finite.Min() : null;

        // A negative result means the survey lies wholly on the wrong side of the entrance, which
        // is a datum or placement problem and not a negative depth; the figure is "none", zero.
        double? totalDepth = highest is { } top && surveyLowestZM is { } bottom
            ? Math.Max(0d, top - bottom)
            : null;
        double? height = lowest is { } door && surveyHighestZM is { } roof
            ? Math.Max(0d, roof - door)
            : null;

        double? potential = highest is { } entrance && resurgenceLevelM is { } level && level < entrance
            ? entrance - level
            : null;

        return new CaveSystemFiguresSummary(
            placeableEntranceCount,
            finite.Count,
            highest,
            lowest,
            highest is { } h && lowest is { } l ? h - l : null,
            totalDepth,
            height,
            resurgenceLevelM,
            potential);
    }
}
