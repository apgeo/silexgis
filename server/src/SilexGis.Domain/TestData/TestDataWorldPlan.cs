// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>Everything a run will write, worked out in memory before anything is written.</summary>
public sealed record TestDataWorldPlan(
    TestDataProfile Profile,
    IReadOnlyList<PlannedRegion> Regions,
    IReadOnlyList<PlannedCave> Caves);

public sealed record PlannedRegion(
    TestDataRegion Region,
    string Description,
    IReadOnlyList<PlannedSubArea> SubAreas);

public sealed record PlannedSubArea(TestDataSubArea Area, string Description);

public sealed record PlannedEntrance(
    string Name,
    double Lon,
    double Lat,
    double AltitudeM,
    string EntranceTypeCode,
    bool IsMain,
    PositionQuality PositionQuality,
    DateOnly? SurveyedAt);

/// <summary>One invented cave with every register field it is given.</summary>
public sealed record PlannedCave
{
    public required int Index { get; init; }

    public required string RegionCode { get; init; }

    /// <summary>The sub-area (by its index in the region) the cave lies in, or null for the open massif.</summary>
    public int? SubAreaIndex { get; init; }

    public required string Name { get; init; }

    public required string IdentificationCode { get; init; }

    public required string CaveTypeCode { get; init; }

    public required string RockTypeCode { get; init; }

    public required Visibility Visibility { get; init; }

    public required bool LocationProtected { get; init; }

    public required IReadOnlyList<PlannedEntrance> Entrances { get; init; }

    public required string Description { get; init; }

    public string? OtherToponyms { get; init; }

    public string? Website { get; init; }

    public required string Region { get; init; }

    public required string HydrographicBasin { get; init; }

    public string? Valley { get; init; }

    public string? TributaryRiver { get; init; }

    public string? ClosestAddress { get; init; }

    public string? LandRegistryNumber { get; init; }

    public string? LocationNotes { get; init; }

    public string? RockAge { get; init; }

    public decimal? SurveyedLength { get; init; }

    public decimal? EstimatedLength { get; init; }

    public decimal? RealExtension { get; init; }

    public decimal? ProjectedExtension { get; init; }

    public decimal? Depth { get; init; }

    public decimal? PositiveDepth { get; init; }

    public decimal? NegativeDepth { get; init; }

    public decimal? PotentialDepth { get; init; }

    public decimal? Altitude { get; init; }

    public decimal? Volume { get; init; }

    public decimal? Area { get; init; }

    public decimal? RamificationIndex { get; init; }

    public int? CaveAge { get; init; }

    public ExplorationStatus ExplorationStatus { get; init; }

    public string? ProtectionClass { get; init; }

    public bool IsShowCave { get; init; }

    public decimal? ShowCaveLength { get; init; }

    public string? DiscoveryDate { get; init; }

    public string? Discoverer { get; init; }
}
