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

    /// <summary>The caving group that organized the trip, when one did.</summary>
    public Guid? OrganizingCavingGroupId { get; set; }

    public Geometry? Geom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>
    /// Where the trip has got to in its lifecycle. A new trip starts as a draft: it is being
    /// written, and nobody named on it is told about it until it is published.
    /// </summary>
    /// <remarks>
    /// This is never consulted when deciding who may read the trip. Visibility and the access
    /// entries answer that on their own, and a second rule saying who may read a row is how the
    /// two come to disagree — a draft with public visibility is public, and that is correct.
    /// </remarks>
    public ActivityState State { get; set; } = ActivityState.Draft;

    /// <summary>
    /// When the trip was first published, or null while it never has been. Stamped once and never
    /// cleared, so unpublishing and publishing again does not move it.
    /// </summary>
    /// <remarks>
    /// The change history could answer this, but it is read on every listing and reconstructing it
    /// per row from an audit trail is the wrong shape for a column that only ever gains a value.
    /// </remarks>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>Whether a person attended the trip or proposed it. Stored as smallint.</summary>
public enum TripParticipantKind : short
{
    Participant = 0,
    Proposer = 1,
}

/// <summary>
/// A person tied to a trip, named through the roster. The same identity model serves both
/// attendees and proposers, distinguished by <see cref="Kind"/>; one person may appear once
/// as each.
/// </summary>
/// <remarks>
/// Pointing at a caver rather than carrying an account id beside a free-text name is what makes
/// per-person history work for the majority who never sign in: "which caves has this person been
/// to" is one join whether or not they have an account. A caver named on a trip cannot be
/// deleted — the roster offers merging two entries for the same person instead.
/// </remarks>
public class TripLogParticipant : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    public TripParticipantKind Kind { get; set; } = TripParticipantKind.Participant;

    public Guid CaverId { get; set; }

    public string AuditId => Id.ToString();

    // Participants surface in their trip's timeline.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}
