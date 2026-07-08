// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A dated exploration/visit report: who went where and what happened. Optionally
/// carries a location geometry (point or area) and links to the caves involved.
/// </summary>
public class TripLog : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public DateOnly TripDate { get; set; }

    public DateOnly? TripDateEnd { get; set; }

    public string? Description { get; set; }

    public string? LocationText { get; set; }

    public Geometry? Geom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>Trip ↔ cave link (unique pair; joins to caves for display).</summary>
public class TripLogCave
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public Guid CaveId { get; set; }
}

/// <summary>
/// Trip participant: either a registered user or a free-text name for people
/// without accounts. Exactly one of the two is set (DB check constraint).
/// </summary>
public class TripLogParticipant
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public Guid? UserId { get; set; }

    public string? NameText { get; set; }
}
