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
        AccessDomain.Expeditions, AccessDomain.Checklists, AccessDomain.Events,
    ];

    /// <summary>
    /// What the starter ruleset grants over a domain. Three of them differ in one bit —
    /// documents, checklists and calendar events — because they carry no position, so the right
    /// to see an exact location says nothing about them and granting it would be noise in a
    /// ruleset an operator has to read and edit. The rest hold rows with a geometry of their
    /// own — an expedition's working area as much as a trip's — and so keep the bit. It decides
    /// nothing about the caves those rows reach: whether a caller sees a cave's exact
    /// position is asked in the feature domain against that cave's own protected roots, and
    /// no entry written here is consulted there.
    /// </summary>
    private static AccessAction StarterActions(AccessDomain domain) =>
        AccessAction.Read | AccessAction.Write | AccessAction.Create
        | (domain is AccessDomain.Documents or AccessDomain.Checklists or AccessDomain.Events
            ? AccessAction.None
            : AccessAction.ViewExactLocation);

    /// <summary>Stages the two seed groups on the context — the caller's SaveChanges
    /// commits them atomically with the caving group itself.</summary>
    public static async Task SeedForNewGroupAsync(
        SilexGisDbContext db, CavingGroup group, Guid creatorUserId, CancellationToken ct = default)
    {
        var members = new PermissionGroup
        {
            Name = await UniqueNameAsync(db, group.Name, " — members", ct),
            Slug = await UniqueSlugAsync(db, group.Slug, "-members", ct),
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
            Name = await UniqueNameAsync(db, group.Name, " — managers", ct),
            Slug = await UniqueSlugAsync(db, group.Slug, "-managers", ct),
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

    /// <summary>
    /// The width of a permission group's name and slug columns — the same as a caving group's, so
    /// a club whose name fills its own column would otherwise derive names its permission groups
    /// cannot hold, and its creation would fail with the database refusing the value.
    /// </summary>
    internal const int DerivedMaxLength = 100;

    private static async Task<string> UniqueNameAsync(
        SilexGisDbContext db, string groupName, string suffix, CancellationToken ct)
    {
        var candidate = Fit(groupName, suffix);
        for (var i = 2; await db.PermissionGroups.AnyAsync(g => g.Name == candidate, ct); i++)
        {
            candidate = Fit(groupName, $"{suffix} ({i})");
        }

        return candidate;
    }

    private static async Task<string> UniqueSlugAsync(
        SilexGisDbContext db, string groupSlug, string suffix, CancellationToken ct)
    {
        var candidate = Fit(groupSlug, suffix);
        for (var i = 2; await db.PermissionGroups.AnyAsync(g => g.Slug == candidate, ct); i++)
        {
            candidate = Fit(groupSlug, $"{suffix}-{i}");
        }

        return candidate;
    }

    /// <summary>
    /// The club's own part shortened so the whole fits the column: the suffix — what the group is
    /// for, and the number that keeps it unique — is the part that has to survive.
    /// </summary>
    internal static string Fit(string stem, string suffix)
    {
        var room = DerivedMaxLength - suffix.Length;
        var kept = stem.Length <= room ? stem : stem[..room].TrimEnd();
        return kept + suffix;
    }
}
