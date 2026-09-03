// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One measurement at a height, as the level-band arithmetic sees it: where it sits vertically, and
/// how much of it there is.
/// </summary>
/// <param name="ElevationM">Height above the vertical datum, metres. Must be finite; a
/// non-finite value is a caller error and is dropped rather than dragging every band with it.</param>
/// <param name="WeightM">How much this measurement is worth. For passage this is metres of passage,
/// so a long gallery counts for more than a short stub at the same height. For a set of points —
/// entrance altitudes, spring altitudes — every measurement weighs one, and passing one is what
/// makes a count-based answer come out of the same arithmetic. Must be finite and not negative;
/// zero is legal and counts as an observation that contributes no length.</param>
public sealed record ElevationSample(double ElevationM, double WeightM);

/// <summary>
/// One sector of the elevation histogram.
/// </summary>
/// <param name="FromM">Lower edge, metres, inclusive.</param>
/// <param name="ToM">Upper edge, metres, exclusive — except on the topmost sector, whose upper edge
/// is closed so the highest measurement is inside the histogram rather than one sector above it.</param>
/// <param name="Count">How many measurements fell here.</param>
/// <param name="WeightM">What they weighed — metres of passage where the samples carried passage.</param>
/// <param name="CountFraction">This sector's share of the measurements, 0 when there were none.</param>
/// <param name="WeightFraction">This sector's share of the weight, 0 when nothing weighed anything.</param>
public sealed record ElevationBin(
    double FromM,
    double ToM,
    int Count,
    double WeightM,
    double CountFraction,
    double WeightFraction);

/// <summary>
/// One level the measurements appear to have been cut at — a proposal, never an assertion.
/// </summary>
/// <remarks>
/// The edges are the extremes actually measured inside the band, not the arithmetic's cut points, so
/// a band always describes real measurements and never claims vertical extent that nothing occupies.
/// </remarks>
/// <param name="FromM">Lowest measurement in the band, metres.</param>
/// <param name="ToM">Highest measurement in the band, metres.</param>
/// <param name="Count">How many measurements the band holds.</param>
/// <param name="WeightM">What they weigh together.</param>
/// <param name="WeightFraction">The band's share of the whole weight, which is the figure that says
/// whether a band is a storey or a curiosity.</param>
public sealed record ElevationBand(
    double FromM,
    double ToM,
    int Count,
    double WeightM,
    double WeightFraction);

/// <summary>
/// What a set of measured heights looks like, and what levels — if any — it appears to be cut at.
/// </summary>
/// <param name="SampleCount">How many measurements were used. Non-finite ones are not.</param>
/// <param name="TotalWeightM">What they weigh together.</param>
/// <param name="LowestM">Lowest measurement, metres; null when there were none.</param>
/// <param name="HighestM">Highest measurement, metres; null when there were none.</param>
/// <param name="BinWidthM">Width of one histogram sector, metres; zero when there is no histogram.</param>
/// <param name="Bins">The histogram in ascending order, an empty sector being a zero rather than a
/// missing entry, so a caller drawing it has no holes to special-case.</param>
/// <param name="Bands">The proposed levels in ascending order, or <b>empty</b> when the measurements
/// do not separate into any. Empty is the ordinary answer for a cave on one level and it is the
/// answer for a cave whose heights are simply spread out; those two are not distinguished here,
/// because the arithmetic cannot tell them apart and pretending otherwise would be the whole
/// failure this class is shaped to avoid.</param>
/// <param name="GoodnessOfVarianceFit">How much of the vertical variance the proposed bands account
/// for, 0..1, or null when nothing was proposed. It is reported because it is the conventional
/// companion to a natural-breaks classification and a reader expects it — but it is a diagnostic,
/// not the acceptance test, and is deliberately not what decides whether bands are proposed. It runs
/// high on measurements with no structure at all: an evenly spread population split down the middle
/// scores 0.75, and every extra band raises it, which is exactly how an unbounded classifier ends up
/// certain about noise.</param>
public sealed record ElevationBandProposal(
    int SampleCount,
    double TotalWeightM,
    double? LowestM,
    double? HighestM,
    double BinWidthM,
    IReadOnlyList<ElevationBin> Bins,
    IReadOnlyList<ElevationBand> Bands,
    double? GoodnessOfVarianceFit);

/// <summary>
/// The level-band arithmetic: an elevation histogram, and a proposal about the levels the
/// measurements appear to have been cut at.
///
/// <para>
/// <b>This is pure arithmetic over numbers.</b> It knows nothing about where the heights came from —
/// midpoints of surveyed passage, entrance altitudes, spring altitudes — and nothing about who may
/// see them. Whether a measurement may be counted at all is a question about position, and it is
/// settled before anything reaches here; a height is a coordinate, so a caller that lets a
/// measurement in has already decided the reader may place it.
/// </para>
/// <para>
/// <b>Weighted natural breaks, not kernel peak-picking.</b> Both find groupings; the difference is
/// what can be checked. A natural-breaks partition has one optimum for a given number of classes and
/// it can be computed by hand on a small set, so the implementation can be pinned against an answer
/// worked out independently of it. A kernel density estimate has no such answer: its peaks move with
/// the bandwidth, the bandwidth is itself a judgement, and a test can only assert that the code
/// agrees with the code. Classes are found by the exact dynamic program rather than by the iterative
/// approximation usually shipped under the name, so the result does not depend on a starting guess.
/// </para>
/// <para>
/// <b>What stops it finding storeys in noise.</b> Minimising within-class variance always improves
/// as classes are added, so a classifier asked only for the best partition will happily cut an
/// evenly-spread population into as many pieces as it is allowed and report a high fit for every one
/// of them. Three separate rules bound it, and each covers a case the others miss:
/// </para>
/// <list type="number">
/// <item><description><b>A cap of five bands</b>, because this proposes storeys to a person who has
/// to read them, and because the exact dynamic program costs class-count times the square of the
/// number of distinct heights. Five is more levels than a cave description normally distinguishes,
/// and the cap is the honest statement that this is an aid to reading a histogram rather than a
/// cluster analysis.</description></item>
/// <item><description><b>Every band holds at least two distinct heights.</b> A band built from one
/// measurement is not a level; it is the outlier that the classification had nowhere else to
/// put.</description></item>
/// <item><description><b>Every boundary must fall in a real gap</b> — see
/// <see cref="SeparationFactor"/> and <see cref="MinimumGapFraction"/>. This is the rule that
/// distinguishes two storeys from one spread-out cave, and it is a statement about the measurements
/// rather than about the classification: a level is a height range that is occupied with empty space
/// above and below it, which is what makes it visible in the histogram in the first place.</description></item>
/// </list>
/// <para>
/// The proposal is searched from the largest permitted number of bands downwards and the first
/// wholly surviving partition wins, so the answer carries as much detail as every one of its own
/// boundaries can justify. When none survives, no bands are proposed at all — one band spanning
/// everything is not returned, because a reader cannot tell "one storey" from "nothing found" and
/// only one of those is true.
/// </para>
/// </summary>
public static class ElevationBands
{
    /// <summary>The most levels that may be proposed.</summary>
    public const int MaximumBands = 5;

    /// <summary>The fewest levels worth proposing: below two there is no structure to report.</summary>
    public const int MinimumBands = 2;

    /// <summary>
    /// How many times the typical spacing between neighbouring measurements a gap must exceed before
    /// a band boundary may sit in it.
    /// </summary>
    /// <remarks>
    /// An evenly spread population cut in half has a gap of exactly one spacing at the cut, so any
    /// factor above one rejects it. Four is chosen to leave room for the ordinary unevenness of a
    /// small sample without admitting it as structure. This is the guard that works on small sets;
    /// on large ones the largest gap in an evenly spread population grows with the logarithm of the
    /// sample size and would eventually clear it, which is what
    /// <see cref="MinimumGapFraction"/> is for.
    /// </remarks>
    public const double SeparationFactor = 4d;

    /// <summary>
    /// The share of the whole vertical range a gap must span before a band boundary may sit in it,
    /// whatever the spacing says.
    /// </summary>
    /// <remarks>
    /// The absolute floor under <see cref="SeparationFactor"/>. It is what refuses a large, evenly
    /// spread set of heights, where the widest gap that occurs by chance is a few times the mean
    /// spacing but is still a small slice of the range — and it is what refuses a handful of
    /// measurements too few for a spacing to mean anything.
    /// </remarks>
    public const double MinimumGapFraction = 0.05d;

    /// <summary>
    /// How many distinct heights the break search may work on before they are collapsed onto an even
    /// vertical ladder of that many rungs.
    /// </summary>
    /// <remarks>
    /// The exact break search costs the square of this, so it is what keeps a cave of many thousands
    /// of survey legs from being expensive. Collapsing loses nothing a reader could use: a level
    /// boundary resolved more finely than a two-hundred-and-fifty-sixth of the cave's vertical range
    /// is finer than the survey that produced it.
    /// </remarks>
    public const int MaximumBreakResolution = 256;

    /// <summary>The most histogram sectors the range may be cut into.</summary>
    public const int MaximumBinCount = 24;

    private const double Epsilon = 1e-9;

    /// <summary>
    /// The width of one histogram sector for a given vertical range: the smallest width on the
    /// 1-2-5 ladder that covers the range in at most <see cref="MaximumBinCount"/> sectors.
    /// </summary>
    /// <remarks>
    /// A round width is not decoration. Sector edges are read as labels — "1200 to 1250 m" — and a
    /// width derived from the data alone gives edges like 1207.3 that a reader cannot compare
    /// between two caves. Both the width and the edges are therefore snapped to the ladder, which
    /// also means the same cave surveyed twice does not shift its sectors because one leg moved.
    /// </remarks>
    public static double BinWidthFor(double lowestM, double highestM)
    {
        var range = highestM - lowestM;
        if (!double.IsFinite(range) || range <= 0)
        {
            return 1d;
        }

        var raw = range / MaximumBinCount;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var step in (double[])[1d, 2d, 5d, 10d])
        {
            var width = step * magnitude;
            if (width >= raw - (Epsilon * magnitude))
            {
                return width;
            }
        }

        return 10d * magnitude;
    }

    /// <summary>
    /// The histogram of a set of heights, and the levels they appear to be cut at.
    /// </summary>
    public static ElevationBandProposal Summarize(IEnumerable<ElevationSample> samples)
    {
        var taken = new List<ElevationSample>();
        foreach (var sample in samples)
        {
            if (!double.IsFinite(sample.ElevationM))
            {
                continue;
            }

            var weight = double.IsFinite(sample.WeightM) && sample.WeightM > 0 ? sample.WeightM : 0d;
            taken.Add(new ElevationSample(sample.ElevationM, weight));
        }

        if (taken.Count == 0)
        {
            return new ElevationBandProposal(0, 0, null, null, 0, [], [], null);
        }

        taken.Sort(static (a, b) => a.ElevationM.CompareTo(b.ElevationM));
        var lowest = taken[0].ElevationM;
        var highest = taken[^1].ElevationM;
        var totalWeight = taken.Sum(s => s.WeightM);

        var bins = Histogram(taken, lowest, highest, totalWeight, out var binWidth);
        var (bands, fit) = Propose(taken, totalWeight);

        return new ElevationBandProposal(
            taken.Count, totalWeight, lowest, highest, binWidth, bins, bands, fit);
    }

    private static IReadOnlyList<ElevationBin> Histogram(
        List<ElevationSample> sorted,
        double lowest,
        double highest,
        double totalWeight,
        out double binWidth)
    {
        binWidth = BinWidthFor(lowest, highest);
        var first = Math.Floor(lowest / binWidth) * binWidth;
        var span = highest - first;
        var count = Math.Max(1, (int)Math.Ceiling((span / binWidth) - Epsilon));
        count = Math.Min(count, MaximumBinCount + 1);

        var counts = new int[count];
        var weights = new double[count];
        foreach (var sample in sorted)
        {
            // The topmost sector's upper edge is closed. Left open, the single highest measurement
            // would land in a sector of its own above the histogram, which reads as a level.
            var index = Math.Clamp((int)Math.Floor((sample.ElevationM - first) / binWidth), 0, count - 1);
            counts[index]++;
            weights[index] += sample.WeightM;
        }

        var bins = new ElevationBin[count];
        for (var i = 0; i < count; i++)
        {
            var from = first + (i * binWidth);
            bins[i] = new ElevationBin(
                from,
                from + binWidth,
                counts[i],
                weights[i],
                (double)counts[i] / sorted.Count,
                totalWeight > 0 ? weights[i] / totalWeight : 0d);
        }

        return bins;
    }

    /// <summary>
    /// The levels, or none. <paramref name="sorted"/> must be ascending by elevation.
    /// </summary>
    private static (IReadOnlyList<ElevationBand> Bands, double? Fit) Propose(
        List<ElevationSample> sorted, double totalWeight)
    {
        var positions = Positions(sorted);
        var m = positions.Count;
        if (m < MinimumBands * 2)
        {
            // Every band needs two distinct heights of its own, so there is not enough here to cut.
            return ([], null);
        }

        var x = new double[m];
        var w = new double[m];
        var weighed = 0d;
        for (var i = 0; i < m; i++)
        {
            x[i] = positions[i].ElevationM;
            w[i] = positions[i].WeightM;
            weighed += w[i];
        }

        if (weighed <= 0)
        {
            // Nothing here carries any length — entrance and spring altitudes arrive that way, and
            // so does a survey of zero-length legs. Every height then counts as one observation, so
            // the search classifies the heights themselves instead of finding every partition
            // equally costless and settling on whichever it looked at first. The reported band
            // weights are untouched by this: they stay the zeroes they are.
            Array.Fill(w, 1d);
        }

        // Prefix sums of weight, weighted value and weighted square, so the within-class sum of
        // squared deviations over any run of positions is a constant-time expression.
        var pw = new double[m + 1];
        var ps = new double[m + 1];
        var pq = new double[m + 1];
        for (var i = 0; i < m; i++)
        {
            pw[i + 1] = pw[i] + w[i];
            ps[i + 1] = ps[i] + (w[i] * x[i]);
            pq[i + 1] = pq[i] + (w[i] * x[i] * x[i]);
        }

        double Cost(int a, int b)
        {
            var weight = pw[b + 1] - pw[a];
            if (weight <= 0)
            {
                return 0d;
            }

            var sum = ps[b + 1] - ps[a];
            return Math.Max(0d, (pq[b + 1] - pq[a]) - (sum * sum / weight));
        }

        var total = Cost(0, m - 1);
        var maximum = Math.Min(MaximumBands, m / 2);

        // best[k][b] is the least achievable within-class sum of squares over positions 0..b cut
        // into k classes; cut[k][b] is where that partition's last class starts.
        var best = new double[maximum + 1][];
        var cut = new int[maximum + 1][];
        for (var k = 1; k <= maximum; k++)
        {
            best[k] = new double[m];
            cut[k] = new int[m];
        }

        for (var b = 0; b < m; b++)
        {
            best[1][b] = Cost(0, b);
            cut[1][b] = 0;
        }

        for (var k = 2; k <= maximum; k++)
        {
            for (var b = k - 1; b < m; b++)
            {
                var lowestCost = double.PositiveInfinity;
                var lowestStart = k - 1;
                for (var a = k - 1; a <= b; a++)
                {
                    var candidate = best[k - 1][a - 1] + Cost(a, b);
                    if (candidate < lowestCost)
                    {
                        lowestCost = candidate;
                        lowestStart = a;
                    }
                }

                best[k][b] = lowestCost;
                cut[k][b] = lowestStart;
            }
        }

        // Most detail first: the answer is the largest number of bands whose every boundary is
        // justified, not the largest that merely fits.
        for (var k = maximum; k >= MinimumBands; k--)
        {
            var starts = Reconstruct(cut, k, m);
            if (!Survives(x, starts, m))
            {
                continue;
            }

            var bands = Assign(sorted, x, starts, totalWeight);
            var fit = total > 0 ? Math.Clamp(1d - (best[k][m - 1] / total), 0d, 1d) : 0d;
            return (bands, fit);
        }

        return ([], null);
    }

    /// <summary>Distinct heights with their weights accumulated, thinned to a workable number.</summary>
    private static List<ElevationSample> Positions(List<ElevationSample> sorted)
    {
        var distinct = new List<ElevationSample>();
        foreach (var sample in sorted)
        {
            if (distinct.Count > 0 && distinct[^1].ElevationM == sample.ElevationM)
            {
                distinct[^1] = distinct[^1] with { WeightM = distinct[^1].WeightM + sample.WeightM };
                continue;
            }

            distinct.Add(sample);
        }

        if (distinct.Count <= MaximumBreakResolution)
        {
            return distinct;
        }

        // Collapse onto an even vertical ladder, each rung standing at the weighted mean of what
        // fell on it — or its plain mean where nothing there weighed anything, so a rung of
        // zero-weight measurements still occupies the height it was measured at rather than zero.
        var lowest = distinct[0].ElevationM;
        var highest = distinct[^1].ElevationM;
        var step = (highest - lowest) / MaximumBreakResolution;
        var weightSums = new double[MaximumBreakResolution];
        var valueSums = new double[MaximumBreakResolution];
        var plainSums = new double[MaximumBreakResolution];
        var counts = new int[MaximumBreakResolution];

        foreach (var sample in distinct)
        {
            var rung = Math.Clamp(
                (int)Math.Floor((sample.ElevationM - lowest) / step), 0, MaximumBreakResolution - 1);
            weightSums[rung] += sample.WeightM;
            valueSums[rung] += sample.WeightM * sample.ElevationM;
            plainSums[rung] += sample.ElevationM;
            counts[rung]++;
        }

        var collapsed = new List<ElevationSample>(MaximumBreakResolution);
        for (var i = 0; i < MaximumBreakResolution; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            var at = weightSums[i] > 0 ? valueSums[i] / weightSums[i] : plainSums[i] / counts[i];
            collapsed.Add(new ElevationSample(at, weightSums[i]));
        }

        return collapsed;
    }

    private static int[] Reconstruct(int[][] cut, int k, int m)
    {
        var starts = new int[k];
        var end = m - 1;
        for (var level = k; level >= 1; level--)
        {
            var start = cut[level][end];
            starts[level - 1] = start;
            end = start - 1;
        }

        return starts;
    }

    /// <summary>
    /// Whether every boundary of a partition sits in a gap wide enough to be a gap, and every class
    /// holds at least two distinct heights.
    /// </summary>
    private static bool Survives(double[] x, int[] starts, int m)
    {
        var range = x[m - 1] - x[0];
        var floor = range * MinimumGapFraction;

        for (var i = 0; i < starts.Length; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Length ? starts[i + 1] - 1 : m - 1;
            if (end - start < 1)
            {
                return false;
            }
        }

        for (var i = 1; i < starts.Length; i++)
        {
            var lowerStart = starts[i - 1];
            var lowerEnd = starts[i] - 1;
            var upperStart = starts[i];
            var upperEnd = i + 1 < starts.Length ? starts[i + 1] - 1 : m - 1;

            var gap = x[upperStart] - x[lowerEnd];
            var spans = (x[lowerEnd] - x[lowerStart]) + (x[upperEnd] - x[upperStart]);
            var steps = (lowerEnd - lowerStart) + (upperEnd - upperStart);
            var spacing = steps > 0 ? spans / steps : 0d;

            if (gap < Math.Max(SeparationFactor * spacing, floor))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Turn a partition of the working positions back into bands over the original measurements.
    /// </summary>
    /// <remarks>
    /// The cut is placed midway across each gap rather than at a position, so it is well defined for
    /// measurements that were collapsed onto the ladder and did not survive as positions of their
    /// own. Each band then reports the extremes actually found inside it.
    /// </remarks>
    private static IReadOnlyList<ElevationBand> Assign(
        List<ElevationSample> sorted, double[] x, int[] starts, double totalWeight)
    {
        var k = starts.Length;
        var cuts = new double[k - 1];
        for (var i = 1; i < k; i++)
        {
            cuts[i - 1] = (x[starts[i] - 1] + x[starts[i]]) / 2d;
        }

        var lows = new double[k];
        var highs = new double[k];
        var counts = new int[k];
        var weights = new double[k];
        Array.Fill(lows, double.PositiveInfinity);
        Array.Fill(highs, double.NegativeInfinity);

        foreach (var sample in sorted)
        {
            var band = 0;
            while (band < cuts.Length && sample.ElevationM >= cuts[band])
            {
                band++;
            }

            counts[band]++;
            weights[band] += sample.WeightM;
            lows[band] = Math.Min(lows[band], sample.ElevationM);
            highs[band] = Math.Max(highs[band], sample.ElevationM);
        }

        var bands = new List<ElevationBand>(k);
        for (var i = 0; i < k; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            bands.Add(new ElevationBand(
                lows[i],
                highs[i],
                counts[i],
                weights[i],
                totalWeight > 0 ? weights[i] / totalWeight : 0d));
        }

        return bands;
    }
}
