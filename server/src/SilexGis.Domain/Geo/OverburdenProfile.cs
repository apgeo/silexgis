// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One place on the passage a ground height is wanted at, and everything about that place which is
/// known before the terrain is consulted.
/// </summary>
/// <param name="DistanceAlongM">How far along the passage this station sits, metres, measured from
/// the start of the ordered line work along the real length of every piece before it. Duplicated
/// legs advance nothing: a leg the file marks as passage already surveyed is the same passage
/// walked again, so counting it would stretch the axis past the length the cave actually claims.</param>
/// <param name="Longitude">Where the station is, degrees.</param>
/// <param name="Latitude">Where the station is, degrees.</param>
/// <param name="PassageAltitudeM">Altitude of the passage at this station, metres, in the survey's
/// own vertical datum — interpolated along the piece of passage the station fell on.</param>
/// <param name="PathIndex">Which polyline of the line work the station fell on; null where the line
/// work is a network of legs rather than a set of ordered paths.</param>
/// <param name="SegmentIndex">Which piece of that line work the station fell on. Carried so a
/// reader hovering a point on the curve can be shown which piece of passage answered.</param>
public readonly record struct OverburdenStation(
    double DistanceAlongM,
    double Longitude,
    double Latitude,
    double PassageAltitudeM,
    int? PathIndex,
    int SegmentIndex);

/// <summary>
/// Where to ask how much rock is over a passage.
///
/// <para>
/// This is the whole of the arithmetic behind an overburden profile that does not involve terrain:
/// putting the line work in order, measuring along it, and choosing a bounded set of places to ask
/// about. The subtraction that turns a ground height into a thickness of rock happens where the
/// ground heights are read, because that is where the vertical datums are reconciled and there is
/// exactly one place that does that.
/// </para>
///
/// <para>
/// <b>The axis is cumulative passage length in the order the line work records it, not a traverse.</b>
/// A survey is a network and a reduced centerline is a set of polylines; neither is a single walk
/// from one end of the cave to the other, and no ordering of them could be. So the horizontal axis
/// says how much passage lies before this point in that order — which is what makes a profile
/// comparable with the cave's own length figure — and it does not claim that a caver could walk it.
/// </para>
///
/// <para>
/// <b>Altitudes are required.</b> A plan-only drawing records no third coordinate, so there is no
/// passage altitude to subtract a ground height from and the honest answer is that no profile can
/// be built — not a profile of zeros, which would draw every passage at sea level.
/// </para>
/// </summary>
public static class OverburdenProfile
{
    /// <summary>
    /// The most stations one profile will ever ask the ground about.
    /// </summary>
    /// <remarks>
    /// The elevation reader answers one batch at a time for the whole installation, so the length of
    /// one request is the length of everybody else's wait. A cave with ten thousand legs must
    /// therefore be drawn at a coarser step rather than sampled leg by leg. Four hundred points is
    /// more than a chart a few hundred pixels wide can distinguish, so the bound costs the reader
    /// nothing they could have seen.
    /// </remarks>
    public const int MaxStations = 400;

    /// <summary>
    /// The closest two stations are ever placed, metres.
    /// </summary>
    /// <remarks>
    /// A short cave does not need four hundred readings of the same hillside: below this step the
    /// samples fall inside one pixel of any elevation model this application prepares, so they would
    /// repeat one number and cost the shared reader four hundred waits to do it.
    /// </remarks>
    public const double MinStepM = 2.0;

    /// <summary>
    /// The stations to ask the ground about along this line work, and the passage length they were
    /// spread over.
    /// </summary>
    /// <remarks>
    /// Segments are taken in the order the substrate records them — by path, then by position within
    /// the path — because that is the only ordering the line work carries. Duplicated legs are
    /// dropped outright rather than merely skipped over, so the length reported here is the same
    /// figure the cave's own length statistic reports.
    /// </remarks>
    /// <param name="segments">The cave's measured line work.</param>
    /// <returns>The stations, in increasing distance along, and the total length they span. Empty
    /// with a length of zero when the line work carries no altitudes, has no extent, or is empty.</returns>
    public static (IReadOnlyList<OverburdenStation> Stations, double PassageLengthM) Stations(
        IReadOnlyList<PassageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var ordered = segments
            .Where(s => !s.IsDuplicate && s.MidZM is not null)
            .OrderBy(s => s.PathIndex ?? int.MinValue)
            .ThenBy(s => s.SegmentIndex)
            .ToArray();

        // Real length along the passage where the altitudes allow it to be known, so that a steep
        // shaft is not drawn as the few metres of ground its shadow covers.
        var lengths = ordered.Select(s => s.SlopeLengthM ?? s.PlanLengthM).ToArray();
        var total = lengths.Sum();

        if (ordered.Length == 0 || total <= 0)
        {
            return ([], 0);
        }

        var count = StationCount(total);
        var step = total / (count - 1);

        var stations = new List<OverburdenStation>(count);
        var segment = 0;
        var consumed = 0d;

        for (var i = 0; i < count; i++)
        {
            // The last station sits exactly on the end of the passage rather than on a multiple of
            // the step, which floating-point accumulation would leave a hair short of it.
            var distance = i == count - 1 ? total : i * step;

            while (segment < ordered.Length - 1 && distance >= consumed + lengths[segment])
            {
                consumed += lengths[segment];
                segment++;
            }

            var length = lengths[segment];
            var fraction = length > 0 ? Math.Clamp((distance - consumed) / length, 0, 1) : 0;
            stations.Add(StationOn(ordered[segment], fraction, distance));
        }

        return (stations, total);
    }

    /// <summary>
    /// How many stations to spread over a passage of this length: enough to draw it, never more than
    /// the shared elevation reader should be asked for in one go, and never so close together that
    /// two of them read the same pixel.
    /// </summary>
    private static int StationCount(double passageLengthM)
    {
        var byStep = (int)Math.Floor(passageLengthM / MinStepM) + 1;
        return Math.Clamp(byStep, 2, MaxStations);
    }

    /// <summary>
    /// A station a given fraction of the way along one piece of passage.
    /// </summary>
    /// <remarks>
    /// The altitude is interpolated from the piece's midpoint and its rise, which are the two
    /// vertical figures a measured segment carries; its ends follow from them. Position is
    /// interpolated straight in degrees rather than along the spheroid — a surveyed leg is tens of
    /// metres long, over which the difference between the two is far below the ground resolution of
    /// any elevation model this reads.
    /// </remarks>
    private static OverburdenStation StationOn(PassageSegment segment, double fraction, double distanceAlongM)
    {
        var mid = segment.MidZM ?? 0;
        var rise = segment.DeltaZM ?? 0;

        return new OverburdenStation(
            distanceAlongM,
            segment.FromLongitude + ((segment.ToLongitude - segment.FromLongitude) * fraction),
            segment.FromLatitude + ((segment.ToLatitude - segment.FromLatitude) * fraction),
            mid + ((fraction - 0.5) * rise),
            segment.PathIndex,
            segment.SegmentIndex);
    }
}
