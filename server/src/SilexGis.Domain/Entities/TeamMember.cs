// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Membership of a user in a team (02-data-model.md §1). Unique per (TeamId, UserId).</summary>
public class TeamMember : ITimestamped
{
    public long Id { get; set; }

    public Guid TeamId { get; set; }

    public Guid UserId { get; set; }

    public TeamRole Role { get; set; } = TeamRole.Member;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
