// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// An entrance of a cave (02-data-model.md §3). Inherits the cave's access control —
/// no own RLS columns. Geometry is a 2D point; altitude is the dedicated column.
/// </summary>
public class CaveEntrance : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaveId { get; set; }

    public string? Name { get; set; }

    public long EntranceTypeId { get; set; }

    public bool IsMain { get; set; }

    public required Point Geom { get; set; }

    public decimal? Altitude { get; set; }

    public string? Description { get; set; }

    public PositionQuality PositionQuality { get; set; } = PositionQuality.Unknown;

    public DateOnly? SurveyedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
