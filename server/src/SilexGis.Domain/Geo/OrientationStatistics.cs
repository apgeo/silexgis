// SPDX-License-Identifier: AGPL-3.0-or-later
using Orientation = Therion.Orientation;

namespace SilexGis.Domain.Geo;

/// <summary>
/// One measured piece of passage as an orientation statistic sees it: which way it runs, and how
/// much of it there is.
/// </summary>
/// <param name="AzimuthDegrees">Bearing of the piece, degrees clockwise from north. Any value is
/// accepted and folded — the caller does not have to normalise, and must not fold it first, since
/// folding twice is harmless but folding into the wrong half-circle is not.</param>
/// <param name="WeightM">How much passage this piece is, metres. Must be finite and not negative.
/// Zero is legal and contributes nothing to the length-weighted answers while still counting as
/// one observation for the count-weighted ones.</param>
public sealed record OrientationSample(double AzimuthDegrees, double WeightM);

/// <summary>
/// One sector of a histogram over angles. Both weightings of a rose share these edges so that the
/// two histograms can be drawn on one axis and read against each other.
///
/// <para>
/// This record states no range of its own, because two histograms that do not share one are built
/// out of it: a rose runs over the folded 0–180 half-circle, and the dip histogram beside it runs
/// from −90 to 90 with its top edge closed rather than opening a further sector. The range is a
/// property of the consumer — see <see cref="OrientationSummary.Bins"/> and
/// <see cref="DipSummary.Bins"/> — and a reader that takes it from here instead will label one of
/// the two wrong.
/// </para>
/// </summary>
/// <param name="FromDegrees">Inclusive lower edge, degrees.</param>
/// <param name="ToDegrees">Upper edge, degrees; exclusive, except for a last sector the consumer
/// documents as closed.</param>
/// <param name="Count">How many pieces of passage fall in this sector.</param>
/// <param name="LengthM">How many metres of passage fall in this sector.</param>
/// <param name="CountFraction">This sector's share of the observations, 0–1. Zero when there are
/// no observations at all.</param>
/// <param name="LengthFraction">This sector's share of the total length, 0–1. Zero when the total
/// length is zero.</param>
public sealed record OrientationBin(
    double FromDegrees,
    double ToDegrees,
    int Count,
    double LengthM,
    double CountFraction,
    double LengthFraction);

/// <summary>
/// The orientation of a set of passage under one weighting.
/// </summary>
/// <param name="MeanAxisDegrees">The mean trend, degrees in 0–180. Null when there is nothing to
/// average, or when the sample is so evenly spread that no direction survives the vector sum — in
/// which case there genuinely is no mean trend, and reporting the arbitrary angle that falls out
/// of a near-zero resultant would be reporting noise as a finding.</param>
/// <param name="ResultantLength">How concentrated the trend is, 0–1. Zero means the passage runs
/// every way equally; one means every piece of it runs the same way. This is the doubled-angle
/// resultant, so it measures concentration about an <i>axis</i>: passage split evenly between
/// 010° and 190° scores one, not zero, because those are the same trend.</param>
/// <param name="EffectiveSampleSize">How many independent observations the concentration is worth.
/// Equal to the observation count under count weighting, and smaller than it under length
/// weighting whenever the lengths are uneven — a kilometre of passage measured as one leg and as
/// four hundred legs is not four hundred times the evidence.</param>
/// <param name="RayleighZ">The Rayleigh statistic. Null when there are fewer than two effective
/// observations, where the test says nothing.</param>
/// <param name="RayleighP">Probability of seeing a resultant at least this concentrated if the
/// passage had no preferred trend at all. Small means the trend is real; a value near one means
/// the rose is a picture of randomness. Null under the same condition as
/// <paramref name="RayleighZ"/>.</param>
/// <param name="EntropyNats">Shannon entropy of the sector histogram, in nats. Zero when all the
/// passage is in one sector; maximal when it is spread evenly across all of them. Null when there
/// is nothing to bin.</param>
/// <param name="EntropyNormalized">The entropy as a fraction of the most it could be for this
/// number of sectors, 0–1 — the form that compares between caves.</param>
public sealed record OrientationMeasure(
    double? MeanAxisDegrees,
    double ResultantLength,
    double EffectiveSampleSize,
    double? RayleighZ,
    double? RayleighP,
    double? EntropyNats,
    double? EntropyNormalized);

/// <summary>
/// What a set of passage orientations amounts to, both ways of counting it.
/// </summary>
/// <param name="SampleCount">How many pieces of passage were measured.</param>
/// <param name="TotalLengthM">How many metres they add up to.</param>
/// <param name="ByCount">Every piece of passage counts once, however long it is.</param>
/// <param name="ByLength">Every metre counts once, so a long straight gallery outweighs a
/// hundred short zig-zags.</param>
/// <param name="Bins">The sector histogram over the folded 0–180 half-circle, always the full set
/// of sectors in ascending order — an empty sector is a zero, never a missing entry, so a rose has
/// no holes in it.</param>
public sealed record OrientationSummary(
    int SampleCount,
    double TotalLengthM,
    OrientationMeasure ByCount,
    OrientationMeasure ByLength,
    IReadOnlyList<OrientationBin> Bins);

/// <summary>
/// The orientation arithmetic for a set of surveyed passage: which way a cave runs, how strongly
/// it runs that way, and whether that is more than chance.
///
/// <para>
/// <b>Passage trend is axial, not directional.</b> A leg surveyed from A to B running 010° and the
/// same leg surveyed from B to A running 190° describe one piece of passage with one trend. Every
/// bearing is therefore folded into the 0–180 half-circle before anything else happens, and the
/// averaging is done on <i>doubled</i> angles. Both steps are load-bearing and neither is
/// cosmetic: averaging bearings directly gives an answer pointing the wrong way whenever the
/// sample straddles north. A cave whose legs run 350°, 010° and 020° trends at about 007°, while
/// the arithmetic mean of those three numbers is 127° — very nearly at right angles to the
/// passage, and a rose drawn from it would show a cave that does not exist. Doubling maps the
/// half-circle onto a full circle, where the vector sum is well defined, and halving the result
/// brings it back.
/// </para>
/// <para>
/// <b>Both weightings are wanted, and they disagree on purpose.</b> Counting legs measures where
/// the surveyor put stations; counting metres measures where the cave is. A splay-free traverse is
/// still dominated by short legs — a tight meander gets a station every two metres and a straight
/// gallery gets one every thirty — so the count-weighted rose systematically over-reports the
/// meanders. Neither is the right one; a reader wants to see both, and the difference between them
/// is itself informative.
/// </para>
/// <para>
/// This is pure arithmetic over numbers. It knows nothing about where the bearings came from,
/// which line work produced them, or which legs were excluded as wall shots — those are the
/// caller's decisions, and folding them in here would put the same judgment in two places.
/// </para>
/// </summary>
public static class OrientationStatistics
{
    /// <summary>Width of one rose sector, degrees.</summary>
    public const double BinWidthDegrees = 10d;

    /// <summary>How many sectors span the folded half-circle.</summary>
    public const int BinCount = 18;

    /// <summary>
    /// Folds a bearing into the 0–180 half-circle, so that a leg and the same leg surveyed
    /// backwards land on the same trend. Accepts any value, including negative ones and ones past
    /// a full turn.
    /// </summary>
    public static double Fold(double azimuthDegrees) => Orientation.Azimuth.FoldAxial(azimuthDegrees);

    /// <summary>
    /// The index of the sector a bearing falls in, 0 to <see cref="BinCount"/> − 1. The upper edge
    /// of the last sector is closed rather than open, because a bearing of exactly 180° folds to
    /// exactly 0° and one a hair under it must not fall off the end of the histogram.
    /// </summary>
    public static int BinIndexOf(double azimuthDegrees)
    {
        var index = (int)(Fold(azimuthDegrees) / BinWidthDegrees);
        return index >= BinCount ? BinCount - 1 : index;
    }

    /// <summary>
    /// Measures a set of passage orientations, both weightings and the shared sector histogram.
    /// </summary>
    /// <remarks>
    /// An empty set is answered rather than refused: no observations, no length, a full row of
    /// zero sectors, and nulls wherever a statistic is undefined. A consumer drawing an empty rose
    /// wants the axis, not an exception.
    /// </remarks>
    public static OrientationSummary Summarize(IEnumerable<OrientationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var folded = new List<double>();
        var weights = new List<double>();
        var binCounts = new int[BinCount];
        var binLengths = new double[BinCount];

        foreach (var sample in samples)
        {
            if (!double.IsFinite(sample.WeightM) || sample.WeightM < 0d)
            {
                // A negative or non-finite weight would not fail loudly: it would quietly bend
                // every mean, resultant and sector share in the answer, and the rose would still
                // look like a rose.
                throw new ArgumentOutOfRangeException(
                    nameof(samples), sample.WeightM, "A passage length must be finite and not negative.");
            }

            var trend = Fold(sample.AzimuthDegrees);
            folded.Add(trend);
            weights.Add(sample.WeightM);

            var bin = (int)(trend / BinWidthDegrees);
            if (bin >= BinCount)
            {
                bin = BinCount - 1;
            }

            binCounts[bin]++;
            binLengths[bin] += sample.WeightM;
        }

        var totalLength = binLengths.Sum();
        var bins = new OrientationBin[BinCount];
        for (var i = 0; i < BinCount; i++)
        {
            bins[i] = new OrientationBin(
                i * BinWidthDegrees,
                (i + 1) * BinWidthDegrees,
                binCounts[i],
                binLengths[i],
                folded.Count == 0 ? 0d : binCounts[i] / (double)folded.Count,
                totalLength <= 0d ? 0d : binLengths[i] / totalLength);
        }

        var unit = new double[folded.Count];
        Array.Fill(unit, 1d);
        var countMass = binCounts.Select(c => (double)c).ToArray();

        return new OrientationSummary(
            folded.Count,
            totalLength,
            Measure([.. folded], unit, countMass),
            Measure([.. folded], [.. weights], binLengths),
            bins);
    }

    /// <summary>
    /// The mean trend, its concentration, the Rayleigh test and the histogram entropy for one
    /// weighting. <paramref name="binMass"/> is the same sample seen as sector totals, and is
    /// passed in rather than recomputed so both weightings bin on identical edges.
    /// </summary>
    private static OrientationMeasure Measure(double[] folded, double[] weights, double[] binMass)
    {
        var total = weights.Sum();
        if (folded.Length == 0 || total <= 0d)
        {
            // No observations, or observations that are all zero metres long. There is nothing to
            // point at; a resultant of zero says exactly that.
            return new OrientationMeasure(null, 0d, 0d, null, null, null, null);
        }

        var axial = Orientation.AxialStatistics.Compute(folded, weights);
        var meanAxis = axial.MeanAxisDegrees;
        var resultant = axial.ResultantLength;
        var effective = axial.EffectiveSampleSize;
        var rayleighZ = axial.RayleighZ;
        var rayleighP = axial.RayleighP;

        var mass = binMass.Sum();
        double? entropy = null;
        double? normalized = null;
        if (mass > 0d)
        {
            var h = 0d;
            foreach (var m in binMass)
            {
                if (m <= 0d)
                {
                    // An empty sector contributes nothing: p·ln p tends to zero as p does, and
                    // evaluating it would be a NaN in the middle of an otherwise finite sum.
                    continue;
                }

                var p = m / mass;
                h -= p * Math.Log(p);
            }

            entropy = h;
            normalized = h / Math.Log(BinCount);
        }

        return new OrientationMeasure(meanAxis, resultant, effective, rayleighZ, rayleighP, entropy, normalized);
    }
}
