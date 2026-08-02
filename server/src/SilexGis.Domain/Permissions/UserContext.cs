// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Permissions;

/// <summary>
/// The caller's identity and caving-group memberships, resolved once per request.
/// Pure data — the profile/caver disclosure rules consume it. Authorization facts live
/// in the access context; nothing here carries a role or a permission.
/// </summary>
public sealed record UserContext(
    Guid UserId,
    IReadOnlyList<Guid> CavingGroupIds);

/// <summary>Resolves the current request's <see cref="UserContext"/> (null when anonymous).</summary>
public interface IUserContextAccessor
{
    Task<UserContext?> GetAsync(CancellationToken ct = default);
}
