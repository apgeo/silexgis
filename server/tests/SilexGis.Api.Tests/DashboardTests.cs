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
/// Dashboard summary: visibility-filtered registry counts and the recent-activity feed.
/// The PostGIS container is shared by the whole collection, so every count assertion here is
/// a *delta* around a freshly-read baseline — absolute totals would race other test classes'
/// seeded rows.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private long caveTypeId;
    private long featureTypeId;

    public DashboardTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"db-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"db-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            featureTypeId = await db.FeatureTypes.Select(t => t.Id).FirstAsync();
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
    public async Task Summary_counts_new_records_and_lists_them_newest_first()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var before = await SummaryAsync(owner);

        // Created oldest-to-newest so the expected feed order is the reverse of creation.
        var caveId = await CreateCaveAsync($"Dash cave {marker}", "authenticated");
        var featureId = await CreateFeatureAsync($"Dash feature {marker}", "authenticated");
        var tripId = await CreateTripAsync($"Dash trip {marker}", "authenticated");

        var after = await SummaryAsync(owner);
        Counts(after, "caves").ShouldBe(Counts(before, "caves") + 1);
        Counts(after, "surfaceFeatures").ShouldBe(Counts(before, "surfaceFeatures") + 1);
        Counts(after, "tripLogs").ShouldBe(Counts(before, "tripLogs") + 1);
        Counts(after, "geofiles").ShouldBe(Counts(before, "geofiles"));

        var activity = Activity(after);
        activity.Count.ShouldBeLessThanOrEqualTo(10);
        activity.Select(x => x.GetProperty("updatedAt").GetDateTimeOffset())
            .ShouldBeInOrder(SortDirection.Descending);

        // The three just-created records lead the feed, newest first, each tagged with its kind.
        var mine = activity.Take(3).ToList();
        mine.Select(x => x.GetProperty("id").GetGuid()).ShouldBe([tripId, featureId, caveId]);
        mine.Select(x => x.GetProperty("kind").GetString())
            .ShouldBe(["tripLog", "surfaceFeature", "cave"]);
        mine[2].GetProperty("name").GetString().ShouldBe($"Dash cave {marker}");

        // The feed is a link list only — it must never carry coordinates.
        mine[2].TryGetProperty("geom", out _).ShouldBeFalse();
        mine[2].TryGetProperty("center", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Recent_activity_is_the_newest_records_overall_not_a_per_kind_sample()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // One more cave than the feed holds, all newer than anything else in the container, so
        // the whole feed must be caves. Reading fewer rows per kind than the feed holds would
        // fill the tail with older features/trips and drop caves that are genuinely newer —
        // which the single-record-per-kind tests above cannot see.
        var caveIds = new List<Guid>();
        for (var i = 0; i < 11; i++)
        {
            caveIds.Add(await CreateCaveAsync($"Dash burst {marker} {i}", "authenticated"));
        }

        var activity = Activity(await SummaryAsync(owner));
        activity.Count.ShouldBe(10);
        activity.Select(x => x.GetProperty("kind").GetString()).ShouldAllBe(k => k == "cave");
        activity.Select(x => x.GetProperty("id").GetGuid())
            .ShouldBe(Enumerable.Reverse(caveIds).Take(10));
    }

    [Fact]
    public async Task Summary_excludes_records_the_caller_cannot_see()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ownerBefore = await SummaryAsync(owner);
        var outsiderBefore = await SummaryAsync(outsider);

        var secretId = await CreateCaveAsync($"Secret cave {marker}", "private");

        // The owner sees their private cave…
        var ownerAfter = await SummaryAsync(owner);
        Counts(ownerAfter, "caves").ShouldBe(Counts(ownerBefore, "caves") + 1);
        Activity(ownerAfter).Select(x => x.GetProperty("id").GetGuid()).ShouldContain(secretId);

        // …and an unrelated Editor sees neither the count nor the activity row. The paired
        // assertions matter: without the owner's side above, absence here could pass simply
        // because nothing was created.
        var outsiderAfter = await SummaryAsync(outsider);
        Counts(outsiderAfter, "caves").ShouldBe(Counts(outsiderBefore, "caves"));
        Activity(outsiderAfter).Select(x => x.GetProperty("id").GetGuid()).ShouldNotContain(secretId);
    }

    private static async Task<JsonElement> SummaryAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary");

    private static int Counts(JsonElement summary, string name) =>
        summary.GetProperty("counts").GetProperty(name).GetInt32();

    private static List<JsonElement> Activity(JsonElement summary) =>
        [.. summary.GetProperty("recentActivity").EnumerateArray()];

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

    private async Task<Guid> CreateFeatureAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/surface-features", new
        {
            name,
            featureTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
            properties = (object?)null,
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
