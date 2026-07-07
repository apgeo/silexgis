// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// Read-visibility filter composed into every list/map query over protected entities.
/// Mirrored in SQL by PermissionSql.VisibleToFragment —
/// a parity test keeps the two in lock-step; change them together or not at all.
/// </summary>
public static class PermissionQueryExtensions
{
    public static IQueryable<T> VisibleTo<T>(this IQueryable<T> query, UserContext user)
        where T : class, IProtectedEntity
    {
        if (user.IsAdmin)
        {
            return query;
        }

        var userId = user.UserId;
        var teamIds = user.TeamIds;
        return query.Where(e =>
            e.OwnerUserId == userId
            || e.Visibility >= Visibility.Authenticated
            || (e.Visibility == Visibility.Team && e.TeamId != null && teamIds.Contains(e.TeamId.Value)));
    }
}
