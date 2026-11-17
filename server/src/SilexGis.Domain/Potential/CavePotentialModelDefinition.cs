// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Potential;

/// <summary>How a cave-potential model arrives at the weight of each evidence layer.</summary>
/// <remarks>
/// Stored as a number and appended to, never renumbered: a method that derives its weights from
/// known caves rather than from judgment is a later value of this same list, so one column says
/// what kind of model a row is.
/// </remarks>
public enum CavePotentialMethod : short
{
    /// <summary>Each layer is given its weight outright.</summary>
    DirectWeights = 0,

    /// <summary>
    /// The layers are compared two at a time and the weights follow from the comparisons, with a
    /// measure of how consistent the comparisons are.
    /// </summary>
    PairwiseComparison = 1,
}

/// <summary>How a layer's cell values are turned into favourability scores between nought and one.</summary>
public enum EvidenceTransformKind
{
    /// <summary>A table of value ranges, each with its score.</summary>
    Classes = 0,

    /// <summary>A straight ramp between two bounds: nought at one, one at the other.</summary>
    FuzzyLinear = 1,

    /// <summary>A score for each class of a categorical layer.</summary>
    Categories = 2,
}

/// <summary>One row of a classes table: every value below the bound, down to the row before.</summary>
/// <param name="UpperBound">
/// The value this class stops short of. Null on the last row only, which takes everything from
/// the previous bound upwards.
/// </param>
/// <param name="Score">
/// The favourability of ground in this class, nought to one. Null only in a model whose method
/// works the scores out itself.
/// </param>
public sealed record EvidenceClass(double? UpperBound, double? Score);

/// <summary>The score of one class of a categorical layer.</summary>
public sealed record EvidenceCategoryScore(string Code, double? Score);

/// <summary>How one layer's values become scores.</summary>
/// <param name="Classes">The table, for <see cref="EvidenceTransformKind.Classes"/>; null otherwise.</param>
/// <param name="Low">For a ramp, the value that scores nought.</param>
/// <param name="High">
/// For a ramp, the value that scores one. Below <paramref name="Low"/> when smaller values are the
/// favourable ones — a ramp from 500 m down to 0 m scores ground on a fault as one and ground half
/// a kilometre from any as nought.
/// </param>
/// <param name="Categories">
/// The scores, for <see cref="EvidenceTransformKind.Categories"/>. A class left out scores nought.
/// </param>
public sealed record EvidenceTransform(
    EvidenceTransformKind Kind,
    IReadOnlyList<EvidenceClass>? Classes = null,
    double? Low = null,
    double? High = null,
    IReadOnlyList<EvidenceCategoryScore>? Categories = null);

/// <summary>One evidence layer in a model.</summary>
/// <param name="Layer">The layer's code, from <see cref="EvidenceLayers"/>.</param>
/// <param name="Weight">
/// The layer's weight when the model gives weights outright — any positive number, since only the
/// proportions between layers matter. Null when the weights follow from comparisons.
/// </param>
public sealed record EvidenceLayerSetting(string Layer, EvidenceTransform Transform, double? Weight = null);

/// <summary>
/// What a cave-potential model says: which evidence it weighs, how each layer is scored, and how
/// the layers are weighed against each other.
/// </summary>
/// <remarks>
/// It names kinds of evidence and numbers, and never a place: no area, no feature, no coordinate.
/// Where a model is run is chosen when it is run, under the runner's own access, so a model shared
/// with a club hands its members a way of weighing ground and nothing about where anything is.
/// </remarks>
/// <param name="Layers">The layers, in the order the comparisons refer to them by.</param>
/// <param name="Pairwise">
/// For <see cref="CavePotentialMethod.PairwiseComparison"/>, the judgments above the diagonal:
/// row <c>i</c> holds how many times more important layer <c>i</c> is than each later layer, on
/// the scale of one ninth to nine. One row for every layer but the last. Null otherwise.
/// </param>
public sealed record CavePotentialModelDefinition(
    IReadOnlyList<EvidenceLayerSetting> Layers,
    IReadOnlyList<IReadOnlyList<double>>? Pairwise = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = Build();

    public static string Serialize(CavePotentialModelDefinition definition) =>
        JsonSerializer.Serialize(definition, SerializerOptions);

    /// <summary>
    /// The definition a stored string holds, or null when it holds something this version cannot
    /// read. Null rather than an exception, so one unreadable row cannot stop a list rendering.
    /// </summary>
    public static CavePotentialModelDefinition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var definition = JsonSerializer.Deserialize<CavePotentialModelDefinition>(json, SerializerOptions);
            return definition?.Layers is null ? null : definition;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
