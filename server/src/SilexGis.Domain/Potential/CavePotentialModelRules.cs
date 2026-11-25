// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Potential;

/// <summary>Something that stops a model definition being stored, and where in it.</summary>
/// <param name="Path">The member at fault, as a path into the definition (<c>layers[1].transform.low</c>).</param>
public sealed record CavePotentialModelProblem(string Path, string Message);

/// <summary>One layer's share of a model, out of one.</summary>
public sealed record EvidenceLayerWeight(string Layer, double Weight);

/// <summary>The weights a model works with, and — when they follow from comparisons — how consistent those were.</summary>
public sealed record CavePotentialWeighting(
    IReadOnlyList<EvidenceLayerWeight> Weights,
    PairwiseConsistency? Consistency);

/// <summary>
/// Whether a cave-potential model may be stored as written, who may change it, and what weights it
/// comes to.
/// </summary>
public static class CavePotentialModelRules
{
    public const int MaxNameLength = 200;

    public const int MaxDescriptionLength = 2000;

    /// <summary>The most rows a classes table may have. A table longer than this is a ramp typed out by hand.</summary>
    public const int MaxClasses = 20;

    /// <summary>The largest weight a layer may be given outright; only proportions matter, so this is a guard against overflow, not a scale.</summary>
    public const double MaxWeight = 1_000_000;

    /// <summary>
    /// Who a model may be shown to: only its author, the members of one caving group, or everyone
    /// signed in. Never anybody without an account.
    /// </summary>
    public static bool IsAllowedVisibility(Visibility visibility) =>
        visibility is Visibility.Private or Visibility.CavingGroup or Visibility.Authenticated;

    /// <summary>Whether a model with this audience names a caving group: exactly when it is shared with one.</summary>
    public static bool NamesACavingGroup(Visibility visibility) => visibility == Visibility.CavingGroup;

    /// <summary>
    /// Whether this caller may rewrite, re-share or delete this model: its author, or a full
    /// administrator — never somebody it was only shared with.
    /// </summary>
    /// <remarks>
    /// A member who may read a shared model may run it and take a copy of their own to change.
    /// The original stays its author's, so a map computed from it last month still means what its
    /// author wrote. Asked in addition to the access decision, never instead of it.
    /// </remarks>
    public static bool MayChange(AccessContext caller, CavePotentialModel model)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(model);
        return caller.IsFullAdmin || model.OwnerUserId == caller.UserId;
    }

    /// <summary>
    /// Whether anything this model weighs is made from recorded positions. A map computed from
    /// such a model is governed by who may place those features.
    /// </summary>
    /// <remarks>
    /// A model that learns from known entrances is made from positions whatever it weighs: the
    /// entrances it counts are positions, and a map of where they are common says where they are.
    /// </remarks>
    public static bool UsesPositions(CavePotentialMethod method, CavePotentialModelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return CavePotentialMethods.IsDataDriven(method)
            || definition.Layers.Any(l => EvidenceLayers.Find(l?.Layer) is { UsesPositions: true });
    }

    /// <summary>What stops this definition being stored. Empty means it may be.</summary>
    public static IReadOnlyList<CavePotentialModelProblem> Problems(
        CavePotentialMethod method, CavePotentialModelDefinition? definition)
    {
        var problems = new List<CavePotentialModelProblem>();
        if (!Enum.IsDefined(method))
        {
            problems.Add(new("method", "This is not a way of weighting a model."));
            return problems;
        }

        if (definition?.Layers is not { Count: > 0 } layers)
        {
            problems.Add(new("layers", "A model weighs at least one evidence layer."));
            return problems;
        }

        if (layers.Count > EvidenceLayers.All.Count)
        {
            problems.Add(new("layers", "A model weighs each evidence layer at most once."));
            return problems;
        }

        var dataDriven = CavePotentialMethods.IsDataDriven(method);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layers.Count; i++)
        {
            var at = string.Create(CultureInfo.InvariantCulture, $"layers[{i}]");
            var setting = layers[i];
            if (setting is null || EvidenceLayers.Find(setting.Layer) is not { } layer)
            {
                problems.Add(new($"{at}.layer", "This is not an evidence layer."));
                continue;
            }

            if (!seen.Add(layer.Code))
            {
                problems.Add(new($"{at}.layer", "A model weighs each evidence layer at most once."));
            }

            if (dataDriven && layer.Code == EvidenceLayers.EntranceDensity)
            {
                // The layer is made from the very entrances the model counts, the kept-back ones
                // included: a model given it would be shown what it is then tested on, and its
                // prediction rate would measure nothing.
                problems.Add(new($"{at}.layer", "A model that learns from known entrances cannot also weigh how dense they are."));
            }

            TransformProblems(layer, setting.Transform, $"{at}.transform", dataDriven, problems);

            if (method == CavePotentialMethod.DirectWeights)
            {
                if (setting.Weight is not { } weight || !double.IsFinite(weight) || weight <= 0 || weight > MaxWeight)
                {
                    problems.Add(new($"{at}.weight", "Each layer needs a weight above nought."));
                }
            }
            else if (dataDriven)
            {
                if (setting.Weight is not null)
                {
                    problems.Add(new($"{at}.weight", "The weights of this model are counted from known entrances, so a layer names none."));
                }
            }
            else if (setting.Weight is not null)
            {
                problems.Add(new($"{at}.weight", "The weights of this model follow from its comparisons, so a layer names none."));
            }
        }

        if (method == CavePotentialMethod.PairwiseComparison)
        {
            PairwiseProblems(layers.Count, definition.Pairwise, problems);
        }
        else if (definition.Pairwise is not null)
        {
            problems.Add(new("pairwise", dataDriven
                ? "A model that learns from known entrances has no comparisons."
                : "A model with weights given outright has no comparisons."));
        }

        if (dataDriven)
        {
            if (definition.Training is not { } training)
            {
                problems.Add(new("training", "A model that learns from known entrances says what share of them it keeps back, and from what seed."));
            }
            else
            {
                if (!TrainingSplits.IsAllowedShare(training.HeldOutShare))
                {
                    problems.Add(new("training.heldOutShare", string.Create(
                        CultureInfo.InvariantCulture,
                        $"The share kept back lies between {TrainingSplits.MinHeldOutShare} and {TrainingSplits.MaxHeldOutShare}.")));
                }

                if (training.Seed < 0)
                {
                    problems.Add(new("training.seed", "A seed is a whole number, nought or above."));
                }
            }
        }
        else if (definition.Training is not null)
        {
            problems.Add(new("training", "Only a model that learns from known entrances keeps any back."));
        }

        return problems;
    }

    /// <summary>
    /// The weights a valid definition comes to, each as a share of one, in the order of its layers.
    /// Null for a model that learns from known entrances: its weights are per class, not per layer,
    /// and exist only once it has been run over an area.
    /// </summary>
    /// <exception cref="ArgumentException">The definition has problems; ask <see cref="Problems"/> first.</exception>
    public static CavePotentialWeighting? Weighting(CavePotentialMethod method, CavePotentialModelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Problems(method, definition).Count > 0)
        {
            throw new ArgumentException("The definition is not a valid model.", nameof(definition));
        }

        if (CavePotentialMethods.IsDataDriven(method))
        {
            return null;
        }

        var layers = definition.Layers;
        if (method == CavePotentialMethod.PairwiseComparison)
        {
            var solved = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(layers.Count, definition.Pairwise!));
            return new CavePotentialWeighting(
                [.. layers.Select((l, i) => new EvidenceLayerWeight(l.Layer, solved.Weights[i]))],
                solved.Consistency);
        }

        var total = layers.Sum(l => l.Weight!.Value);
        return new CavePotentialWeighting(
            [.. layers.Select(l => new EvidenceLayerWeight(l.Layer, l.Weight!.Value / total))], null);
    }

    private static void TransformProblems(
        EvidenceLayer layer,
        EvidenceTransform? transform,
        string at,
        bool dataDriven,
        List<CavePotentialModelProblem> problems)
    {
        if (transform is null || !Enum.IsDefined(transform.Kind))
        {
            problems.Add(new(at, "Each layer says how its values are scored."));
            return;
        }

        if (!layer.Accepts(transform.Kind))
        {
            problems.Add(new($"{at}.kind", layer.ValueKind switch
            {
                EvidenceValueKind.Categorical => "A layer of classes is scored class by class.",
                EvidenceValueKind.Circular => "A bearing is scored by a table of classes, never by a ramp.",
                _ => "A measured layer is scored by a table of classes or by a ramp.",
            }));
            return;
        }

        if (dataDriven && transform.Kind == EvidenceTransformKind.FuzzyLinear)
        {
            // Entrances are counted per class, and a ramp has none.
            problems.Add(new($"{at}.kind", "A model that learns from known entrances cuts a measured layer into classes."));
            return;
        }

        switch (transform.Kind)
        {
            case EvidenceTransformKind.Classes:
                ClassProblems(layer, transform.Classes, $"{at}.classes", dataDriven, problems);
                break;
            case EvidenceTransformKind.FuzzyLinear:
                if (transform.Low is not { } low || !double.IsFinite(low))
                {
                    problems.Add(new($"{at}.low", "A ramp needs the value that scores nought."));
                }

                if (transform.High is not { } high || !double.IsFinite(high))
                {
                    problems.Add(new($"{at}.high", "A ramp needs the value that scores one."));
                }
                else if (transform.Low == high)
                {
                    problems.Add(new($"{at}.high", "A ramp needs two different bounds."));
                }

                break;
            default:
                CategoryProblems(layer, transform.Categories, $"{at}.categories", dataDriven, problems);
                break;
        }
    }

    private static void ClassProblems(
        EvidenceLayer layer,
        IReadOnlyList<EvidenceClass>? classes,
        string at,
        bool dataDriven,
        List<CavePotentialModelProblem> problems)
    {
        if (classes is not { Count: >= 2 } || classes.Count > MaxClasses)
        {
            problems.Add(new(at, string.Create(
                CultureInfo.InvariantCulture, $"A classes table has between 2 and {MaxClasses} rows.")));
            return;
        }

        double? previous = null;
        for (var i = 0; i < classes.Count; i++)
        {
            var row = classes[i];
            var rowAt = string.Create(CultureInfo.InvariantCulture, $"{at}[{i}]");
            if (row is null)
            {
                problems.Add(new(rowAt, "A class has a bound and a score."));
                continue;
            }

            var last = i == classes.Count - 1;
            if (last)
            {
                if (row.UpperBound is not null)
                {
                    problems.Add(new($"{rowAt}.upperBound", "The last class has no upper bound: it takes everything above the one before."));
                }
            }
            else if (row.UpperBound is not { } bound || !double.IsFinite(bound))
            {
                problems.Add(new($"{rowAt}.upperBound", "Every class but the last needs its upper bound."));
            }
            else
            {
                if (previous is { } below && bound <= below)
                {
                    problems.Add(new($"{rowAt}.upperBound", "The bounds of a classes table rise from row to row."));
                }

                if (layer.ValueKind == EvidenceValueKind.Circular && bound is <= 0 or >= 360)
                {
                    problems.Add(new($"{rowAt}.upperBound", "A bearing's bounds lie between 0 and 360 degrees."));
                }

                previous = bound;
            }

            ScoreProblems(row.Score, $"{rowAt}.score", dataDriven, problems);
        }
    }

    private static void CategoryProblems(
        EvidenceLayer layer,
        IReadOnlyList<EvidenceCategoryScore>? categories,
        string at,
        bool dataDriven,
        List<CavePotentialModelProblem> problems)
    {
        // Every class of the layer is counted whatever such a model lists, so it may list none.
        if (dataDriven && categories is null or { Count: 0 })
        {
            return;
        }

        if (categories is not { Count: > 0 } || categories.Count > layer.Categories.Count)
        {
            problems.Add(new(at, "A layer of classes scores at least one of its classes, and each at most once."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < categories.Count; i++)
        {
            var row = categories[i];
            var rowAt = string.Create(CultureInfo.InvariantCulture, $"{at}[{i}]");
            if (row?.Code is null || !layer.Categories.Contains(row.Code))
            {
                problems.Add(new($"{rowAt}.code", "This is not a class of the layer."));
                continue;
            }

            if (!seen.Add(row.Code))
            {
                problems.Add(new($"{rowAt}.code", "A class is scored once."));
            }

            ScoreProblems(row.Score, $"{rowAt}.score", dataDriven, problems);
        }
    }

    private static void ScoreProblems(double? score, string at, bool dataDriven, List<CavePotentialModelProblem> problems)
    {
        if (dataDriven)
        {
            // A score left in such a model would read as a judgment the map never used.
            if (score is not null)
            {
                problems.Add(new(at, "The scores of this model are counted from known entrances, so a class names none."));
            }

            return;
        }

        if (score is not { } value || !double.IsFinite(value) || value < 0 || value > 1)
        {
            problems.Add(new(at, "A score lies between nought and one."));
        }
    }

    private static void PairwiseProblems(
        int size, IReadOnlyList<IReadOnlyList<double>>? pairwise, List<CavePotentialModelProblem> problems)
    {
        if (pairwise is null || pairwise.Count != size - 1)
        {
            problems.Add(new("pairwise", "The comparisons have one row for every layer but the last."));
            return;
        }

        for (var i = 0; i < pairwise.Count; i++)
        {
            var row = pairwise[i];
            var rowAt = string.Create(CultureInfo.InvariantCulture, $"pairwise[{i}]");
            if (row is null || row.Count != size - 1 - i)
            {
                problems.Add(new(rowAt, "Each row compares one layer with every later one."));
                continue;
            }

            for (var k = 0; k < row.Count; k++)
            {
                // The lower end is written as a fraction, and a ninth typed as 0.111 must not be
                // refused for the rounding.
                if (!double.IsFinite(row[k])
                    || row[k] < AnalyticHierarchy.LeastJudgment - 0.001
                    || row[k] > AnalyticHierarchy.GreatestJudgment)
                {
                    problems.Add(new(
                        string.Create(CultureInfo.InvariantCulture, $"{rowAt}[{k}]"),
                        "A comparison lies between one ninth and nine."));
                }
            }
        }
    }
}
