// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The explicit-grant layer end-to-end: ACL grants unlock read/write/exact-location on
/// otherwise inaccessible objects (for users and via teams), ManagePermissions gates the
/// ACL endpoints, EF and SQL visibility stay in parity with the ACL branch, and teams
/// have their own lifecycle rules.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AclAndTeamTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, creates the objects
    private HttpClient grantee = null!;  // Editor, receives ACL grants
    private HttpClient manager = null!;  // Manager role, creates teams
    private Guid granteeId;
    private Guid managerId;
    private long caveTypeId;
    private long entranceTypeId;

    public AclAndTeamTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acl-own-{suffix}@t.local");
        granteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acl-grant-{suffix}@t.local");
        managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"acl-mgr-{suffix}@t.local");

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
        var caveId = await CreateCaveAsync("ACL Cave", "private");

        // Baseline: a private cave is invisible to the grantee everywhere.
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCaveIdsAsync(grantee)).ShouldNotContain(caveId);
        (await grantee.GetAsync($"/api/v1/objects/cave/{caveId}/acl")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Read grant → detail + list + effective-permissions open up; writes still 403.
        await ReplaceAclAsync(owner, caveId, [(AclSubjectKind.User, granteeId, ObjectPermission.Read)]);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListCaveIdsAsync(grantee)).ShouldContain(caveId);
        // Flags enums serialize as a comma-joined string; assert loosely on the raw body.
        var effectiveRaw = await (await grantee.GetAsync($"/api/v1/objects/cave/{caveId}/effective-permissions"))
            .Content.ReadAsStringAsync();
        effectiveRaw.ShouldContain("read");
        effectiveRaw.ShouldNotContain("write");
        (await grantee.PutAsJsonAsync($"/api/v1/caves/{caveId}", CaveBody("ACL Cave renamed", "private")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Read+Write grant → update succeeds; ACL management still locked.
        await ReplaceAclAsync(owner, caveId,
            [(AclSubjectKind.User, granteeId, ObjectPermission.Read | ObjectPermission.Write)]);
        (await grantee.PutAsJsonAsync($"/api/v1/caves/{caveId}", CaveBody("ACL Cave renamed", "private")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await grantee.GetAsync($"/api/v1/objects/cave/{caveId}/acl")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ManagePermissions grant → the grantee can now administer the ACL.
        await ReplaceAclAsync(owner, caveId,
            [(AclSubjectKind.User, granteeId,
              ObjectPermission.Read | ObjectPermission.Write | ObjectPermission.ManagePermissions)]);
        (await grantee.GetAsync($"/api/v1/objects/cave/{caveId}/acl")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revoke everything → back to invisible.
        await ReplaceAclAsync(owner, caveId, []);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Team_grants_apply_to_all_members_and_parity_holds()
    {
        // The manager creates a team and adds the grantee as a plain member.
        var teamResponse = await manager.PostAsJsonAsync("/api/v1/teams/", new
        {
            name = $"ACL Grant Team {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            website = (string?)null,
        });
        teamResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await teamResponse.Content.ReadAsStringAsync());
        var teamId = (await teamResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await manager.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new
        {
            userId = granteeId,
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A private cave (NOT team-bound) with a team ACL grant becomes readable to members.
        var caveId = await CreateCaveAsync("Team ACL Cave", "private");
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ReplaceAclAsync(owner, caveId, [(AclSubjectKind.Team, teamId, ObjectPermission.Read)]);
        (await grantee.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK); // team owner too

        // EF ↔ SQL parity including the ACL branch for the grantee.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var teams = await db.TeamMembers.Where(m => m.UserId == granteeId)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role);
        var user = new UserContext(granteeId, new HashSet<string>(), teams);

        var efIds = await db.Caves.VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave)
            .Select(c => c.Id).OrderBy(id => id).ToListAsync();
        var (fragment, parameters) = PermissionSql.VisibleToFragment(user, "c", AttachedEntityType.Cave);
        var sqlIds = (await db.Database.GetDbConnection().QueryAsync<Guid>(
                $"SELECT c.id FROM caves c WHERE c.deleted_at IS NULL AND {fragment}", parameters))
            .OrderBy(id => id).ToList();
        sqlIds.ShouldBe(efIds);
        efIds.ShouldContain(caveId);
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

        // With a ViewExactLocation (+Read) grant: exact coordinates.
        await ReplaceAclAsync(owner, caveId,
            [(AclSubjectKind.User, granteeId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);
        var entrancesAfter = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        entrancesAfter[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);
        entrancesAfter[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Acl_endpoint_validates_subjects_and_editor_cannot_create_teams()
    {
        var caveId = await CreateCaveAsync("Validation Cave", "private");

        var badSubject = await owner.PutAsJsonAsync($"/api/v1/objects/cave/{caveId}/acl", new
        {
            entries = new[] { new { subjectKind = "user", subjectId = Guid.NewGuid(), permissions = "read" } },
        });
        badSubject.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.PutAsJsonAsync($"/api/v1/objects/cave/{Guid.NewGuid()}/acl", new
        {
            entries = Array.Empty<object>(),
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Editors cannot create teams (Manager+ only).
        (await owner.PostAsJsonAsync("/api/v1/teams/", new
        {
            name = "Editor Team Attempt",
            description = (string?)null,
            website = (string?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- helpers ----

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

    private static async Task<List<Guid>> ListCaveIdsAsyncCore(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/caves?pageSize=200");
        return [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    private static Task<List<Guid>> ListCaveIdsAsync(HttpClient client) => ListCaveIdsAsyncCore(client);

    private static object CaveBody(string name, string visibility) => new
    {
        name,
        caveTypeId = 1,
        visibility,
        locationProtected = false,
        explorationStatus = "Unknown",
        isShowCave = false,
    };

    private static async Task ReplaceAclAsync(
        HttpClient client, Guid caveId, (AclSubjectKind Kind, Guid SubjectId, ObjectPermission Permissions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/cave/{caveId}/acl", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = e.Kind == AclSubjectKind.User ? "user" : "team",
                subjectId = e.SubjectId,
                permissions = e.Permissions.ToString().Replace(" ", string.Empty),
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
