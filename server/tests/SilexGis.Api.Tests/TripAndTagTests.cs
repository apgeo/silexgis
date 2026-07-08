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
/// Phase-4 surface: trip logs (participants/caves/visibility/map/search), tags with
/// entity filters down to the clustered map SQL, and the admin-only audit trail.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripAndTagTests : IAsyncLifetime, IDisposable
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private HttpClient viewer = null!;   // Viewer role
    private HttpClient admin = null!;
    private Guid outsiderId;
    private long caveTypeId;
    private long entranceTypeId;

    public TripAndTagTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tt-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tt-out-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tt-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"tt-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tt-out-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"tt-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tt-adm-{suffix}@t.local");
    }

    [Fact]
    public async Task Trip_logs_cover_crud_participants_visibility_map_and_search()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync($"Trip Cave {marker}", "authenticated");

        // Validation: viewer role cannot create; participants need exactly one identity.
        (await viewer.PostAsJsonAsync("/api/v1/trip-logs/", TripBody($"V {marker}", caveIds: [])))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var badParticipant = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Bad {marker}",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { userId = (Guid?)null, nameText = (string?)null } },
            visibility = "private",
        });
        badParticipant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Create with a linked cave, a registered participant and a guest name.
        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Exploration camp {marker}",
            tripDate = "2026-06-20",
            tripDateEnd = "2026-06-22",
            description = "Two pitches rigged.",
            locationText = "Piatra Mare",
            geom = new { type = "Point", coordinates = new[] { 25.61, 45.55 } },
            caveIds = new[] { caveId },
            participants = new object[]
            {
                new { userId = outsiderId, nameText = (string?)null },
                new { userId = (Guid?)null, nameText = "Guest Caver" },
            },
            visibility = "authenticated",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var trip = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tripId = trip.GetProperty("id").GetGuid();
        trip.GetProperty("caveIds").EnumerateArray().Single().GetGuid().ShouldBe(caveId);
        var participants = trip.GetProperty("participants").EnumerateArray().ToList();
        participants.Count.ShouldBe(2);
        participants.ShouldContain(p => p.GetProperty("nameText").GetString() == "Guest Caver");
        participants.ShouldContain(p => p.GetProperty("userId").ValueKind == JsonValueKind.String
            && p.GetProperty("displayName").GetString() != null);

        // Visible to other authenticated users; a private trip is not.
        (await outsider.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var privateTrip = await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody($"Secret {marker}", caveIds: []));
        var privateId = (await privateTrip.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await outsider.GetAsync($"/api/v1/trip-logs/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Update replaces children; others cannot write.
        (await outsider.PutAsJsonAsync($"/api/v1/trip-logs/{tripId}", TripBody("X", caveIds: [])))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var update = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Exploration camp {marker} (updated)",
            tripDate = "2026-06-20",
            geom = new { type = "Point", coordinates = new[] { 25.61, 45.55 } },
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { userId = (Guid?)null, nameText = "Solo" } },
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("caveIds").GetArrayLength().ShouldBe(0);
        updated.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("nameText").GetString().ShouldBe("Solo");

        // Search and the map layer surface the trip.
        var search = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/search?q=camp {marker}");
        search.GetProperty("trips").EnumerateArray()
            .Any(x => x.GetProperty("id").GetGuid() == tripId).ShouldBeTrue();

        var mapTrips = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/map/trip-logs?bbox={WorldBbox}");
        mapTrips.GetProperty("features").EnumerateArray()
            .Any(f => f.GetProperty("properties").GetProperty("id").GetGuid() == tripId).ShouldBeTrue();

        // Delete removes the trip.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trips_redact_links_to_location_protected_caves()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync($"Protected Trip Cave {marker}", "authenticated", locationProtected: true);

        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Rigging day {marker}",
            tripDate = "2026-05-05",
            geom = new { type = "Point", coordinates = new[] { 25.71, 45.62 } },
            caveIds = new[] { caveId },
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tripId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Owner keeps the link; the outsider sees the trip but not the cave link.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"))
            .GetProperty("caveIds").GetArrayLength().ShouldBe(1);
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"))
            .GetProperty("caveIds").GetArrayLength().ShouldBe(0);

        // Cave-filtered trip lists behave as if nothing were linked.
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?caveId={caveId}"))
            .GetProperty("items").GetArrayLength().ShouldBe(0);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?caveId={caveId}"))
            .GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Tags_filter_lists_and_both_map_paths()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var tagged = await CreateCaveAsync($"Tagged {marker}", "authenticated");
        var untagged = await CreateCaveAsync($"Untagged {marker}", "authenticated");
        await CreateEntranceAsync(tagged, 25.31, 45.31);
        await CreateEntranceAsync(untagged, 25.32, 45.32);

        // Tag with diacritics → slug is normalized; tagging twice is idempotent.
        var tagName = $"Zonă Verticală {marker}";
        var slug = $"zona-verticala-{marker}";
        var tagging = await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName,
            entityType = "cave",
            entityId = tagged,
        });
        tagging.StatusCode.ShouldBe(HttpStatusCode.Created, await tagging.Content.ReadAsStringAsync());
        (await tagging.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("tag").GetProperty("slug").GetString().ShouldBe(slug);
        (await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName,
            entityType = "cave",
            entityId = tagged,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The catalog finds it (accent-insensitive) and the entity lists it.
        var tags = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/tags?search=zona verticala {marker}");
        tags.EnumerateArray().Count(x => x.GetProperty("slug").GetString() == slug).ShouldBe(1);
        var taggings = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/taggings/?entityType=cave&entityId={tagged}");
        taggings.GetArrayLength().ShouldBe(1);
        var taggingId = taggings[0].GetProperty("id").GetInt64();

        // Viewer role: readable entity but not writable → cannot tag or untag.
        (await viewer.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName,
            entityType = "cave",
            entityId = untagged,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/taggings/{taggingId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Cave list filter.
        var list = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/caves?tag={slug}");
        var ids = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(tagged);
        ids.ShouldNotContain(untagged);

        // Map: point path (zoom 14) and clustered SQL path (zoom 7) both honor the tag.
        var points = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/map/cave-entrances?bbox=25.2,45.2,25.4,45.4&zoom=14&tag={slug}");
        points.GetProperty("features").GetArrayLength().ShouldBe(1);
        var clusters = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/map/cave-entrances?bbox=25.2,45.2,25.4,45.4&zoom=7&tag={slug}");
        clusters.GetProperty("features").EnumerateArray()
            .Sum(f => f.GetProperty("properties").GetProperty("count").GetInt32()).ShouldBe(1);

        // Owner removes the tag; the filter empties.
        (await owner.DeleteAsync($"/api/v1/taggings/{taggingId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/caves?tag={slug}"))
            .GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Audit_trail_is_admin_only_and_carries_change_diffs()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync($"Audited {marker}", "private");

        (await owner.GetAsync("/api/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var audit = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/audit?entityType=Cave&entityId={caveId}");
        var items = audit.GetProperty("items").EnumerateArray().ToList();
        items.ShouldContain(x => x.GetProperty("action").GetString() == "created");
        items[0].GetProperty("userName").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- helpers ----

    private static object TripBody(string title, Guid[] caveIds) => new
    {
        title,
        tripDate = "2026-07-01",
        caveIds,
        participants = Array.Empty<object>(),
        visibility = "private",
    };

    private async Task<Guid> CreateCaveAsync(string name, string visibility, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
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

    private async Task CreateEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
