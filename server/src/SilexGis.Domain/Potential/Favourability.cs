// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>
/// The arithmetic of a knowledge-driven potential map: each layer's value rescored to a
/// favourability between nought and one, and the scores combined by weight.
/// </summary>
public static class Favourability
{
    /// <summary>
    /// Linear fuzzy membership: nought at <paramref name="low"/>, one at <paramref name="high"/>,
    /// a straight line between, and flat beyond either end.
    /// </summary>
    /// <remarks>
    /// The bounds may run either way. With <paramref name="low"/> the larger, membership falls as
    /// the value rises, which is how "the nearer the better" is written.
    /// </remarks>
    public static double FuzzyLinear(double low, double high, double value)
    {
        if (low == high)
        {
            throw new ArgumentException("A ramp needs two different bounds.", nameof(high));
        }

        return Math.Clamp((value - low) / (high - low), 0, 1);
    }

    /// <summary>
    /// Which row of a classes table a value falls in: the first whose bound lies above it. A value
    /// equal to a bound belongs to the class that starts there.
    /// </summary>
    /// <remarks>
    /// The one statement of where a class ends. A table scored by judgment and a table whose
    /// weights are counted from known caves must put the same ground in the same class, or the
    /// same model would draw two different maps of one hillside.
    /// </remarks>
    public static int ClassIndex(IReadOnlyList<EvidenceClass> classes, double value)
    {
        ArgumentNullException.ThrowIfNull(classes);
        for (var i = 0; i < classes.Count; i++)
        {
            if (classes[i].UpperBound is not { } bound || value < bound)
            {
                return i;
            }
        }

        throw new ArgumentException("A classes table ends with a row that has no upper bound.", nameof(classes));
    }

    /// <summary>The score of the class a value falls in, by <see cref="ClassIndex"/>.</summary>
    public static double Reclassify(IReadOnlyList<EvidenceClass> classes, double value) =>
        classes[ClassIndex(classes, value)].Score ?? 0;

    /// <summary>
    /// A bearing brought into [0, 360): 360 is north again, and belongs with 0 rather than in the
    /// last class of a table.
    /// </summary>
    public static double OnTheCompass(double bearing) => ((bearing % 360) + 360) % 360;

    /// <summary>
    /// What a cell of a measured layer scores under a transform, or null where the cell holds no
    /// measurement.
    /// </summary>
    public static double? Score(EvidenceLayer layer, EvidenceTransform transform, double value)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(transform);
        if (!double.IsFinite(value))
        {
            return null;
        }

        if (layer.ValueKind == EvidenceValueKind.Circular)
        {
            value = OnTheCompass(value);
        }

        return transform.Kind switch
        {
            EvidenceTransformKind.Classes => Reclassify(transform.Classes!, value),
            EvidenceTransformKind.FuzzyLinear => FuzzyLinear(transform.Low!.Value, transform.High!.Value, value),
            _ => throw new ArgumentException("A measured layer is scored by classes or by a ramp.", nameof(transform)),
        };
    }

    /// <summary>
    /// What a cell of a categorical layer scores, or null where the cell has no class. A class the
    /// model does not mention scores nought: it was not judged favourable.
    /// </summary>
    public static double? ScoreOfCategory(EvidenceTransform transform, string? code)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (code is null)
        {
            return null;
        }

        foreach (var category in transform.Categories ?? [])
        {
            if (string.Equals(category.Code, code, StringComparison.Ordinal))
            {
                return category.Score ?? 0;
            }
        }

        return 0;
    }

    /// <summary>
    /// The weighted mean of the layers' scores in one cell, or null when any layer has nothing to
    /// say there.
    /// </summary>
    /// <remarks>
    /// A cell missing one layer is left empty rather than averaged over the rest: the remaining
    /// weights would be silently rescaled, and two neighbouring cells would then be scored by two
    /// different models with nothing on the map to say so. The weights need not sum to one.
    /// </remarks>
    public static double? Combine(ReadOnlySpan<double?> scores, ReadOnlySpan<double> weights)
    {
        if (scores.Length != weights.Length || scores.Length == 0)
        {
            throw new ArgumentException("One weight for every score.", nameof(weights));
        }

        var sum = 0d;
        var total = 0d;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i] is not { } score)
            {
                return null;
            }

            sum += score * weights[i];
            total += weights[i];
        }

        return total > 0 ? sum / total : null;
    }
}
