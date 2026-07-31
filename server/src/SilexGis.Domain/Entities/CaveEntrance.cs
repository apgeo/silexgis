// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Cave-entrance subtype row (shared PK with its <see cref="Feature"/>, which owns the
/// name and the entrance point — a PointZ; Z mirrors <see cref="Altitude"/> when known).
/// The entrance is a child of its cave in the containment hierarchy; it has no access
/// control of its own — visibility and protection resolve through the feature row and
/// its ancestors.
/// </summary>
public class CaveEntrance : IAuditable
{
    /// <summary>Equals the feature id (shared primary key).</summary>
    public Guid Id { get; set; }

    public Feature Feature { get; set; } = null!;

    /// <summary>
    /// The owning cave (feature id; FK to the cave subtype row). Structural truth for the
    /// exactly-one-cave constraint; the write service mirrors it as the primary
    /// containment edge, and the verifier checks the two agree.
    /// </summary>
    public Guid CaveFeatureId { get; set; }

    public long EntranceTypeId { get; set; }

    /// <summary>The cave's representative entrance — mirrored into the cave feature's point geometry.</summary>
    public bool IsMain { get; set; }

    public decimal? Altitude { get; set; }

    public PositionQuality PositionQuality { get; set; } = PositionQuality.Unknown;

    public DateOnly? SurveyedAt { get; set; }

    public string AuditId => Id.ToString();
}
