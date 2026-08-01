// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A person's membership of a caving group. Unique per (CaverId, CavingGroupId); a caver may
/// belong to several groups, or none.
/// </summary>
/// <remarks>
/// Membership is recorded for <b>people</b>, not accounts, so a club roster can hold the members
/// who never sign in. Anything that needs the users of a group derives them from the accounts
/// among its cavers, which is why a member without an account contributes nothing to access.
/// <see cref="Role"/> is roster metadata for display — no authorization reads it.
/// Roster rows are audited: membership moves whatever rights flow through the group's
/// permission-group memberships, so a roster write is a grant edit in effect.
/// </remarks>
public class CavingGroupMembership : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public Guid CaverId { get; set; }

    public Guid CavingGroupId { get; set; }

    public CavingGroupRole Role { get; set; } = CavingGroupRole.Member;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// The account-holding members of a caving group, flattened to (user, group) pairs.
/// </summary>
/// <remarks>
/// The single shape in which membership reaches anything access- or profile-related. Cavers
/// without an account are absent by construction, so no caller has to remember to exclude them.
/// </remarks>
public readonly record struct CavingGroupUser(Guid UserId, Guid CavingGroupId);
