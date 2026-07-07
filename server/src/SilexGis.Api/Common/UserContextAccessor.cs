// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Common;

/// <summary>
/// Builds the request's <see cref="UserContext"/>: identity + roles from the bearer token,
/// team memberships from the database (once per request, cached in the scoped instance).
/// </summary>
public sealed class UserContextAccessor(IHttpContextAccessor httpContextAccessor, SilexGisDbContext db)
    : IUserContextAccessor
{
    private UserContext? cached;
    private bool resolved;

    public async Task<UserContext?> GetAsync(CancellationToken ct = default)
    {
        if (resolved)
        {
            return cached;
        }

        var principal = httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue(Claims.Subject) ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId))
        {
            resolved = true;
            return null;
        }

        var roles = principal!.Claims
            .Where(c => c.Type is Claims.Role or ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        var teams = await db.TeamMembers
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role, ct);

        cached = new UserContext(userId, roles, teams);
        resolved = true;
        return cached;
    }
}
