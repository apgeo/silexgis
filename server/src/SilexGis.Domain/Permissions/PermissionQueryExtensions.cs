// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Permissions;

/// <summary>
/// Read-visibility filter composed into every list/map query over protected entities.
/// Mirrored in SQL by PermissionSql.VisibleToFragment —
/// a parity test keeps the two in lock-step; change them together or not at all.
/// </summary>
public static class PermissionQueryExtensions
{
    /// <summary>Roles/ownership/visibility/team layers only — use where ACL rows cannot apply.</summary>
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

    /// <summary>
    /// Full read filter for features, including explicit object_acl Read grants keyed by
    /// the feature FK (to the user or any of their teams). One filter for every feature
    /// kind — the discriminator parameter of the polymorphic overload does not exist
    /// here. Grants are deliberately non-cascading: a grant applies to its target row
    /// only; the hierarchy-cascade semantics belong to the planned ruleset permission
    /// system, which will consume the features' ancestor arrays.
    /// </summary>
    public static IQueryable<Feature> VisibleTo(
        this IQueryable<Feature> query, UserContext user, IQueryable<ObjectAcl> acls)
    {
        if (user.IsAdmin)
        {
            return query;
        }

        var userId = user.UserId;
        var teamIds = user.TeamIds;
        return query.Where(f =>
            f.OwnerUserId == userId
            || f.Visibility >= Visibility.Authenticated
            || (f.Visibility == Visibility.Team && f.TeamId != null && teamIds.Contains(f.TeamId.Value))
            || acls.Any(a => a.FeatureId == f.Id
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.Team && teamIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.Read)));
    }

    /// <summary>
    /// Full read filter for non-feature entities including explicit object_acl Read
    /// grants via the polymorphic pair (to the user or any of their teams).
    /// <paramref name="acls"/> is the ObjectAcl set of the same context so the grant
    /// check stays one correlated EXISTS in the translated SQL.
    /// </summary>
    public static IQueryable<T> VisibleTo<T>(
        this IQueryable<T> query, UserContext user, IQueryable<ObjectAcl> acls, AttachedEntityType entityType)
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
            || (e.Visibility == Visibility.Team && e.TeamId != null && teamIds.Contains(e.TeamId.Value))
            || acls.Any(a => a.EntityType == entityType && a.EntityId == e.Id
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.Team && teamIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.Read)));
    }
}
