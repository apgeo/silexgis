// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import;

/// <summary>
/// Who a rule set belongs to and who may change it — one home, because the answer decides
/// both what the API refuses and what the screen offers.
///
/// <para>
/// The shape the owner asked for: an installation ships a default, anyone may edit it and
/// thereby get a copy of their own, and only an administrator moves a set back up to be what
/// a group or the whole installation inherits. A member of a club cannot quietly change what
/// every other member's next import proposes.
/// </para>
/// </summary>
public static class TermRuleSetRules
{
    public const string NotEditableCode = "term_rules.not_editable";

    public const string PromotionForbiddenCode = "term_rules.promotion_forbidden";

    /// <summary>
    /// The set a user's next import starts from: their own default, else one of their groups',
    /// else the installation's, else the shipped one. Group sets are ordered by the group ids
    /// as the caller's context lists them, so a user in two clubs gets a stable answer rather
    /// than whichever row the database returned first.
    /// </summary>
    public static TermRuleSet? Resolve(
        IEnumerable<TermRuleSet> sets, Guid userId, IReadOnlyList<Guid> cavingGroupIds)
    {
        var all = sets as IReadOnlyList<TermRuleSet> ?? [.. sets];

        var own = all.FirstOrDefault(s => s.Scope == TermRuleScope.User && s.OwnerUserId == userId && s.IsDefault);
        if (own is not null)
        {
            return own;
        }

        foreach (var groupId in cavingGroupIds)
        {
            var group = all.FirstOrDefault(s =>
                s.Scope == TermRuleScope.CavingGroup && s.CavingGroupId == groupId && s.IsDefault);
            if (group is not null)
            {
                return group;
            }
        }

        return all.FirstOrDefault(s => s.Scope == TermRuleScope.Installation && s.IsDefault)
            ?? all.FirstOrDefault(s => s.IsSeeded);
    }

    /// <summary>
    /// Whether the caller may rewrite this set in place. Their own set is theirs; anything a
    /// group or the installation inherits is an administrator's.
    /// </summary>
    public static bool MayEdit(AccessContext? ctx, TermRuleSet set)
    {
        if (ctx is null)
        {
            return false;
        }

        if (IsAdministrator(ctx))
        {
            return true;
        }

        return set.Scope == TermRuleScope.User && set.OwnerUserId == ctx.UserId;
    }

    /// <summary>
    /// Whether the caller may move a set to a wider scope, or make it the default of one.
    /// Administrators only — promoting a set changes what everybody else's next import
    /// proposes, which is a configuration change rather than a personal one.
    /// </summary>
    public static bool MayPromote(AccessContext? ctx) => ctx is not null && IsAdministrator(ctx);

    /// <summary>Whether the caller may delete the set. A shipped set is never deletable.</summary>
    public static bool MayDelete(AccessContext? ctx, TermRuleSet set) => !set.IsSeeded && MayEdit(ctx, set);

    /// <summary>
    /// Everyone signed in reads every set — there is nothing in a rule to protect, and a club
    /// that cannot show another club its rules cannot hand them over either.
    /// </summary>
    public static bool MayRead(AccessContext? ctx) => ctx is not null;

    private static bool IsAdministrator(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed;
}
