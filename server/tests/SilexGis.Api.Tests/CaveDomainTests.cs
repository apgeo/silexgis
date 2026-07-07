// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Cave domain end-to-end: CRUD, visibility matrix, team access, location obfuscation,
/// map bbox/cluster endpoint, search, and EF↔SQL visibility parity (05-auth-permissions §3).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaveDomainTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;      // Editor, owns everything created here
    private HttpClient outsider = null!;   // Editor, unrelated user
    private HttpClient teammate = null!;   // Editor, member of the team
    private HttpClient viewer = null!;     // Viewer role
    private Guid ownerId;
    private Guid teammateId;
    private Guid teamId;
    private long caveTypeId;
    private long entranceTypeId;

    public CaveDomainTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"owner-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"out-{suffix}@t.local");
        teammateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mate-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var team = new Team { Name = $"Team {suffix}", Slug = $"team-{suffix}" };
            db.Teams.Add(team);
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = ownerId, Role = TeamRole.Owner });
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = teammateId, Role = TeamRole.Member });
            await db.SaveChangesAsync();
            teamId = team.Id;
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"owner-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"out-{suffix}@t.local");
        teammate = await AuthHelper.BearerClientAsync(factory, $"mate-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"view-{suffix}@t.local");
    }

    [Fact]
    public async Task Crud_visibility_obfuscation_map_and_search_work_end_to_end()
    {
        // ---- create: viewer forbidden, editor allowed, validation enforced
        (await viewer.PostAsJsonAsync("/api/v1/caves", CaveBody("Viewer Cave", Visibility.Private)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync("/api/v1/caves", CaveBody("", Visibility.Private)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var privateCave = await CreateCaveAsync(owner, CaveBody("Private Cave", Visibility.Private));
        var authCave = await CreateCaveAsync(owner, CaveBody("Peștera Țestoasei", Visibility.Authenticated));
        var teamCave = await CreateCaveAsync(owner, CaveBody("Team Cave", Visibility.Team, teamId: teamId));
        var protectedCave = await CreateCaveAsync(
            owner, CaveBody("Protected Cave", Visibility.Authenticated, locationProtected: true,
                closestAddress: "Secret forest road km 3"));

        // ---- entrances (exact coordinates)
        var exactLon = 25.44721;
        var exactLat = 45.53127;
        var entrance = await CreateEntranceAsync(owner, protectedCave, exactLon, exactLat, isMain: true);
        entrance.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        await CreateEntranceAsync(owner, authCave, 25.6001, 45.6002, isMain: true);

        // ---- visibility matrix on list
        var outsiderIds = await ListCaveIdsAsync(outsider);
        outsiderIds.ShouldContain(authCave);
        outsiderIds.ShouldContain(protectedCave);
        outsiderIds.ShouldNotContain(privateCave);
        outsiderIds.ShouldNotContain(teamCave);
        (await ListCaveIdsAsync(teammate)).ShouldContain(teamCave);

        // ---- unreadable cave is a 404, not a 403
        (await outsider.GetAsync($"/api/v1/caves/{privateCave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // ---- write permissions: outsider can read authCave but not write it
        (await outsider.PutAsJsonAsync($"/api/v1/caves/{authCave}", CaveBody("Hijacked", Visibility.Public)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teammate.PutAsJsonAsync($"/api/v1/caves/{teamCave}", CaveBody("Team Cave Updated", Visibility.Team, teamId: teamId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        // team member cannot delete (only team admin/owner)
        (await teammate.DeleteAsync($"/api/v1/caves/{teamCave}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ---- location protection: outsider sees obfuscated data
        var protectedForOutsider = await GetJsonAsync(outsider, $"/api/v1/caves/{protectedCave}");
        protectedForOutsider.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        protectedForOutsider.GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);
        var snapped = protectedForOutsider.GetProperty("mainGeom").GetProperty("coordinates");
        snapped[0].GetDouble().ShouldNotBe(exactLon);
        var cell = LocationProtection.CellDegrees(5000);
        (snapped[0].GetDouble() / cell).ShouldBe(Math.Round(snapped[0].GetDouble() / cell), 1e-6);

        var protectedForOwner = await GetJsonAsync(owner, $"/api/v1/caves/{protectedCave}");
        protectedForOwner.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        protectedForOwner.GetProperty("mainGeom").GetProperty("coordinates")[0].GetDouble().ShouldBe(exactLon, 1e-9);

        var entrancesForOutsider = await GetJsonAsync(outsider, $"/api/v1/caves/{protectedCave}/entrances");
        entrancesForOutsider[0].GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        entrancesForOutsider[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(exactLon);

        // ---- map endpoint: points at high zoom (obfuscated for outsider), clusters at low zoom
        var bbox = "25.0,45.0,26.0,46.0";
        var points = await GetJsonAsync(outsider, $"/api/v1/map/cave-entrances?bbox={bbox}&zoom=14");
        var features = points.GetProperty("features").EnumerateArray().ToList();
        features.Count.ShouldBeGreaterThanOrEqualTo(2);
        var protectedFeature = features.Single(f =>
            f.GetProperty("properties").GetProperty("caveId").GetGuid() == protectedCave);
        protectedFeature.GetProperty("properties").GetProperty("approximate").GetBoolean().ShouldBeTrue();
        protectedFeature.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(exactLon);

        var clusters = await GetJsonAsync(outsider, $"/api/v1/map/cave-entrances?bbox={bbox}&zoom=7");
        var clusterFeatures = clusters.GetProperty("features").EnumerateArray().ToList();
        clusterFeatures.ShouldNotBeEmpty();
        clusterFeatures.Sum(f => f.GetProperty("properties").GetProperty("count").GetInt32())
            .ShouldBeGreaterThanOrEqualTo(2);
        clusterFeatures.All(f => f.GetProperty("properties").GetProperty("cluster").GetBoolean()).ShouldBeTrue();

        // ---- search: accent-insensitive, visibility-filtered
        var search = await GetJsonAsync(outsider, "/api/v1/search?q=pestera");
        search.GetProperty("caves").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ShouldContain(authCave);
        (await outsider.GetAsync("/api/v1/search?q=p")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // ---- EF ↔ SQL visibility parity for owner and teammate contexts
        await AssertVisibilityParityForUserAsync(ownerId);
        await AssertVisibilityParityForUserAsync(teammateId);

        // ---- soft delete by owner
        (await owner.DeleteAsync($"/api/v1/caves/{privateCave}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/caves/{privateCave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task AssertVisibilityParityForUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var teams = await db.TeamMembers.Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role);
        var user = new UserContext(userId, new HashSet<string>(), teams);

        var efIds = await db.Caves.VisibleTo(user).Select(c => c.Id).OrderBy(id => id).ToListAsync();

        var (fragment, parameters) = PermissionSql.VisibleToFragment(user);
        var sqlIds = (await db.Database.GetDbConnection().QueryAsync<Guid>(
                $"SELECT id FROM caves WHERE deleted_at IS NULL AND {fragment}", parameters))
            .OrderBy(id => id).ToList();

        sqlIds.ShouldBe(efIds);
    }

    private static object CaveBody(
        string name, Visibility visibility, Guid? teamId = null,
        bool locationProtected = false, string? closestAddress = null) => new
    {
        name,
        caveTypeId = 1, // patched by CreateCaveAsync via real id below where needed
        visibility = visibility.ToString(),
        teamId,
        locationProtected,
        closestAddress,
        explorationStatus = "Unknown",
        isShowCave = false,
    };

    private async Task<Guid> CreateCaveAsync(HttpClient client, object body)
    {
        // Replace the placeholder caveTypeId with the real seeded id.
        var json = JsonSerializer.SerializeToNode(body)!;
        json["caveTypeId"] = caveTypeId;
        var response = await client.PostAsJsonAsync("/api/v1/caves", json);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateEntranceAsync(
        HttpClient client, Guid caveId, double lon, double lat, bool isMain)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<List<Guid>> ListCaveIdsAsync(HttpClient client)
    {
        var page = await GetJsonAsync(client, "/api/v1/caves?pageSize=200");
        return [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())];
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
        teammate.Dispose();
        viewer.Dispose();
        factory.Dispose();
    }
}
