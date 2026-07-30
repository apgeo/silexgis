// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Batch resolution of user ids to what a given caller may see of those users. Every path that
/// emits another user's name or profile goes through here, so the visibility rules have one home
/// and a new caller cannot accidentally invent a looser one.
/// </summary>
public static class ProfileDirectory
{
    private static readonly IReadOnlySet<Guid> NoTeams = new HashSet<Guid>();

    /// <summary>
    /// Display labels for attribution rows — team members, trip participants, ACL grants, audit
    /// and history rows, file uploaders.
    /// </summary>
    /// <remarks>
    /// The label is always safe to show, and is never the email address: accounts are created
    /// with the address as the user name, so a plain "display name or user name" fallback would
    /// publish the address of everyone who never chose a display name.
    /// </remarks>
    public static async Task<Dictionary<Guid, string>> ResolveLabelsAsync(
        SilexGisDbContext db, UserContext? user, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        // Defence in depth: an anonymous route must never turn ids into names, or a shared link
        // plus a directory would identify the owner of a saved view.
        if (user is null)
        {
            return [];
        }

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.UserName, u.Email })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => ProfileProtection.Label(r.Id, r.DisplayName, r.UserName, r.Email));
    }

    /// <summary>The full profile of each user, reduced to what this caller may see.</summary>
    public static async Task<Dictionary<Guid, PublicProfile>> ResolveAsync(
        SilexGisDbContext db, UserContext? user, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        if (user is null)
        {
            return [];
        }

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        if (users.Count == 0)
        {
            return [];
        }

        var teamsBySubject = await TeamsAsync(db, ids, ct);

        // Addresses are read only for the subjects whose address setting could possibly pass, so
        // an ordinary directory page does not drag everyone's home address out of the database.
        var addressCandidates = users
            .Where(u => ProfileProtection.CanView(
                u.AddressVisibility,
                ProfileProtection.Relate(user, u.Id, teamsBySubject.GetValueOrDefault(u.Id, NoTeams))))
            .Select(u => u.Id)
            .ToList();

        var addresses = addressCandidates.Count == 0
            ? []
            : await db.UserAddresses.AsNoTracking()
                .Where(a => addressCandidates.Contains(a.UserId))
                .ToListAsync(ct);

        var addressesBySubject = addresses
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, IReadOnlyList<UserAddress> (g) => [.. g]);

        return users.ToDictionary(
            u => u.Id,
            u => ProfileProtection.Project(
                u,
                ProfileProtection.SettingsOf(u),
                addressesBySubject.GetValueOrDefault(u.Id, []),
                ProfileProtection.Relate(user, u.Id, teamsBySubject.GetValueOrDefault(u.Id, NoTeams))));
    }

    /// <summary>
    /// Which of these ids belong to real accounts, for validating a reference before storing it.
    /// </summary>
    /// <remarks>
    /// Ungated on purpose, and deliberately the only way a slice may ask: existence is not
    /// profile data — a caller who already holds an id learns nothing from being told it resolves
    /// — but routing the question through here keeps slices out of the user table entirely, so
    /// the rule that profile data has one home stays mechanically enforceable.
    /// </remarks>
    public static async Task<HashSet<Guid>> ExistingIdsAsync(
        SilexGisDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);

        return [.. found];
    }

    /// <summary>Single-subject variant for detail endpoints.</summary>
    public static async Task<PublicProfile?> ResolveOneAsync(
        SilexGisDbContext db, UserContext? user, Guid userId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(db, user, [userId], ct);
        return resolved.GetValueOrDefault(userId);
    }

    private static async Task<Dictionary<Guid, IReadOnlySet<Guid>>> TeamsAsync(
        SilexGisDbContext db, IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        var memberships = await db.TeamMembers.AsNoTracking()
            .Where(m => userIds.Contains(m.UserId))
            .Select(m => new { m.UserId, m.TeamId })
            .ToListAsync(ct);

        return memberships
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, IReadOnlySet<Guid> (g) => g.Select(m => m.TeamId).ToHashSet());
    }
}
