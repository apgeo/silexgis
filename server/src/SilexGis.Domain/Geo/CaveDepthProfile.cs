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
/// <b>The survey's highest and lowest points are always on the curve.</b> The stations are evenly
/// spaced, and a point between two of them is otherwise not drawn; where the extreme of the survey
/// is such a point it is added as a reading of its own, so the curve may carry one or two readings
/// more than the overhead profile at the same distances along.
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

        // The stations are spaced evenly along the passage, and on a long survey the step is tens of
        // metres: a short terminal leg holding the deepest point can fall between two of them. So
        // the survey's own extremes are found at the ends of every segment, and each is added to
        // the curve as a reading of its own where the stations missed it. The deepest point of the
        // curve is then exactly the figure printed beside it, and that figure is the survey's.
        var points = stations
            .Select(s => (s.DistanceAlongM, s.PassageAltitudeM, s.PathIndex, s.SegmentIndex))
            .ToList();
        var (top, bottom) = SegmentEndExtremes(OverburdenProfile.InPassageOrder(segments));
        foreach (var extreme in new[] { top, bottom })
        {
            if (!points.Any(p => p.PassageAltitudeM == extreme.PassageAltitudeM))
            {
                points.Add(extreme);
            }
        }

        points.Sort(static (a, b) => a.DistanceAlongM.CompareTo(b.DistanceAlongM));

        var highest = points.Max(p => p.PassageAltitudeM);
        var lowest = points.Min(p => p.PassageAltitudeM);

        return new CaveDepthProfileResult(
            [.. points.Select(p => new CaveDepthProfileSample(
                p.DistanceAlongM,
                p.PassageAltitudeM,
                Math.Max(0, highest - p.PassageAltitudeM),
                p.PathIndex,
                p.SegmentIndex))],
            length,
            highest,
            lowest);
    }

    /// <summary>
    /// The highest and the lowest segment end, each with the distance along the passage it sits
    /// at, measured the way the stations measure it.
    /// </summary>
    private static (
        (double DistanceAlongM, double PassageAltitudeM, int? PathIndex, int SegmentIndex) Highest,
        (double DistanceAlongM, double PassageAltitudeM, int? PathIndex, int SegmentIndex) Lowest)
        SegmentEndExtremes(PassageSegment[] ordered)
    {
        var along = 0d;
        (double, double, int?, int) highest = (0, double.NegativeInfinity, null, 0);
        (double, double, int?, int) lowest = (0, double.PositiveInfinity, null, 0);
        foreach (var segment in ordered)
        {
            var length = segment.SlopeLengthM ?? segment.PlanLengthM;
            var mid = segment.MidZM ?? 0;
            var rise = segment.DeltaZM ?? 0;
            foreach (var (distance, altitude) in new[] { (along, mid - (rise / 2)), (along + length, mid + (rise / 2)) })
            {
                if (altitude > highest.Item2)
                {
                    highest = (distance, altitude, segment.PathIndex, segment.SegmentIndex);
                }

                if (altitude < lowest.Item2)
                {
                    lowest = (distance, altitude, segment.PathIndex, segment.SegmentIndex);
                }
            }

            along += length;
        }

        return (highest, lowest);
    }
}
