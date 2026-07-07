// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// The caller's identity, global roles and team memberships, resolved once per request.
/// Pure data — the permission evaluator and visibility filters consume it.
/// </summary>
public sealed record UserContext(
    Guid UserId,
    IReadOnlySet<string> Roles,
    IReadOnlyDictionary<Guid, TeamRole> Teams)
{
    public bool IsAdmin => Roles.Contains(GlobalRoles.Admin);

    /// <summary>Editor and above may create content.</summary>
    public bool CanCreateContent =>
        Roles.Contains(GlobalRoles.Editor) || Roles.Contains(GlobalRoles.Manager) || IsAdmin;

    public IReadOnlyList<Guid> TeamIds => [.. Teams.Keys];

    public bool IsMemberOf(Guid teamId) => Teams.ContainsKey(teamId);

    public bool IsTeamAdmin(Guid teamId) =>
        Teams.TryGetValue(teamId, out var role) && role is TeamRole.Admin or TeamRole.Owner;
}

/// <summary>Resolves the current request's <see cref="UserContext"/> (null when anonymous).</summary>
public interface IUserContextAccessor
{
    Task<UserContext?> GetAsync(CancellationToken ct = default);
}
