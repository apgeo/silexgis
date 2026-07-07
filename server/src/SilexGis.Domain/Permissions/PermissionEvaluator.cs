// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// Effective-permission algorithm. Current scope: global roles,
/// ownership, visibility and team roles. Explicit ACL grants (object_acl) are added in the
/// permissions phase — this is the single place they will plug into.
/// Team defaults: member → Read+Write; team admin/owner → +Delete+Share+Manage.
/// </summary>
public static class PermissionEvaluator
{
    public static bool Can(UserContext? user, IProtectedEntity entity, ObjectPermission permission)
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

        if (entity.TeamId is { } teamId && user.IsMemberOf(teamId))
        {
            var granted = user.IsTeamAdmin(teamId)
                ? ObjectPermission.Read | ObjectPermission.Write | ObjectPermission.Delete
                    | ObjectPermission.Share | ObjectPermission.ManagePermissions
                    | ObjectPermission.ViewExactLocation
                : ObjectPermission.Read | ObjectPermission.Write | ObjectPermission.ViewExactLocation;
            return granted.HasFlag(permission);
        }

        return false;
    }

    private static bool VisibilityAllowsRead(UserContext user, IProtectedEntity entity) =>
        entity.Visibility switch
        {
            Visibility.Public or Visibility.Authenticated => true,
            Visibility.Team => entity.TeamId is { } teamId && user.IsMemberOf(teamId),
            _ => false,
        };
}
