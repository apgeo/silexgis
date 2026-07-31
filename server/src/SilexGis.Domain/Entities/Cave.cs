// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Cave subtype row (shared PK with its <see cref="Feature"/>, which owns name,
/// description, access control, protection and the representative point geometry —
/// the main-entrance cache). This table holds only cave-specific attributes.
/// The cave's physical shape lives in its default <see cref="Centerline"/> child.
/// </summary>
public class Cave : IAuditable
{
    /// <summary>Equals the feature id (shared primary key).</summary>
    public Guid Id { get; set; }

    public Feature Feature { get; set; } = null!;

    // Identification & toponymy (Name lives on the feature row)
    public string? OtherToponyms { get; set; }

    public string? IdentificationCode { get; set; }

    public long CaveTypeId { get; set; }

    public string? Website { get; set; }

    // Localization (textual parts are redacted for protected caves)
    public string? Region { get; set; }

    public string? HydrographicBasin { get; set; }

    public string? Valley { get; set; }

    public string? TributaryRiver { get; set; }

    public string? ClosestAddress { get; set; }

    public string? LandRegistryNumber { get; set; }

    public string? LocationNotes { get; set; }

    // Geology
    public long? RockTypeId { get; set; }

    public string? RockAge { get; set; }

    // Morphometry (meters unless noted)
    public decimal? SurveyedLength { get; set; }

    public decimal? EstimatedLength { get; set; }

    public decimal? RealExtension { get; set; }

    public decimal? ProjectedExtension { get; set; }

    public decimal? Depth { get; set; }

    public decimal? PositiveDepth { get; set; }

    public decimal? NegativeDepth { get; set; }

    public decimal? PotentialDepth { get; set; }

    public decimal? Altitude { get; set; }

    public decimal? Volume { get; set; }

    public decimal? Area { get; set; }

    public decimal? RamificationIndex { get; set; }

    public int? CaveAge { get; set; }

    // Status
    public ExplorationStatus ExplorationStatus { get; set; } = ExplorationStatus.Unknown;

    public string? ProtectionClass { get; set; }

    public bool IsShowCave { get; set; }

    public decimal? ShowCaveLength { get; set; }

    // Discovery (free-text date: historical/partial dates are common)
    public string? DiscoveryDate { get; set; }

    public string? Discoverer { get; set; }

    // Derived from entrance children (maintained by the feature write service)
    public int EntranceCount { get; set; }

    public string AuditId => Id.ToString();
}
