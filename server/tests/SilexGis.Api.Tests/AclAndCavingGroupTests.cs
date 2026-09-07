// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
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
/// The explicit-grant layer end-to-end over the per-object target vocabulary: direct access
/// rules on features (one route name for every kind) unlock read/write/exact-location for
/// users and via caving groups, mapView rules are manageable and effective,
/// ManagePermissions gates the per-object access endpoints, newly granted people are
/// notified, and caving groups have their own lifecycle rules.
/// </summary>
public sealed class AclAndCavingGroupTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, creates the objects
    private HttpClient grantee = null!;  // Viewer (regular user), receives grants — Editors read everything now
    private HttpClient manager = null!;  // Manager role, creates caving groups
    private Guid granteeId;
    private long caveTypeId;
    private long entranceTypeId;

    public AclAndCavingGroupTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acl-own-{suffix}@t.local");
        granteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acl-grant-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"acl-mgr-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"acl-own-{suffix}@t.local");
        grantee = await AuthHelper.BearerClientAsync(factory, $"acl-grant-{suffix}@t.local");
        manager = await AuthHelper.BearerClientAsync(factory, $"acl-mgr-{suffix}@t.local");
    }

    [Fact]
    public async Task User_grants_unlock_read_write_and_manage_progressively()
    {
        var caveId = await CreateCaveAsync("Granted Cave", "private");

        // Baseline: a private cave is invisible to the grantee everywhere — detail, list,
        // and the access/effective endpoints all deny by non-disclosure.
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCaveIdsAsync(grantee)).ShouldNotContain(caveId);
        (await grantee.GetAsync($"/api/v1/objects/feature/{caveId}/access")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await grantee.GetAsync($"/api/v1/objects/feature/{caveId}/effective-access"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Read grant → detail + list + effective-access open up; writes still 403.
        await ReplaceAccessRulesAsync(owner, "feature", caveId, [(AccessSubjectKind.User, granteeId, AccessAction.Read)]);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListCaveIdsAsync(grantee)).ShouldContain(caveId);
        // Flags enums serialize as a comma-joined string; assert loosely on that string.
        var effectiveActions = await EffectiveActionsAsync(grantee, "feature", caveId);
        effectiveActions.ShouldContain("read");
        effectiveActions.ShouldNotContain("write");
        (await grantee.PutAsJsonAsync($"/api/v1/caves/{caveId}", CaveBody("Granted Cave renamed", "private")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Read+Write grant → update succeeds; managing the rules still locked.
        await ReplaceAccessRulesAsync(owner, "feature", caveId,
            [(AccessSubjectKind.User, granteeId, AccessAction.Read | AccessAction.Write)]);
        (await grantee.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", CaveBody("Granted Cave renamed", "private")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await grantee.GetAsync($"/api/v1/objects/feature/{caveId}/access")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ManagePermissions grant → the grantee can now administer the rules themselves.
        await ReplaceAccessRulesAsync(owner, "feature", caveId,
            [(AccessSubjectKind.User, granteeId,
              AccessAction.Read | AccessAction.Write | AccessAction.ManagePermissions)]);
        (await grantee.GetAsync($"/api/v1/objects/feature/{caveId}/access")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revoke everything → back to invisible.
        await ReplaceAccessRulesAsync(owner, "feature", caveId, []);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CavingGroup_grants_apply_to_all_members_notify_them_once_and_parity_holds()
    {
        // The manager creates a caving group and adds the grantee as a plain member.
        var cavingGroupId = await CreateCavingGroupAsync();
        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members", new
        {
            caverId = await RosterHelper.CaverIdForAsync(factory, granteeId),
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A private cave (NOT caving group-bound) with a caving group rule becomes readable to members.
        var caveId = await CreateCaveAsync("CavingGroup Granted Cave", "private");
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ReplaceAccessRulesAsync(owner, "feature", caveId, [(AccessSubjectKind.CavingGroup, cavingGroupId, AccessAction.Read)]);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK); // caving group owner too

        // A caving group grant notifies each member — and only once: re-saving the same rule
        // is not news and must not queue another message.
        (await CountPermissionNotificationsAsync(granteeId)).ShouldBe(1);
        await ReplaceAccessRulesAsync(owner, "feature", caveId, [(AccessSubjectKind.CavingGroup, cavingGroupId, AccessAction.Read)]);
        (await CountPermissionNotificationsAsync(granteeId)).ShouldBe(1);

        // EF ↔ SQL parity of the feature filter including the caving group-subject branch,
        // scoped to this test's cave (the full caller matrix lives in the parity suite).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, granteeId);

        var efIds = await db.Features.VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Id == caveId).Select(f => f.Id).ToListAsync();
        var (fragment, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        parameters.Add("scope_id", caveId);
        var sqlIds = (await db.Database.GetDbConnection().QueryAsync<Guid>(
                $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL AND f.id = @scope_id AND {fragment}",
                parameters))
            .ToList();
        sqlIds.ShouldBe(efIds);
        efIds.ShouldContain(caveId);
    }

    [Fact]
    public async Task A_caving_group_bound_row_is_readable_by_its_members_whatever_the_visibility()
    {
        // Binding a row to a caving group is its own grant: members read it even when the
        // visibility says private. The single-row check and the list filter must agree —
        // if they disagree, a member can open the cave but everything that hangs off it
        // (attachments, tags, history, files) answers 404 for the same caller.
        var cavingGroupId = await CreateCavingGroupAsync();
        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members", new
        {
            caverId = await RosterHelper.CaverIdForAsync(factory, granteeId),
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Created by the caving group's own manager: binding a row to a caving group requires membership.
        var response = await manager.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"CavingGroup Bound Cave {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "private",
            cavingGroupId,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var caveId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The single-row gate lets the member in...
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // ...so every list and every dependent read must too.
        (await ListCaveIdsAsync(grantee)).ShouldContain(caveId);
        (await grantee.GetAsync($"/api/v1/attachments?entityType=feature&entityId={caveId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await grantee.GetAsync($"/api/v1/taggings?entityType=feature&entityId={caveId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A non-member regular user still sees nothing at all — the arm keys on
        // membership, not on the row merely carrying a caving group id.
        var strangerEmail = $"acl-stranger-{Guid.NewGuid():N}"[..20] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, strangerEmail);
        using var stranger = await AuthHelper.BearerClientAsync(factory, strangerEmail);
        (await stranger.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCaveIdsAsync(stranger)).ShouldNotContain(caveId);

        // EF <-> SQL parity for the caving group arm specifically.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, granteeId);
        var efIds = await db.Features.VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Id == caveId).Select(f => f.Id).ToListAsync();
        var (fragment, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        parameters.Add("scope_id", caveId);
        var sqlIds = (await db.Database.GetDbConnection().QueryAsync<Guid>(
                $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL AND f.id = @scope_id AND {fragment}",
                parameters))
            .ToList();
        efIds.ShouldContain(caveId);
        sqlIds.ShouldBe(efIds);
    }

    [Fact]
    public async Task ViewExactLocation_grant_reveals_protected_coordinates()
    {
        var caveId = await CreateCaveAsync("Exact Location Cave", "authenticated", locationProtected: true);
        const double exactLon = 25.44721;
        const double exactLat = 45.53127;
        (await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { exactLon, exactLat } },
            positionQuality = "Gps",
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Without the grant: visible but obfuscated.
        var entrancesBefore = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        var lonBefore = entrancesBefore[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble();
        lonBefore.ShouldNotBe(exactLon);
        entrancesBefore[0].GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // With a ViewExactLocation (+Read) grant on the cave feature: exact coordinates.
        await ReplaceAccessRulesAsync(owner, "feature", caveId,
            [(AccessSubjectKind.User, granteeId, AccessAction.Read | AccessAction.ViewExactLocation)]);
        var entrancesAfter = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        entrancesAfter[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);
        entrancesAfter[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task MapView_grants_are_manageable_and_effective()
    {
        var createResponse = await owner.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name = $"Granted View {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            config = new { },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var viewId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Baseline: a private view is absent from the grantee's list and its access
        // endpoints deny by non-disclosure.
        (await ListMapViewIdsAsync(grantee)).ShouldNotContain(viewId);
        (await grantee.GetAsync($"/api/v1/objects/mapView/{viewId}/access")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Read grant → the view appears in the grantee's list; writes still locked.
        await ReplaceAccessRulesAsync(owner, "mapView", viewId, [(AccessSubjectKind.User, granteeId, AccessAction.Read)]);
        (await ListMapViewIdsAsync(grantee)).ShouldContain(viewId);
        (await EffectiveActionsAsync(grantee, "mapView", viewId)).ShouldContain("read");
        (await grantee.PutAsJsonAsync($"/api/v1/map-views/{viewId}", MapViewBody("Renamed by grantee")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Read+Write grant → the grantee can edit the view.
        await ReplaceAccessRulesAsync(owner, "mapView", viewId,
            [(AccessSubjectKind.User, granteeId, AccessAction.Read | AccessAction.Write)]);
        (await grantee.PutAsJsonAsync($"/api/v1/map-views/{viewId}", MapViewBody("Renamed by grantee")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revoke → gone from the list, and an edit attempt is "not found", not 403.
        await ReplaceAccessRulesAsync(owner, "mapView", viewId, []);
        (await ListMapViewIdsAsync(grantee)).ShouldNotContain(viewId);
        (await grantee.PutAsJsonAsync($"/api/v1/map-views/{viewId}", MapViewBody("Renamed again")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Access_endpoint_validates_target_vocabulary_and_subjects()
    {
        var caveId = await CreateCaveAsync("Validation Cave", "private");

        // The target vocabulary is feature | tripLog | geofile | georeferencedMap | mapView |
        // expedition | event.
        // Anything else — including the retired per-kind names — is rejected with a stable code.
        foreach (var badName in new[] { "cave", "caveEntrance", "surfaceFeature", "banana" })
        {
            var response = await owner.GetAsync($"/api/v1/objects/{badName}/{caveId}/access");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, badName);
            (await response.Content.ReadAsStringAsync())
                .ShouldContain("access.entity_type_unknown", customMessage: badName);
        }

        // Route names are case-insensitive (clients echo camelCase payload values back into URLs).
        (await owner.GetAsync($"/api/v1/objects/FEATURE/{caveId}/access")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A rule subject that does not exist is refused.
        var badSubject = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = Guid.NewGuid(),
                    effect = "allow",
                    actions = "read",
                    scopeKind = "object",
                },
            },
        });
        badSubject.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badSubject.Content.ReadAsStringAsync()).ShouldContain("access.subject_unknown");

        // An unknown target id is "not found" — never a hint that the id shape was right.
        (await owner.PutAsJsonAsync($"/api/v1/objects/feature/{Guid.NewGuid()}/access", new
        {
            entries = Array.Empty<object>(),
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Creating a caving group is open to every account now: the affiliation pickers
        // create one inline, so the right rides the All Users seed rather than a role.
        (await owner.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = "Editor CavingGroup Attempt",
            description = (string?)null,
            website = (string?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---- helpers ----

    private async Task<Guid> CreateCavingGroupAsync()
    {
        var response = await manager.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Grantee CavingGroup {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            website = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name, string visibility, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListCaveIdsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/caves?pageSize=200");
        return [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    private static async Task<List<Guid>> ListMapViewIdsAsync(HttpClient client)
    {
        var views = await client.GetFromJsonAsync<JsonElement>("/api/v1/map-views/");
        return [.. views.EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    private object CaveBody(string name, string visibility) => new
    {
        name,
        caveTypeId,
        visibility,
        locationProtected = false,
        explorationStatus = "Unknown",
        isShowCave = false,
    };

    private static object MapViewBody(string name) => new
    {
        name,
        description = (string?)null,
        config = new { },
        isHome = false,
        cavingGroupId = (Guid?)null,
        visibility = "private",
    };

    private async Task<int> CountPermissionNotificationsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .CountAsync(n => n.RecipientUserId == userId && n.Category == NotificationCategory.PermissionGranted);
    }

    /// <summary>
    /// Replaces the rules written directly onto one object: plain allow rules that reach
    /// this object only, which is exactly what a one-off grant has always meant here.
    /// </summary>
    private static async Task ReplaceAccessRulesAsync(
        HttpClient client,
        string entityType,
        Guid id,
        (AccessSubjectKind Kind, Guid SubjectId, AccessAction Actions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/{entityType}/{id}/access", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = e.Kind == AccessSubjectKind.User ? "user" : "cavingGroup",
                subjectId = e.SubjectId,
                effect = "allow",
                actions = e.Actions.ToString().Replace(" ", string.Empty),
                scopeKind = "object",
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The caller's own actions on one object, as the comma-joined flag string.</summary>
    private static async Task<string> EffectiveActionsAsync(HttpClient client, string entityType, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/objects/{entityType}/{id}/effective-access");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("actions").GetString() ?? string.Empty;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
