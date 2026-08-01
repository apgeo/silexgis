// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// Effective-permission algorithm: global roles, ownership, visibility, caving group roles and
/// explicit ACL grants. The evaluator stays pure — callers that want the ACL layer load
/// the applicable grants first (IPermissionService does this) and pass their OR-ed
/// flags in <c>aclGranted</c>; most-permissive layer wins.
/// CavingGroup defaults: member → Read+Write; caving group admin/owner → +Delete+Share+Manage.
/// </summary>
public static class PermissionEvaluator
{
    public static bool Can(
        UserContext? user,
        IProtectedEntity entity,
        ObjectPermission permission,
        ObjectPermission aclGranted = ObjectPermission.None)
    {
        if (user is null)
        {
            // Anonymous read of Public content is an installation-level switch handled at
            // the endpoint layer; the evaluator itself never grants anonymous access.
            return false;
        }

        if (user.IsAdmin || entity.OwnerUserId == user.UserId)
        {
            return true;
        }

        if (permission == ObjectPermission.Read && VisibilityAllowsRead(user, entity))
        {
            return true;
        }

        if (entity.CavingGroupId is { } cavingGroupId && user.IsMemberOf(cavingGroupId))
        {
            var granted = user.IsCavingGroupAdmin(cavingGroupId)
                ? ObjectPermission.Read | ObjectPermission.Write | ObjectPermission.Delete
                    | ObjectPermission.Share | ObjectPermission.ManagePermissions
                    | ObjectPermission.ViewExactLocation
                : ObjectPermission.Read | ObjectPermission.Write | ObjectPermission.ViewExactLocation;
            if (granted.HasFlag(permission))
            {
                return true;
            }
        }

        return aclGranted.HasFlag(permission);
    }

    private static bool VisibilityAllowsRead(UserContext user, IProtectedEntity entity) =>
        entity.Visibility switch
        {
            Visibility.Public or Visibility.Authenticated => true,
            Visibility.CavingGroup => entity.CavingGroupId is { } cavingGroupId && user.IsMemberOf(cavingGroupId),
            _ => false,
        };
}
