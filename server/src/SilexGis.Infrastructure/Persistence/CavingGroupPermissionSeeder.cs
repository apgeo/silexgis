// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Creating a caving group seeds its default permission list: two ordinary, renameable,
/// deletable permission groups. "«name» — members" holds the caving group itself as
/// trustee with the starter ruleset — Read/Write/Create on the group's content, plus
/// ViewExactLocation wherever content can carry a position — so club members keep today's
/// behavior, now as editable entries (a club that wants to hide protected locations from
/// its own members removes the VEL bit). Delete-own-content needs no entry: the ownership
/// built-in already grants it.
/// "«name» — managers" holds the creator: manage the group record itself plus enroll
/// people (including account-less cavers) from day one. The "default list" is nothing
/// but membership — deleting these groups simply leaves members with the built-ins.
/// </summary>
public static class CavingGroupPermissionSeeder
{
    /// <summary>The content domains the starter ruleset ranges over. Files flow through
    /// attachments; tags are not group-bound.</summary>
    private static readonly AccessDomain[] StarterDomains =
    [
        AccessDomain.Features, AccessDomain.TripLogs, AccessDomain.Geofiles,
        AccessDomain.GeoreferencedMaps, AccessDomain.MapViews, AccessDomain.Documents,
        AccessDomain.Expeditions,
    ];

    /// <summary>
    /// What the starter ruleset grants over a domain. Documents differ in one bit: they
    /// carry no position, so the right to see an exact location says nothing about them and
    /// granting it would be noise in a ruleset an operator has to read and edit. Every other
    /// domain here holds rows with a geometry of their own — an expedition's working area as
    /// much as a trip's — and so keeps the bit. It decides nothing about the caves those rows
    /// reach: whether a caller sees a cave's exact position is asked in the feature domain
    /// against that cave's own protected roots, and no entry written here is consulted there.
    /// </summary>
    private static AccessAction StarterActions(AccessDomain domain) =>
        AccessAction.Read | AccessAction.Write | AccessAction.Create
        | (domain == AccessDomain.Documents ? AccessAction.None : AccessAction.ViewExactLocation);

    /// <summary>Stages the two seed groups on the context — the caller's SaveChanges
    /// commits them atomically with the caving group itself.</summary>
    public static async Task SeedForNewGroupAsync(
        SilexGisDbContext db, CavingGroup group, Guid creatorUserId, CancellationToken ct = default)
    {
        var members = new PermissionGroup
        {
            Name = await UniqueNameAsync(db, $"{group.Name} — members", ct),
            Slug = await UniqueSlugAsync(db, $"{group.Slug}-members", ct),
            IsSeeded = true,
        };
        db.PermissionGroups.Add(members);
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = members.Id,
            MemberKind = AccessSubjectKind.CavingGroup,
            MemberId = group.Id,
        });
        foreach (var domain in StarterDomains)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                PermissionGroupId = members.Id,
                Effect = AccessEffect.Allow,
                Domain = domain,
                Actions = StarterActions(domain),
                ScopeKind = AccessScopeKind.CavingGroup,
                ScopeId = group.Id,
                GrantedBy = creatorUserId,
            });
        }

        var managers = new PermissionGroup
        {
            Name = await UniqueNameAsync(db, $"{group.Name} — managers", ct),
            Slug = await UniqueSlugAsync(db, $"{group.Slug}-managers", ct),
            IsSeeded = true,
        };
        db.PermissionGroups.Add(managers);
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = managers.Id,
            MemberKind = AccessSubjectKind.User,
            MemberId = creatorUserId,
        });
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = managers.Id,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.CavingGroups,
            // Execute is here so the person who starts a club can write to everyone on it. It is
            // a separate action from Write on purpose — editing a list of names and sending a
            // message to every account on that list are different acts, and an installation can
            // take this one back from a club's managers without taking the roster with it — but
            // withholding it by default would leave the club's own leader unable to do the thing
            // a club leader does, and only an installation administrator able to do it for them.
            Actions = AccessAction.Read | AccessAction.Write | AccessAction.ManagePermissions
                | AccessAction.Execute,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = group.Id,
            GrantedBy = creatorUserId,
        });
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = managers.Id,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Cavers,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
            GrantedBy = creatorUserId,
        });
    }

    private static async Task<string> UniqueNameAsync(SilexGisDbContext db, string name, CancellationToken ct)
    {
        var candidate = name;
        for (var i = 2; await db.PermissionGroups.AnyAsync(g => g.Name == candidate, ct); i++)
        {
            candidate = $"{name} ({i})";
        }

        return candidate;
    }

    private static async Task<string> UniqueSlugAsync(SilexGisDbContext db, string slug, CancellationToken ct)
    {
        var candidate = slug;
        for (var i = 2; await db.PermissionGroups.AnyAsync(g => g.Slug == candidate, ct); i++)
        {
            candidate = $"{slug}-{i}";
        }

        return candidate;
    }
}
