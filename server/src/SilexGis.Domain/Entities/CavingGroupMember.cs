// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Membership of a user in a caving group. Unique per (CavingGroupId, UserId).</summary>
public class CavingGroupMember : ITimestamped
{
    public long Id { get; set; }

    public Guid CavingGroupId { get; set; }

    public Guid UserId { get; set; }

    public CavingGroupRole Role { get; set; } = CavingGroupRole.Member;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
