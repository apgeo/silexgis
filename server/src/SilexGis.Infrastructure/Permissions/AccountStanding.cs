// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Whether an account, read outside any session of its own, is one the application still
/// answers for.
/// </summary>
/// <remarks>
/// For the surfaces that resolve an account from something other than its sign-in — a feed
/// address, a scheduled pass — and have to stop where the account stops. A feature slice asking
/// this reaches no user row itself: what a caller may see of another account is decided in one
/// place, and this is the one question about an account that such a surface is allowed to ask.
/// Read the way the administration page reads it, so the two cannot disagree about what "locked"
/// means.
/// </remarks>
public static class AccountStanding
{
    /// <summary>True when the account exists and is not locked at <paramref name="now"/>.</summary>
    public static async Task<bool> IsOpenAsync(
        SilexGisDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.LockoutEnd })
            .FirstOrDefaultAsync(ct);
        return account is not null && !(account.LockoutEnd is { } end && end > now);
    }
}
