// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Who a rule set answers to. Stored as smallint; append-only.
/// </summary>
public enum TermRuleScope : short
{
    /// <summary>Everybody's fallback. Editing one is an administrator's act.</summary>
    Installation = 0,

    /// <summary>A club's set. Members read it; whoever may bind content to the group may edit it.</summary>
    CavingGroup = 1,

    /// <summary>One person's own. Made by editing a set they did not own.</summary>
    User = 2,
}

/// <summary>
/// A named, ordered list of detection rules — a club's naming habits, configured once.
///
/// <para>
/// Rule sets deliberately carry no visibility column and are readable by every signed-in
/// user. A rule says "a waypoint whose name starts with P. is probably a cave"; it names no
/// cave and holds no coordinate, so there is nothing here to protect, and making sets
/// private would mean a club could not hand one to a neighbouring club without an export —
/// which is the opposite of the point.
/// </para>
/// <para>
/// Which set a given user gets by default resolves from the narrowest scope outwards: their
/// own, then their caving groups', then the installation's. Editing a set you do not own
/// produces a copy that is yours, which is what makes the installation default safe to ship
/// and safe to leave alone.
/// </para>
/// </summary>
public class TermRuleSet : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    public TermRuleScope Scope { get; set; } = TermRuleScope.User;

    /// <summary>
    /// Who made it. Null for the set that shipped with the installation, which nobody wrote,
    /// and for a set whose author's account has since gone — the rules are still the club's.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Set for <see cref="TermRuleScope.CavingGroup"/>, null otherwise.</summary>
    public Guid? CavingGroupId { get; set; }

    /// <summary>
    /// The set its scope hands out. At most one per scope instance — one installation
    /// default, one per group, one per user — enforced by partial unique indexes.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>The ordered rule array (jsonb), in the shape of <see cref="Import.TermRuleDocument"/>.</summary>
    public required string Rules { get; set; }

    /// <summary>
    /// The set that shipped with the installation. Re-seeding finds it by this flag and
    /// leaves its rules alone once an administrator has edited them; deleting it is refused,
    /// because it is the last fallback when every other scope is empty.
    /// </summary>
    public bool IsSeeded { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
