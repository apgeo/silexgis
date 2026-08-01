// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Common;

/// <summary>
/// Builds the request's <see cref="AccessContext"/>: identity from the bearer token,
/// memberships and reachable access entries from the database (once per request,
/// cached in the scoped instance). The resolution itself lives in
/// <see cref="AccessContextResolver"/> so tests evaluate exactly what requests do.
/// </summary>
public sealed class AccessContextAccessor(IHttpContextAccessor httpContextAccessor, SilexGisDbContext db)
    : IAccessContextAccessor
{
    private AccessContext? cached;
    private bool resolved;

    public async Task<AccessContext?> GetAsync(CancellationToken ct = default)
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

        cached = await AccessContextResolver.ResolveAsync(db, userId, ct);
        resolved = true;
        return cached;
    }
}
