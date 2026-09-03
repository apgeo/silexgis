// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace SilexGis.Domain.Geo;

/// <summary>A position in a projected system, in metres, as a point-pattern statistic sees it.</summary>
public readonly record struct PlanarPoint(double X, double Y);

/// <summary>
/// Whether points sit closer together, or further apart, than chance would put them.
/// </summary>
/// <param name="Count">
/// How many points the statistic was computed over. It is reported because a nearest-neighbour
/// index says nothing on its own: the same number over eight points and over eight hundred are
/// different claims, and the set here is only what one caller may both read and place exactly.
/// </param>
/// <param name="AreaM2">
/// The ground the points were counted over. This is the other half of what makes the index
/// readable — the expected spacing under chance is derived from it, so an index quoted without it
/// is a ratio to an unstated denominator.
/// </param>
/// <param name="MeanNearestNeighbourM">Mean distance from a point to its nearest other point.</param>
/// <param name="ExpectedMeanM">
/// What that mean would be if the same number of points were scattered at random over the same
/// ground: half the reciprocal square root of the density.
/// </param>
/// <param name="Index">
/// Observed mean over expected mean. One is indistinguishable from chance; below one is clustered;
/// above one is more evenly spaced than chance, up to about 2.15 for a perfect triangular lattice.
/// </param>
/// <param name="ZScore">
/// How many standard errors the observed mean sits from the expected one. Negative is clustered.
/// </param>
/// <param name="PValue">
/// Two-sided probability of a departure at least this large arising from chance alone.
/// </param>
public sealed record ClarkEvansResult(
    int Count,
    double AreaM2,
    double MeanNearestNeighbourM,
    double ExpectedMeanM,
    double Index,
    double ZScore,
    double PValue);

/// <summary>
/// One radius of a Ripley curve: what was observed at that distance, and the band that repeated
/// random scatters of the same points over the same ground produced.
/// </summary>
/// <param name="RadiusM">The distance the neighbours were counted within.</param>
/// <param name="ObservedL">
/// The observed L. Under complete spatial randomness L equals the radius, so the departure from
/// the diagonal is the reading — above it clustered, below it spaced out.
/// </param>
/// <param name="LowerL">
/// Lowest L any of the simulated random patterns produced at this radius, or null when no
/// simulation contributed one. Null and not the observed value: a band reported as zero-width
/// around the observed curve is indistinguishable, to anything downstream, from a real band the
/// observation happens to sit exactly on — which reads as "indistinguishable from randomness", the
/// opposite of "no test was performed".
/// </param>
/// <param name="UpperL">Highest L any of them produced, or null for the same reason.</param>
public sealed record RipleyStep(double RadiusM, double ObservedL, double? LowerL, double? UpperL);

/// <summary>
/// Ripley's L over a range of distances, with the envelope the simulations drew around it.
/// </summary>
/// <param name="Count">How many points contributed — the n every step was computed over.</param>
/// <param name="AreaM2">The ground they were counted over, which is the estimator's denominator.</param>
/// <param name="Simulations">
/// How many random patterns the envelope is the extremes of — the number that actually ran, not
/// the number that was asked for. The two differ when the caller asked for none, and when a window
/// so thin relative to its bounding box that points could not be scattered into it stopped the
/// loop; in both cases the steps carry no band, and this says so with a number.
/// </param>
/// <param name="Seed">
/// The seed those patterns were drawn from. It is stated, and settable, because an envelope from an
/// unstated seed makes the same data answer differently every time somebody reloads the page, and
/// two readers comparing notes could not tell a real difference from a different draw.
/// </param>
public sealed record RipleyResult(
    int Count,
    double AreaM2,
    int Simulations,
    int Seed,
    IReadOnlyList<RipleyStep> Steps);

/// <summary>
/// The two classical readings of whether a scatter of points is clustered, random or spaced out,
/// computed over projected coordinates in metres.
///
/// <para>
/// <b>Both need a stated window.</b> Neither statistic is a property of the points alone: they
/// compare the observed spacing against what the <i>same number of points over the same ground</i>
/// would give, so the answer changes with the window and an index quoted without one is not a
/// statistic about anything. Every result here therefore carries the count and the area it used,
/// and the caller is expected to publish both.
/// </para>
/// <para>
/// <b>Edge effects are corrected, not ignored, for the Ripley curve.</b> A point near the window's
/// edge has fewer neighbours simply because part of its neighbourhood is off the map, which biases
/// the curve downwards and would read as regularity that is not there. Each point's contribution
/// at a radius is therefore divided by the fraction of the circle of that radius, centred on it,
/// that lies inside the window. The fraction is measured by sampling the circle at a fixed number
/// of evenly-spaced bearings, which makes it work for any window shape rather than only a
/// rectangle; a point whose whole circle is inside the window is recognised as such from its
/// distance to the boundary and costs nothing.
/// </para>
/// <para>
/// The Clark–Evans index is deliberately <i>not</i> edge-corrected: its correction requires the
/// window to be a rectangle, and the small upward bias it leaves is stated here rather than
/// silently removed by an approximation that would be wrong for a karst outline. On a square
/// window the bias is a few per cent of the index — far less than the difference between a
/// clustered and a random pattern, which is what the number is read for.
/// </para>
/// </summary>
public static class PointPattern
{
    /// <summary>
    /// How many bearings the edge-correction circle is sampled at. Enough that the fraction is
    /// accurate to a few per cent, and no more, because it is evaluated once per point per radius
    /// per simulation.
    /// </summary>
    private const int CircleSamples = 36;

    /// <summary>
    /// Clark–Evans nearest-neighbour index with its z-test, or null when there are too few points
    /// for a nearest neighbour to mean anything.
    /// </summary>
    public static ClarkEvansResult? ClarkEvans(IReadOnlyList<PlanarPoint> points, double areaM2)
    {
        if (points.Count < 2 || !(areaM2 > 0d))
        {
            return null;
        }

        var n = points.Count;
        var meanObserved = MeanNearestNeighbour(points);

        // Density, and the mean spacing a random scatter of that density produces. Both are the
        // textbook forms: expected mean is half the reciprocal square root of the density, and the
        // standard error of that mean falls as one over the root of the point count.
        var density = n / areaM2;
        var expected = 0.5d / Math.Sqrt(density);
        var standardError = 0.26136d / Math.Sqrt(n * density);

        var z = standardError > 0d ? (meanObserved - expected) / standardError : 0d;

        return new ClarkEvansResult(
            n,
            areaM2,
            meanObserved,
            expected,
            expected > 0d ? meanObserved / expected : 0d,
            z,
            TwoSidedNormalP(z));
    }

    /// <summary>
    /// Ripley's L over evenly-spaced radii, with a Monte-Carlo envelope from repeated random
    /// scatters of the same number of points over the same window.
    /// </summary>
    /// <param name="window">
    /// The ground the points sit on, in the same projected system as they are. Both the area used
    /// as the estimator's denominator and the random scatters come from it, so a window that is not
    /// the one the points were selected from produces a curve that is about nothing.
    /// </param>
    /// <param name="maxRadiusM">
    /// The largest distance to count neighbours within. Beyond roughly a quarter of the window's
    /// shorter side the estimate is mostly edge correction, so the caller is expected to keep it
    /// well inside that.
    /// </param>
    /// <param name="steps">How many radii to report, evenly spaced up to the maximum.</param>
    /// <param name="simulations">
    /// How many random patterns the envelope is drawn from. The band is their pointwise extremes,
    /// so a point of the observed curve outside it is a departure significant at about two over
    /// one-plus-the-count — a little under five per cent at nineteen, a little under two at ninety-nine.
    /// </param>
    /// <param name="seed">The seed the random scatters are drawn from.</param>
    public static RipleyResult? RipleyL(
        IReadOnlyList<PlanarPoint> points,
        Geometry window,
        double maxRadiusM,
        int steps,
        int simulations,
        int seed,
        CancellationToken ct = default)
    {
        if (points.Count < 2 || steps < 1 || !(maxRadiusM > 0d))
        {
            return null;
        }

        var areaM2 = window.Area;
        if (!(areaM2 > 0d))
        {
            return null;
        }

        var radii = new double[steps];
        for (var s = 0; s < steps; s++)
        {
            radii[s] = maxRadiusM * (s + 1) / steps;
        }

        var prepared = PreparedGeometryFactory.Prepare(window);
        var boundary = window.Boundary;

        var observed = LCurve(points, radii, areaM2, prepared, boundary, ct);

        var lower = new double[steps];
        var upper = new double[steps];
        Array.Fill(lower, double.PositiveInfinity);
        Array.Fill(upper, double.NegativeInfinity);

        var random = new DeterministicRandom(seed);
        var envelope = window.EnvelopeInternal;

        var completed = 0;
        for (var sim = 0; sim < simulations; sim++)
        {
            // The loop is the expensive part — every simulation walks every pair at every radius —
            // so it is checked between simulations as well as inside the curve. A caller who has
            // navigated away should not still own a core.
            ct.ThrowIfCancellationRequested();

            var scatter = RandomScatter(points.Count, prepared, envelope, ref random, ct);
            if (scatter.Count < points.Count)
            {
                // The window is so thin relative to its bounding box that placing points in it by
                // rejection did not finish. Reporting a band drawn from fewer points than the
                // observed pattern would compare two different things, so no band is reported.
                break;
            }

            var simulated = LCurve(scatter, radii, areaM2, prepared, boundary, ct);
            for (var s = 0; s < steps; s++)
            {
                lower[s] = Math.Min(lower[s], simulated[s]);
                upper[s] = Math.Max(upper[s], simulated[s]);
            }

            completed++;
        }

        var stepsOut = new List<RipleyStep>(steps);
        for (var s = 0; s < steps; s++)
        {
            var hasBand = completed > 0
                && !double.IsInfinity(lower[s]) && !double.IsInfinity(upper[s]);
            stepsOut.Add(new RipleyStep(
                radii[s],
                observed[s],
                hasBand ? lower[s] : null,
                hasBand ? upper[s] : null));
        }

        return new RipleyResult(points.Count, areaM2, completed, seed, stepsOut);
    }

    /// <summary>
    /// The bearings of the lines joining pairs of points, as orientation samples ready for the same
    /// rose the passage trends are drawn on.
    ///
    /// <para>
    /// <b>What this measures that the spacing statistics do not.</b> Clark–Evans and Ripley answer
    /// how <i>thickly</i> points sit; neither can see a direction. But entrances strung out along a
    /// fault or a bedding strike are clustered and <i>aligned</i>, and the alignment is the part
    /// that says which structure put them there. Taking the bearing of every joining line and
    /// binning it turns that into the same picture a cave's passage trends are already read from —
    /// a preferred direction shows as a lobe, and ground with no structural control comes out
    /// round.
    /// </para>
    /// <para>
    /// <b>It is axial and it is weighted by separation.</b> A line from A to B and the same line
    /// from B to A are one alignment, so bearings are folded into the half-circle by the rose
    /// itself. The weight is the separation, because a pair a kilometre apart is evidence of a
    /// kilometre-long lineament and a pair thirty metres apart is barely evidence of a direction at
    /// all — its bearing is dominated by whichever side of the doline each entrance opens on. The
    /// count-weighted rose is offered beside it, as everywhere else, and the two disagreeing is
    /// itself the reading.
    /// </para>
    /// <para>
    /// <b>Distant pairs are dropped, not kept.</b> Every pair in the window contributes something,
    /// and across a wide window most pairs are long ones that join caves in unrelated massifs; kept,
    /// they swamp the local structure with a smooth background whose shape is the window's, not the
    /// karst's. So a separation ceiling is required, and the caller states it.
    /// </para>
    /// </summary>
    /// <param name="maxSeparationM">
    /// The longest joining line that counts. Pairs further apart than this are not evidence of a
    /// shared lineament and are left out.
    /// </param>
    /// <param name="minSeparationM">
    /// The shortest one. Two entrances a few metres apart share a doline rather than a direction,
    /// and their bearing is noise given full weight by being counted at all.
    /// </param>
    public static IReadOnlyList<OrientationSample> PairAzimuths(
        IReadOnlyList<PlanarPoint> points,
        double maxSeparationM,
        double minSeparationM,
        CancellationToken ct = default)
    {
        var samples = new List<OrientationSample>();
        if (points.Count < 2 || !(maxSeparationM > 0d))
        {
            return samples;
        }

        for (var i = 0; i < points.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[j].X - points[i].X;
                var dy = points[j].Y - points[i].Y;
                var separation = Math.Sqrt((dx * dx) + (dy * dy));

                if (separation > maxSeparationM || separation < minSeparationM || separation <= 0d)
                {
                    continue;
                }

                // Grid bearing, clockwise from north: atan2 of easting over northing, and not the
                // other way round. The mathematical convention measures anticlockwise from east,
                // which would produce a rose mirrored about the north-east diagonal — a picture
                // that looks entirely plausible and points the wrong way.
                var bearing = Math.Atan2(dx, dy) * 180d / Math.PI;
                samples.Add(new OrientationSample(bearing, separation));
            }
        }

        return samples;
    }

    /// <summary>Mean distance from each point to its nearest other point.</summary>
    private static double MeanNearestNeighbour(IReadOnlyList<PlanarPoint> points)
    {
        var total = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var nearest = double.PositiveInfinity;
            for (var j = 0; j < points.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                var d2 = (dx * dx) + (dy * dy);
                if (d2 < nearest)
                {
                    nearest = d2;
                }
            }

            total += Math.Sqrt(nearest);
        }

        return total / points.Count;
    }

    /// <summary>
    /// L(t) at each radius: the edge-corrected K, taken through the square root that turns the
    /// expected curve under randomness into the straight line L(t) = t.
    /// </summary>
    private static double[] LCurve(
        IReadOnlyList<PlanarPoint> points,
        double[] radii,
        double areaM2,
        IPreparedGeometry window,
        Geometry boundary,
        CancellationToken ct)
    {
        var n = points.Count;
        var steps = radii.Length;
        var maxRadius = radii[^1];

        // Neighbour counts per point per radius, cumulative in radius.
        var counts = new int[n, steps];
        for (var i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (var j = i + 1; j < n; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d > maxRadius)
                {
                    continue;
                }

                var first = FirstRadiusAtLeast(radii, d);
                for (var s = first; s < steps; s++)
                {
                    counts[i, s]++;
                    counts[j, s]++;
                }
            }
        }

        var factory = boundary.Factory;
        var k = new double[steps];
        for (var i = 0; i < n; i++)
        {
            // The edge correction below samples a circle per point per radius, so this loop is the
            // other half of the cost and is checked for the same reason.
            ct.ThrowIfCancellationRequested();
            var point = factory.CreatePoint(new Coordinate(points[i].X, points[i].Y));
            var toEdge = boundary.Distance(point);

            for (var s = 0; s < steps; s++)
            {
                if (counts[i, s] == 0)
                {
                    continue;
                }

                // A circle that does not reach the boundary is wholly inside the window, so the
                // sampling can be skipped for it — which is most points at most radii.
                var weight = toEdge >= radii[s]
                    ? 1d
                    : CircleFractionInside(points[i], radii[s], window, factory);

                if (weight > 0d)
                {
                    k[s] += counts[i, s] / weight;
                }
            }
        }

        var l = new double[steps];
        for (var s = 0; s < steps; s++)
        {
            var kHat = areaM2 * k[s] / ((double)n * n);
            l[s] = Math.Sqrt(kHat / Math.PI);
        }

        return l;
    }

    /// <summary>Index of the first radius at least as large as <paramref name="distance"/>.</summary>
    private static int FirstRadiusAtLeast(double[] radii, double distance)
    {
        for (var s = 0; s < radii.Length; s++)
        {
            if (radii[s] >= distance)
            {
                return s;
            }
        }

        return radii.Length;
    }

    /// <summary>
    /// What fraction of a circle of the given radius, centred on the point, lies inside the window,
    /// measured by sampling evenly-spaced bearings. Never zero: a point inside the window always
    /// counts for at least one sample's worth, so a neighbour count is never divided by nothing.
    /// </summary>
    private static double CircleFractionInside(
        PlanarPoint centre, double radius, IPreparedGeometry window, GeometryFactory factory)
    {
        var inside = 0;
        for (var s = 0; s < CircleSamples; s++)
        {
            var angle = 2d * Math.PI * s / CircleSamples;
            var probe = factory.CreatePoint(new Coordinate(
                centre.X + (radius * Math.Cos(angle)),
                centre.Y + (radius * Math.Sin(angle))));
            if (window.Contains(probe))
            {
                inside++;
            }
        }

        return Math.Max(inside, 1) / (double)CircleSamples;
    }

    /// <summary>
    /// <paramref name="count"/> points scattered uniformly over the window, by drawing in its
    /// bounding box and keeping what lands inside. The attempt budget bounds a window so thin that
    /// rejection would not finish; the caller treats a short scatter as no envelope rather than as
    /// a smaller one.
    /// </summary>
    private static List<PlanarPoint> RandomScatter(
        int count,
        IPreparedGeometry window,
        Envelope box,
        ref DeterministicRandom random,
        CancellationToken ct)
    {
        var factory = window.Geometry.Factory;
        var points = new List<PlanarPoint>(count);
        var attempts = 0;
        var budget = Math.Max(1_000, count * 200);

        while (points.Count < count && attempts < budget)
        {
            attempts++;
            if ((attempts & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var x = box.MinX + (random.NextDouble() * box.Width);
            var y = box.MinY + (random.NextDouble() * box.Height);
            if (window.Contains(factory.CreatePoint(new Coordinate(x, y))))
            {
                points.Add(new PlanarPoint(x, y));
            }
        }

        return points;
    }

    /// <summary>
    /// Two-sided tail probability of the standard normal, from a rational approximation of the
    /// complementary error function accurate to better than one part in a million — far finer than
    /// a p-value read off a Monte-Carlo-sized sample is ever quoted to.
    /// </summary>
    private static double TwoSidedNormalP(double z)
    {
        var x = Math.Abs(z) / Math.Sqrt(2d);
        var t = 1d / (1d + (0.5d * x));
        var series = -(x * x) - 1.26551223d + (t * (1.00002368d + (t * (0.37409196d
            + (t * (0.09678418d + (t * (-0.18628806d + (t * (0.27886807d + (t * (-1.13520398d
            + (t * (1.48851587d + (t * (-0.82215223d + (t * 0.17087277d))))))))))))))))); 

        return Math.Clamp(t * Math.Exp(series), 0d, 1d);
    }

    /// <summary>
    /// A small counter-based generator, written out here rather than taken from the framework so a
    /// stated seed means the same numbers on every machine and every runtime version. An envelope
    /// whose seed does not reproduce is not a stated seed.
    /// </summary>
    private struct DeterministicRandom(int seed)
    {
        private ulong state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;

        /// <summary>The next value in [0, 1).</summary>
        public double NextDouble()
        {
            // SplitMix64: one multiply-shift mixing round over an incrementing counter.
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            var z = state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            z ^= z >> 31;

            return (z >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
