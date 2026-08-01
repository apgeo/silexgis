// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Batch resolution of caver ids to what a caller may see of those people, and the bridge
/// between an account and its roster entry.
/// </summary>
/// <remarks>
/// The roster's counterpart to <see cref="ProfileDirectory"/>, and for the same reason: a person's
/// name appears on trip lists, rosters and credits, so deciding what to show belongs in one place
/// rather than in each slice. Where a caver holds an account, their account label wins — one
/// person must not appear under two names on the same page.
/// </remarks>
public static class CaverDirectory
{
    /// <summary>Display labels for roster rows, trip participants and credits.</summary>
    public static async Task<Dictionary<Guid, string>> ResolveLabelsAsync(
        SilexGisDbContext db, UserContext? user, IEnumerable<Guid> caverIds, CancellationToken ct)
    {
        // Anonymous callers never turn ids into names, matching the profile rule.
        if (user is null)
        {
            return [];
        }

        var ids = caverIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var cavers = await db.Cavers.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName, c.UserId })
            .ToListAsync(ct);

        var accountLabels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, cavers.Where(c => c.UserId is not null).Select(c => c.UserId!.Value), ct);

        return cavers.ToDictionary(
            c => c.Id,
            c => c.UserId is { } userId && accountLabels.TryGetValue(userId, out var label)
                ? label
                : c.FullName);
    }

    /// <summary>
    /// Adds the roster entry for a newly created account. Tracked, not saved, so it commits with
    /// the registration that made it.
    /// </summary>
    /// <remarks>
    /// Every account gets one at sign-up, so a member is countable from their first trip without
    /// anyone having to remember to add them, and so "this account" and "this person" never drift
    /// apart. The name follows the same rule attribution rows use, which is never the email
    /// address.
    /// </remarks>
    public static Caver CreateForNewAccount(
        SilexGisDbContext db, Guid userId, string? displayName, string? userName, string? email)
    {
        var caver = new Caver
        {
            FullName = ProfileProtection.Label(userId, displayName, userName, email),
            UserId = userId,
        };
        db.Cavers.Add(caver);
        return caver;
    }

    /// <summary>The roster entry of an account, or null when it has none.</summary>
    public static Task<Caver?> ForUserAsync(SilexGisDbContext db, Guid userId, CancellationToken ct) =>
        db.Cavers.FirstOrDefaultAsync(c => c.UserId == userId, ct);

    /// <summary>
    /// The roster entry of an account, created if missing. Added to the change tracker but not
    /// saved, so it commits with whatever the caller is already doing.
    /// </summary>
    /// <remarks>
    /// Accounts get a roster entry when they register, so this is a repair path — for accounts
    /// that predate the roster, and for the first time an older account is named on a trip.
    /// </remarks>
    public static async Task<Caver> EnsureForUserAsync(
        SilexGisDbContext db, UserContext user, Guid userId, CancellationToken ct)
    {
        var existing = await ForUserAsync(db, userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, [userId], ct);
        var caver = new Caver { FullName = labels.GetValueOrDefault(userId) ?? userId.ToString(), UserId = userId };
        db.Cavers.Add(caver);
        return caver;
    }
}
