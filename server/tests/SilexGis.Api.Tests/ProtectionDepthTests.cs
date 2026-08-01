// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Location protection over a containment chain deeper than one root: a protected karst
/// area containing a protected cave containing an entrance. Exact view requires
/// ViewExactLocation on EVERY protected root above a row (most-restrictive veto), a
/// row's own owner is never locked out by a foreign protected ancestor, and after a
/// normal API workout the integrity verifier finds the derived state (closures,
/// effective protection, mirrors, delegated access) fully consistent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProtectionDepthTests : IAsyncLifetime, IDisposable
{
    private const double ExactLon = 25.44721;
    private const double ExactLat = 45.53127;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, owns the protected area and chain
    private HttpClient grantee = null!;  // Editor, receives VEL grants / owns a nested cave
    private HttpClient stranger = null!; // Editor, no grants anywhere
    private Guid granteeId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public ProtectionDepthTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"depth-own-{suffix}@t.local");
        granteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"depth-grant-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"depth-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"depth-own-{suffix}@t.local");
        grantee = await AuthHelper.BearerClientAsync(factory, $"depth-grant-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"depth-str-{suffix}@t.local");
    }

    [Fact]
    public async Task Exact_view_requires_a_grant_on_every_protected_root_of_the_chain()
    {
        // Protected area > protected cave > entrance — two protected roots above the entrance.
        var areaId = await CreateProtectedAreaAsync("Chain Area");
        var caveId = await CreateCaveAsync(owner, "Chain Cave", parentId: areaId,
            locationProtected: true, closestAddress: "Precise trailhead 7");
        await AddEntranceAsync(owner, caveId);

        // No grants: the chain is readable (authenticated) but fully obfuscated.
        await AssertEntranceObfuscatedAsync(grantee, caveId);
        var detailBefore = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}");
        detailBefore.GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);
        detailBefore.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // A ViewExactLocation grant on the OUTER root alone is insufficient — the cave
        // root still vetoes (most-restrictive wins over the whole ancestry).
        await ReplaceFeatureAclAsync(owner, areaId,
            [(granteeId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);
        await AssertEntranceObfuscatedAsync(grantee, caveId);
        (await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}"))
            .GetProperty("closestAddress").ValueKind.ShouldBe(JsonValueKind.Null);

        // Grants on BOTH protected roots: the entrance point, the cave's precise text
        // fields and the approximate flag all open up.
        await ReplaceFeatureAclAsync(owner, caveId,
            [(granteeId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);
        var entrances = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        entrances[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(ExactLon, 1e-9);
        entrances[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        var detailAfter = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}");
        detailAfter.GetProperty("closestAddress").GetString().ShouldBe("Precise trailhead 7");
        detailAfter.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Row_owner_keeps_exact_view_of_their_own_cave_under_a_foreign_protected_area()
    {
        // The area belongs to one user; the cave nested inside it to another.
        var areaId = await CreateProtectedAreaAsync("Foreign Area");
        var caveId = await CreateCaveAsync(grantee, "Nested Own Cave", parentId: areaId, locationProtected: false);
        await AddEntranceAsync(grantee, caveId);

        // The cave's owner sees their own rows exactly — a foreign protection root above
        // must never lock an owner out of their own data.
        var ownRows = await grantee.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        ownRows[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(ExactLon, 1e-9);
        ownRows[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();

        // The area's owner holds ViewExactLocation on the only protected root (their own
        // area), so the nested cave is exact for them as well.
        var areaOwnerRows = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        areaOwnerRows[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();

        // Everyone else is still snapped — the area's protection covers the whole subtree
        // even though the cave itself is not marked protected.
        await AssertEntranceObfuscatedAsync(stranger, caveId);
    }

    [Fact]
    public async Task Integrity_verifier_reports_zero_problems_after_a_normal_api_workout()
    {
        // A workout across the write paths that maintain derived state: nested creation,
        // main-entrance promotion, entrance deletion, protection flip, access change and
        // a subtree soft delete.
        var areaId = await CreateProtectedAreaAsync("Workout Area");
        var caveId = await CreateCaveAsync(owner, "Workout Cave", parentId: areaId, locationProtected: true);
        var first = await AddEntranceAsync(owner, caveId);
        var second = await AddEntranceAsync(owner, caveId, isMain: false, lon: ExactLon + 0.01, lat: ExactLat + 0.01);

        // Promote the second entrance (demotes the first, moves the cave's mirror point).
        (await owner.PutAsJsonAsync($"/api/v1/cave-entrances/{second}", new
        {
            name = (string?)null,
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { ExactLon + 0.01, ExactLat + 0.01 } },
            positionQuality = "Gps",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Delete the demoted entrance (mirror recount + representative point stay correct).
        (await owner.DeleteAsync($"/api/v1/cave-entrances/{first}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var remaining = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        remaining.GetArrayLength().ShouldBe(1);
        remaining[0].GetProperty("isMain").GetBoolean().ShouldBeTrue();

        // Un-protect the cave and pull it private (protection restamp + delegated-trio sync).
        (await owner.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", new
        {
            name = $"Workout Cave edited {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Create a sibling cave and soft-delete it (subtree stamp).
        var doomed = await CreateCaveAsync(owner, "Doomed Cave", parentId: areaId, locationProtected: false);
        await AddEntranceAsync(owner, doomed);
        (await owner.DeleteAsync($"/api/v1/caves/{doomed}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Every piece of derived, security-bearing state must re-derive to what is stored.
        using var scope = factory.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>();
        var problems = await verifier.VerifyAsync();
        problems.ShouldBeEmpty(string.Join("; ", problems.Select(p => $"{p.Check} {p.FeatureId}: {p.Detail}")));
    }

    // ---- helpers ----

    private async Task<Guid> CreateProtectedAreaAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {Guid.NewGuid():N}"[..40],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 }, new[] { 25.55, 45.50 }, new[] { 25.55, 45.62 },
                        new[] { 25.40, 45.62 }, new[] { 25.40, 45.50 },
                    },
                },
            },
            locationProtected = true,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(
        HttpClient client, string name, Guid parentId, bool locationProtected, string? closestAddress = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            closestAddress,
            parentId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddEntranceAsync(
        HttpClient client, Guid caveId, bool isMain = true, double lon = ExactLon, double lat = ExactLat)
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
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertEntranceObfuscatedAsync(HttpClient client, Guid caveId)
    {
        var entrances = await client.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/entrances");
        entrances.GetArrayLength().ShouldBe(1);
        entrances[0].GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldNotBe(ExactLon);
        entrances[0].GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
    }

    private static async Task ReplaceFeatureAclAsync(
        HttpClient client, Guid featureId, (Guid UserId, ObjectPermission Permissions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/acl", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.UserId,
                permissions = e.Permissions.ToString().Replace(" ", string.Empty),
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
