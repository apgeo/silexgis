// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Dashboard summary: visibility-filtered registry counts over the feature supertype and the
/// merged recent-activity feed. Caves and data-driven features are counted separately (the two
/// headline numbers must be readable side by side), while every feature kind — entrances
/// included — is routable activity.
/// The PostGIS container is shared by the whole collection, so every count assertion here is
/// a *delta* around a freshly-read baseline — absolute totals would race other test classes'
/// seeded rows.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private long caveTypeId;
    private long entranceTypeId;
    private long featureTypeId;

    public DashboardTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"db-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"db-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
            featureTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"db-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"db-out-{suffix}@t.local");
    }

    [Fact]
    public async Task Summary_requires_authentication()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/dashboard/summary")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Counts_separate_caves_from_data_driven_features_and_skip_delegated_kinds()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var before = await SummaryAsync(owner);

        var caveId = await CreateCaveAsync($"Dash cave {marker}", "authenticated");
        _ = await CreateFeatureAsync($"Dash feature {marker}", "authenticated");
        _ = await CreateTripAsync($"Dash trip {marker}", "authenticated");

        var after = await SummaryAsync(owner);
        Counts(after, "caves").ShouldBe(Counts(before, "caves") + 1);
        Counts(after, "features").ShouldBe(Counts(before, "features") + 1);
        Counts(after, "tripLogs").ShouldBe(Counts(before, "tripLogs") + 1);
        Counts(after, "geofiles").ShouldBe(Counts(before, "geofiles"));

        // Caves live in the same table as every other feature now, so a cave must be counted
        // once and only under its own figure.
        after.GetProperty("counts").TryGetProperty("surfaceFeatures", out _).ShouldBeFalse();

        // An entrance is a feature row of a delegated kind: it belongs to its cave, not to the
        // registry totals — but it is still activity, tagged with its own kind so the client
        // can route the link.
        var entranceId = await CreateEntranceAsync(caveId);
        var withEntrance = await SummaryAsync(owner);
        Counts(withEntrance, "caves").ShouldBe(Counts(after, "caves"));
        Counts(withEntrance, "features").ShouldBe(Counts(after, "features"));
        Activity(withEntrance).ShouldContain(x => Id(x) == entranceId && Kind(x) == "caveEntrance");
    }

    [Fact]
    public async Task Recent_activity_lists_the_newest_records_first_with_their_routing_kind()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // Created oldest-to-newest so the expected feed order is the reverse of creation.
        var caveId = await CreateCaveAsync($"Dash cave {marker}", "authenticated");
        var featureId = await CreateFeatureAsync($"Dash feature {marker}", "authenticated");
        var tripId = await CreateTripAsync($"Dash trip {marker}", "authenticated");

        var activity = Activity(await SummaryAsync(owner));
        activity.Count.ShouldBeLessThanOrEqualTo(10);
        activity.Select(x => x.GetProperty("updatedAt").GetDateTimeOffset())
            .ShouldBeInOrder(SortDirection.Descending);

        // The three just-created records lead the feed, newest first, each tagged with its kind.
        var mine = activity.Take(3).ToList();
        mine.Select(Id).ShouldBe([tripId, featureId, caveId]);
        mine.Select(Kind).ShouldBe(["tripLog", "feature", "cave"]);
        mine[2].GetProperty("name").GetString().ShouldBe($"Dash cave {marker}");

        // The feed is a link list only — it must never carry coordinates, so location
        // protection has no second code path to go wrong here.
        mine[2].TryGetProperty("geom", out _).ShouldBeFalse();
        mine[2].TryGetProperty("geometry", out _).ShouldBeFalse();
        mine[2].TryGetProperty("center", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Recent_activity_is_the_newest_records_overall_not_a_per_kind_sample()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // One more cave than the feed holds, all newer than anything else in the container, so
        // the whole feed must be caves. Reading fewer rows per source than the feed holds would
        // fill the tail with older trips and drop caves that are genuinely newer — which the
        // single-record-per-kind tests above cannot see.
        var caveIds = new List<Guid>();
        for (var i = 0; i < 11; i++)
        {
            caveIds.Add(await CreateCaveAsync($"Dash burst {marker} {i}", "authenticated"));
        }

        var activity = Activity(await SummaryAsync(owner));
        activity.Count.ShouldBe(10);
        activity.Select(Kind).ShouldAllBe(k => k == "cave");
        activity.Select(Id).ShouldBe(Enumerable.Reverse(caveIds).Take(10));
    }

    [Fact]
    public async Task Summary_excludes_records_the_caller_cannot_see()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ownerBefore = await SummaryAsync(owner);
        var outsiderBefore = await SummaryAsync(outsider);

        var secretId = await CreateCaveAsync($"Secret cave {marker}", "private");
        // The entrance inherits the cave's access columns, so it must disappear with it.
        var secretEntranceId = await CreateEntranceAsync(secretId);

        // The owner sees their private cave and its entrance…
        var ownerAfter = await SummaryAsync(owner);
        Counts(ownerAfter, "caves").ShouldBe(Counts(ownerBefore, "caves") + 1);
        Activity(ownerAfter).Select(Id).ShouldContain(secretId);
        Activity(ownerAfter).Select(Id).ShouldContain(secretEntranceId);

        // …and an unrelated regular user sees neither the count nor the activity rows. The paired
        // assertions matter: without the owner's side above, absence here could pass simply
        // because nothing was created.
        var outsiderAfter = await SummaryAsync(outsider);
        Counts(outsiderAfter, "caves").ShouldBe(Counts(outsiderBefore, "caves"));
        Activity(outsiderAfter).Select(Id).ShouldNotContain(secretId);
        Activity(outsiderAfter).Select(Id).ShouldNotContain(secretEntranceId);
    }

    [Fact]
    public async Task Soft_deleted_features_leave_the_counts_and_the_feed_with_their_subtree()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var before = await SummaryAsync(owner);

        var caveId = await CreateCaveAsync($"Dash doomed {marker}", "authenticated");
        var entranceId = await CreateEntranceAsync(caveId);

        var seeded = await SummaryAsync(owner);
        Counts(seeded, "caves").ShouldBe(Counts(before, "caves") + 1);
        Activity(seeded).Select(Id).ShouldContain(entranceId);

        var deleted = await owner.DeleteAsync($"/api/v1/caves/{caveId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // The delete stamps the whole containment subtree, so the entrance goes with the cave.
        var after = await SummaryAsync(owner);
        Counts(after, "caves").ShouldBe(Counts(before, "caves"));
        Activity(after).Select(Id).ShouldNotContain(caveId);
        Activity(after).Select(Id).ShouldNotContain(entranceId);
    }

    private static async Task<JsonElement> SummaryAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary");

    private static int Counts(JsonElement summary, string name) =>
        summary.GetProperty("counts").GetProperty(name).GetInt32();

    private static List<JsonElement> Activity(JsonElement summary) =>
        [.. summary.GetProperty("recentActivity").EnumerateArray()];

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string Kind(JsonElement item) => item.GetProperty("kind").GetString()!;

    private async Task<Guid> CreateCaveAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = "Dash entrance",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.4611, 45.5322 } },
            positionQuality = "Gps",
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateFeatureAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
            visibility,
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateTripAsync(string title, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility,
        });
        return await CreatedIdAsync(response);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
