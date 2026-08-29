// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two places a camp has to appear for somebody to find one they did not navigate to: the
/// global search box and the dashboard's recent-activity feed. Both are hand-written registries
/// with no extension point, so nothing fails when an entity is left out of one of them — which
/// is exactly why each arm is driven by a test here rather than left to be noticed.
///
/// Both surfaces are coordinate-free by construction, and both carry the same visibility walk as
/// the camp's own page. A camp names a place as surely as a cave does, so a name reaching a
/// caller who may not read the row would be the disclosure, geometry or no geometry.
/// The PostGIS container is shared across the collection, so every assertion here is about
/// specific marked rows rather than totals.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionDiscoveryTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader with no grant of any kind. It has to be a Viewer: the seeded Editors group
    // holds every content domain at the widest scope, so an Editor who "cannot see" a camp would
    // prove nothing at all about the visibility walk.
    private HttpClient outsider = null!;

    public ExpeditionDiscoveryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xd-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xd-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xd-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xd-out-{suffix}@t.local");
    }

    [Fact]
    public async Task Search_finds_a_camp_by_name_and_by_description_and_carries_no_position()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var named = await CreateAsync($"Peștera Mare camp {marker}", "authenticated");
        var described = await CreateAsync(
            $"Autumn recce {Guid.NewGuid():N}", "authenticated", description: $"Digging at {marker}.");

        var hits = await ExpeditionHitsAsync(owner, marker);
        hits.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(named);
        hits.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(described);

        var hit = hits.Single(h => h.GetProperty("id").GetGuid() == named);
        hit.GetProperty("name").GetString().ShouldBe($"Peștera Mare camp {marker}");
        hit.GetProperty("startDate").GetString().ShouldBe("2026-07-18");
        hit.GetProperty("endDate").GetString().ShouldBe("2026-08-01");

        // Search is navigation, never location: a camp's working area is a polygon over the karst
        // its trips worked, and publishing it here would make the search box a second way to ask
        // roughly where a group's caves are.
        hit.TryGetProperty("geom", out _).ShouldBeFalse();
        hit.TryGetProperty("geometry", out _).ShouldBeFalse();
        hit.TryGetProperty("center", out _).ShouldBeFalse();

        // Accent-insensitive, like every other section of this box.
        var accentless = await ExpeditionHitsAsync(owner, $"pestera mare camp {marker}");
        accentless.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(named);
    }

    [Fact]
    public async Task Search_shows_a_camp_only_to_a_caller_who_may_read_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var readable = await CreateAsync($"Open camp {marker}", "authenticated");
        var secret = await CreateAsync($"Closed camp {marker}", "private");

        // The owner's side is asserted in the same test, so that the absence below cannot pass
        // merely because the search never matched anything.
        var mine = await ExpeditionHitsAsync(owner, marker);
        mine.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(readable);
        mine.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(secret);

        // The outsider holds no grant on either camp, and the private one is not merely hidden
        // from the list — its name never reaches them.
        var theirs = await ExpeditionHitsAsync(outsider, marker);
        theirs.Select(h => h.GetProperty("id").GetGuid()).ShouldContain(readable);
        theirs.Select(h => h.GetProperty("id").GetGuid()).ShouldNotContain(secret);
        theirs.ShouldAllBe(h => !h.GetProperty("name").GetString()!.Contains("Closed camp"));
    }

    [Fact]
    public async Task A_camp_is_recent_activity_in_its_own_right_and_only_for_who_may_read_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var readable = await CreateAsync($"Feed camp {marker}", "authenticated");
        var secret = await CreateAsync($"Feed secret {marker}", "private");

        var mine = await ActivityAsync(owner);
        var row = mine.SingleOrDefault(x => x.GetProperty("id").GetGuid() == readable);
        row.ValueKind.ShouldBe(JsonValueKind.Object, "the camp just written should lead the feed");

        // The kind is what the client routes on; a camp is not a feature and must not borrow a
        // feature's kind, or the client hands it to the feature resolver and the row dead-ends.
        row.GetProperty("kind").GetString().ShouldBe("expedition");
        row.GetProperty("name").GetString().ShouldBe($"Feed camp {marker}");

        // The feed is a link list. No coordinates here either.
        row.TryGetProperty("geom", out _).ShouldBeFalse();
        row.TryGetProperty("geometry", out _).ShouldBeFalse();
        row.TryGetProperty("center", out _).ShouldBeFalse();

        mine.Select(x => x.GetProperty("id").GetGuid()).ShouldContain(secret);

        // The readable camp is asserted present for the same caller before its private sibling is
        // asserted absent. Without it, a feed that had lost its camps arm entirely for a reader
        // holding no grant — every camp reached only through its visibility — would pass this test
        // while silently showing plain readers no camps at all.
        var theirs = await ActivityAsync(outsider);
        theirs.Select(x => x.GetProperty("id").GetGuid()).ShouldContain(readable);
        theirs.Select(x => x.GetProperty("id").GetGuid()).ShouldNotContain(secret);
    }

    /// <summary>
    /// A camp with the given visibility, dated so a hit's dates are assertable. Everything else
    /// is the write path's own business and is covered where that path is tested.
    /// </summary>
    private async Task<Guid> CreateAsync(string name, string visibility, string? description = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name,
            description = description ?? "A camp.",
            startDate = "2026-07-18",
            endDate = "2026-08-01",
            geom = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 }, new[] { 25.50, 45.50 }, new[] { 25.50, 45.56 },
                        new[] { 25.40, 45.56 }, new[] { 25.40, 45.50 },
                    },
                },
            },
            cavingGroupId = (Guid?)null,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> ExpeditionHitsAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(term)}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonDocument.Parse(payload).RootElement.Clone();
        return [.. body.GetProperty("expeditions").EnumerateArray()];
    }

    private static async Task<List<JsonElement>> ActivityAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/dashboard/summary");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonDocument.Parse(payload).RootElement.Clone();
        return [.. body.GetProperty("recentActivity").EnumerateArray()];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
