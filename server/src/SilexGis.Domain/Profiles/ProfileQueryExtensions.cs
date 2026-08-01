// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Profiles;

/// <summary>
/// The SQL-side twin of <see cref="ProfileProtection.CanView"/>: restricts a user query to the
/// rows whose given field the caller may see.
/// </summary>
/// <remarks>
/// <para>
/// This exists because gating only the projection leaves a working oracle. A search that still
/// <c>WHERE</c>s on a hidden email lets a caller confirm any address has an account by probing
/// patterns, without the address ever being rendered. Any query that filters, matches or sorts on
/// a governed field must compose this in — and the two halves must change together or not at all,
/// which a parity test enforces.
/// </para>
/// <para>
/// <paramref name="memberships"/> and <paramref name="cavers"/> are the same context's sets, so the
/// shared-caving-group check stays correlated EXISTS clauses in the translated SQL and this layer
/// needs no EF reference — the same shape as the ACL-aware overload of <c>VisibleTo</c>. They are
/// passed separately rather than pre-joined because a subquery over a projection does not
/// translate; the join happens inside the EXISTS instead. A member without an account cannot
/// match, since the correlation is on the account id.
/// </para>
/// </remarks>
public static class ProfileQueryExtensions
{
    /// <summary>
    /// Keeps only the users whose <paramref name="field"/> is visible to <paramref name="viewer"/>.
    /// </summary>
    public static IQueryable<T> WhereFieldVisibleTo<T>(
        this IQueryable<T> users,
        ProfileField field,
        UserContext? viewer,
        IQueryable<CavingGroupMembership> memberships,
        IQueryable<Caver> cavers)
        where T : class, IUserProfile
    {
        if (viewer is null)
        {
            return users.Where(_ => false);
        }

        var userId = viewer.UserId;
        var cavingGroupIds = viewer.CavingGroupIds;

        // One arm per field rather than a selector expression: the property to test is chosen at
        // runtime, and a dynamic property lookup does not translate to SQL. Administrators get no
        // bypass here, matching CanView.
        return field switch
        {
            ProfileField.RealName => users.Where(u =>
                u.Id == userId
                || u.RealNameVisibility == ProfileVisibility.Authenticated
                || (u.RealNameVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.Bio => users.Where(u =>
                u.Id == userId
                || u.BioVisibility == ProfileVisibility.Authenticated
                || (u.BioVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.Email => users.Where(u =>
                u.Id == userId
                || u.EmailVisibility == ProfileVisibility.Authenticated
                || (u.EmailVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.Phone => users.Where(u =>
                u.Id == userId
                || u.PhoneVisibility == ProfileVisibility.Authenticated
                || (u.PhoneVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.CavingClub => users.Where(u =>
                u.Id == userId
                || u.CavingClubVisibility == ProfileVisibility.Authenticated
                || (u.CavingClubVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.Address => users.Where(u =>
                u.Id == userId
                || u.AddressVisibility == ProfileVisibility.Authenticated
                || (u.AddressVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            ProfileField.AddressPoint => users.Where(u =>
                u.Id == userId
                || u.AddressPointVisibility == ProfileVisibility.Authenticated
                || (u.AddressPointVisibility == ProfileVisibility.CavingGroup
                    && memberships.Any(m => cavingGroupIds.Contains(m.CavingGroupId)
                        && cavers.Any(c => c.Id == m.CaverId && c.UserId == u.Id)))),
            // A field added to the enum but not here must hide every row, never show them all.
            _ => users.Where(_ => false),
        };
    }
}
