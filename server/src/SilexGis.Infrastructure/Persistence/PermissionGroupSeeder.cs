// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Startup seeding of the well-known permission groups. Idempotent by slug and
/// deliberately create-only: a group that exists is never touched again, because its
/// entries are editable content an operator may have tightened — reseeding must not
/// undo policy. When the groups are created for the first time, existing Identity role
/// holders are mapped onto them (Admin → Full Administrators; Manager → Editors +
/// Caving Group Managers; Editor → Editors; Viewer → All Users only), which is how a
/// pre-redesign database keeps its people's capabilities.
/// </summary>
public static class PermissionGroupSeeder
{
    /// <summary>Content domains: the trio-carrying five plus files and tags — what the
    /// Editors and Reviewers seeds range over.</summary>
    private static readonly AccessDomain[] ContentDomains =
    [
        AccessDomain.Features, AccessDomain.TripLogs, AccessDomain.Geofiles,
        AccessDomain.GeoreferencedMaps, AccessDomain.MapViews, AccessDomain.Files,
        AccessDomain.Tags,
    ];

    /// <summary>Regular admin runs the installation but cannot rewrite the security
    /// model or its secrets — every domain except these three.</summary>
    private static readonly AccessDomain[] AdministratorsExcluded =
    [
        AccessDomain.PermissionGroups, AccessDomain.FeatureSets, AccessDomain.Settings,
    ];

    public static async Task SeedAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        var created = new List<string>();

        await EnsureGroupAsync(db, created, SeededPermissionGroups.FullAdministratorsSlug,
            SeededPermissionGroups.FullAdministratorsName, isProtected: true, entries: _ => [], ct);

        await EnsureGroupAsync(db, created, SeededPermissionGroups.AllUsersSlug,
            SeededPermissionGroups.AllUsersName, isProtected: true, entries: id =>
            [
                // The baseline authenticated reads every account holds — where today's
                // ungated catalog endpoints live on as editable seed content.
                RulesetEntry(id, AccessDomain.MapLayers, AccessAction.Read),
                RulesetEntry(id, AccessDomain.Tags, AccessAction.Read),
                RulesetEntry(id, AccessDomain.Taxonomies, AccessAction.Read),
                RulesetEntry(id, AccessDomain.Hierarchies, AccessAction.Read),
                RulesetEntry(id, AccessDomain.Cavers, AccessAction.Read),
                // Create keeps the pick-or-create affiliation flow open to everyone.
                RulesetEntry(id, AccessDomain.CavingGroups, AccessAction.Read | AccessAction.Create),
            ], ct);

        await EnsureGroupAsync(db, created, SeededPermissionGroups.AdministratorsSlug,
            SeededPermissionGroups.AdministratorsName, isProtected: false, entries: id =>
            [
                .. Enum.GetValues<AccessDomain>()
                    .Where(domain => !AdministratorsExcluded.Contains(domain))
                    .Select(domain => RulesetEntry(id, domain, AccessActions.Everything)),
            ], ct);

        await EnsureGroupAsync(db, created, SeededPermissionGroups.CavingGroupManagersSlug,
            SeededPermissionGroups.CavingGroupManagersName, isProtected: false, entries: id =>
            [
                // Trust assumption, stated: roster writes move inherited rights, so this
                // group means being trusted to enroll people into any club.
                RulesetEntry(id, AccessDomain.CavingGroups,
                    AccessAction.Read | AccessAction.Create | AccessAction.Write | AccessAction.ManagePermissions),
                RulesetEntry(id, AccessDomain.Cavers,
                    AccessAction.Read | AccessAction.Create | AccessAction.Write | AccessAction.ManagePermissions),
            ], ct);

        await EnsureGroupAsync(db, created, SeededPermissionGroups.EditorsSlug,
            SeededPermissionGroups.EditorsName, isProtected: false, entries: id =>
            [
                // Execute keeps geofile import and raster reprocess working for the
                // people who could run them before the redesign.
                .. ContentDomains.Select(domain => RulesetEntry(id, domain,
                    AccessAction.Read | AccessAction.Create | AccessAction.Write
                    | AccessAction.Share | AccessAction.Execute)),
            ], ct);

        await EnsureGroupAsync(db, created, SeededPermissionGroups.ReviewersSlug,
            SeededPermissionGroups.ReviewersName, isProtected: false, entries: id =>
            [
                // Reads past visibility — an auditing role.
                .. ContentDomains.Select(domain => RulesetEntry(id, domain, AccessAction.Read)),
            ], ct);

        if (created.Count > 0)
        {
            await MapExistingRoleHoldersAsync(db, created, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The permission groups a legacy global role maps onto.</summary>
    public static IReadOnlyList<string> GroupSlugsForRole(string role) => role switch
    {
        GlobalRoles.Admin => [SeededPermissionGroups.FullAdministratorsSlug],
        GlobalRoles.Manager => [SeededPermissionGroups.EditorsSlug, SeededPermissionGroups.CavingGroupManagersSlug],
        GlobalRoles.Editor => [SeededPermissionGroups.EditorsSlug],
        _ => [],
    };

    /// <summary>Adds the user to the groups their legacy role maps onto (idempotent).
    /// Test infrastructure that mints role-holding accounts uses this too, so accounts
    /// created after first seed behave like accounts that lived through it.</summary>
    public static async Task EnsureRoleMembershipsAsync(
        SilexGisDbContext db, Guid userId, string role, CancellationToken ct = default)
    {
        var slugs = GroupSlugsForRole(role);
        if (slugs.Count == 0)
        {
            return;
        }

        var groupIds = await db.PermissionGroups
            .Where(g => slugs.Contains(g.Slug))
            .Select(g => g.Id)
            .ToListAsync(ct);
        var existing = await db.PermissionGroupMembers
            .Where(m => groupIds.Contains(m.PermissionGroupId)
                && m.MemberKind == AccessSubjectKind.User && m.MemberId == userId)
            .Select(m => m.PermissionGroupId)
            .ToListAsync(ct);
        foreach (var groupId in groupIds.Except(existing))
        {
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = groupId,
                MemberKind = AccessSubjectKind.User,
                MemberId = userId,
            });
        }
    }

    private static async Task MapExistingRoleHoldersAsync(
        SilexGisDbContext db, List<string> createdSlugs, CancellationToken ct)
    {
        var assignments = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            select new { userRole.UserId, RoleName = role.Name })
            .ToListAsync(ct);

        var groupIdsBySlug = await db.PermissionGroups
            .Where(g => createdSlugs.Contains(g.Slug))
            .ToDictionaryAsync(g => g.Slug, g => g.Id, ct);

        var seen = new HashSet<(Guid GroupId, Guid UserId)>();
        foreach (var assignment in assignments)
        {
            foreach (var slug in GroupSlugsForRole(assignment.RoleName ?? string.Empty))
            {
                // Only groups created in this run: an existing group's membership is
                // operator-owned data the seeder must not append to.
                if (groupIdsBySlug.TryGetValue(slug, out var groupId)
                    && seen.Add((groupId, assignment.UserId)))
                {
                    db.PermissionGroupMembers.Add(new PermissionGroupMember
                    {
                        PermissionGroupId = groupId,
                        MemberKind = AccessSubjectKind.User,
                        MemberId = assignment.UserId,
                    });
                }
            }
        }
    }

    private static async Task EnsureGroupAsync(
        SilexGisDbContext db,
        List<string> created,
        string slug,
        string name,
        bool isProtected,
        Func<Guid, IEnumerable<AccessEntry>> entries,
        CancellationToken ct)
    {
        if (await db.PermissionGroups.AnyAsync(g => g.Slug == slug, ct))
        {
            return;
        }

        var group = new PermissionGroup
        {
            Name = name,
            Slug = slug,
            IsProtected = isProtected,
            IsSeeded = true,
        };
        db.PermissionGroups.Add(group);
        db.AccessEntries.AddRange(entries(group.Id));
        created.Add(slug);
    }

    private static AccessEntry RulesetEntry(
        Guid permissionGroupId, AccessDomain domain, AccessAction actions) => new()
    {
        PermissionGroupId = permissionGroupId,
        Effect = AccessEffect.Allow,
        Domain = domain,
        Actions = actions,
        ScopeKind = AccessScopeKind.All,
    };
}
