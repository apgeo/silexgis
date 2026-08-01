// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// The full effective-permission check: the pure <see cref="PermissionEvaluator"/>
/// layers (roles/ownership/visibility/caving group) plus explicit object_acl grants loaded
/// from storage. Endpoint guards call this; pure-computation paths that already know
/// the applicable grants call the evaluator directly.
/// </summary>
public interface IPermissionService
{
    Task<bool> CanAsync(
        UserContext? user, IProtectedEntity entity, ObjectPermission permission, CancellationToken ct = default);

    /// <summary>All permissions the caller holds on the entity (for UI capability hints).</summary>
    Task<ObjectPermission> EffectiveAsync(
        UserContext? user, IProtectedEntity entity, CancellationToken ct = default);
}
