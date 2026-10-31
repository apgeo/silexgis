// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One reading on a cave's descent profile.
/// </summary>
/// <param name="DistanceAlongM">How much passage lies before this point in the recorded order of the
/// line work, metres. Duplicated legs advance nothing.</param>
/// <param name="AltitudeM">Altitude of the passage here, in the survey's own datum.</param>
/// <param name="DepthM">How far below the highest point the survey reaches this point lies, metres.
/// Never negative.</param>
/// <param name="PathIndex">Which polyline of the line work the point fell on, when it is a set of
/// ordered paths.</param>
/// <param name="SegmentIndex">Which piece of that line work the point fell on.</param>
public readonly record struct CaveDepthProfileSample(
    double DistanceAlongM, double AltitudeM, double DepthM, int? PathIndex, int SegmentIndex);

/// <summary>
/// A cave's descent profile: depth below its highest surveyed point against distance along its
/// passages. Carries no horizontal position at all.
/// </summary>
/// <param name="Samples">The readings, in increasing distance along. Empty when the line work
/// carries no altitudes.</param>
/// <param name="PassageLengthM">The length they were spread over.</param>
/// <param name="HighestM">Highest altitude the survey reaches, or null without altitudes.</param>
/// <param name="LowestM">Lowest altitude the survey reaches, or null without altitudes.</param>
public sealed record CaveDepthProfileResult(
    IReadOnlyList<CaveDepthProfileSample> Samples,
    double PassageLengthM,
    double? HighestM,
    double? LowestM)
{
    /// <summary>Depth of the deepest point the survey reaches below its highest, metres.</summary>
    public double? DeepestM => HighestM is { } high && LowestM is { } low ? high - low : null;
}

/// <summary>
/// The descent profile of a cave, drawn from the same stations the rock-overhead profile asks the
/// ground about, so that the two curves of one cave share an axis.
///
/// <para>
/// <b>The axis is cumulative passage length in recorded order, not a traverse</b> — the reasoning
/// is the overhead profile's: a survey is a network and no ordering of it is a walk from one end of
/// the cave to the other. The curve therefore reads as "how deep the passage is, piece by piece",
/// and a branch that climbs back shows as a rise rather than being folded away.
/// </para>
/// <para>
/// <b>No altitudes means no profile</b>, never a profile of zeros: a plan drawing is not a flat
/// cave, and a curve at sea level would be indistinguishable from a real one.
/// </para>
/// </summary>
public static class CaveDepthProfile
{
    /// <summary>The profile of this line work.</summary>
    public static CaveDepthProfileResult Build(IReadOnlyList<PassageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var (stations, length) = OverburdenProfile.Stations(segments);
        if (stations.Count == 0)
        {
            return new CaveDepthProfileResult([], 0, null, null);
        }

        // The extremes come from the stations the curve is drawn through, so that the deepest point
        // of the curve is exactly the figure printed beside it. The survey's own extremes (which
        // the statistics route reports) can lie between two stations of a long leg.
        var highest = stations.Max(s => s.PassageAltitudeM);
        var lowest = stations.Min(s => s.PassageAltitudeM);

        return new CaveDepthProfileResult(
            [.. stations.Select(s => new CaveDepthProfileSample(
                s.DistanceAlongM,
                s.PassageAltitudeM,
                Math.Max(0, highest - s.PassageAltitudeM),
                s.PathIndex,
                s.SegmentIndex))],
            length,
            highest,
            lowest);
    }
}
