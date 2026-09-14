// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What one participant of a tracked trip is called on the trip's published page — the admin's
/// choice, one row per person per trip, and the only path by which anything a person is actually
/// named can reach a caller with no account.
/// </summary>
/// <remarks>
/// <para>
/// A row exists only where somebody typed a label. Its absence is not "unknown": it is the
/// ordinary state, and the published read then names the person by a position in the party and
/// nothing else. That is why the label lives here rather than as a column on the roster — the
/// roster is who was on the trip, this is how the trip chose to show them, and a roster row that
/// carried a display name would make the non-identifying default a thing to remember rather than
/// the thing that happens by itself.
/// </para>
/// <para>
/// The label is free text on purpose. It is a caption written for followers — a real name, a
/// nickname, a job on the trip — and the application has no business deciding which of those the
/// party meant.
/// </para>
/// </remarks>
public class TripTrackingParticipant : ITimestamped, IAuditable, IAuditChild
{
    /// <summary>
    /// A key of its own, where the trip and the person would have served: the audit trail
    /// identifies a row by one short string, and a pair of uuids spelled into one does not fit
    /// the column it is written to. Uniqueness of the pair is an index instead.
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TripLogId { get; set; }

    public Guid CaverId { get; set; }

    /// <summary>How followers see this person. Never empty — clearing it deletes the row.</summary>
    public required string DisplayLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
