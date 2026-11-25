// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>
/// Which class of a layer each cell falls in — the step a model trained on known caves takes
/// before it counts anything, since its weights are per class and not per value.
/// </summary>
public static class EvidenceClassing
{
    /// <summary>What a cell with no reading is given in place of a class.</summary>
    public const int NoClass = -1;

    /// <summary>
    /// How many classes a layer is cut into: the rows of its table, or for a layer that is classes
    /// already, every class it has.
    /// </summary>
    /// <remarks>
    /// A categorical layer is counted over its whole list whatever the model mentions. Leaving a
    /// rock out of the list would not take its ground out of the area, and ground that belongs to
    /// no class cannot be counted for or against anything.
    /// </remarks>
    public static int ClassCount(EvidenceLayer layer, EvidenceTransform transform)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(transform);
        if (layer.ValueKind == EvidenceValueKind.Categorical)
        {
            return layer.Categories.Count;
        }

        return transform is { Kind: EvidenceTransformKind.Classes, Classes.Count: > 0 }
            ? transform.Classes.Count
            : throw new ArgumentException("A measured layer is cut into classes by a table of bounds.", nameof(transform));
    }

    /// <summary>The class of one cell value, or <see cref="NoClass"/> where the cell holds no reading.</summary>
    /// <param name="value">
    /// A measurement, or for a categorical layer the position of the cell's class in the layer's
    /// own list, which is how such a layer is built.
    /// </param>
    public static int ClassOf(EvidenceLayer layer, EvidenceTransform transform, double value)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(transform);
        if (!double.IsFinite(value))
        {
            return NoClass;
        }

        if (layer.ValueKind == EvidenceValueKind.Categorical)
        {
            var index = Math.Round(value);
            return index >= 0 && index < layer.Categories.Count ? (int)index : NoClass;
        }

        if (transform is not { Kind: EvidenceTransformKind.Classes, Classes.Count: > 0 })
        {
            throw new ArgumentException("A measured layer is cut into classes by a table of bounds.", nameof(transform));
        }

        return Favourability.ClassIndex(
            transform.Classes,
            layer.ValueKind == EvidenceValueKind.Circular ? Favourability.OnTheCompass(value) : value);
    }

    /// <summary>The class of every cell of a built layer, in the layer's own cell order.</summary>
    public static int[] Classify(EvidenceLayer layer, EvidenceTransform transform, ReadOnlySpan<float> values)
    {
        // Asked once up front so a layer with no cells still refuses a transform it cannot use.
        _ = ClassCount(layer, transform);

        var classes = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            classes[i] = ClassOf(layer, transform, values[i]);
        }

        return classes;
    }
}
