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
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The cave aggregate end-to-end over the feature supertype: CRUD and validation, the
/// visibility matrix with 404 non-disclosure, containment parents on the detail view, the
/// summary header, location protection (snapped point + redacted precise-location text),
/// the write guard that keeps a non-exact editor from overwriting what they never saw,
/// subtree soft delete, and EF ↔ SQL parity of the feature visibility filter for caves.
/// </summary>
public sealed class CaveDomainTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;      // Editor, owns everything created here
    private HttpClient outsider = null!;   // Viewer (regular user), unrelated — Editors read everything now
    private HttpClient groupMate = null!;   // Viewer, plain member of the caving group (rights via the member ruleset)
    private HttpClient viewer = null!;     // Viewer role
    private string suffix = null!;
    private Guid ownerId;
    private Guid outsiderId;
    private Guid groupMateId;
    private Guid cavingGroupId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public CaveDomainTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"owner-{suffix}@t.local");
        // Regular users on purpose: Editors hold domain-wide content rights now, so the
        // visibility-filtering and membership assertions need callers whose access comes
        // only from visibility, membership or explicit grants.
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"out-{suffix}@t.local");
        groupMateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mate-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"mgr-{suffix}@t.local");

        // The caving group is created through the API so its seeded member ruleset
        // exists — membership rights flow from that ruleset now, not from the role.
        using (var manager = await AuthHelper.BearerClientAsync(factory, $"mgr-{suffix}@t.local"))
        {
            var created = await manager.PostAsJsonAsync("/api/v1/caving-groups/", new
            {
                name = $"CavingGroup {suffix}",
                description = (string?)null,
                website = (string?)null,
            });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            cavingGroupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            foreach (var memberId in new[] { ownerId, groupMateId })
            {
                (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members", new
                {
                    caverId = await RosterHelper.CaverIdForAsync(factory, memberId),
                    role = "member",
                })).StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"owner-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"out-{suffix}@t.local");
        groupMate = await AuthHelper.BearerClientAsync(factory, $"mate-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"view-{suffix}@t.local");
    }

    [Fact]
    public async Task Crud_visibility_and_non_disclosure_hold_end_to_end()
    {
        // ---- anonymous callers never reach the cave surface
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/caves")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync($"/api/v1/caves/{Guid.NewGuid()}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // ---- create: viewer forbidden, validation enforced, editor allowed
        var viewerCreate = await viewer.PostAsJsonAsync("/api/v1/caves", CaveBody("Viewer Cave"));
        viewerCreate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(viewerCreate)).ShouldBe(CreateRules.ForbiddenCode);

        var noName = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody(string.Empty));
        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(noName)).ShouldBe("validation.failed");
        (await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = Named("No Type"),
            caveTypeId = 0,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var privateCave = await CreateCaveAsync(owner, CaveBody(Named("Private Cave")));
        var authCave = await CreateCaveAsync(owner, CaveBody(Named("Auth Cave"), "authenticated"));
        var cavingGroupCave = await CreateCaveAsync(owner, CaveBody(Named("CavingGroup Cave"), "cavingGroup", cavingGroupId: cavingGroupId));

        // The created cave is a feature: the id is the feature id and the kind is carried.
        var created = await GetJsonAsync(owner, $"/api/v1/caves/{authCave}");
        created.GetProperty("kind").GetString().ShouldBe("cave");
        created.GetProperty("id").GetGuid().ShouldBe(authCave);
        // No entrance yet, so the cave's representative point is still empty.
        created.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetProperty("entranceCount").GetInt32().ShouldBe(0);

        // ---- visibility matrix on the list (scoped to this run's caves)
        var outsiderIds = await ListCaveIdsAsync(outsider);
        outsiderIds.ShouldContain(authCave);
        outsiderIds.ShouldNotContain(privateCave);
        outsiderIds.ShouldNotContain(cavingGroupCave);
        (await ListCaveIdsAsync(groupMate)).ShouldContain(cavingGroupCave);
        (await ListCaveIdsAsync(owner)).Count.ShouldBe(3);

        // ---- unreadable and unknown caves are both a 404, never a 403
        var hidden = await outsider.GetAsync($"/api/v1/caves/{privateCave}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(hidden)).ShouldBe("cave.not_found");
        (await outsider.GetAsync($"/api/v1/caves/{privateCave}/summary"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/caves/{privateCave}/entrances"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PutWithIfMatchAsync($"/api/v1/caves/{privateCave}", CaveBody(Named("Hijacked"))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/caves/{privateCave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/caves/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // ---- readable but not writable is a 403
        (await outsider.PutWithIfMatchAsync($"/api/v1/caves/{authCave}", CaveBody(Named("Hijacked"), "public")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/v1/caves/{authCave}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ---- a caving group member writes through the seeded member ruleset, but the
        // ruleset carries no Delete: removing the row stays with its owner (group roles
        // themselves grant nothing over content).
        var cavingGroupUpdate = await groupMate.PutWithIfMatchAsync(
            $"/api/v1/caves/{cavingGroupCave}", CaveBody(Named("CavingGroup Cave Updated"), "cavingGroup", cavingGroupId: cavingGroupId));
        cavingGroupUpdate.StatusCode.ShouldBe(HttpStatusCode.OK, await cavingGroupUpdate.Content.ReadAsStringAsync());
        (await cavingGroupUpdate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString()
            .ShouldBe(Named("CavingGroup Cave Updated"));
        (await groupMate.DeleteAsync($"/api/v1/caves/{cavingGroupCave}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ---- binding a cave to a caving group the caller is not in is refused
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var foreignCavingGroup = new CavingGroup { Name = $"Foreign {suffix}", Slug = $"foreign-{suffix}" };
        db.CavingGroups.Add(foreignCavingGroup);
        await db.SaveChangesAsync();
        var foreignBinding = await owner.PostAsJsonAsync(
            "/api/v1/caves", CaveBody(Named("Foreign CavingGroup Cave"), "cavingGroup", cavingGroupId: foreignCavingGroup.Id));
        foreignBinding.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(foreignBinding)).ShouldBe(CavingGroupBindingRules.ForbiddenCode);
    }

    [Fact]
    public async Task Detail_carries_parents_and_summary_reports_the_aggregate()
    {
        // A private containing area: the cave under it stays readable, its parent does not.
        var area = await CreateAreaAsync(owner, Named("Karst Area"), "private");
        var caveId = await CreateCaveAsync(
            owner, CaveBody(Named("Nested Cave"), "authenticated", parentId: area));

        var forOwner = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}");
        var parents = forOwner.GetProperty("parents").EnumerateArray().ToList();
        parents.Count.ShouldBe(1);
        parents[0].GetProperty("id").GetGuid().ShouldBe(area);
        parents[0].GetProperty("name").GetString().ShouldBe(Named("Karst Area"));
        parents[0].GetProperty("isPrimary").GetBoolean().ShouldBeTrue();

        // Breadcrumbs are visibility-filtered: an unreadable parent is not named.
        (await GetJsonAsync(outsider, $"/api/v1/caves/{caveId}"))
            .GetProperty("parents").GetArrayLength().ShouldBe(0);

        // A parent the caller cannot read is reported exactly like a missing one. The
        // prober must hold Create yet not see the area — Editors read everything now,
        // so an object-scope deny on the area is what takes it out of their sight.
        var proberEmail = $"prober-{suffix}@t.local";
        var proberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, proberEmail);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = proberId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.Object,
                ScopeFeatureId = area,
            });
            await db.SaveChangesAsync();
        }

        using var prober = await AuthHelper.BearerClientAsync(factory, proberEmail);
        var badParent = await prober.PostAsJsonAsync(
            "/api/v1/caves", CaveBody(Named("Sneaky Cave"), "private", parentId: area));
        badParent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(badParent)).ShouldBe("cave.parent_not_found");

        // ---- summary: counts, main entrance and the caller's capabilities
        var main = await CreateEntranceAsync(owner, caveId, 25.51, 45.61, isMain: true);
        var second = await CreateEntranceAsync(owner, caveId, 25.52, 45.62, isMain: false);
        second.GetProperty("isMain").GetBoolean().ShouldBeFalse();
        second.GetProperty("caveId").GetGuid().ShouldBe(caveId);

        var summary = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}/summary");
        summary.GetProperty("id").GetGuid().ShouldBe(caveId);
        summary.GetProperty("name").GetString().ShouldBe(Named("Nested Cave"));
        summary.GetProperty("entranceCount").GetInt32().ShouldBe(2);
        summary.GetProperty("centerlineCount").GetInt32().ShouldBe(0);
        summary.GetProperty("surveyModelCount").GetInt32().ShouldBe(0);
        summary.GetProperty("attachmentCount").GetInt32().ShouldBe(0);
        summary.GetProperty("tripLogCount").GetInt32().ShouldBe(0);
        var mainEntrance = summary.GetProperty("mainEntrance");
        mainEntrance.GetProperty("id").GetGuid().ShouldBe(main.GetProperty("id").GetGuid());
        mainEntrance.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        mainEntrance.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(25.51, 1e-9);
        // Deleting the main entrance re-elects one: a cave with entrances always has a main,
        // otherwise it loses its anchor silently — the representative point keeps working
        // off any entrance, so nothing else would show the gap.
        var mainId = main.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"/api/v1/cave-entrances/{mainId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDelete = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}/summary");
        afterDelete.GetProperty("entranceCount").GetInt32().ShouldBe(1);
        afterDelete.GetProperty("mainEntrance").GetProperty("id").GetGuid().ShouldBe(secondId);

        // Same rule when the last main is merely demoted rather than deleted.
        var demoted = await owner.PutAsJsonAsync($"/api/v1/cave-entrances/{secondId}", new
        {
            name = "Demoted",
            entranceTypeId,
            geom = new { type = "Point", coordinates = new[] { 25.52, 45.62 } },
            isMain = false,
        });
        demoted.StatusCode.ShouldBe(HttpStatusCode.OK, await demoted.Content.ReadAsStringAsync());
        (await GetJsonAsync(owner, $"/api/v1/caves/{caveId}/summary"))
            .GetProperty("mainEntrance").GetProperty("id").GetGuid().ShouldBe(secondId);

        // Restore the two-entrance shape the rest of this test expects.
        main = await CreateEntranceAsync(owner, caveId, 25.51, 45.61, isMain: true);
        summary = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}/summary");
        summary.GetProperty("entranceCount").GetInt32().ShouldBe(2);

        var ownerCaps = summary.GetProperty("permissions");
        ownerCaps.GetProperty("canWrite").GetBoolean().ShouldBeTrue();
        ownerCaps.GetProperty("canDelete").GetBoolean().ShouldBeTrue();
        ownerCaps.GetProperty("canShare").GetBoolean().ShouldBeTrue();
        ownerCaps.GetProperty("canManagePermissions").GetBoolean().ShouldBeTrue();
        ownerCaps.GetProperty("canViewExactLocation").GetBoolean().ShouldBeTrue();

        var outsiderCaps = (await GetJsonAsync(outsider, $"/api/v1/caves/{caveId}/summary"))
            .GetProperty("permissions");
        outsiderCaps.GetProperty("canWrite").GetBoolean().ShouldBeFalse();
        outsiderCaps.GetProperty("canDelete").GetBoolean().ShouldBeFalse();
        outsiderCaps.GetProperty("canManagePermissions").GetBoolean().ShouldBeFalse();
        // The cave is not protected, so an unprivileged reader still sees it exactly.
        outsiderCaps.GetProperty("canViewExactLocation").GetBoolean().ShouldBeTrue();

        // The cave's own geometry mirrors the main entrance.
        (await GetJsonAsync(owner, $"/api/v1/caves/{caveId}"))
            .GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(25.51, 1e-9);
    }

    [Fact]
    public async Task Protected_caves_are_obfuscated_for_callers_without_exact_view()
    {
        const double exactLon = 25.44721;
        const double exactLat = 45.53127;

        var protectedCave = await CreateCaveAsync(owner, CaveBody(
            Named("Protected Cave"), "authenticated", locationProtected: true,
            closestAddress: "Secret forest road km 3",
            landRegistryNumber: "CF 12345",
            locationNotes: "Behind the third spring"));
        var entrance = await CreateEntranceAsync(owner, protectedCave, exactLon, exactLat, isMain: true, altitude: 1240m);
        // Coordinates the caller supplied themselves are echoed back exactly.
        entrance.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        entrance.GetProperty("altitude").GetDecimal().ShouldBe(1240m);

        // ---- detail: text fields redacted, point snapped to the protection grid
        var forOutsider = await GetJsonAsync(outsider, $"/api/v1/caves/{protectedCave}");
        forOutsider.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        forOutsider.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        forOutsider.GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);
        forOutsider.GetProperty("landRegistryNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        forOutsider.GetProperty("locationNotes").ValueKind.ShouldBe(JsonValueKind.Null);
        var snapped = forOutsider.GetProperty("geom").GetProperty("coordinates");
        snapped[0].GetDouble().ShouldNotBe(exactLon);
        AssertSnappedToGrid(snapped[0].GetDouble());
        AssertSnappedToGrid(snapped[1].GetDouble());

        var forOwner = await GetJsonAsync(owner, $"/api/v1/caves/{protectedCave}");
        forOwner.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        forOwner.GetProperty("closestAddress").GetString().ShouldBe("Secret forest road km 3");
        forOwner.GetProperty("landRegistryNumber").GetString().ShouldBe("CF 12345");
        forOwner.GetProperty("locationNotes").GetString().ShouldBe("Behind the third spring");
        forOwner.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);

        // ---- the list view obfuscates identically
        var listed = (await GetJsonAsync(outsider, $"/api/v1/caves?pageSize=200&search={suffix}"))
            .GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == protectedCave);
        listed.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        listed.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(exactLon);

        // ---- entrances: snapped point, altitude and position quality withheld
        var entrancesForOutsider = await GetJsonAsync(outsider, $"/api/v1/caves/{protectedCave}/entrances");
        var row = entrancesForOutsider[0];
        row.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        row.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(exactLon);
        AssertSnappedToGrid(row.GetProperty("geom").GetProperty("coordinates")[0].GetDouble());
        row.GetProperty("altitude").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("positionQuality").GetString().ShouldBe("unknown");

        var entrancesForOwner = await GetJsonAsync(owner, $"/api/v1/caves/{protectedCave}/entrances");
        entrancesForOwner[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble()
            .ShouldBe(exactLon, 1e-9);
        entrancesForOwner[0].GetProperty("positionQuality").GetString().ShouldBe("gps");

        // ---- the map layer applies the same rule (and clusters below the threshold)
        const string bbox = "25.0,45.0,26.0,46.0";
        var points = await GetJsonAsync(outsider, $"/api/v1/map/cave-entrances?bbox={bbox}&zoom=14");
        var mapped = points.GetProperty("features").EnumerateArray().Single(f =>
            f.GetProperty("properties").GetProperty("id").GetGuid() == entrance.GetProperty("id").GetGuid());
        mapped.GetProperty("properties").GetProperty("caveId").GetGuid().ShouldBe(protectedCave);
        mapped.GetProperty("properties").GetProperty("protected").GetBoolean().ShouldBeTrue();
        mapped.GetProperty("properties").GetProperty("approximate").GetBoolean().ShouldBeTrue();
        mapped.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(exactLon);

        var clusters = await GetJsonAsync(outsider, $"/api/v1/map/cave-entrances?bbox={bbox}&zoom=7");
        var clusterFeatures = clusters.GetProperty("features").EnumerateArray().ToList();
        clusterFeatures.ShouldNotBeEmpty();
        clusterFeatures.All(f => f.GetProperty("properties").GetProperty("cluster").GetBoolean()).ShouldBeTrue();
        clusterFeatures.Sum(f => f.GetProperty("properties").GetProperty("count").GetInt32())
            .ShouldBeGreaterThanOrEqualTo(1);

        // ---- caving group members hold ViewExactLocation on their group's bound content
        // through the seeded member ruleset
        var cavingGroupProtected = await CreateCaveAsync(owner, CaveBody(
            Named("CavingGroup Protected Cave"), "cavingGroup", cavingGroupId: cavingGroupId, locationProtected: true,
            closestAddress: "Club hut, second turn"));
        await CreateEntranceAsync(owner, cavingGroupProtected, 25.61, 45.71, isMain: true);
        var forGroupMate = await GetJsonAsync(groupMate, $"/api/v1/caves/{cavingGroupProtected}");
        forGroupMate.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        forGroupMate.GetProperty("closestAddress").GetString().ShouldBe("Club hut, second turn");
        forGroupMate.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(25.61, 1e-9);
    }

    [Fact]
    public async Task Non_exact_editors_cannot_overwrite_protected_values()
    {
        const double exactLon = 25.31118;
        const double exactLat = 45.42219;

        var caveId = await CreateCaveAsync(owner, CaveBody(
            Named("Guarded Cave"), "private", locationProtected: true,
            closestAddress: "Gate on the forestry road",
            landRegistryNumber: "CF 99887",
            locationNotes: "Ask the warden"));
        var entrance = await CreateEntranceAsync(
            owner, caveId, exactLon, exactLat, isMain: true, altitude: 980m);
        var entranceId = entrance.GetProperty("id").GetGuid();

        // Read+Write, deliberately WITHOUT ViewExactLocation.
        await ReplaceAccessRulesAsync(owner, caveId, AccessAction.Read | AccessAction.Write);

        // The grantee only ever sees the obfuscated projection…
        var seenByGrantee = await GetJsonAsync(outsider, $"/api/v1/caves/{caveId}");
        seenByGrantee.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        seenByGrantee.GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);

        // …and a full-replace PUT of that projection must not write it back.
        var update = await outsider.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", CaveBody(
            Named("Guarded Cave Renamed"), "private", locationProtected: false,
            closestAddress: "leaked address", landRegistryNumber: "CF 00000",
            locationNotes: "leaked notes"));
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var echoed = await update.Content.ReadFromJsonAsync<JsonElement>();
        echoed.GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);
        echoed.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var afterUpdate = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}");
        afterUpdate.GetProperty("name").GetString().ShouldBe(Named("Guarded Cave Renamed"));
        afterUpdate.GetProperty("closestAddress").GetString().ShouldBe("Gate on the forestry road");
        afterUpdate.GetProperty("landRegistryNumber").GetString().ShouldBe("CF 99887");
        afterUpdate.GetProperty("locationNotes").GetString().ShouldBe("Ask the warden");
        // The protection flag itself is preserved — clearing it would expose everything else.
        afterUpdate.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();

        // The same guard on the entrance: coordinates, altitude and quality survive the echo.
        var entranceUpdate = await outsider.PutWithIfMatchAsync($"/api/v1/cave-entrances/{entranceId}", new
        {
            name = "Moved by grantee",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 20.0, 40.0 } },
            altitude = (decimal?)null,
            description = (string?)null,
            positionQuality = "estimated",
            surveyedAt = (DateOnly?)null,
        });
        entranceUpdate.StatusCode.ShouldBe(HttpStatusCode.OK, await entranceUpdate.Content.ReadAsStringAsync());
        var entranceEcho = await entranceUpdate.Content.ReadFromJsonAsync<JsonElement>();
        entranceEcho.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        entranceEcho.GetProperty("altitude").ValueKind.ShouldBe(JsonValueKind.Null);
        entranceEcho.GetProperty("positionQuality").GetString().ShouldBe("unknown");

        var storedEntrance = (await GetJsonAsync(owner, $"/api/v1/caves/{caveId}/entrances"))[0];
        storedEntrance.GetProperty("name").GetString().ShouldBe("Moved by grantee"); // theirs to edit
        storedEntrance.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);
        storedEntrance.GetProperty("geom").GetProperty("coordinates")[1].GetDouble().ShouldBe(exactLat, 1e-9);
        storedEntrance.GetProperty("altitude").GetDecimal().ShouldBe(980m);
        storedEntrance.GetProperty("positionQuality").GetString().ShouldBe("gps");
    }

    [Fact]
    public async Task Deleting_a_cave_soft_deletes_its_subtree()
    {
        var caveId = await CreateCaveAsync(owner, CaveBody(Named("Doomed Cave"), "authenticated"));
        var entranceId = (await CreateEntranceAsync(owner, caveId, 25.71, 45.81, isMain: true))
            .GetProperty("id").GetGuid();

        (await owner.DeleteAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Every read path forgets the cave AND everything under it.
        (await owner.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/caves/{caveId}/summary")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/caves/{caveId}/entrances")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCaveIdsAsync(owner)).ShouldNotContain(caveId);
        var mapped = await GetJsonAsync(owner, "/api/v1/map/cave-entrances?bbox=25.0,45.0,26.0,46.0&zoom=14");
        mapped.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())
            .ShouldNotContain(entranceId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // One stamp over the whole containment subtree, so a restore undoes exactly this deletion.
        var stamps = await db.Features.IgnoreQueryFilters()
            .Where(f => f.Id == caveId || f.Id == entranceId)
            .Select(f => new { f.Id, f.DeletedAt })
            .ToListAsync();
        stamps.Count.ShouldBe(2);
        stamps.ShouldAllBe(s => s.DeletedAt != null);
        stamps.Select(s => s.DeletedAt).Distinct().Count().ShouldBe(1);

        // Deletions are forensics: both rows land in the audit log under their kind names.
        var caveKey = caveId.ToString();
        var entranceKey = entranceId.ToString();
        var audit = await db.AuditEntries
            .Where(a => a.Action == AuditActions.Deleted && (a.EntityId == caveKey || a.EntityId == entranceKey))
            .Select(a => new { a.EntityType, a.EntityId })
            .ToListAsync();
        audit.ShouldContain(a => a.EntityId == caveKey && a.EntityType == FeatureAudit.TypeName(FeatureKind.Cave));
        audit.ShouldContain(a =>
            a.EntityId == entranceKey && a.EntityType == FeatureAudit.TypeName(FeatureKind.CaveEntrance));
    }

    [Fact]
    public async Task Search_is_coordinate_free_and_visibility_filters_stay_in_parity()
    {
        var publicName = $"Peștera Țestoasei {suffix}";
        var publicCave = await CreateCaveAsync(owner, CaveBody(publicName, "authenticated"));
        var privateCave = await CreateCaveAsync(owner, CaveBody($"Peștera Secretă {suffix}", "private"));

        // Visibility-filtered: both caves match the term, only the readable one comes back.
        var scoped = await GetJsonAsync(outsider, $"/api/v1/search?q={suffix}");
        var featureIds = scoped.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("id").GetGuid()).ToList();
        featureIds.ShouldContain(publicCave);
        featureIds.ShouldNotContain(privateCave);
        scoped.TryGetProperty("trips", out var trips).ShouldBeTrue();
        trips.ValueKind.ShouldBe(JsonValueKind.Array);

        // Accent-insensitive ("pestera testoasei" finds "Peștera Țestoasei"), and results
        // deliberately carry no coordinates — search is navigation, not location.
        var accentless = Uri.EscapeDataString($"pestera testoasei {suffix}");
        var hits = await GetJsonAsync(outsider, $"/api/v1/search?q={accentless}");
        var hit = hits.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == publicCave);
        hit.GetProperty("kind").GetString().ShouldBe("cave");
        hit.GetProperty("name").GetString().ShouldBe(publicName);
        hit.GetProperty("typeCode").ValueKind.ShouldBe(JsonValueKind.Null); // subtyped kinds name themselves
        hit.TryGetProperty("geom", out _).ShouldBeFalse();
        hit.TryGetProperty("geometry", out _).ShouldBeFalse();

        var tooShort = await outsider.GetAsync("/api/v1/search?q=p");
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(tooShort)).ShouldBe("search.query_too_short");

        // The cave list's EF filter and its Dapper twin must select the same feature rows.
        await AssertCaveVisibilityParityAsync(ownerId);
        await AssertCaveVisibilityParityAsync(outsiderId);
        await AssertCaveVisibilityParityAsync(groupMateId);
    }

    /// <summary>
    /// The cave slice's own parity check over the feature supertype: what
    /// <c>VisibleTo</c> returns for cave features and what the SQL fragment selects must
    /// never diverge (the broader matrix lives with the explicit-grant suite).
    /// </summary>
    private async Task AssertCaveVisibilityParityAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, userId);

        var efIds = await db.Features
            .Where(f => f.Kind == FeatureKind.Cave)
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Select(f => f.Id)
            .OrderBy(id => id)
            .ToListAsync();

        var (fragment, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        var sql = $"SELECT f.id FROM features f WHERE f.kind = {(short)FeatureKind.Cave} "
            + $"AND f.deleted_at IS NULL AND {fragment}";
        var sqlIds = (await db.Database.GetDbConnection().QueryAsync<Guid>(sql, parameters))
            .OrderBy(id => id).ToList();

        sqlIds.ShouldBe(efIds);
    }

    private static void AssertSnappedToGrid(double value)
    {
        var cell = LocationProtection.CellDegrees(5000);
        (value / cell).ShouldBe(Math.Round(value / cell), 1e-6);
    }

    private string Named(string name) => $"{name} {suffix}";

    private object CaveBody(
        string name,
        string visibility = "private",
        Guid? cavingGroupId = null,
        Guid? parentId = null,
        bool locationProtected = false,
        string? closestAddress = null,
        string? landRegistryNumber = null,
        string? locationNotes = null) => new
        {
            name,
            caveTypeId,
            visibility,
            cavingGroupId,
            parentId,
            locationProtected,
            closestAddress,
            landRegistryNumber,
            locationNotes,
            explorationStatus = "unknown",
            isShowCave = false,
        };

    private async Task<Guid> CreateCaveAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A containing area (a generic feature) to hang caves under.</summary>
    private async Task<Guid> CreateAreaAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = karstAreaTypeId,
            geometry = (object?)null,
            description = (string?)null,
            properties = (object?)null,
            parents = Array.Empty<object>(),
            locationProtected = false,
            cavingGroupId = (Guid?)null,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateEntranceAsync(
        HttpClient client, Guid caveId, double lon, double lat, bool isMain, decimal? altitude = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = (string?)null,
            entranceTypeId,
            isMain,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            altitude,
            description = (string?)null,
            positionQuality = "gps",
            surveyedAt = (DateOnly?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>
    /// Replaces the outsider's direct access rules on a feature (any kind shares the
    /// "feature" target name).
    /// </summary>
    private async Task ReplaceAccessRulesAsync(HttpClient client, Guid featureId, AccessAction actions)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = actions.ToString().Replace(" ", string.Empty),
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Cave ids of this test run only — the database is shared by the whole collection.</summary>
    private async Task<List<Guid>> ListCaveIdsAsync(HttpClient client)
    {
        var page = await GetJsonAsync(client, $"/api/v1/caves?pageSize=200&search={suffix}");
        return [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())];
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {payload}");
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        outsider.Dispose();
        groupMate.Dispose();
        viewer.Dispose();
        factory.Dispose();
    }
}
