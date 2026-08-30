// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One measured piece of passage as a dip statistic sees it: how steeply it runs, and how much of
/// it there is.
/// </summary>
/// <param name="DipDegrees">Inclination of the piece, degrees, positive upwards, in −90..90.
/// Values outside that range are a caller error and are clamped into it rather than silently
/// binned at an edge that means something else.</param>
/// <param name="WeightM">How much passage this piece is, metres. Must be finite and not negative;
/// zero is legal and counts as one observation without contributing any length.</param>
public sealed record DipSample(double DipDegrees, double WeightM);

/// <summary>
/// How steeply a set of passage runs.
/// </summary>
/// <param name="SampleCount">How many pieces of passage were measured.</param>
/// <param name="TotalLengthM">How many metres they add up to.</param>
/// <param name="MeanDipDegrees">The average inclination, degrees, positive upwards, weighted by
/// length where there is any length to weight by. Near zero for a system that climbs as much as it
/// descends, which most caves do — so this number says how <i>balanced</i> the passage is, not how
/// steep it is. Null when there is nothing to average.</param>
/// <param name="MeanAbsoluteDipDegrees">The average steepness regardless of direction, degrees in
/// 0..90, weighted the same way. This is the number that says how steep the cave is: a shaft and a
/// horizontal gallery differ here, and can be identical in
/// <paramref name="MeanDipDegrees"/>.</param>
/// <param name="MinimumDipDegrees">The least inclination observed, degrees, positive upwards. This
/// is the steepest descent only for a cave that has any descending passage at all: for one whose
/// every leg climbs it is the gentlest climb, and is positive. Named for what it is rather than for
/// what it usually means, because a field called "steepest descent" reporting a climb is a figure a
/// reader cannot tell is wrong.</param>
/// <param name="MaximumDipDegrees">The greatest inclination observed, degrees, positive upwards —
/// the steepest ascent where there is one, and otherwise the gentlest descent. See
/// <paramref name="MinimumDipDegrees"/>.</param>
/// <param name="Bins">The full histogram in ascending order, an empty sector being a zero rather
/// than a missing entry. The sector edges run from −90 to 90 here, not over the folded half-circle
/// an orientation rose uses, and the top edge is closed rather than opening a further sector.</param>
public sealed record DipSummary(
    int SampleCount,
    double TotalLengthM,
    double? MeanDipDegrees,
    double? MeanAbsoluteDipDegrees,
    double? MinimumDipDegrees,
    double? MaximumDipDegrees,
    IReadOnlyList<OrientationBin> Bins);

/// <summary>
/// The dip arithmetic for a set of surveyed passage: how steeply it runs and how that steepness is
/// distributed.
///
/// <para>
/// <b>Dip is not circular and is not axial</b>, which is what separates this from the orientation
/// arithmetic next to it. A bearing wraps at 360° and a piece of passage running 010° is the same
/// trend as one running 190°, so bearings must be folded and averaged as doubled angles. An
/// inclination does neither: it is bounded at ±90° with no wrap to cross, and a leg climbing 30°
/// is emphatically not the same passage as one dropping 30°. So the mean here is the ordinary
/// arithmetic one — and the sign is kept, because losing it turns a pitch and a ramp into the same
/// answer.
/// </para>
/// <para>
/// <b>There is no dip at all without altitudes.</b> A centerline drawn as a plan carries no third
/// coordinate, and the reduction that produces its segments fills the missing altitude with zero so
/// the result can be stored and drawn. Feeding those zeros through here would report a plan drawing
/// as a cave that is perfectly level everywhere, with total confidence. Callers must therefore
/// refuse to summarise dip at all for line work with no altitudes rather than passing it samples
/// that read as flat; nothing in this class can tell the two apart.
/// </para>
/// </summary>
public static class DipStatistics
{
    /// <summary>Width of one histogram sector, degrees.</summary>
    public const double BinWidthDegrees = 10d;

    /// <summary>How many sectors span the −90..90 range at <see cref="BinWidthDegrees"/>.</summary>
    public const int BinCount = 18;

    /// <summary>Which sector an inclination falls in, 0 for −90..−80 up to 17 for 80..90.</summary>
    /// <remarks>
    /// The top edge belongs to the last sector rather than opening a nineteenth: a leg that is
    /// exactly vertical is a pitch, and a histogram with a sector holding only the single value
    /// +90 would be an artefact of the arithmetic rather than anything about the cave.
    /// </remarks>
    public static int BinIndexOf(double dipDegrees)
    {
        var clamped = Math.Clamp(dipDegrees, -90d, 90d);
        var index = (int)Math.Floor((clamped + 90d) / BinWidthDegrees);
        return Math.Clamp(index, 0, BinCount - 1);
    }

    /// <summary>
    /// How steeply a set of passage runs, both ways of counting it.
    /// </summary>
    /// <remarks>
    /// The means are weighted by length whenever the samples carry any, because a metre of passage
    /// is what a dip describes and a survey that put forty stations down one pitch and one across a
    /// gallery would otherwise report the pitch forty times over. Where every weight is zero the
    /// means fall back to counting observations, so a set of degenerate legs still answers rather
    /// than dividing by zero.
    /// </remarks>
    public static DipSummary Summarize(IEnumerable<DipSample> samples)
    {
        var taken = samples as IReadOnlyList<DipSample> ?? [.. samples];
        var counts = new int[BinCount];
        var lengths = new double[BinCount];

        var totalLength = 0d;
        var weightedSum = 0d;
        var weightedAbsoluteSum = 0d;
        var plainSum = 0d;
        var plainAbsoluteSum = 0d;
        var lowest = double.PositiveInfinity;
        var highest = double.NegativeInfinity;

        foreach (var sample in taken)
        {
            var dip = Math.Clamp(sample.DipDegrees, -90d, 90d);
            var weight = double.IsFinite(sample.WeightM) && sample.WeightM > 0 ? sample.WeightM : 0d;
            var bin = BinIndexOf(dip);

            counts[bin]++;
            lengths[bin] += weight;
            totalLength += weight;
            weightedSum += dip * weight;
            weightedAbsoluteSum += Math.Abs(dip) * weight;
            plainSum += dip;
            plainAbsoluteSum += Math.Abs(dip);
            lowest = Math.Min(lowest, dip);
            highest = Math.Max(highest, dip);
        }

        if (taken.Count == 0)
        {
            return new DipSummary(0, 0, null, null, null, null, Bins(counts, lengths, 0, 0));
        }

        var mean = totalLength > 0 ? weightedSum / totalLength : plainSum / taken.Count;
        var meanAbsolute = totalLength > 0
            ? weightedAbsoluteSum / totalLength
            : plainAbsoluteSum / taken.Count;

        return new DipSummary(
            taken.Count,
            totalLength,
            mean,
            meanAbsolute,
            lowest,
            highest,
            Bins(counts, lengths, taken.Count, totalLength));
    }

    private static IReadOnlyList<OrientationBin> Bins(
        int[] counts, double[] lengths, int totalCount, double totalLength)
    {
        var bins = new OrientationBin[BinCount];
        for (var i = 0; i < BinCount; i++)
        {
            var from = -90d + (i * BinWidthDegrees);
            bins[i] = new OrientationBin(
                from,
                from + BinWidthDegrees,
                counts[i],
                lengths[i],
                totalCount > 0 ? (double)counts[i] / totalCount : 0d,
                totalLength > 0 ? lengths[i] / totalLength : 0d);
        }

        return bins;
    }
}
