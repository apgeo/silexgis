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
    /// <summary>Roles/ownership/visibility/caving group layers only — use where ACL rows cannot apply.</summary>
    public static IQueryable<T> VisibleTo<T>(this IQueryable<T> query, UserContext user)
        where T : class, IProtectedEntity
    {
        if (user.IsAdmin)
        {
            return query;
        }

        var userId = user.UserId;
        var cavingGroupIds = user.CavingGroupIds;
        return query.Where(e =>
            e.OwnerUserId == userId
            || e.Visibility >= Visibility.Authenticated
            // Binding a row to a caving group grants its members Read whatever the visibility
            // says: caving group membership is its own layer, not a rung of the visibility
            // ladder. Gating this on Visibility.CavingGroup would hide a private caving group object
            // from the very caving group that owns it — while the exact-location rule, which
            // has never been gated, still hands them its coordinates.
            || (e.CavingGroupId != null && cavingGroupIds.Contains(e.CavingGroupId.Value)));
    }

    /// <summary>
    /// Full read filter for features, including explicit object_acl Read grants keyed by
    /// the feature FK (to the user or any of their caving groups). One filter for every feature
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
        var cavingGroupIds = user.CavingGroupIds;
        return query.Where(f =>
            f.OwnerUserId == userId
            || f.Visibility >= Visibility.Authenticated
            // CavingGroup membership is its own layer, independent of the visibility ladder.
            || (f.CavingGroupId != null && cavingGroupIds.Contains(f.CavingGroupId.Value))
            || acls.Any(a => a.FeatureId == f.Id
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.CavingGroup && cavingGroupIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.Read)));
    }

    /// <summary>
    /// Full read filter for non-feature entities including explicit object_acl Read
    /// grants via the polymorphic pair (to the user or any of their caving groups).
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
        var cavingGroupIds = user.CavingGroupIds;
        return query.Where(e =>
            e.OwnerUserId == userId
            || e.Visibility >= Visibility.Authenticated
            // CavingGroup membership is its own layer, independent of the visibility ladder.
            || (e.CavingGroupId != null && cavingGroupIds.Contains(e.CavingGroupId.Value))
            || acls.Any(a => a.EntityType == entityType && a.EntityId == e.Id
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.CavingGroup && cavingGroupIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.Read)));
    }
}
