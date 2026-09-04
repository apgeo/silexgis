// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One set of wall distances measured at one station, as the cross-section arithmetic sees it.
/// </summary>
/// <remarks>
/// A station may carry more than one of these. Two legs meeting at a station each measure its
/// walls and they need not agree, and neither reading is the correction of the other — so they
/// arrive here as they were recorded and are collapsed by the arithmetic rather than by whoever
/// stored them.
/// </remarks>
/// <param name="StationName">Which station these walls belong to. Readings sharing a name are
/// readings of the same walls.</param>
/// <param name="LeftM">Distance to the left wall, metres, or null where it was not measured. Zero
/// is a measurement — a station hard against the wall — and is not the same statement as null.</param>
/// <param name="RightM">Distance to the right wall, metres, or null where it was not measured.</param>
/// <param name="UpM">Distance to the ceiling, metres, or null where it was not measured.</param>
/// <param name="DownM">Distance to the floor, metres, or null where it was not measured.</param>
/// <param name="ElevationM">Height of the station above the vertical datum, metres, or null where
/// the line work carried no altitudes. Only used to place the reading in an elevation band; a
/// reading without one still counts everywhere else.</param>
public sealed record CrossSectionReading(
    string StationName,
    double? LeftM,
    double? RightM,
    double? UpM,
    double? DownM,
    double? ElevationM);

/// <summary>
/// One station's passage cross-section, after every reading taken there has been collapsed into
/// one.
/// </summary>
/// <param name="StationName">The station.</param>
/// <param name="ElevationM">Its height, metres, or null where the line work carried no altitudes.</param>
/// <param name="ReadingCount">How many readings were collapsed to produce this.</param>
/// <param name="WidthM">Left plus right, metres, or null unless <b>both</b> were measured. Half a
/// measurement is not a width: a passage whose left wall is three metres away and whose right wall
/// was never reached is not a three-metre passage.</param>
/// <param name="HeightM">Up plus down, metres, or null unless both were measured.</param>
/// <param name="WidthHeightRatio">Width over height — above one is a passage wider than it is tall,
/// below one is a canyon. Null unless both exist, and null for a height of zero, where the ratio is
/// a division by zero rather than an infinitely wide passage.</param>
/// <param name="AreaM2">Cross-sectional area, square metres, or null unless all four walls were
/// measured. See <see cref="CrossSectionMorphometry.AreaOf"/> for the shape assumed.</param>
public sealed record StationCrossSection(
    string StationName,
    double? ElevationM,
    int ReadingCount,
    double? WidthM,
    double? HeightM,
    double? WidthHeightRatio,
    double? AreaM2)
{
    /// <summary>
    /// Whether this station has a cross-section a volume can be built on, which needs all four
    /// walls. A station that measured three of them is a measured station for the purpose of the
    /// figure it can produce and an unmeasured one here.
    /// </summary>
    public bool HasArea => AreaM2 is not null;
}

/// <summary>
/// One straight piece of passage between two stations, as the volume estimate sees it.
/// </summary>
/// <param name="FromStationName">Station at the start, or null where the line work did not resolve
/// one. A leg with an unnamed end can never be matched to a cross-section, so it counts towards the
/// length offered and never towards the length measured.</param>
/// <param name="ToStationName">Station at the end, on the same terms.</param>
/// <param name="LengthM">Real length along the passage, metres.</param>
/// <param name="IsDuplicate">Passage already surveyed on another trip. Carried rather than left to
/// the caller to filter because a duplicated leg adds a second copy of a volume that is already
/// counted, and unlike a duplicated length that error is invisible in the answer: nobody knows what
/// this cave's volume should be.</param>
public sealed record CrossSectionLeg(
    string? FromStationName,
    string? ToStationName,
    double LengthM,
    bool IsDuplicate);

/// <summary>
/// What a set of measurements of one kind looks like — the five-number summary a box plot is drawn
/// from, plus the mean.
/// </summary>
/// <param name="Count">How many measurements. This is the denominator every other field here is
/// true of, and it is not the number of stations in the cave.</param>
/// <param name="Minimum">Smallest measurement.</param>
/// <param name="LowerQuartile">The quarter point, by linear interpolation between the two
/// neighbouring order statistics.</param>
/// <param name="Median">The half point, on the same rule.</param>
/// <param name="UpperQuartile">The three-quarter point, on the same rule.</param>
/// <param name="Maximum">Largest measurement.</param>
/// <param name="Mean">Arithmetic mean, which a box plot does not show and a reader still asks
/// for.</param>
public sealed record CrossSectionDistribution(
    int Count,
    double Minimum,
    double LowerQuartile,
    double Median,
    double UpperQuartile,
    double Maximum,
    double Mean);

/// <summary>
/// How much space the passage encloses, and how much of the cave that figure actually describes.
/// </summary>
/// <param name="VolumeM3">Cubic metres, or <b>null</b> when no leg had a cross-section at both
/// ends. Null rather than zero: a cave whose walls were never measured does not enclose nothing.</param>
/// <param name="LegCount">How many legs were offered, duplicates excluded.</param>
/// <param name="MeasuredLegCount">How many of them had a cross-section at both ends and so
/// contributed.</param>
/// <param name="LengthM">Metres of passage offered, duplicates excluded.</param>
/// <param name="MeasuredLengthM">Metres of passage the volume was computed over. This is the figure
/// that says how much of the cave the volume describes, and it is why the volume is safe to report
/// at all: an estimate over a tenth of a cave and an estimate over all of it are the same number
/// with two completely different meanings.</param>
/// <param name="LengthFraction">Measured length over offered length, 0..1, or null when nothing was
/// offered.</param>
public sealed record PassageVolumeEstimate(
    double? VolumeM3,
    int LegCount,
    int MeasuredLegCount,
    double LengthM,
    double MeasuredLengthM,
    double? LengthFraction);

/// <summary>
/// How a passage's height scales with its width across one cave, as a power law fitted in
/// logarithms.
/// </summary>
/// <remarks>
/// This is the hydraulic-geometry reading of a set of cross-sections: in a channel the width and
/// the depth each follow a power of the discharge, so one follows a power of the other, and the
/// exponent says which of the two grows faster as the passage gets bigger. An exponent near one is
/// a cave whose big passages are scaled-up copies of its small ones; below one is a cave that grows
/// wider faster than it grows taller, which is what a passage enlarged along a bedding plane does;
/// above one is a cave that grows taller faster, which is what a canyon deepening in place does.
/// <para>
/// It is a description of the measurements and not a discharge estimate. Nothing in a survey
/// measures the water that cut the passage, so the exponent is the shape of the relationship
/// between two measured dimensions and stops there.
/// </para>
/// </remarks>
/// <param name="StationCount">Stations the fit was computed over — those with a width and a height
/// both above zero. A logarithm of zero is not a small passage, it is no passage, so a station
/// hard against both walls takes no part.</param>
/// <param name="Exponent">The power: height rises as width to this power.</param>
/// <param name="Coefficient">The multiplier at a width of one metre, metres.</param>
/// <param name="RSquared">How much of the spread in log height the fit accounts for, nought to one.
/// A low value with a confident exponent is the failure this figure is most prone to, and it is
/// reported beside the exponent so the two cannot be separated.</param>
public sealed record CrossSectionScaling(
    int StationCount,
    double Exponent,
    double Coefficient,
    double RSquared);

/// <summary>
/// The passage sizes found in one slice of the cave's vertical range.
/// </summary>
/// <param name="FromM">Lower edge, metres, inclusive.</param>
/// <param name="ToM">Upper edge, metres, exclusive.</param>
/// <param name="StationCount">Stations in this slice that measured anything at all.</param>
/// <param name="AreaStationCount">Stations in this slice with all four walls measured.</param>
/// <param name="Width">Width distribution here, or null where no station gave a width.</param>
/// <param name="Height">Height distribution here, or null where no station gave a height.</param>
/// <param name="Area">Area distribution here, or null where no station gave an area.</param>
public sealed record CrossSectionElevationBand(
    double FromM,
    double ToM,
    int StationCount,
    int AreaStationCount,
    CrossSectionDistribution? Width,
    CrossSectionDistribution? Height,
    CrossSectionDistribution? Area);

/// <summary>
/// How big one cave's passages are, and over how much of it that was worked out.
/// </summary>
/// <param name="ReadingCount">How many wall readings were offered, before stations carrying more
/// than one were collapsed.</param>
/// <param name="StationCount">How many distinct stations those readings describe.</param>
/// <param name="WidthStationCount">How many of them gave a width — both side walls measured.</param>
/// <param name="HeightStationCount">How many gave a height.</param>
/// <param name="AreaStationCount">How many gave an area — all four walls. Never larger than either
/// of the two above, and the denominator the volume is honest about.</param>
/// <param name="Width">Width distribution over the whole cave, or null where nothing gave one.</param>
/// <param name="Height">Height distribution, or null.</param>
/// <param name="WidthHeightRatio">Ratio distribution, or null. Its median is the figure that
/// separates a phreatic tube from a vadose canyon.</param>
/// <param name="Area">Area distribution, or null.</param>
/// <param name="Scaling">How height scales with width across the cave, or null when too few
/// stations measured both to fit anything.</param>
/// <param name="Volume">The volume estimate and its coverage. Never null; its own
/// <see cref="PassageVolumeEstimate.VolumeM3"/> is null when there was nothing to compute.</param>
/// <param name="BandWidthM">Height of one elevation slice, metres; zero when there are no
/// bands.</param>
/// <param name="Bands">The vertical slices, ascending, <b>omitting those no station fell in</b>. A
/// slice with no measurements is absent rather than reported as a slice of zero-sized passage.</param>
public sealed record CrossSectionSummary(
    int ReadingCount,
    int StationCount,
    int WidthStationCount,
    int HeightStationCount,
    int AreaStationCount,
    CrossSectionDistribution? Width,
    CrossSectionDistribution? Height,
    CrossSectionDistribution? WidthHeightRatio,
    CrossSectionDistribution? Area,
    CrossSectionScaling? Scaling,
    PassageVolumeEstimate Volume,
    double BandWidthM,
    IReadOnlyList<CrossSectionElevationBand> Bands);

/// <summary>
/// The cross-section arithmetic: how big a cave's passages are, from the wall distances recorded at
/// its stations.
///
/// <para>
/// <b>An unmeasured dimension is not a zero, and this is the whole shape of the class.</b> A
/// surveyor who did not reach a wall recorded nothing, and nothing is not a wall at distance zero.
/// Every figure here therefore refuses a station rather than substituting for it: a station missing
/// one side wall has no width, a station missing any of the four has no area, and a leg with no
/// area at both ends leaves the volume entirely instead of contributing a flattened sliver of it.
/// Substituting a default would invent passage the surveyor never saw; substituting a zero would
/// shrink the cave. Both produce a number that is wrong in one direction always and looks entirely
/// plausible, which is why every answer here is paired with the count it was computed over.
/// </para>
/// <para>
/// <b>The volume is a chain of prisms between stations, not a bundle of tubes.</b> Each leg
/// contributes its own length times the mean of the cross-sections at its two ends, so the passage
/// around a junction is divided among the legs that meet there and is counted once. The obvious
/// alternative — giving every leg an independently capped tube of its own, which is how a survey is
/// rendered as a solid — over-counts at every junction, because the caps of the legs meeting at a
/// station all fill the same piece of cave. That is invisible in a picture and is worst in exactly
/// the mazy caves where a volume is most interesting.
/// </para>
/// <para>
/// <b>This is pure arithmetic over numbers.</b> It knows nothing about where the readings came from
/// and nothing about who may see them. A wall distance is measured from a cave coordinate, so
/// whether a reading may be counted at all is settled before anything reaches here.
/// </para>
/// </summary>
public static class CrossSectionMorphometry
{
    /// <summary>The most vertical slices a cave's range is cut into.</summary>
    /// <remarks>
    /// The slice height comes from the same 1-2-5 ladder the elevation histogram uses, so the edges
    /// are round numbers a reader can compare between two caves, and one slice more than the ladder
    /// promises is allowed for the remainder at the top.
    /// </remarks>
    public const int MaximumBandCount = ElevationBands.MaximumBinCount + 1;

    /// <summary>
    /// Cross-sectional area from a width and a height, square metres, or null unless both exist.
    /// </summary>
    /// <remarks>
    /// The cross-section is taken as an ellipse inscribed in the measured box, so the area is
    /// <c>π/4</c> of width times height. This is not a free choice of shape dressed up: building the
    /// section out of four quarter-ellipses, one per quadrant, with semi-axes taken from the four
    /// wall distances actually measured — the standard reading of a set of wall distances — gives
    /// <c>(π/4)(LU + UR + RD + DL)</c>, which factorises exactly to <c>(π/4)(L+R)(U+D)</c>. The two
    /// are the same number, so the simpler form loses nothing. Taking the box itself instead would
    /// report every passage as about 27% larger than the walls support.
    /// </remarks>
    public static double? AreaOf(double? widthM, double? heightM) =>
        widthM is { } width && heightM is { } height ? Math.PI / 4d * width * height : null;

    /// <summary>
    /// Collapse the readings taken at each station into one cross-section per station, in station
    /// name order.
    /// </summary>
    /// <remarks>
    /// Readings of one station are readings of the same walls, so where two disagree the difference
    /// is measurement spread and the collapse is their mean. It is taken <b>per wall</b>, over the
    /// readings that measured that wall: a station whose one reading found the left wall and whose
    /// other found the right has a width, and discarding it because neither reading alone was
    /// complete would throw away a measurement to no purpose. A wall no reading measured stays
    /// unmeasured.
    /// </remarks>
    public static IReadOnlyList<StationCrossSection> Stations(IEnumerable<CrossSectionReading> readings)
    {
        var byStation = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var reading in readings)
        {
            if (string.IsNullOrEmpty(reading.StationName))
            {
                continue;
            }

            if (!byStation.TryGetValue(reading.StationName, out var accumulator))
            {
                accumulator = new Accumulator();
                byStation[reading.StationName] = accumulator;
            }

            accumulator.Add(reading);
        }

        return byStation
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.Close(pair.Key))
            .ToList();
    }

    /// <summary>
    /// The five-number summary of a set of measurements, or null when there are none.
    /// </summary>
    /// <remarks>
    /// Quartiles are read off the sorted measurements by linear interpolation between the two
    /// neighbouring order statistics — the same rule the box plots on the client are drawn with, so
    /// a figure quoted in words and the box beside it cannot disagree.
    /// </remarks>
    public static CrossSectionDistribution? Distribution(IEnumerable<double> values)
    {
        var sorted = values.Where(double.IsFinite).ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        Array.Sort(sorted);
        return new CrossSectionDistribution(
            sorted.Length,
            sorted[0],
            Quantile(sorted, 0.25),
            Quantile(sorted, 0.5),
            Quantile(sorted, 0.75),
            sorted[^1],
            sorted.Average());
    }

    /// <summary>
    /// How much space the passage encloses, from the cross-sections at the stations its legs join.
    /// </summary>
    /// <remarks>
    /// A leg contributes only when <b>both</b> its ends have an area. Half a prism is not half a
    /// volume: the cross-section at the far end is unknown, not zero, and pretending otherwise both
    /// under-counts the leg and hides that it was ever in doubt. Duplicate legs never contribute and
    /// are not offered either, so the coverage fraction is a fraction of passage that exists once.
    /// </remarks>
    public static PassageVolumeEstimate Volume(
        IEnumerable<StationCrossSection> stations, IEnumerable<CrossSectionLeg> legs)
    {
        var areas = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var station in stations)
        {
            if (station.AreaM2 is { } area)
            {
                areas[station.StationName] = area;
            }
        }

        var legCount = 0;
        var measuredLegCount = 0;
        var lengthM = 0d;
        var measuredLengthM = 0d;
        var volumeM3 = 0d;

        foreach (var leg in legs)
        {
            if (leg.IsDuplicate || !double.IsFinite(leg.LengthM) || leg.LengthM < 0)
            {
                continue;
            }

            legCount++;
            lengthM += leg.LengthM;

            if (leg.FromStationName is not { } from
                || leg.ToStationName is not { } to
                || !areas.TryGetValue(from, out var fromArea)
                || !areas.TryGetValue(to, out var toArea))
            {
                continue;
            }

            measuredLegCount++;
            measuredLengthM += leg.LengthM;
            volumeM3 += leg.LengthM * (fromArea + toArea) / 2d;
        }

        return new PassageVolumeEstimate(
            measuredLegCount > 0 ? volumeM3 : null,
            legCount,
            measuredLegCount,
            lengthM,
            measuredLengthM,
            lengthM > 0 ? measuredLengthM / lengthM : null);
    }

    /// <summary>
    /// The passage sizes found in each slice of the cave's vertical range, ascending.
    /// </summary>
    /// <remarks>
    /// Stations with no altitude are absent from every slice — the line work that produced them
    /// carried no third coordinate, and putting them at zero would stack a plan drawing into one
    /// band at sea level. Slices no station fell in are omitted rather than reported empty, because
    /// an empty slice drawn beside occupied ones reads as passage of no size.
    /// </remarks>
    public static IReadOnlyList<CrossSectionElevationBand> ByElevation(
        IEnumerable<StationCrossSection> stations, out double bandWidthM)
    {
        var placed = stations
            .Where(s => s.ElevationM is { } z && double.IsFinite(z))
            .Where(s => s.WidthM is not null || s.HeightM is not null)
            .ToList();

        bandWidthM = 0d;
        if (placed.Count == 0)
        {
            return [];
        }

        var lowest = placed.Min(s => s.ElevationM!.Value);
        var highest = placed.Max(s => s.ElevationM!.Value);
        bandWidthM = ElevationBands.BinWidthFor(lowest, highest);
        var first = Math.Floor(lowest / bandWidthM) * bandWidthM;

        var buckets = new SortedDictionary<int, List<StationCrossSection>>();
        foreach (var station in placed)
        {
            var index = Math.Clamp(
                (int)Math.Floor((station.ElevationM!.Value - first) / bandWidthM), 0, MaximumBandCount - 1);
            if (!buckets.TryGetValue(index, out var bucket))
            {
                bucket = [];
                buckets[index] = bucket;
            }

            bucket.Add(station);
        }

        var bands = new List<CrossSectionElevationBand>(buckets.Count);
        foreach (var (index, bucket) in buckets)
        {
            var from = first + (index * bandWidthM);
            bands.Add(new CrossSectionElevationBand(
                from,
                from + bandWidthM,
                bucket.Count,
                bucket.Count(s => s.HasArea),
                Distribution(bucket.Where(s => s.WidthM is not null).Select(s => s.WidthM!.Value)),
                Distribution(bucket.Where(s => s.HeightM is not null).Select(s => s.HeightM!.Value)),
                Distribution(bucket.Where(s => s.AreaM2 is not null).Select(s => s.AreaM2!.Value))));
        }

        return bands;
    }

    /// <summary>How few stations a scaling relationship will not be claimed from.</summary>
    /// <remarks>
    /// A line through two points is those two points and not a relationship, and three is already
    /// generous. The threshold exists because the exponent looks exactly as authoritative either
    /// way, and a reader has no way to tell a fit over four stations from a fit over four hundred
    /// unless one is refused and the other reports its count.
    /// </remarks>
    public const int MinimumScalingStations = 5;

    /// <summary>
    /// Fit height against width as a power law over the stations that measured both, or null when
    /// too few did or the widths do not vary at all.
    /// </summary>
    /// <remarks>
    /// Ordinary least squares on the logarithms, which is the fit the literature reports and the
    /// same one the client draws over a log-log scatter. Stations with a zero width or a zero
    /// height are excluded rather than nudged: a logarithm of zero is not a very small passage.
    /// </remarks>
    public static CrossSectionScaling? Scaling(IEnumerable<StationCrossSection> stations)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var station in stations)
        {
            if (station.WidthM is { } width && station.HeightM is { } height
                && width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))
            {
                xs.Add(Math.Log(width));
                ys.Add(Math.Log(height));
            }
        }

        if (xs.Count < MinimumScalingStations)
        {
            return null;
        }

        var meanX = xs.Average();
        var meanY = ys.Average();
        var covariance = 0d;
        var varianceX = 0d;
        var varianceY = 0d;
        for (var i = 0; i < xs.Count; i++)
        {
            var dx = xs[i] - meanX;
            var dy = ys[i] - meanY;
            covariance += dx * dy;
            varianceX += dx * dx;
            varianceY += dy * dy;
        }

        // Every station the same width: the passages differ in height at one width, which says
        // nothing about how height changes with width. The slope through them is a division by
        // zero, not a vertical relationship.
        if (varianceX <= 0)
        {
            return null;
        }

        var slope = covariance / varianceX;
        var intercept = meanY - (slope * meanX);

        // No spread in height either means the fit accounts for a spread that is not there. One is
        // the honest reading: every point is exactly on the line.
        var rSquared = varianceY > 0 ? covariance * covariance / (varianceX * varianceY) : 1d;

        return new CrossSectionScaling(xs.Count, slope, Math.Exp(intercept), rSquared);
    }

    /// <summary>
    /// Everything the wall readings of one cave say about how big its passages are.
    /// </summary>
    public static CrossSectionSummary Summarize(
        IEnumerable<CrossSectionReading> readings, IEnumerable<CrossSectionLeg> legs)
    {
        var stations = Stations(readings);
        var bands = ByElevation(stations, out var bandWidthM);

        return new CrossSectionSummary(
            stations.Sum(s => s.ReadingCount),
            stations.Count,
            stations.Count(s => s.WidthM is not null),
            stations.Count(s => s.HeightM is not null),
            stations.Count(s => s.HasArea),
            Distribution(stations.Where(s => s.WidthM is not null).Select(s => s.WidthM!.Value)),
            Distribution(stations.Where(s => s.HeightM is not null).Select(s => s.HeightM!.Value)),
            Distribution(stations.Where(s => s.WidthHeightRatio is not null).Select(s => s.WidthHeightRatio!.Value)),
            Distribution(stations.Where(s => s.AreaM2 is not null).Select(s => s.AreaM2!.Value)),
            Scaling(stations),
            Volume(stations, legs),
            bandWidthM,
            bands);
    }

    private static double Quantile(double[] sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((position - lower) * (sorted[upper] - sorted[lower]));
    }

    /// <summary>
    /// A wall distance that is a measurement. A stored dimension is already constrained to be zero
    /// or more, so this is the guard against a value that arrived from somewhere with weaker
    /// promises rather than a second copy of the sentinel rule.
    /// </summary>
    private static double? Usable(double? value) =>
        value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private sealed class Accumulator
    {
        private double leftSum;
        private double rightSum;
        private double upSum;
        private double downSum;
        private int leftCount;
        private int rightCount;
        private int upCount;
        private int downCount;
        private double? elevationM;

        public int ReadingCount { get; private set; }

        public void Add(CrossSectionReading reading)
        {
            ReadingCount++;
            elevationM ??= reading.ElevationM is { } z && double.IsFinite(z) ? z : null;

            if (Usable(reading.LeftM) is { } left)
            {
                leftSum += left;
                leftCount++;
            }

            if (Usable(reading.RightM) is { } right)
            {
                rightSum += right;
                rightCount++;
            }

            if (Usable(reading.UpM) is { } up)
            {
                upSum += up;
                upCount++;
            }

            if (Usable(reading.DownM) is { } down)
            {
                downSum += down;
                downCount++;
            }
        }

        public StationCrossSection Close(string stationName)
        {
            var left = leftCount > 0 ? leftSum / leftCount : (double?)null;
            var right = rightCount > 0 ? rightSum / rightCount : (double?)null;
            var up = upCount > 0 ? upSum / upCount : (double?)null;
            var down = downCount > 0 ? downSum / downCount : (double?)null;

            var width = left is { } l && right is { } r ? l + r : (double?)null;
            var height = up is { } u && down is { } d ? u + d : (double?)null;

            // A height of zero is a real measurement — both walls found at the station itself — and
            // the ratio against it is a division by zero, not an infinitely wide passage.
            var ratio = width is { } w && height is { } h && h > 0 ? w / h : (double?)null;

            return new StationCrossSection(
                stationName, elevationM, ReadingCount, width, height, ratio, AreaOf(width, height));
        }
    }
}
