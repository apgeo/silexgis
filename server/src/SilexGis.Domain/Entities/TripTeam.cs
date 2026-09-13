// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A titled grouping of a trip's participants while they are underground — display and
/// addressing only. Teams are free to appear, rename and dissolve as a party splits and
/// recombines; positions always belong to cavers, never to the team, so deleting a team
/// degrades labels and nothing else.
/// </summary>
public class TripTeam : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TripLogId { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
