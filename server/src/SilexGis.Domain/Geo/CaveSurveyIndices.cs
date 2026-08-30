// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One straight piece of surveyed passage as the per-cave indices see it. This mirrors the
/// measured substrate field for field so that the mapping into it cannot go wrong; nothing here
/// knows or cares which body of line work produced the numbers.
/// </summary>
/// <param name="PathIndex">Which polyline this piece belongs to, or null when the line work is a
/// network of legs whose paths nobody has reconstructed. Sinuosity is a property of a path, so a
/// piece with no path contributes to every other index and to none of the sinuosities — refusing
/// the number is correct, and treating a whole network as one path would produce a figure that
/// looks like sinuosity and measures nothing.</param>
/// <param name="SegmentIndex">Position along <paramref name="PathIndex"/>. Pieces of one path are
/// contiguous and in order, so the path's two ends are the first piece's start and the last
/// piece's end.</param>
/// <param name="IsDuplicate">Passage already surveyed on another trip. Its altitudes are real and
/// count towards the cave's vertical extent; its length must never be added, or the passage is
/// counted twice.</param>
/// <param name="PlanLengthM">Map distance between the ends, metres.</param>
/// <param name="SlopeLengthM">Real length along the passage, metres; null where the line work
/// carried no altitudes, in which case the plan length is all there is.</param>
/// <param name="DeltaZM">Altitude of the end minus altitude of the start, metres; null where the
/// line work carried no altitudes.</param>
/// <param name="MidZM">Altitude of the midpoint, metres; null on the same condition. The two end
/// altitudes are this value minus and plus half of <paramref name="DeltaZM"/> — the midpoint of a
/// straight piece is the mean of its ends, so the pair is recovered exactly rather than
/// approximately.</param>
public sealed record PassageSegment(
    int? PathIndex,
    int SegmentIndex,
    bool IsDuplicate,
    double FromLongitude,
    double FromLatitude,
    double ToLongitude,
    double ToLatitude,
    double PlanLengthM,
    double? SlopeLengthM,
    double? DeltaZM,
    double? MidZM);

/// <summary>
/// How much a single path wanders relative to going straight there.
/// </summary>
/// <param name="PathIndex">The path this describes.</param>
/// <param name="LengthM">Length of the path across the map, metres — its plan length, since the
/// distance it is compared against is a plan distance too.</param>
/// <param name="StraightLineM">Distance between the path's two ends, metres.</param>
/// <param name="Sinuosity">Length divided by straight-line distance: 1 is a straight tube, 2 is a
/// passage twice as long as the ground it covers. Null for a path that returns to where it
/// started, where the ratio is a division by zero and the concept does not apply.</param>
public sealed record PathSinuosity(int PathIndex, double LengthM, double StraightLineM, double? Sinuosity);

/// <summary>
/// What became of a comparison between a number computed from the survey and the number somebody
/// typed into the registry.
/// </summary>
public enum MorphometryAgreement : short
{
    /// <summary>Nobody typed a value in, so there is nothing to compare against.</summary>
    NotDeclared = 0,

    /// <summary>
    /// The survey cannot produce this number — most often a depth for line work that carries no
    /// altitudes at all. Distinct from agreement: an uncomparable pair is not a matching pair.
    /// </summary>
    NotComputed = 1,

    /// <summary>The two are close enough that the difference is not worth showing anybody.</summary>
    Agrees = 2,

    /// <summary>
    /// The two differ by more than measurement and method explain. This is a statement that the
    /// pair disagrees, not a verdict on which one is wrong: a typed length older than the survey
    /// it is compared against is the ordinary case, and so is a survey that covers part of a cave
    /// the registry describes whole.
    /// </summary>
    Disagrees = 3,
}

/// <summary>
/// A computed number set beside the declared one.
/// </summary>
/// <param name="ComputedM">What the survey says, metres; null when it cannot say.</param>
/// <param name="DeclaredM">What the record says, metres; null when the field is empty.</param>
/// <param name="DifferenceM">Declared minus computed, metres. Positive means the record claims
/// more than the survey found. Null unless both sides exist.</param>
/// <param name="RelativeDifference">The absolute difference over the larger of the two magnitudes,
/// 0–1. The larger magnitude rather than the declared value so the measure is symmetric — a
/// declared 100 against a computed 50 and a declared 50 against a computed 100 are the same
/// disagreement, and dividing by the declared value alone would call one of them half the size of
/// the other.</param>
/// <param name="Agreement">The outcome, which is what a reader acts on.</param>
public sealed record MorphometryComparison(
    double? ComputedM,
    decimal? DeclaredM,
    double? DifferenceM,
    double? RelativeDifference,
    MorphometryAgreement Agreement)
{
    /// <summary>Whether this pair is worth putting in front of somebody.</summary>
    public bool Disagrees => Agreement == MorphometryAgreement.Disagrees;
}

/// <summary>
/// The shape of one cave as its survey measures it.
/// </summary>
/// <param name="SegmentCount">How many pieces of passage were measured, duplicates included.</param>
/// <param name="PathCount">How many distinct paths those pieces belong to; zero when the line work
/// is a network with no reconstructed paths.</param>
/// <param name="TotalLengthM">Real length of passage, metres, duplicates excluded. This is the
/// figure the declared length is compared against.</param>
/// <param name="PlanLengthM">Length of its shadow on the map, metres, duplicates excluded.</param>
/// <param name="HasAltitudes">Whether the line work carried a third coordinate. When false every
/// vertical figure below is null rather than zero: a plan drawing of a cave is not a flat
/// cave.</param>
/// <param name="HighestZM">Altitude of the highest point reached, metres.</param>
/// <param name="LowestZM">Altitude of the lowest point reached, metres.</param>
/// <param name="VerticalExtentM">Highest minus lowest, metres — the full vertical range the survey
/// covers, which is what the declared depth is compared against.</param>
/// <param name="MaximumExtentM">Distance between the two furthest-apart points of the cave in plan,
/// metres. The straight line a cave of this length could have covered if it went one way.</param>
/// <param name="Verticality">Vertical extent over total length, 0–1. A horizontal system tends to
/// zero; a single shaft tends to one.</param>
/// <param name="Horizontality">Plan length over total length, 0–1. One for passage that never
/// changes level, falling as the passage steepens.</param>
/// <param name="Linearity">Maximum extent over total length, 0–1. One for a single straight tube;
/// small for a maze, a branchwork, or anything that doubles back — it measures how much ground the
/// passage covers per metre walked, which is the axis on which a labyrinth differs from a
/// river cave.</param>
/// <param name="LengthToDepthRatio">Total length over vertical extent. The number cavers quote to
/// say whether a system is a horizontal cave or a pit; null for a cave with no vertical extent at
/// all, where the ratio is unbounded rather than large.</param>
/// <param name="Sinuosity">The paths' sinuosity averaged with each path weighted by its length.
/// Weighted, because a hundred two-metre stubs off a gallery would otherwise outvote the gallery,
/// and the stubs are where the ratio is least meaningful. Null when no path has a defined
/// sinuosity.</param>
public sealed record CaveIndexSummary(
    int SegmentCount,
    int PathCount,
    double TotalLengthM,
    double PlanLengthM,
    bool HasAltitudes,
    double? HighestZM,
    double? LowestZM,
    double? VerticalExtentM,
    double? MaximumExtentM,
    double? Verticality,
    double? Horizontality,
    double? Linearity,
    double? LengthToDepthRatio,
    double? Sinuosity);

/// <summary>
/// The per-cave indices: how long the passage is, how much ground it covers, how steeply it runs,
/// how much it wanders — and whether any of that matches what somebody typed into the record.
///
/// <para>
/// <b>Every ratio here is a ratio of two lengths this class computed itself</b>, never of a
/// computed length against a declared one. Mixing the two would produce an index whose value
/// depends on how carefully a particular club fills in its forms, and the whole point of the
/// comparison at the end is that those two families of number are independent.
/// </para>
/// <para>
/// <b>Pure arithmetic over measured pieces.</b> It knows nothing about which body of line work
/// produced them, which legs were excluded as wall shots, or who may see the cave. Those are the
/// caller's decisions and folding them in here would put the same judgment in two places.
/// </para>
/// </summary>
public static class CaveSurveyIndices
{
    /// <summary>
    /// How far apart a computed and a declared figure may be, as a fraction of the larger of them,
    /// before the pair is called a disagreement.
    ///
    /// <para>
    /// Ten per cent, and the number is chosen rather than picked. The floor under it is the method:
    /// where a cave has no parsed survey file, its passage is measured over a shape-based reduction
    /// of its centerline, and that reduction is documented as retaining 92–95% of the surveyed
    /// length — it drops the last couple of metres of every dead end. A threshold at eight per cent
    /// would therefore fire on a cave whose declared length is exactly right, and the flag would be
    /// reporting which method answered rather than anything about the cave. Ten per cent clears
    /// that band with a little margin.
    /// </para>
    /// <para>
    /// The ceiling on it is usefulness: the disagreements worth showing are the ones where a
    /// registry figure predates its survey, and those are routinely tens of per cent, not fractions
    /// of one. A threshold much above ten would let a cave declared at 1 km and surveyed at 1.15 km
    /// pass silently, which is exactly the case this exists to catch.
    /// </para>
    /// </summary>
    public const double RelativeTolerance = 0.10;

    /// <summary>
    /// A length difference smaller than this is never flagged however large it is in proportion.
    /// Five metres, because the conventions that decide where a survey starts and stops — at the
    /// drip line, at the first station, at the end of the crawl somebody could not fit through —
    /// each cost a few metres, and a twelve-metre cave recorded as fifteen is two people measuring
    /// the same hole rather than a discrepancy anybody should chase.
    /// </summary>
    public const double LengthAbsoluteToleranceM = 5d;

    /// <summary>
    /// The same floor for depth, and lower, because a depth is one number between two points rather
    /// than a sum over hundreds of legs, and it is typed to the metre. Two metres is the width of
    /// the rounding, not of a real difference.
    /// </summary>
    public const double DepthAbsoluteToleranceM = 2d;

    /// <summary>
    /// Measures a cave from its pieces of passage. An empty set is answered rather than refused,
    /// with zero lengths and nulls wherever an index is undefined: a cave with no line work is a
    /// state the read surfaces have to render, not an error.
    /// </summary>
    public static CaveIndexSummary Compute(IEnumerable<PassageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var all = segments as IReadOnlyList<PassageSegment> ?? [.. segments];

        var totalLength = 0d;
        var planLength = 0d;
        var hasAltitudes = false;
        var lowest = double.PositiveInfinity;
        var highest = double.NegativeInfinity;
        var points = new List<(double Lon, double Lat)>(all.Count + 1);

        foreach (var piece in all)
        {
            if (!double.IsFinite(piece.PlanLengthM) || piece.PlanLengthM < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segments), piece.PlanLengthM, "A passage length must be finite and not negative.");
            }

            if (!piece.IsDuplicate)
            {
                planLength += piece.PlanLengthM;
                totalLength += piece.SlopeLengthM ?? piece.PlanLengthM;
            }

            // Altitudes of a duplicated leg are real ground, so they widen the vertical extent even
            // though its length is not added. The same passage walked twice is still that deep.
            if (piece.MidZM is { } mid && piece.DeltaZM is { } rise)
            {
                hasAltitudes = true;
                var from = mid - (rise / 2d);
                var to = mid + (rise / 2d);
                lowest = Math.Min(lowest, Math.Min(from, to));
                highest = Math.Max(highest, Math.Max(from, to));
            }

            points.Add((piece.FromLongitude, piece.FromLatitude));
            points.Add((piece.ToLongitude, piece.ToLatitude));
        }

        var paths = PathSinuosities(all);

        double? verticalExtent = hasAltitudes ? highest - lowest : null;
        var maximumExtent = points.Count == 0 ? (double?)null : MaximumExtentMeters(points);

        return new CaveIndexSummary(
            all.Count,
            paths.Count,
            totalLength,
            planLength,
            hasAltitudes,
            hasAltitudes ? highest : null,
            hasAltitudes ? lowest : null,
            verticalExtent,
            maximumExtent,
            Ratio(verticalExtent, totalLength),
            Ratio(planLength, totalLength),
            Ratio(maximumExtent, totalLength),
            verticalExtent is > 0d ? totalLength / verticalExtent.Value : null,
            WeightedSinuosity(paths));
    }

    /// <summary>
    /// How much each path wanders, one row per path, ascending by path index. Exposed separately
    /// from <see cref="Compute"/> because a large cave reduces to hundreds of paths and a read
    /// surface that renders one block per cave does not want them all — but a caller that wants
    /// the distribution should not have to recompute it from the segments.
    /// </summary>
    public static IReadOnlyList<PathSinuosity> PathSinuosities(IEnumerable<PassageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var rows = new List<PathSinuosity>();
        foreach (var group in segments
            .Where(s => s.PathIndex.HasValue && !s.IsDuplicate)
            .GroupBy(s => s.PathIndex!.Value)
            .OrderBy(g => g.Key))
        {
            var ordered = group.OrderBy(s => s.SegmentIndex).ToList();

            // Both sides of the ratio are plan measurements. Sinuosity asks how much the passage
            // wanders across the map on its way between two points, and putting the slope length
            // over a plan distance would answer a different question badly: a vertical shaft, which
            // does not wander at all, would come out as the most sinuous thing in the cave. How
            // steeply the passage runs is already reported, separately, as its verticality.
            var walked = ordered.Sum(s => s.PlanLengthM);

            // The pieces of a path are contiguous and in order, so its two ends are the start of
            // the first and the end of the last.
            var first = ordered[0];
            var last = ordered[^1];
            var straight = Geodesy.DistanceMeters(
                first.FromLongitude, first.FromLatitude, last.ToLongitude, last.ToLatitude);

            rows.Add(new PathSinuosity(
                group.Key,
                walked,
                straight,
                straight > 0d ? walked / straight : null));
        }

        return rows;
    }

    /// <summary>
    /// Sets a computed length beside the declared one and says whether the pair disagrees.
    /// </summary>
    public static MorphometryComparison CompareLength(double? computedM, decimal? declaredM) =>
        Compare(computedM, declaredM, LengthAbsoluteToleranceM);

    /// <summary>
    /// Sets the computed vertical extent beside the declared depth.
    ///
    /// <para>
    /// Both sides are compared as magnitudes, which sidesteps a sign convention the record never
    /// states: a declared depth is entered as a positive number and nothing says whether it is
    /// measured from the entrance, from the highest point, or from the ground above. The computed
    /// side is the full range the survey covers, so a cave with passage rising above its entrance
    /// legitimately measures more than a depth taken downwards from that entrance. That is a real
    /// difference between two things that were never the same measurement, and the honest thing is
    /// to show it rather than to guess which convention the typist used and quietly correct for it.
    /// </para>
    /// </summary>
    public static MorphometryComparison CompareDepth(double? computedM, decimal? declaredM) =>
        Compare(computedM, declaredM, DepthAbsoluteToleranceM);

    /// <summary>
    /// The comparison itself. The declared side crosses from the record's fixed-point decimal into
    /// floating point here and nowhere else, so there is one place to look when a rounding question
    /// comes up; the values involved are metres to two decimal places, which double carries exactly
    /// enough of for a tolerance measured in whole metres.
    /// </summary>
    private static MorphometryComparison Compare(double? computedM, decimal? declaredM, double absoluteToleranceM)
    {
        if (declaredM is null)
        {
            return new MorphometryComparison(computedM, null, null, null, MorphometryAgreement.NotDeclared);
        }

        if (computedM is not { } computed || !double.IsFinite(computed))
        {
            return new MorphometryComparison(null, declaredM, null, null, MorphometryAgreement.NotComputed);
        }

        var declared = (double)declaredM.Value;
        var difference = declared - computed;
        var magnitude = Math.Max(Math.Abs(declared), Math.Abs(computed));
        var relative = magnitude > 0d ? Math.Abs(difference) / magnitude : 0d;

        // Both gates, not either: a proportional threshold alone flags a three-metre cave recorded
        // as five, and an absolute one alone lets a kilometre of missing passage through.
        var disagrees = relative > RelativeTolerance && Math.Abs(difference) > absoluteToleranceM;

        return new MorphometryComparison(
            computed,
            declaredM,
            difference,
            relative,
            disagrees ? MorphometryAgreement.Disagrees : MorphometryAgreement.Agrees);
    }

    private static double? Ratio(double? numerator, double denominator) =>
        numerator is { } value && denominator > 0d ? value / denominator : null;

    private static double? WeightedSinuosity(IReadOnlyList<PathSinuosity> paths)
    {
        var weight = 0d;
        var sum = 0d;
        foreach (var path in paths)
        {
            if (path.Sinuosity is { } sinuosity && path.LengthM > 0d)
            {
                weight += path.LengthM;
                sum += path.LengthM * sinuosity;
            }
        }

        return weight > 0d ? sum / weight : null;
    }

    /// <summary>
    /// The distance between the two furthest-apart points of the cave in plan, metres.
    ///
    /// <para>
    /// The furthest pair is always a pair of hull vertices, so the hull is taken first and the
    /// pairs are then enumerated over it. Enumerating every pair of the raw points would be
    /// quadratic in a count that reaches tens of thousands on a real export; a hull has tens of
    /// vertices. The hull is computed on a local flat frame — longitude shrunk by the cosine of the
    /// latitude so a degree east and a degree north are the same distance — because at the scale of
    /// one cave that frame is faithful to well under a metre, and it is only ever used to
    /// <i>choose</i> the pair. The distance itself is then measured on the sphere from the original
    /// coordinates, so the reported number does not inherit the frame's error.
    /// </para>
    /// </summary>
    private static double MaximumExtentMeters(IReadOnlyList<(double Lon, double Lat)> points)
    {
        if (points.Count < 2)
        {
            return 0d;
        }

        var scale = Math.Cos(points[0].Lat * Math.PI / 180d);
        var sorted = points
            .Select(p => (X: p.Lon * scale, Y: p.Lat, p.Lon, p.Lat))
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        // Andrew's monotone chain: the lower hull left to right, then the upper hull right to
        // left, each dropping any vertex that does not turn left. Collinear points are dropped
        // with it, which is what leaves a set of points all on one line with just its two ends.
        var hull = new List<(double X, double Y, double Lon, double Lat)>(sorted.Count + 1);
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            var sequence = pass == 0 ? sorted : Enumerable.Reverse(sorted);
            foreach (var point in sequence)
            {
                while (hull.Count - start >= 2 && Cross(hull[^2], hull[^1], point) <= 0d)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            // Each pass ends on the point the next one starts from; dropping it keeps the ring
            // free of duplicates.
            hull.RemoveAt(hull.Count - 1);
        }

        var best = 0d;
        for (var i = 0; i < hull.Count; i++)
        {
            for (var j = i + 1; j < hull.Count; j++)
            {
                best = Math.Max(
                    best, Geodesy.DistanceMeters(hull[i].Lon, hull[i].Lat, hull[j].Lon, hull[j].Lat));
            }
        }

        return best;
    }

    private static double Cross(
        (double X, double Y, double Lon, double Lat) origin,
        (double X, double Y, double Lon, double Lat) a,
        (double X, double Y, double Lon, double Lat) b) =>
        ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));
}
