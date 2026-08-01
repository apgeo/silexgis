// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// The caller's identity, global roles and caving group memberships, resolved once per request.
/// Pure data — the permission evaluator and visibility filters consume it.
/// </summary>
public sealed record UserContext(
    Guid UserId,
    IReadOnlySet<string> Roles,
    IReadOnlyDictionary<Guid, CavingGroupRole> CavingGroups)
{
    public bool IsAdmin => Roles.Contains(GlobalRoles.Admin);

    /// <summary>Editor and above may create content.</summary>
    public bool CanCreateContent =>
        Roles.Contains(GlobalRoles.Editor) || Roles.Contains(GlobalRoles.Manager) || IsAdmin;

    public IReadOnlyList<Guid> CavingGroupIds => [.. CavingGroups.Keys];

    public bool IsMemberOf(Guid cavingGroupId) => CavingGroups.ContainsKey(cavingGroupId);

    public bool IsCavingGroupAdmin(Guid cavingGroupId) =>
        CavingGroups.TryGetValue(cavingGroupId, out var role) && role is CavingGroupRole.Admin or CavingGroupRole.Owner;
}

/// <summary>Resolves the current request's <see cref="UserContext"/> (null when anonymous).</summary>
public interface IUserContextAccessor
{
    Task<UserContext?> GetAsync(CancellationToken ct = default);
}
