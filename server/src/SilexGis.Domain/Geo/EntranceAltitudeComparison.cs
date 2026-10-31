// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>What became of setting an entrance's recorded altitude beside the ground height under it.</summary>
public enum EntranceAltitudeVerdict : short
{
    /// <summary>Nobody recorded an altitude, so there is nothing to compare; the modelled one may fill it.</summary>
    NotRecorded = 0,

    /// <summary>
    /// The elevation data could not answer at this place — no terrain built, the place outside what
    /// was built, or a void. Distinct from agreement: an unread place is a gap, never a match.
    /// </summary>
    NotModelled = 1,

    /// <summary>The two are within what the elevation model's own error explains.</summary>
    Agrees = 2,

    /// <summary>
    /// The two differ by more than the model's error explains. This is a statement that the pair
    /// disagrees and not a verdict on which side is wrong: a recorded altitude read off a barometer,
    /// an entrance at the foot of a cliff and a mistyped coordinate all look the same here.
    /// </summary>
    Disagrees = 3,
}

/// <summary>An entrance's recorded altitude set beside the modelled ground height under it.</summary>
/// <param name="RecordedM">What the record says, metres; null when no altitude was recorded.</param>
/// <param name="ModelledM">The ground height the elevation data gives at the entrance's place,
/// metres, already in the survey's vertical datum; null when it could not answer.</param>
/// <param name="DifferenceM">Recorded minus modelled, metres. Positive means the record puts the
/// entrance above the modelled ground. Null unless both sides exist.</param>
/// <param name="Verdict">The outcome, which is what a reader acts on.</param>
public sealed record EntranceAltitudeComparison(
    decimal? RecordedM,
    double? ModelledM,
    double? DifferenceM,
    EntranceAltitudeVerdict Verdict)
{
    /// <summary>The size of the gap, whichever way it points; what the report is ranked by.</summary>
    public double? AbsoluteDifferenceM => DifferenceM is { } d ? Math.Abs(d) : null;

    /// <summary>Whether this pair is worth putting in front of somebody.</summary>
    public bool Disagrees => Verdict == EntranceAltitudeVerdict.Disagrees;
}

/// <summary>
/// Compares the altitude recorded for a cave entrance with the one the elevation model gives for
/// the same place — the data-quality signal that a large gap nearly always means a wrong coordinate.
///
/// <para>
/// Pure arithmetic. It knows nothing about who may see which entrance: the caller puts only the
/// entrances the reader may place exactly through it, because a rank order or a count over a set
/// that included a withheld entrance would say which entrances are withheld.
/// </para>
/// </summary>
public static class EntranceAltitudes
{
    /// <summary>
    /// How far apart the two altitudes may be, in metres, before the pair is called a disagreement.
    /// Twenty metres, a floor set by the elevation model rather than by taste: ground heights come
    /// from rasters of tens of metres per pixel, and an entrance at the foot of a cliff or in the
    /// bottom of a doline legitimately sits that far from the interpolated surface. The gaps worth
    /// chasing — the forty-metre ones that mean a coordinate is on the wrong hillside — are well
    /// clear of it.
    /// </summary>
    public const double ToleranceM = 20d;

    /// <summary>
    /// Compares one pair. A recorded altitude of zero is a recorded altitude (sea level is a real
    /// place); only null means nothing was recorded.
    /// </summary>
    public static EntranceAltitudeComparison Compare(decimal? recordedM, double? modelledM)
    {
        if (modelledM is { } m && !double.IsFinite(m))
        {
            modelledM = null;
        }

        if (recordedM is null)
        {
            return new EntranceAltitudeComparison(null, modelledM, null, EntranceAltitudeVerdict.NotRecorded);
        }

        if (modelledM is null)
        {
            return new EntranceAltitudeComparison(recordedM, null, null, EntranceAltitudeVerdict.NotModelled);
        }

        var difference = (double)recordedM.Value - modelledM.Value;
        var verdict = Math.Abs(difference) > ToleranceM
            ? EntranceAltitudeVerdict.Disagrees
            : EntranceAltitudeVerdict.Agrees;
        return new EntranceAltitudeComparison(recordedM, modelledM, difference, verdict);
    }
}
