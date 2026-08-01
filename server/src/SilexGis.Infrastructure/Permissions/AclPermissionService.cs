// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// IPermissionService backed by object_acl: loads the caller's applicable grants for the
/// entity (direct user grants plus grants to any of their caving groups), ORs the flags and hands
/// them to the pure evaluator. Fast paths (admin/owner) skip the lookup entirely.
/// Features are keyed by the feature FK; non-feature entities by the polymorphic pair.
///
/// Grants are deliberately NON-CASCADING over the feature hierarchy — a grant applies to
/// its target row only. The owner-decided cascade semantics (all flags, with overrides
/// and deny, evaluated at read time) belong to the planned ruleset-based permission
/// system, which will consume the features' ancestor arrays; nothing here anticipates it.
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
    /// Bulk variant for obfuscation paths that process many features per request: all
    /// feature ids on which the caller holds an explicit ViewExactLocation grant
    /// (directly or via a caving group). The exact-view rule evaluates these against a row's
    /// protected ROOTS — generalizing the old cave-hardcoded lookup to any protection
    /// root. Admin/owner fast paths are evaluated per row by the caller.
    /// </summary>
    public async Task<HashSet<Guid>> ExactLocationGrantFeatureIdsAsync(UserContext user, CancellationToken ct)
    {
        var userId = user.UserId;
        var cavingGroupIds = user.CavingGroupIds;
        var ids = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.FeatureId != null
                && ((a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == AclSubjectKind.CavingGroup && cavingGroupIds.Contains(a.SubjectId)))
                && a.Permissions.HasFlag(ObjectPermission.ViewExactLocation))
            .Select(a => a.FeatureId!.Value)
            .ToListAsync(ct);
        return [.. ids];
    }

    private async Task<ObjectPermission> LoadGrantsAsync(
        UserContext user, IProtectedEntity entity, CancellationToken ct)
    {
        var entityId = entity.Id;
        var userId = user.UserId;
        var cavingGroupIds = user.CavingGroupIds;

        IQueryable<ObjectAcl> query;
        if (entity is Feature)
        {
            query = db.ObjectAcls.AsNoTracking().Where(a => a.FeatureId == entityId);
        }
        else
        {
            var entityType = ProtectedEntityTypes.Of(entity);
            query = db.ObjectAcls.AsNoTracking()
                .Where(a => a.EntityType == entityType && a.EntityId == entityId);
        }

        var flags = await query
            .Where(a => (a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId)
                || (a.SubjectKind == AclSubjectKind.CavingGroup && cavingGroupIds.Contains(a.SubjectId)))
            .Select(a => (int)a.Permissions)
            .ToListAsync(ct);

        return flags.Aggregate(ObjectPermission.None, (acc, f) => acc | (ObjectPermission)f);
    }
}
