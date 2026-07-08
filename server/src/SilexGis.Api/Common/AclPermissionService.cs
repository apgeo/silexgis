// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// IPermissionService backed by object_acl: loads the caller's applicable grants for the
/// entity (direct user grants plus grants to any of their teams), ORs the flags and hands
/// them to the pure evaluator. Fast paths (admin/owner) skip the lookup entirely.
/// </summary>
public sealed class AclPermissionService(SilexGisDbContext db) : IPermissionService
{
    public async Task<bool> CanAsync(
        UserContext? user, IProtectedEntity entity, ObjectPermission permission, CancellationToken ct = default)
    {
        if (user is null)
        {
            return false;
        }

        // Evaluate the storage-free layers first; only consult ACL rows when they deny.
        if (PermissionEvaluator.Can(user, entity, permission))
        {
            return true;
        }

        var granted = await LoadGrantsAsync(user, entity, ct);
        return PermissionEvaluator.Can(user, entity, permission, granted);
    }

    public async Task<ObjectPermission> EffectiveAsync(
        UserContext? user, IProtectedEntity entity, CancellationToken ct = default)
    {
        if (user is null)
        {
            return ObjectPermission.None;
        }

        var granted = user.IsAdmin || entity.OwnerUserId == user.UserId
            ? ObjectPermission.None // irrelevant — the layers below grant everything
            : await LoadGrantsAsync(user, entity, ct);

        var all = new[]
        {
            ObjectPermission.Read, ObjectPermission.Write, ObjectPermission.Delete,
            ObjectPermission.Share, ObjectPermission.ManagePermissions, ObjectPermission.ViewExactLocation,
        };
        var effective = ObjectPermission.None;
        foreach (var permission in all)
        {
            if (PermissionEvaluator.Can(user, entity, permission, granted))
            {
                effective |= permission;
            }
        }

        return effective;
    }

    /// <summary>
    /// Bulk variant for obfuscation paths that process many caves per request: all cave
    /// ids on which the caller holds an explicit ViewExactLocation grant (directly or
    /// via a team). Admin/owner fast paths are evaluated per row by the caller.
    /// </summary>
    public async Task<HashSet<Guid>> CaveExactLocationGrantsAsync(UserContext user, CancellationToken ct)
    {
        var userId = user.UserId;
        var teamIds = user.TeamIds;
        var ids = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.EntityType == AttachedEntityType.Cave
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.Team && teamIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.ViewExactLocation))
            .Select(a => a.EntityId)
            .ToListAsync(ct);
        return [.. ids];
    }

    private async Task<ObjectPermission> LoadGrantsAsync(
        UserContext user, IProtectedEntity entity, CancellationToken ct)
    {
        var entityType = ProtectedEntityTypes.Of(entity);
        var entityId = entity.Id;
        var userId = user.UserId;
        var teamIds = user.TeamIds;

        var flags = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.Team && teamIds.Contains(a.SubjectId))))
            .Select(a => (int)a.Permissions)
            .ToListAsync(ct);

        return flags.Aggregate(ObjectPermission.None, (acc, f) => acc | (ObjectPermission)f);
    }
}
