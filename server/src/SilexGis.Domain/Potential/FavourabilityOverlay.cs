// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Potential;

/// <summary>One layer's part in a run: its share, and what it was made from.</summary>
/// <param name="Weight">
/// The layer's share of the model, out of one. Nought in a model that learns from known entrances,
/// whose weights are per class and are in the run's training tables.
/// </param>
/// <param name="Features">How many recorded features the layer was made from; nought for a terrain layer.</param>
/// <param name="ProtectedFeatures">How many of those were location-protected.</param>
/// <param name="Cells">How many cells inside the area the layer had a value for.</param>
public sealed record CavePotentialLayerSummary(
    string Layer, double Weight, int Features, int ProtectedFeatures, int Cells);

/// <summary>What a run of a cave-potential model found.</summary>
/// <param name="AreaCells">How many cells have their centre inside the area.</param>
/// <param name="TerrainInputs">
/// The elevation build and the computed pictures the terrain layers were read from, with those
/// pictures' settings, so a map can later be told apart from one made from other ground. Null for
/// a run that read none.
/// </param>
/// <param name="Training">
/// For a model that learns from known entrances: how many it used, how they were parted, the
/// weight tables and the two rate curves. Null for a model weighed by judgment.
/// </param>
/// <param name="ScoredCells">
/// How many of them were scored. Fewer than <paramref name="AreaCells"/> where some layer had
/// nothing to say — ground off the edge of the elevation, or outside every mapped rock unit.
/// </param>
public sealed record CavePotentialRunSummary(
    IReadOnlyList<CavePotentialLayerSummary> Layers,
    int AreaCells,
    int ScoredCells,
    double? Minimum,
    double? Maximum,
    double? Mean,
    IReadOnlyList<TerrainInput>? TerrainInputs = null,
    CavePotentialTrainingSummary? Training = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(CavePotentialRunSummary summary) =>
        JsonSerializer.Serialize(summary, SerializerOptions);

    /// <summary>The summary a stored string holds, or null for a run that has none yet.</summary>
    public static CavePotentialRunSummary? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var summary = JsonSerializer.Deserialize<CavePotentialRunSummary>(json, SerializerOptions);
            return summary?.Layers is null ? null : summary;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The favourability of every cell of a run, and what was found on the way.</summary>
/// <param name="Values">
/// One favourability per cell, nought to one, row by row from the north-west. Not-a-number
/// outside the area and wherever a layer had nothing to say.
/// </param>
public sealed record FavourabilityMap(float[] Values, CavePotentialRunSummary Summary);

/// <summary>
/// The weighted overlay of a knowledge-driven potential map: every evidence layer rescored cell
/// by cell as the model says, and the scores combined by the model's weights.
/// </summary>
public static class FavourabilityOverlay
{
    /// <summary>
    /// The map a model comes to over a grid's cells.
    /// </summary>
    /// <param name="layers">The model's layers, in the model's order.</param>
    /// <param name="weights">One weight per layer, in the same order.</param>
    /// <param name="evidence">One built layer per model layer, in the same order, every one available.</param>
    /// <param name="inside">Which cells belong to the area. Every other cell is left empty.</param>
    /// <remarks>
    /// A cell outside the area is never scored, whatever evidence lies under it: the map is an
    /// answer about the area that was asked for, and its edge is the area's edge.
    /// </remarks>
    public static FavourabilityMap Compute(
        IReadOnlyList<EvidenceLayerSetting> layers,
        IReadOnlyList<double> weights,
        IReadOnlyList<BuiltEvidence> evidence,
        IReadOnlyList<bool> inside)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(inside);
        if (layers.Count == 0 || weights.Count != layers.Count || evidence.Count != layers.Count)
        {
            throw new ArgumentException("One weight and one built layer for every layer of the model.", nameof(evidence));
        }

        for (var l = 0; l < layers.Count; l++)
        {
            if (evidence[l].Values is not { } cells || cells.Length != inside.Count
                || !string.Equals(evidence[l].Layer.Code, layers[l].Layer, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every layer is built, for the layer the model names, on the grid being scored.", nameof(evidence));
            }
        }

        var values = new float[inside.Count];
        var layerCells = new int[layers.Count];
        var weightSpan = weights.ToArray();
        var scores = new double?[layers.Count];
        var areaCells = 0;
        var scoredCells = 0;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var sum = 0d;

        for (var cell = 0; cell < values.Length; cell++)
        {
            values[cell] = float.NaN;
            if (!inside[cell])
            {
                continue;
            }

            areaCells++;
            for (var l = 0; l < layers.Count; l++)
            {
                scores[l] = ScoreOf(evidence[l].Layer, layers[l].Transform, evidence[l].Values![cell]);
                if (scores[l] is not null)
                {
                    layerCells[l]++;
                }
            }

            if (Favourability.Combine(scores, weightSpan) is not { } favourability)
            {
                continue;
            }

            values[cell] = (float)favourability;
            scoredCells++;
            sum += favourability;
            minimum = Math.Min(minimum, favourability);
            maximum = Math.Max(maximum, favourability);
        }

        var total = weightSpan.Sum();
        var summaries = new List<CavePotentialLayerSummary>(layers.Count);
        for (var l = 0; l < layers.Count; l++)
        {
            summaries.Add(new CavePotentialLayerSummary(
                layers[l].Layer,
                total > 0 ? weightSpan[l] / total : 0,
                evidence[l].Provenance.FeatureIds.Count,
                evidence[l].Provenance.ProtectedFeatures,
                layerCells[l]));
        }

        return new FavourabilityMap(
            values,
            new CavePotentialRunSummary(
                summaries,
                areaCells,
                scoredCells,
                scoredCells > 0 ? minimum : null,
                scoredCells > 0 ? maximum : null,
                scoredCells > 0 ? sum / scoredCells : null));
    }

    private static double? ScoreOf(EvidenceLayer layer, EvidenceTransform transform, float value)
    {
        if (float.IsNaN(value))
        {
            return null;
        }

        if (layer.ValueKind != EvidenceValueKind.Categorical)
        {
            return Favourability.Score(layer, transform, value);
        }

        // A categorical layer's cell holds the position of its class in the layer's own list.
        var index = (int)value;
        return index >= 0 && index < layer.Categories.Count
            ? Favourability.ScoreOfCategory(transform, layer.Categories[index])
            : null;
    }
}

/// <summary>Why a run of a cave-potential model could not be finished, as stable codes.</summary>
public static class CavePotentialRunFailures
{
    /// <summary>The account the run is for may no longer read the terrain or place the area.</summary>
    public const string AccessChanged = "cave_potential.access_changed";

    /// <summary>The definition recorded for the run is not a model this version can run.</summary>
    public const string ModelInvalid = "cave_potential.model_invalid";

    /// <summary>The area is no longer an outline a grid can be cut over at this cell size.</summary>
    public const string AreaUnusable = "cave_potential.area_unusable";

    /// <summary>
    /// One or more of the model's layers could not be made — most often a terrain surface nobody
    /// has computed for the current elevation.
    /// </summary>
    public const string EvidenceUnavailable = "cave_potential.evidence_unavailable";

    /// <summary>No cell inside the area had a value from every layer.</summary>
    public const string NothingScored = "cave_potential.nothing_scored";

    /// <summary>
    /// A model that learns from known entrances found too few of them in the area — counted in
    /// cells, and only those the account may place — to keep some back and still learn from the rest.
    /// </summary>
    public const string TooFewEntrances = "cave_potential.too_few_entrances";

    /// <summary>The area holds more known entrances, among those the account may place, than a run reads.</summary>
    public const string TooManyEntrances = "cave_potential.too_many_entrances";

    /// <summary>The known entrances leave nothing to tell apart: no ground without one, or no map to test.</summary>
    public const string NothingToLearn = "cave_potential.nothing_to_learn";

    /// <summary>Anything else: the map could not be computed or written.</summary>
    public const string Failed = "cave_potential.failed";
}

/// <summary>A run that cannot be finished, for a reason the person who asked can be told.</summary>
public sealed class CavePotentialRunException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
