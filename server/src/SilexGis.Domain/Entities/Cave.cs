// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A cave with toponymy, localization, geology, morphometry and protection data
/// (02-data-model.md §3). Soft-deleted; geometry lives on entrances — <see cref="MainGeom"/>
/// and <see cref="EntranceCount"/> are derived copies maintained by the entrance handlers.
/// </summary>
public class Cave : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Identification & toponymy
    public required string Name { get; set; }

    public string? OtherToponyms { get; set; }

    public string? IdentificationCode { get; set; }

    public long CaveTypeId { get; set; }

    public string? Description { get; set; }

    public string? Website { get; set; }

    // Localization (textual parts are redacted for protected caves — 05 §5)
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

    /// <summary>When true, exact coordinates require ViewExactLocation (05 §5).</summary>
    public bool LocationProtected { get; set; }

    // Derived from entrances (maintained by entrance handlers)
    public int EntranceCount { get; set; }

    public Point? MainGeom { get; set; }

    // Access control (ADR-007)
    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string AuditId => Id.ToString();
}
