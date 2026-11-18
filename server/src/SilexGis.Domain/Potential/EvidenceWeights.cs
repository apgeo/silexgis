// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>What the known caves say about one class of one evidence layer.</summary>
/// <remarks>
/// Counted in cells, not in caves: a cell either holds a known entrance or does not, and two
/// entrances in one cell are one cell that holds caves. That is the unit the method is defined
/// on, and it is what keeps the count of occupied cells in a class from exceeding the class.
/// </remarks>
/// <param name="Class">The class's position in the layer's table or list of classes.</param>
/// <param name="Cells">How many cells of the ground being weighed fall in the class.</param>
/// <param name="Occurrences">How many of those hold a known entrance the model was trained on.</param>
/// <param name="FrequencyRatio">
/// The class's share of the occupied cells over its share of the ground. One where the class
/// holds caves in proportion to its size, above one where it holds more, nought where it holds
/// none. Null for a class no cell falls in.
/// </param>
/// <param name="PositiveWeight">
/// W+, the natural logarithm of how much likelier a cell of this class is among occupied cells
/// than among the rest. Null for a class no cell falls in.
/// </param>
/// <param name="NegativeWeight">
/// W−, the same for ground outside the class. Null for a class no cell falls in or that no cell
/// falls outside of.
/// </param>
/// <param name="Contrast">C = W+ − W−: the strength of the association, in either direction.</param>
/// <param name="ContrastDeviation">s(C), the standard deviation of the contrast.</param>
/// <param name="StudentisedContrast">
/// C / s(C): how many of its own standard deviations the contrast lies from nought. A contrast
/// counted from a handful of cells is large and means little, and this is the column that says so.
/// </param>
/// <param name="Corrected">
/// Whether a half was added to each of the four counts before the weights were taken, because
/// one of them was nought.
/// </param>
public sealed record EvidenceClassWeights(
    int Class,
    int Cells,
    int Occurrences,
    double? FrequencyRatio,
    double? PositiveWeight,
    double? NegativeWeight,
    double? Contrast,
    double? ContrastDeviation,
    double? StudentisedContrast,
    bool Corrected);

/// <summary>One layer's table of weights, a row for every class in the layer's own order.</summary>
public sealed record TrainedEvidenceLayer(string Layer, IReadOnlyList<EvidenceClassWeights> Classes);

/// <summary>What a model learnt from the known caves of one area.</summary>
/// <param name="Layers">The weight tables, in the model's order of layers.</param>
/// <param name="Cells">How many cells of ground were weighed: inside the area and classed in every layer.</param>
/// <param name="Occurrences">How many of them hold an entrance the model was trained on.</param>
public sealed record TrainedEvidence(IReadOnlyList<TrainedEvidenceLayer> Layers, int Cells, int Occurrences)
{
    /// <summary>The chance that a cell picked blind holds a known entrance, before any evidence is read.</summary>
    public double PriorProbability => (double)Occurrences / Cells;
}

/// <summary>
/// Weights counted from known caves rather than given by judgment: the frequency ratio, and the
/// weights of evidence of Bonham-Carter, Agterberg and Wright ("Weights of evidence modelling: a
/// new approach to mapping mineral potential", Geological Survey of Canada Paper 89-9, 1989;
/// Bonham-Carter, "Geographic Information Systems for Geoscientists", 1994, chapter 9).
/// </summary>
/// <remarks>
/// <para>
/// Every figure here is a count over cells or follows from such counts in closed form. Nothing is
/// fitted, so the same cells and the same occupied cells give the same tables on every machine.
/// </para>
/// <para>
/// Nothing here takes or returns a position. The occupied cells are handed in by whoever read the
/// entrances, under that reader's own access, and what comes back is one row per class.
/// </para>
/// </remarks>
public static class EvidenceWeights
{
    /// <summary>
    /// What is added to each of a class's four counts when one of them is nought (the correction
    /// of Haldane and Anscombe for the logarithm of an odds ratio).
    /// </summary>
    public const double ContinuityCorrection = 0.5d;

    /// <summary>
    /// The row of one class, from four counts: the class's cells and occupied cells, and the whole
    /// ground's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <c>a</c> occupied cells in the class, <c>b</c> empty cells in it, <c>c</c> occupied cells
    /// outside it and <c>d</c> empty cells outside it:
    /// W+ = ln[(a / (a + c)) / (b / (b + d))], W− = ln[(c / (a + c)) / (d / (b + d))],
    /// s²(W+) = 1/a + 1/b, s²(W−) = 1/c + 1/d, C = W+ − W− and s(C) = √(s²(W+) + s²(W−)).
    /// </para>
    /// <para>
    /// A nought among the four would make a weight infinite: a class that holds no known cave is
    /// not thereby proven to hold none. So when any of the four is nought, a half is added to all
    /// four — and only for that class, so one empty class does not shift the figures of the rest —
    /// and the row says it was done. The frequency ratio needs no such help: a class with no
    /// occupied cell has a ratio of nought, which is a number.
    /// </para>
    /// <para>
    /// Two classes are beyond correcting. One that no cell falls in has nothing to be counted and
    /// gets an empty row. One that every cell falls in cannot tell any ground from any other: its
    /// ratio is one, its positive weight nought, and it has no negative weight or contrast,
    /// there being no ground outside it to speak of.
    /// </para>
    /// </remarks>
    public static EvidenceClassWeights OfClass(
        int classIndex, int cells, int occurrences, int totalCells, int totalOccurrences)
    {
        if (totalOccurrences <= 0 || totalOccurrences >= totalCells)
        {
            throw new ArgumentException(
                "Weights need some cells that hold a known entrance and some that do not.", nameof(totalOccurrences));
        }

        if (cells < 0 || cells > totalCells || occurrences < 0 || occurrences > cells
            || occurrences > totalOccurrences || totalOccurrences - occurrences > totalCells - cells)
        {
            throw new ArgumentException("A class's counts lie within the whole ground's.", nameof(cells));
        }

        if (cells == 0)
        {
            return new EvidenceClassWeights(classIndex, 0, 0, null, null, null, null, null, null, false);
        }

        var ratio = ((double)occurrences / totalOccurrences) / ((double)cells / totalCells);
        if (cells == totalCells)
        {
            return new EvidenceClassWeights(classIndex, cells, occurrences, ratio, 0d, null, null, null, null, false);
        }

        double a = occurrences;
        double b = cells - occurrences;
        double c = totalOccurrences - occurrences;
        double d = totalCells - cells - c;
        var corrected = a == 0 || b == 0 || c == 0 || d == 0;
        if (corrected)
        {
            a += ContinuityCorrection;
            b += ContinuityCorrection;
            c += ContinuityCorrection;
            d += ContinuityCorrection;
        }

        var positive = Math.Log((a / (a + c)) / (b / (b + d)));
        var negative = Math.Log((c / (a + c)) / (d / (b + d)));
        var contrast = positive - negative;
        var deviation = Math.Sqrt((1d / a) + (1d / b) + (1d / c) + (1d / d));
        return new EvidenceClassWeights(
            classIndex, cells, occurrences, ratio, positive, negative, contrast, deviation, contrast / deviation, corrected);
    }

    /// <summary>
    /// The ground a model is counted and mapped over: the cells inside the area that every layer
    /// puts in a class.
    /// </summary>
    /// <remarks>
    /// A cell missing one layer is left out altogether rather than weighed on the rest, for the
    /// reason a judged model leaves it empty: its figure would come from a different set of layers
    /// than its neighbour's with nothing on the map to say so. Leaving it out of the counting as
    /// well keeps every layer's table a table of the same ground.
    /// </remarks>
    /// <param name="classes">One array per layer: the class of every cell, <see cref="EvidenceClassing.NoClass"/> where it has none.</param>
    public static bool[] Ground(IReadOnlyList<int[]> classes, IReadOnlyList<bool> inside)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(inside);
        if (classes.Count == 0 || classes.Any(layer => layer.Length != inside.Count))
        {
            throw new ArgumentException("Every layer is classed on the grid being weighed.", nameof(classes));
        }

        var ground = new bool[inside.Count];
        for (var i = 0; i < ground.Length; i++)
        {
            if (!inside[i])
            {
                continue;
            }

            var classed = true;
            foreach (var layer in classes)
            {
                if (layer[i] < 0)
                {
                    classed = false;
                    break;
                }
            }

            ground[i] = classed;
        }

        return ground;
    }

    /// <summary>
    /// The weight tables of every layer, or null when there is nothing to learn from: no occupied
    /// cell on the ground, or no cell that is not.
    /// </summary>
    /// <param name="layers">The layers' codes, in the model's order.</param>
    /// <param name="classes">One array per layer: the class of every cell.</param>
    /// <param name="classCounts">How many classes each layer has.</param>
    /// <param name="ground">Which cells are weighed, from <see cref="Ground"/>.</param>
    /// <param name="occupiedCells">
    /// The cells that hold an entrance the model is trained on. One off the ground is a caller's
    /// mistake and is refused, not skipped: a count that silently drops cells is a wrong count.
    /// </param>
    public static TrainedEvidence? Train(
        IReadOnlyList<string> layers,
        IReadOnlyList<int[]> classes,
        IReadOnlyList<int> classCounts,
        bool[] ground,
        IReadOnlyCollection<int> occupiedCells)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(classCounts);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(occupiedCells);
        if (layers.Count == 0 || classes.Count != layers.Count || classCounts.Count != layers.Count
            || classes.Any(layer => layer.Length != ground.Length))
        {
            throw new ArgumentException("One classed layer and one class count for every layer of the model.", nameof(classes));
        }

        var occupied = new bool[ground.Length];
        var totalOccurrences = 0;
        foreach (var cell in occupiedCells)
        {
            if (cell < 0 || cell >= ground.Length || !ground[cell])
            {
                throw new ArgumentException("An occupied cell lies on the ground being weighed.", nameof(occupiedCells));
            }

            if (!occupied[cell])
            {
                occupied[cell] = true;
                totalOccurrences++;
            }
        }

        var totalCells = ground.Count(cell => cell);
        if (totalOccurrences == 0 || totalOccurrences >= totalCells)
        {
            return null;
        }

        var tables = new TrainedEvidenceLayer[layers.Count];
        for (var l = 0; l < layers.Count; l++)
        {
            var cells = new int[classCounts[l]];
            var occurrences = new int[classCounts[l]];
            var layer = classes[l];
            for (var i = 0; i < ground.Length; i++)
            {
                if (!ground[i])
                {
                    continue;
                }

                if (layer[i] >= cells.Length)
                {
                    throw new ArgumentException("A cell's class lies within its layer's classes.", nameof(classCounts));
                }

                cells[layer[i]]++;
                if (occupied[i])
                {
                    occurrences[layer[i]]++;
                }
            }

            var rows = new EvidenceClassWeights[cells.Length];
            for (var k = 0; k < rows.Length; k++)
            {
                rows[k] = OfClass(k, cells[k], occurrences[k], totalCells, totalOccurrences);
            }

            tables[l] = new TrainedEvidenceLayer(layers[l], rows);
        }

        return new TrainedEvidence(tables, totalCells, totalOccurrences);
    }

    /// <summary>
    /// The frequency-ratio index of every cell: the ratios of the classes it falls in, one per
    /// layer, added up. Not-a-number off the ground.
    /// </summary>
    /// <remarks>
    /// An index and not a probability. A cell whose every class holds caves in proportion to its
    /// size comes to the number of layers; more means classes that hold more than their share.
    /// </remarks>
    public static float[] SummedFrequencyRatios(TrainedEvidence trained, IReadOnlyList<int[]> classes, bool[] ground) =>
        Map(trained, classes, ground, row => row.FrequencyRatio, sum => sum);

    /// <summary>
    /// The posterior probability of every cell: the chance that it holds a known entrance once the
    /// class it falls in on every layer has been read. Not-a-number off the ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The logarithm of the prior odds plus the positive weight of the cell's class on each layer,
    /// turned back into a probability. With a single layer this is exactly the share of the
    /// class's cells that are occupied, which is what the weights are for.
    /// </para>
    /// <para>
    /// Adding the layers' weights assumes that, among occupied cells and among empty ones alike,
    /// the layers say independent things. Layers that repeat each other — slope and ruggedness —
    /// are counted twice and the probabilities come out too high; <see cref="ExpectedOccurrences"/>
    /// is the check on that. The order of the cells survives it far better than their figures do.
    /// </para>
    /// </remarks>
    public static float[] PosteriorProbabilities(TrainedEvidence trained, IReadOnlyList<int[]> classes, bool[] ground)
    {
        ArgumentNullException.ThrowIfNull(trained);
        var prior = Math.Log(trained.Occurrences / (double)(trained.Cells - trained.Occurrences));
        return Map(trained, classes, ground, row => row.PositiveWeight, sum => 1d / (1d + Math.Exp(-(prior + sum))));
    }

    /// <summary>
    /// How many occupied cells a map of posterior probabilities adds up to. Set beside the number
    /// the model was trained on, it measures how far the layers repeat each other: independent
    /// layers give the trained number back, and the more this exceeds it the less the
    /// probabilities should be read as probabilities.
    /// </summary>
    public static double ExpectedOccurrences(ReadOnlySpan<float> posterior)
    {
        var sum = 0d;
        foreach (var value in posterior)
        {
            if (float.IsFinite(value))
            {
                sum += value;
            }
        }

        return sum;
    }

    private static float[] Map(
        TrainedEvidence trained,
        IReadOnlyList<int[]> classes,
        bool[] ground,
        Func<EvidenceClassWeights, double?> weight,
        Func<double, double> value)
    {
        ArgumentNullException.ThrowIfNull(trained);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(ground);
        if (classes.Count != trained.Layers.Count || classes.Any(layer => layer.Length != ground.Length))
        {
            throw new ArgumentException("One classed layer for every table, on the grid being mapped.", nameof(classes));
        }

        // The weights are looked up once per class rather than once per cell.
        var weights = trained.Layers
            .Select(layer => layer.Classes.Select(row => weight(row) ?? double.NaN).ToArray())
            .ToArray();

        var values = new float[ground.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!ground[i])
            {
                values[i] = float.NaN;
                continue;
            }

            var sum = 0d;
            for (var l = 0; l < weights.Length; l++)
            {
                sum += weights[l][classes[l][i]];
            }

            values[i] = (float)value(sum);
        }

        return values;
    }
}
