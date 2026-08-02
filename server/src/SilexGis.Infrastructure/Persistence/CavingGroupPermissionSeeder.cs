// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Creating a caving group seeds its default permission list: two ordinary, renameable,
/// deletable permission groups. "«name» — members" holds the caving group itself as
/// trustee with the starter ruleset — Read/Write/Create/ViewExactLocation on the
/// group's content — so club members keep today's behavior, now as editable entries (a
/// club that wants to hide protected locations from its own members removes the VEL
/// bit). Delete-own-content needs no entry: the ownership built-in already grants it.
/// "«name» — managers" holds the creator: manage the group record itself plus enroll
/// people (including account-less cavers) from day one. The "default list" is nothing
/// but membership — deleting these groups simply leaves members with the built-ins.
/// </summary>
public static class CavingGroupPermissionSeeder
{
    /// <summary>The content domains the starter ruleset ranges over. Files flow through
    /// attachments; tags are not group-bound. Documents are deliberately absent rather than
    /// overlooked: this ruleset has never carried the right to add an upload, and nothing
    /// binds a document to a club yet, so granting it here would be a new right nobody
    /// asked for.</summary>
    private static readonly AccessDomain[] StarterDomains =
    [
        AccessDomain.Features, AccessDomain.TripLogs, AccessDomain.Geofiles,
        AccessDomain.GeoreferencedMaps, AccessDomain.MapViews,
    ];

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
                Actions = AccessAction.Read | AccessAction.Write | AccessAction.Create
                    | AccessAction.ViewExactLocation,
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
            Actions = AccessAction.Read | AccessAction.Write | AccessAction.ManagePermissions,
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
