// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>One known entrance offered to a model that learns from them.</summary>
/// <remarks>
/// A cell and a flag, and nothing that says which entrance it is: whoever read the entrances keeps
/// their identities, and is told by position in the list which of them were used.
/// </remarks>
/// <param name="Cell">The cell of the run's grid the entrance lies in, or null when it lies off the grid.</param>
/// <param name="IsProtected">Whether the entrance's position is protected.</param>
public readonly record struct TrainingEntrance(int? Cell, bool IsProtected);

/// <summary>What a model learnt from the known entrances of one area, and how well its map finds them.</summary>
/// <remarks>
/// Counts, tables per class and curves. No position, no cell and nothing per entrance: everything
/// here is a sum over classes or over shares of the ground.
/// </remarks>
/// <param name="HeldOutShare">The share of occupied cells kept back, as asked for.</param>
/// <param name="Seed">What the draw of the kept-back cells started from.</param>
/// <param name="TrainingEntrances">How many entrances the weights were counted from.</param>
/// <param name="HeldOutEntrances">How many entrances were kept back and only tested against.</param>
/// <param name="TrainingCells">How many cells those training entrances occupy.</param>
/// <param name="HeldOutCells">How many cells the kept-back entrances occupy.</param>
/// <param name="ProtectedEntrances">How many of the entrances used, on either side, were location-protected.</param>
/// <param name="Cells">How many cells of ground were weighed.</param>
/// <param name="PriorProbability">The share of that ground occupied by a training entrance.</param>
/// <param name="ValueMinimum">
/// The least value the method itself gave a cell — a posterior probability or a summed frequency
/// ratio — before the map was stretched onto nought to one for drawing.
/// </param>
/// <param name="ValueMaximum">The greatest such value. A drawn value <c>v</c> stands for minimum + v × (maximum − minimum).</param>
/// <param name="ExpectedOccurrences">
/// For weights of evidence, how many occupied cells the posterior probabilities add up to; set
/// beside <paramref name="TrainingCells"/> it says how far the layers repeat each other. Null for
/// a frequency-ratio model, whose index is not a probability.
/// </param>
/// <param name="Layers">The weight tables, in the model's order of layers.</param>
/// <param name="SuccessRate">The map's curve against the cells it was trained on.</param>
/// <param name="PredictionRate">The map's curve against the cells kept back.</param>
public sealed record CavePotentialTrainingSummary(
    double HeldOutShare,
    int Seed,
    int TrainingEntrances,
    int HeldOutEntrances,
    int TrainingCells,
    int HeldOutCells,
    int ProtectedEntrances,
    int Cells,
    double PriorProbability,
    double ValueMinimum,
    double ValueMaximum,
    double? ExpectedOccurrences,
    IReadOnlyList<TrainedEvidenceLayer> Layers,
    RateCurve SuccessRate,
    RateCurve PredictionRate);

/// <summary>A map made by a model that learnt from known entrances.</summary>
/// <param name="Values">Favourability from nought to one, not-a-number where there is no answer.</param>
/// <param name="Summary">What the run found, its <see cref="CavePotentialRunSummary.Training"/> filled.</param>
/// <param name="UsedEntrances">
/// Which of the entrances offered were used, on either side of the split, as positions in the
/// list offered. The rest lay off the ground that was weighed and shaped nothing.
/// </param>
public sealed record TrainedFavourabilityMap(
    float[] Values, CavePotentialRunSummary Summary, IReadOnlyList<int> UsedEntrances);

/// <summary>
/// The whole of a data-driven run once its evidence is built and its entrances read: class the
/// layers, part the occupied cells, count the weights, draw the map and test it.
/// </summary>
public static class TrainedFavourability
{
    /// <exception cref="CavePotentialRunException">
    /// Nothing can be weighed, or there are too few known entrances to learn from and test against.
    /// </exception>
    public static TrainedFavourabilityMap Compute(
        CavePotentialMethod method,
        CavePotentialModelDefinition definition,
        IReadOnlyList<BuiltEvidence> evidence,
        bool[] inside,
        IReadOnlyList<TrainingEntrance> entrances)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(inside);
        ArgumentNullException.ThrowIfNull(entrances);
        if (!CavePotentialMethods.IsDataDriven(method) || definition.Training is not { } training)
        {
            throw new ArgumentException("Only a model that learns from known entrances is trained.", nameof(method));
        }

        var layers = definition.Layers;
        if (evidence.Count != layers.Count || evidence.Any(e => e.Values is null || e.Values.Length != inside.Length))
        {
            throw new ArgumentException("One built layer for every layer of the model, on the run's grid.", nameof(evidence));
        }

        var classes = new int[layers.Count][];
        var classCounts = new int[layers.Count];
        for (var l = 0; l < layers.Count; l++)
        {
            classes[l] = EvidenceClassing.Classify(evidence[l].Layer, layers[l].Transform, evidence[l].Values);
            classCounts[l] = EvidenceClassing.ClassCount(evidence[l].Layer, layers[l].Transform);
        }

        var ground = EvidenceWeights.Ground(classes, inside);
        var groundCells = ground.Count(cell => cell);
        if (groundCells == 0)
        {
            throw new CavePotentialRunException(
                CavePotentialRunFailures.NothingScored,
                "No part of the area had a value from every layer of the model.");
        }

        // An entrance outside the area, or on a cell some layer has no reading for, is not ground
        // the model is counted over, so it neither teaches nor tests.
        var used = new List<int>();
        var occupied = new SortedSet<int>();
        for (var i = 0; i < entrances.Count; i++)
        {
            if (entrances[i].Cell is { } cell && cell >= 0 && cell < ground.Length && ground[cell])
            {
                used.Add(i);
                occupied.Add(cell);
            }
        }

        var split = TrainingSplits.Of(occupied, training.HeldOutShare, training.Seed)
            ?? throw new CavePotentialRunException(
                CavePotentialRunFailures.TooFewEntrances,
                "Too few cells of the area hold an entrance this account may place for some to be kept back.");

        var codes = layers.Select(l => l.Layer).ToList();
        var trained = EvidenceWeights.Train(codes, classes, classCounts, ground, split.Training)
            ?? throw new CavePotentialRunException(
                CavePotentialRunFailures.NothingToLearn,
                "Every cell of the area holds a known entrance, so no ground can be told from any other.");

        var weightsOfEvidence = method == CavePotentialMethod.WeightsOfEvidence;
        var raw = weightsOfEvidence
            ? EvidenceWeights.PosteriorProbabilities(trained, classes, ground)
            : EvidenceWeights.SummedFrequencyRatios(trained, classes, ground);

        // The curves rank the method's own values, so that stretching the map for drawing cannot
        // merge two values the method told apart.
        var success = RateCurves.Of(raw, split.Training);
        var prediction = RateCurves.Of(raw, split.HeldOut);
        if (success is null || prediction is null)
        {
            throw new CavePotentialRunException(
                CavePotentialRunFailures.NothingToLearn,
                "The map could not be tested against the entrances it was made from.");
        }

        var (values, minimum, maximum, mean) = Stretched(raw);

        var trainingCells = split.Training.ToHashSet();
        var trainingEntrances = used.Count(i => trainingCells.Contains(entrances[i].Cell!.Value));
        var summary = new CavePotentialRunSummary(
            [
                .. layers.Select((layer, l) => new CavePotentialLayerSummary(
                    layer.Layer,
                    0d,
                    evidence[l].Provenance.FeatureIds.Count,
                    evidence[l].Provenance.ProtectedFeatures,
                    CountClassed(classes[l], inside))),
            ],
            inside.Count(cell => cell),
            groundCells,
            0d,
            maximum > minimum ? 1d : 0d,
            mean,
            Training: new CavePotentialTrainingSummary(
                training.HeldOutShare,
                training.Seed,
                trainingEntrances,
                used.Count - trainingEntrances,
                split.Training.Count,
                split.HeldOut.Count,
                used.Count(i => entrances[i].IsProtected),
                trained.Cells,
                trained.PriorProbability,
                minimum,
                maximum,
                weightsOfEvidence ? EvidenceWeights.ExpectedOccurrences(raw) : null,
                trained.Layers,
                success,
                prediction));

        return new TrainedFavourabilityMap(values, summary, used);
    }

    /// <summary>
    /// The map brought onto nought to one, with the least and greatest of the values it had.
    /// </summary>
    /// <remarks>
    /// A posterior probability is a small number everywhere — entrances are rare — and a summed
    /// ratio has no ceiling, so neither can be drawn on the scale a judged map is drawn on. What
    /// both say reliably is which ground ranks above which, and a stretch between the least and
    /// the greatest keeps exactly that. The two ends are reported, so the method's own figure for
    /// a cell can be had back from the drawn one.
    /// </remarks>
    private static (float[] Values, double Minimum, double Maximum, double Mean) Stretched(float[] raw)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var value in raw)
        {
            if (float.IsFinite(value))
            {
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        var span = maximum - minimum;
        var values = new float[raw.Length];
        var sum = 0d;
        var cells = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (!float.IsFinite(raw[i]))
            {
                values[i] = float.NaN;
                continue;
            }

            // A map with one value everywhere ranks nothing, and is drawn as nought.
            values[i] = span > 0 ? (float)Math.Clamp((raw[i] - minimum) / span, 0d, 1d) : 0f;
            sum += values[i];
            cells++;
        }

        return (values, minimum, maximum, sum / cells);
    }

    private static int CountClassed(int[] classes, bool[] inside)
    {
        var count = 0;
        for (var i = 0; i < classes.Length; i++)
        {
            if (inside[i] && classes[i] >= 0)
            {
                count++;
            }
        }

        return count;
    }
}
