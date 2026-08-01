// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>Purpose of a trip, for filtering and reporting. Stored as smallint.</summary>
public enum TripType : short
{
    Exploration = 0,
    Survey = 1,
    Maintenance = 2,
    Training = 3,
    Tourism = 4,
    Rescue = 5,
    Science = 6,
    Other = 7,
}

/// <summary>
/// A dated exploration/visit report: who went where and what happened. Optionally
/// carries a location geometry (point or area) and links to the caves involved.
/// </summary>
public class TripLog : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public TripType? Type { get; set; }

    public DateOnly TripDate { get; set; }

    public DateOnly? TripDateEnd { get; set; }

    /// <summary>Underground entry/exit wall-clock times (no time zone; duration is derived).</summary>
    public TimeOnly? EntryTime { get; set; }

    public TimeOnly? ExitTime { get; set; }

    public string? Description { get; set; }

    /// <summary>Outcomes/observations, kept distinct from the narrative <see cref="Description"/>.</summary>
    public string? Results { get; set; }

    public string? WeatherConditions { get; set; }

    public string? LocationText { get; set; }

    /// <summary>Free-text organizing club/organization (external clubs need not be a <see cref="CavingGroup"/>).</summary>
    public string? OrganizingClub { get; set; }

    public Geometry? Geom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>Trip ↔ cave link (unique pair; joins to caves for display).</summary>
public class TripLogCave : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public Guid CaveId { get; set; }

    public string AuditId => Id.ToString();

    // Cave links surface in their trip's timeline.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}

/// <summary>Whether a person attended the trip or proposed it. Stored as smallint.</summary>
public enum TripParticipantKind : short
{
    Participant = 0,
    Proposer = 1,
}

/// <summary>
/// A person tied to a trip — either a registered user or a free-text name for people
/// without accounts (exactly one of the two is set, DB check constraint). The same
/// identity model serves both attendees and proposers, distinguished by <see cref="Kind"/>;
/// one person may appear once as each.
/// </summary>
public class TripLogParticipant : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public TripParticipantKind Kind { get; set; } = TripParticipantKind.Participant;

    public Guid? UserId { get; set; }

    public string? NameText { get; set; }

    public string AuditId => Id.ToString();

    // Participants surface in their trip's timeline.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}
