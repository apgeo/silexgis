// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Potential;

/// <summary>What sort of value an evidence layer holds in each cell.</summary>
public enum EvidenceValueKind
{
    /// <summary>A measurement on a line: a height, an angle of slope, a distance, a density.</summary>
    Continuous = 0,

    /// <summary>A compass bearing, where 359 and 1 are neighbours.</summary>
    Circular = 1,

    /// <summary>One of a closed list of classes.</summary>
    Categorical = 2,
}

/// <summary>Where an evidence layer's values come from.</summary>
public enum EvidenceSource
{
    /// <summary>The elevation model and the surfaces computed from it. Holds no position of anything recorded.</summary>
    Terrain = 0,

    /// <summary>
    /// Recorded features — entrances, sinkholes, fault lines, structural readings, geological
    /// units. A surface made from them says where they are, so whatever is computed from one is
    /// governed by who may place those features, not by who may read the terrain.
    /// </summary>
    Features = 1,
}

/// <summary>One kind of evidence a cave-potential model can weigh.</summary>
/// <param name="Code">The stable name a model stores. Never renamed once shipped.</param>
/// <param name="Unit">What a cell's value is measured in, as a stable code, or null for a class.</param>
/// <param name="Categories">The class codes of a categorical layer, in their own order; empty otherwise.</param>
public sealed record EvidenceLayer(
    string Code,
    EvidenceValueKind ValueKind,
    EvidenceSource Source,
    string? Unit,
    IReadOnlyList<string> Categories)
{
    /// <summary>Whether a surface of this kind is made from recorded positions.</summary>
    public bool UsesPositions => Source == EvidenceSource.Features;

    /// <summary>Whether this layer can be rescored by this kind of transform.</summary>
    /// <remarks>
    /// A bearing takes a table of classes and never a ramp: a ramp from 0 to 360 would score due
    /// north as both the least and the most favourable ground.
    /// </remarks>
    public bool Accepts(EvidenceTransformKind transform) => ValueKind switch
    {
        EvidenceValueKind.Continuous =>
            transform is EvidenceTransformKind.Classes or EvidenceTransformKind.FuzzyLinear,
        EvidenceValueKind.Circular => transform == EvidenceTransformKind.Classes,
        EvidenceValueKind.Categorical => transform == EvidenceTransformKind.Categories,
        _ => false,
    };
}

/// <summary>
/// The evidence a cave-potential model can be built from: surfaces the application computes
/// itself, from the elevation model and from what has been recorded.
/// </summary>
/// <remarks>
/// A closed list. A model names its layers by code and is stored for years, so a code is appended
/// and never renamed or reused, and the list's order is the order a form offers them in.
/// </remarks>
public static class EvidenceLayers
{
    public const string Elevation = "elevation";
    public const string Slope = "slope";
    public const string Aspect = "aspect";
    public const string PositionIndex = "position_index";
    public const string Ruggedness = "ruggedness";
    public const string Roughness = "roughness";
    public const string Geomorphons = "geomorphons";
    public const string FaultDistance = "fault_distance";
    public const string Lithology = "lithology";
    public const string SinkholeDistance = "sinkhole_distance";
    public const string EntranceDensity = "entrance_density";

    public static IReadOnlyList<EvidenceLayer> All { get; } =
    [
        new(Elevation, EvidenceValueKind.Continuous, EvidenceSource.Terrain, "m", []),
        new(Slope, EvidenceValueKind.Continuous, EvidenceSource.Terrain, "deg", []),
        new(Aspect, EvidenceValueKind.Circular, EvidenceSource.Terrain, "deg", []),
        new(PositionIndex, EvidenceValueKind.Continuous, EvidenceSource.Terrain, "m", []),
        new(Ruggedness, EvidenceValueKind.Continuous, EvidenceSource.Terrain, "m", []),
        new(Roughness, EvidenceValueKind.Continuous, EvidenceSource.Terrain, "m", []),
        new(Geomorphons, EvidenceValueKind.Categorical, EvidenceSource.Terrain, null, TerrainGeomorphons.Classes),
        // The nearest fault, whether drawn as a line or measured as a structural reading of a
        // fault plane: both say "the rock is broken here".
        new(FaultDistance, EvidenceValueKind.Continuous, EvidenceSource.Features, "m", []),
        new(Lithology, EvidenceValueKind.Categorical, EvidenceSource.Features, null,
            [.. Geo.Lithology.Classes.Select(c => c.Code)]),
        new(SinkholeDistance, EvidenceValueKind.Continuous, EvidenceSource.Features, "m", []),
        new(EntranceDensity, EvidenceValueKind.Continuous, EvidenceSource.Features, "per_km2", []),
    ];

    private static readonly Dictionary<string, EvidenceLayer> ByCode =
        All.ToDictionary(l => l.Code, StringComparer.Ordinal);

    /// <summary>The layer of this code, or null for a code outside the list.</summary>
    public static EvidenceLayer? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var layer) ? layer : null;
}
