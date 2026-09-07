// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The access model around the evaluator: the seeded permission groups, the per-caving-
/// group starter rulesets, the role→group mapping, the caving-group binding guard, the
/// no-amplification bound on the per-object grant surface, and the last-full-admin
/// lockout guard.
/// </summary>
public sealed class AccessModelTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    public AccessModelTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeded_permission_groups_exist_with_their_rulesets()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var groups = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.IsSeeded)
            .ToDictionaryAsync(g => g.Slug);

        // The six installation-level seeds (per-caving-group seeds are also IsSeeded but
        // carry generated slugs).
        groups.ShouldContainKey(SeededPermissionGroups.FullAdministratorsSlug);
        groups.ShouldContainKey(SeededPermissionGroups.AllUsersSlug);
        groups.ShouldContainKey(SeededPermissionGroups.AdministratorsSlug);
        groups.ShouldContainKey(SeededPermissionGroups.CavingGroupManagersSlug);
        groups.ShouldContainKey(SeededPermissionGroups.EditorsSlug);
        groups.ShouldContainKey(SeededPermissionGroups.ReviewersSlug);

        // Full Administrators is protected and entry-less: membership IS the grant,
        // which is what keeps it unreachable by deny.
        var fullAdmins = groups[SeededPermissionGroups.FullAdministratorsSlug];
        fullAdmins.IsProtected.ShouldBeTrue();
        (await db.AccessEntries.AnyAsync(e => e.PermissionGroupId == fullAdmins.Id)).ShouldBeFalse();

        // All Users carries the baseline catalog reads plus the pick-or-create
        // affiliation Create.
        var allUsers = groups[SeededPermissionGroups.AllUsersSlug];
        allUsers.IsProtected.ShouldBeTrue();
        var allUsersEntries = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == allUsers.Id)
            .ToListAsync();
        allUsersEntries.ShouldAllBe(e => e.Effect == AccessEffect.Allow && e.ScopeKind == AccessScopeKind.All);
        allUsersEntries.Single(e => e.Domain == AccessDomain.CavingGroups).Actions
            .ShouldBe(AccessAction.Read | AccessAction.Create);
        allUsersEntries.Select(e => e.Domain).ShouldBe(
            [
                AccessDomain.MapLayers, AccessDomain.Tags, AccessDomain.Taxonomies,
                AccessDomain.Hierarchies, AccessDomain.Cavers, AccessDomain.CavingGroups,
                // A saved view is the caller's own workspace state, so every account keeps
                // being able to make one.
                AccessDomain.MapViews,
                // And so is a list of what has to be settled before a trip sets off: anybody
                // may write one and decide for themselves who else sees it.
                AccessDomain.Checklists,
                // And so is a date in the calendar: anybody may put one there, and decide for
                // themselves who else sees that.
                AccessDomain.Events,
            ],
            ignoreOrder: true);

        // Administrators: everything except the security model and its secrets.
        var administrators = groups[SeededPermissionGroups.AdministratorsSlug];
        var adminDomains = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == administrators.Id)
            .Select(e => e.Domain)
            .ToListAsync();
        adminDomains.ShouldNotContain(AccessDomain.PermissionGroups);
        adminDomains.ShouldNotContain(AccessDomain.FeatureSets);
        adminDomains.ShouldNotContain(AccessDomain.Settings);
        adminDomains.ShouldContain(AccessDomain.Users);
        adminDomains.ShouldContain(AccessDomain.Audit);
    }

    [Fact]
    public async Task Roles_map_onto_the_seeded_groups()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"acc-adm-{suffix}@t.local");
        var editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-edi-{suffix}@t.local");
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-vie-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        (await RosterHelper.AccessContextOfAsync(db, adminId)).IsFullAdmin.ShouldBeTrue();

        var editor = await RosterHelper.AccessContextOfAsync(db, editorId);
        editor.IsFullAdmin.ShouldBeFalse();
        editor.Entries.ShouldContain(e =>
            e.Domain == AccessDomain.Features && (e.Actions & AccessAction.Create) != 0);

        // A regular account holds All Users and nothing more: catalog reads plus the
        // affiliation Create, no content entries.
        var viewer = await RosterHelper.AccessContextOfAsync(db, viewerId);
        viewer.IsFullAdmin.ShouldBeFalse();
        viewer.Entries.ShouldContain(e => e.Domain == AccessDomain.MapLayers);
        viewer.Entries.ShouldNotContain(e => e.Domain == AccessDomain.Features);
    }

    [Fact]
    public async Task Creating_a_caving_group_seeds_its_default_permission_list()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"acc-mgr-{suffix}@t.local");
        using var manager = await AuthHelper.BearerClientAsync(factory, $"acc-mgr-{suffix}@t.local");

        var groupId = await CreateCavingGroupAsync(manager, $"Seeded Club {suffix}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // "«name» — members": the caving group as trustee, the starter ruleset on the
        // group's content — Read/Write/Create AND ViewExactLocation (club members keep
        // exact view by default; a club that wants otherwise edits the entries).
        var membersGroupId = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == groupId)
            .Select(m => m.PermissionGroupId)
            .SingleAsync();
        var starter = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == membersGroupId)
            .ToListAsync();
        starter.ShouldAllBe(e => e.Effect == AccessEffect.Allow
            && e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId == groupId
            && (e.Actions & (AccessAction.Read | AccessAction.Write | AccessAction.Create))
                == (AccessAction.Read | AccessAction.Write | AccessAction.Create));
        // Expeditions are on the list for the reason trips are: a club's camps are club
        // content, and a starter ruleset that left them out would give a club members who may
        // write every trip of a camp and not the camp holding them.
        starter.Select(e => e.Domain).ShouldBe(
            [
                AccessDomain.Features, AccessDomain.TripLogs, AccessDomain.Geofiles,
                AccessDomain.GeoreferencedMaps, AccessDomain.MapViews, AccessDomain.Documents,
                AccessDomain.Expeditions, AccessDomain.Checklists, AccessDomain.Events,
            ],
            ignoreOrder: true);

        // Every domain that can carry a position carries the exact-view bit; documents,
        // checklists and calendar events carry none, so granting it there would be a line
        // nobody editing this ruleset could act on. An event is where a club meets, written
        // down for a person to read — an address, never a coordinate.
        starter.ShouldAllBe(e => ((e.Actions & AccessAction.ViewExactLocation) != 0)
            == (e.Domain != AccessDomain.Documents && e.Domain != AccessDomain.Checklists
                && e.Domain != AccessDomain.Events));

        // "«name» — managers": the creator manages the group record and can enroll
        // people from day one.
        var managerCtx = await RosterHelper.AccessContextOfAsync(db, managerId);
        managerCtx.Entries.ShouldContain(e =>
            e.Domain == AccessDomain.CavingGroups && e.ScopeKind == AccessScopeKind.Object
            && e.ScopeId == groupId && (e.Actions & AccessAction.ManagePermissions) != 0);
        managerCtx.Entries.ShouldContain(e =>
            e.Domain == AccessDomain.Cavers && (e.Actions & AccessAction.Create) != 0);

        // And can write to everyone on it. Without this the club's own leader could not announce
        // anything and only an installation administrator could, which is not what a club is.
        managerCtx.Entries.ShouldContain(e =>
            e.Domain == AccessDomain.CavingGroups && e.ScopeKind == AccessScopeKind.Object
            && e.ScopeId == groupId && (e.Actions & AccessAction.Execute) != 0);
    }

    [Fact]
    public async Task Members_write_club_content_through_the_starter_ruleset()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"acc-flow-mgr-{suffix}@t.local");
        var editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-flow-edi-{suffix}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-flow-mem-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-flow-str-{suffix}@t.local");

        using var manager = await AuthHelper.BearerClientAsync(factory, $"acc-flow-mgr-{suffix}@t.local");
        using var editor = await AuthHelper.BearerClientAsync(factory, $"acc-flow-edi-{suffix}@t.local");
        using var member = await AuthHelper.BearerClientAsync(factory, $"acc-flow-mem-{suffix}@t.local");
        using var strangerClient = await AuthHelper.BearerClientAsync(factory, $"acc-flow-str-{suffix}@t.local");

        var groupId = await CreateCavingGroupAsync(manager, $"Flow Club {suffix}");
        foreach (var userId in new[] { editorId, memberId })
        {
            (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
            {
                caverId = await RosterHelper.CaverIdForAsync(factory, userId),
                role = "member",
            })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // An Editor member binds a PRIVATE cave to the club.
        var caveId = await CreateCaveAsync(editor, $"Flow Cave {suffix}", "private", groupId);

        // A regular member reads and edits it through the starter ruleset — no role, no
        // ownership, no visibility: entries alone.
        var read = await member.GetAsync($"/api/v1/caves/{caveId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = await read.Content.ReadFromJsonAsync<JsonElement>();

        (await member.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", new
        {
            name = current.GetProperty("name").GetString(),
            caveTypeId = current.GetProperty("caveTypeId").GetInt64(),
            visibility = "private",
            cavingGroupId = groupId,
            explorationStatus = "Unknown",
            isShowCave = false,
            description = "edited by a club member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A non-member regular user gets non-disclosure.
        (await strangerClient.GetAsync($"/api/v1/caves/{caveId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Binding_content_to_a_foreign_caving_group_is_refused()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"acc-bind-mgr-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-bind-edi-{suffix}@t.local");
        using var manager = await AuthHelper.BearerClientAsync(factory, $"acc-bind-mgr-{suffix}@t.local");
        using var editor = await AuthHelper.BearerClientAsync(factory, $"acc-bind-edi-{suffix}@t.local");

        var groupId = await CreateCavingGroupAsync(manager, $"Bind Club {suffix}");

        // Editors hold domain-wide Create/Write — deliberately NOT enough to push
        // content into a club they are no part of.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Bind Cave {suffix}",
            caveTypeId,
            visibility = "private",
            cavingGroupId = groupId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync())
            .ShouldContain(CavingGroupBindingRules.ForbiddenCode);
    }

    [Fact]
    public async Task Grant_surface_is_bounded_by_what_the_granter_holds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-amp-own-{suffix}@t.local");
        var delegateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-amp-del-{suffix}@t.local");
        var granteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-amp-gra-{suffix}@t.local");
        using var owner = await AuthHelper.BearerClientAsync(factory, $"acc-amp-own-{suffix}@t.local");
        using var delegated = await AuthHelper.BearerClientAsync(factory, $"acc-amp-del-{suffix}@t.local");

        var caveId = await CreateCaveAsync(owner, $"Amp Cave {suffix}", "private", cavingGroupId: null);

        // The owner delegates Read + ManagePermissions — but not ViewExactLocation.
        (await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user", subjectId = delegateId, effect = "allow",
                    actions = "read, managePermissions", scopeKind = "object",
                },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The delegate may pass on what they hold…
        (await delegated.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new object[]
            {
                new
                {
                    subjectKind = "user", subjectId = delegateId, effect = "allow",
                    actions = "read, managePermissions", scopeKind = "object",
                },
                new { subjectKind = "user", subjectId = granteeId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // …but not rights they lack: no amplification.
        var exceeded = await delegated.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new object[]
            {
                new
                {
                    subjectKind = "user", subjectId = delegateId, effect = "allow",
                    actions = "read, managePermissions", scopeKind = "object",
                },
                new
                {
                    subjectKind = "user", subjectId = granteeId, effect = "allow",
                    actions = "read, viewExactLocation", scopeKind = "object",
                },
            },
        });
        exceeded.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await exceeded.Content.ReadAsStringAsync()).ShouldContain(AccessEntryRules.ExceedsOwnRightsCode);
    }

    [Fact]
    public async Task The_last_reachable_full_administrator_cannot_be_severed()
    {
        // The roster paths can only sever a full administrator who reaches the group
        // THROUGH a caving group — a direct membership row survives any roster edit, and
        // removing one is its own (not yet exposed) operation. So this builds exactly the
        // fragile arrangement the guard exists for: one club, trustee of Full
        // Administrators, with one person in it and no direct membership anywhere.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"acc-last-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"acc-last-{suffix}@t.local");

        var groupId = await CreateCavingGroupAsync(admin, $"Last Admin Club {suffix}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caverId = await db.Cavers.Where(c => c.UserId == adminId).Select(c => c.Id).SingleAsync();
        var fullAdminsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => g.Id).SingleAsync();

        // The database is shared across suites, so other full administrators exist. Park
        // every direct membership (including this account's own) for the duration: the
        // group-mediated path must then be the installation's only one.
        var parkedMembers = await db.PermissionGroupMembers
            .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
            .ToListAsync();
        try
        {
            db.PermissionGroupMembers.RemoveRange(parkedMembers);
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = fullAdminsId,
                MemberKind = AccessSubjectKind.CavingGroup,
                MemberId = groupId,
            });
            await db.SaveChangesAsync();

            // Unlinking that account severs the only path in, and must be refused.
            var severed = await admin.DeleteAsync($"/api/v1/cavers/{caverId}/account-link");
            severed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await severed.Content.ReadAsStringAsync()).ShouldContain(FullAdminGuard.LastFullAdminCode);

            // The row is untouched — the refusal rolled the write back.
            (await db.Cavers.AsNoTracking().Where(c => c.Id == caverId).Select(c => c.UserId).SingleAsync())
                .ShouldBe(adminId);

            // Emptying the club is the same severance by another route, and equally refused.
            var emptied = await admin.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
            emptied.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await emptied.Content.ReadAsStringAsync()).ShouldContain(FullAdminGuard.LastFullAdminCode);
        }
        finally
        {
            await db.PermissionGroupMembers
                .Where(m => m.PermissionGroupId == fullAdminsId
                    && m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == groupId)
                .ExecuteDeleteAsync();
            foreach (var member in parkedMembers)
            {
                db.PermissionGroupMembers.Add(new PermissionGroupMember
                {
                    PermissionGroupId = member.PermissionGroupId,
                    MemberKind = member.MemberKind,
                    MemberId = member.MemberId,
                });
            }

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Deleting_a_caving_group_cleans_its_allows_and_blocks_on_denies()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"acc-del-adm-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"acc-del-adm-{suffix}@t.local");

        var groupId = await CreateCavingGroupAsync(admin, $"Del Club {suffix}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // A deny anchored on the group blocks deletion: removing it must stay an
        // explicit act on the entry, never a delete side effect.
        var deny = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = Guid.CreateVersion7(),
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.CavingGroup,
            ScopeId = groupId,
        };
        db.AccessEntries.Add(deny);
        await db.SaveChangesAsync();

        var blocked = await admin.DeleteAsync($"/api/v1/caving-groups/{groupId}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).ShouldContain("caving_group.deny_entries_exist");

        db.AccessEntries.Remove(deny);
        await db.SaveChangesAsync();

        // Without denies the group goes, taking its starter allows and trustee rows
        // with it — nothing dangling for the verifier to flag.
        (await admin.DeleteAsync($"/api/v1/caving-groups/{groupId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await db.AccessEntries.AnyAsync(e => e.ScopeId == groupId || e.SubjectId == groupId))
            .ShouldBeFalse();
        (await db.PermissionGroupMembers.AnyAsync(
                m => m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == groupId))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task The_guard_counts_only_live_unlocked_reachable_accounts()
    {
        // No endpoint deletes or locks a user yet (/users administration is unbuilt), so
        // the lock/delete arms of the §-style "live membership" rule are pinned against
        // the guard itself: a locked or deleted account must stop counting, exactly as
        // the spec words it — live-membership aware, not row-count aware.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var soleId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-live-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<FullAdminGuard>();
        var fullAdminsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => g.Id).SingleAsync();

        var parked = await db.PermissionGroupMembers
            .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
            .ToListAsync();
        try
        {
            db.PermissionGroupMembers.RemoveRange(parked);
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = fullAdminsId,
                MemberKind = AccessSubjectKind.User,
                MemberId = soleId,
            });
            await db.SaveChangesAsync();
            (await guard.AnyLiveFullAdminAsync()).ShouldBeTrue();

            // Locked: the row exists, the person cannot sign in — that is not an admin.
            var sole = await db.Users.SingleAsync(u => u.Id == soleId);
            sole.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
            (await guard.AnyLiveFullAdminAsync()).ShouldBeFalse();

            // An expired lock counts again.
            sole.LockoutEnd = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
            (await guard.AnyLiveFullAdminAsync()).ShouldBeTrue();

            // Deleted: the membership row dangling behind a vanished account counts for
            // nothing.
            db.Users.Remove(sole);
            await db.SaveChangesAsync();
            (await guard.AnyLiveFullAdminAsync()).ShouldBeFalse();
        }
        finally
        {
            await db.PermissionGroupMembers
                .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
                .ExecuteDeleteAsync();
            foreach (var member in parked)
            {
                db.PermissionGroupMembers.Add(new PermissionGroupMember
                {
                    PermissionGroupId = member.PermissionGroupId,
                    MemberKind = member.MemberKind,
                    MemberId = member.MemberId,
                });
            }

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Removing_the_last_full_administrator_trustee_is_refused()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var soleId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-sole-{suffix}@t.local");
        using var sole = await AuthHelper.BearerClientAsync(factory, $"acc-sole-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fullAdminsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => g.Id).SingleAsync();

        var parked = await db.PermissionGroupMembers
            .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
            .ToListAsync();
        try
        {
            db.PermissionGroupMembers.RemoveRange(parked);
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = fullAdminsId,
                MemberKind = AccessSubjectKind.User,
                MemberId = soleId,
            });
            await db.SaveChangesAsync();

            // The sole administrator asks to remove their own trustee row: refused with
            // the lockout code — the installation must never orphan its escape hatch.
            var severed = await sole.DeleteAsync(
                $"/api/v1/permission-groups/{fullAdminsId}/members/user/{soleId}");
            severed.StatusCode.ShouldBe(HttpStatusCode.Conflict, await severed.Content.ReadAsStringAsync());
            (await severed.Content.ReadAsStringAsync()).ShouldContain(FullAdminGuard.LastFullAdminCode);

            // The refusal rolled back: they are still a full administrator.
            (await RosterHelper.AccessContextOfAsync(db, soleId)).IsFullAdmin.ShouldBeTrue();
        }
        finally
        {
            await db.PermissionGroupMembers
                .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
                .ExecuteDeleteAsync();
            foreach (var member in parked)
            {
                db.PermissionGroupMembers.Add(new PermissionGroupMember
                {
                    PermissionGroupId = member.PermissionGroupId,
                    MemberKind = member.MemberKind,
                    MemberId = member.MemberId,
                });
            }

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Create_scoped_to_a_subtree_reaches_only_that_subtree()
    {
        // A Create deny or grant is evaluated against the TARGET context — the
        // prospective parent's chain — so a subtree-scoped Create opens exactly the area
        // it names and nothing else. This is the walk deciding creation, not a role.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-sub-edi-{suffix}@t.local");
        var creatorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-sub-cre-{suffix}@t.local");
        using var editor = await AuthHelper.BearerClientAsync(factory, $"acc-sub-edi-{suffix}@t.local");
        using var creator = await AuthHelper.BearerClientAsync(factory, $"acc-sub-cre-{suffix}@t.local");

        Guid areaId;
        long caveTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            var karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();

            var created = await editor.PostAsJsonAsync("/api/v1/features", new
            {
                kind = "generic",
                name = $"Create Area {suffix}",
                featureTypeId = karstAreaTypeId,
                geometry = new
                {
                    type = "Polygon",
                    coordinates = new[]
                    {
                        new[]
                        {
                            new[] { 25.10, 45.10 }, new[] { 25.20, 45.10 }, new[] { 25.20, 45.20 },
                            new[] { 25.10, 45.20 }, new[] { 25.10, 45.10 },
                        },
                    },
                },
                visibility = "authenticated",
            });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            areaId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = creatorId,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Read | AccessAction.Create,
                ScopeKind = AccessScopeKind.Subtree,
                ScopeFeatureId = areaId,
            });
            await db.SaveChangesAsync();
        }

        // Under the area: allowed by the subtree entry.
        var under = await creator.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Sub Create {suffix}",
            caveTypeId,
            visibility = "private",
            parentId = areaId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        under.StatusCode.ShouldBe(HttpStatusCode.Created, await under.Content.ReadAsStringAsync());

        // At the root, no entry matches the target context: the default deny stands.
        var elsewhere = await creator.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Root Create {suffix}",
            caveTypeId,
            visibility = "private",
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await elsewhere.Content.ReadAsStringAsync()).ShouldContain(CreateRules.ForbiddenCode);
    }

    // ---- helpers ----

    private static async Task<Guid> CreateCavingGroupAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name,
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(HttpClient author, string name, string visibility, Guid? cavingGroupId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            cavingGroupId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
