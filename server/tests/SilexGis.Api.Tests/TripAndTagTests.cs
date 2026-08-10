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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Trip logs (participants/report fields/caves/visibility/map/search), tags with entity
/// filters down to the clustered map SQL, and the admin-only audit trail.
/// <para>
/// A trip's cave links point at cave FEATURE ids, and tags address their target in the
/// two-world vocabulary — "feature" plus a feature id for anything physical. Both are
/// re-anchored addresses for unchanged rules: a link to a location-protected cave is still
/// redacted for callers without exact view, and still preserved across their edits.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripAndTagTests : IAsyncLifetime, IDisposable
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private HttpClient viewer = null!;   // Viewer role
    private HttpClient admin = null!;
    private Guid ownerId;
    private Guid outsiderId;
    private Guid outsiderCaverId;
    private Guid cavingGroupId;
    private long caveTypeId;
    private long entranceTypeId;

    public TripAndTagTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tt-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-out-{suffix}@t.local");
        outsiderCaverId = await RosterHelper.CaverIdForAsync(factory, outsiderId);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tt-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();

            // The organizing club is a caving group now, so a trip that names one needs one.
            var cavingGroup = new CavingGroup { Name = $"Trip Club {suffix}", Slug = $"trip-club-{suffix}" };
            db.CavingGroups.Add(cavingGroup);
            await db.SaveChangesAsync();
            cavingGroupId = cavingGroup.Id;
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
            participants = new[] { new { caverId = (Guid?)null, newCaverName = (string?)null } },
            visibility = "private",
        });
        badParticipant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A cave id the caller cannot read is reported exactly like a nonexistent one, so
        // linking cannot be used to probe for caves.
        (await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody($"Ghost {marker}", caveIds: [Guid.NewGuid()])))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

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
                new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)null, newCaverName = "Guest Caver" },
            },
            visibility = "authenticated",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var trip = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tripId = trip.GetProperty("id").GetGuid();
        trip.GetProperty("caveIds").EnumerateArray().Single().GetGuid().ShouldBe(caveId);
        var participants = trip.GetProperty("participants").EnumerateArray().ToList();
        participants.Count.ShouldBe(2);
        // The guest became a roster entry, which is what makes them countable later.
        participants.ShouldContain(p => p.GetProperty("name").GetString() == "Guest Caver"
            && p.GetProperty("userId").ValueKind == JsonValueKind.Null);
        participants.ShouldContain(p => p.GetProperty("userId").ValueKind == JsonValueKind.String
            && p.GetProperty("name").GetString() != null);

        // The linked id is the cave's feature id — the same id the uniform resolver answers.
        var resolved = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/features/{caveId}");
        resolved.GetProperty("kind").GetString().ShouldBe("cave");
        resolved.GetProperty("feature").GetProperty("id").GetGuid().ShouldBe(caveId);

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
            participants = new[] { new { caverId = (Guid?)null, newCaverName = "Solo" } },
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("caveIds").GetArrayLength().ShouldBe(0);
        updated.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("name").GetString().ShouldBe("Solo");

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
    public async Task Trip_date_windows_span_a_multi_day_trip()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // Ran across the end of the month: started in February, came out in March.
        var spanning = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Camp {marker}",
            tripDate = "2026-02-27",
            tripDateEnd = "2026-03-02",
            geom = new { type = "Point", coordinates = new[] { 25.61, 45.55 } },
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        spanning.StatusCode.ShouldBe(HttpStatusCode.Created, await spanning.Content.ReadAsStringAsync());
        var spanningId = (await spanning.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The control: one day, entirely before the window, and it must stay out of it.
        var before = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Day out {marker}",
            tripDate = "2026-02-27",
            geom = new { type = "Point", coordinates = new[] { 25.62, 45.56 } },
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        var beforeId = (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var march = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/trip-logs/?search={marker}&from=2026-03-01&to=2026-03-31");
        var listed = march.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        listed.ShouldContain(spanningId);
        listed.ShouldNotContain(beforeId);

        var mapped = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/map/trip-logs?bbox={WorldBbox}&from=2026-03-01&to=2026-03-31");
        var onMap = mapped.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid()).ToList();
        onMap.ShouldContain(spanningId);
        onMap.ShouldNotContain(beforeId);
    }

    [Fact]
    public async Task Trip_report_fields_and_proposers_round_trip()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // Create with the enrichment fields; the same registered user is both a participant
        // and a proposer, alongside a free-text proposer.
        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Survey push {marker}",
            type = "survey",
            tripDate = "2026-06-01",
            entryTime = "09:30:00",
            exitTime = "16:15:00",
            description = "Rigged the entrance series.",
            results = "200 m of new passage surveyed.",
            weatherConditions = "Cold, low water.",
            locationText = "Piatra Craiului",
            organizingCavingGroupId = cavingGroupId,
            caveIds = Array.Empty<Guid>(),
            participants = new object[] { new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null } },
            proposers = new object[]
            {
                new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)null, newCaverName = "Ana Ionescu" },
            },
            visibility = "authenticated",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tripId = created.GetProperty("id").GetGuid();

        void AssertReportFields(JsonElement t)
        {
            t.GetProperty("type").GetString().ShouldBe("survey");
            t.GetProperty("entryTime").GetString().ShouldBe("09:30:00");
            t.GetProperty("exitTime").GetString().ShouldBe("16:15:00");
            t.GetProperty("results").GetString().ShouldBe("200 m of new passage surveyed.");
            t.GetProperty("weatherConditions").GetString().ShouldBe("Cold, low water.");
            t.GetProperty("organizingCavingGroupId").GetGuid().ShouldBe(cavingGroupId);
            var proposers = t.GetProperty("proposers").EnumerateArray().ToList();
            proposers.Count.ShouldBe(2);
            proposers.ShouldContain(p => p.GetProperty("name").GetString() == "Ana Ionescu");
            proposers.ShouldContain(p => p.GetProperty("userId").ValueKind == JsonValueKind.String
                && p.GetProperty("name").GetString() != null);
            // The same registered user is independently a participant (attendance ≠ proposing).
            t.GetProperty("participants").GetArrayLength().ShouldBe(1);
        }

        AssertReportFields(created);
        AssertReportFields(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"));

        // Update: retype the trip, clear the times, keep one free-text proposer.
        var update = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Survey push {marker}",
            type = "exploration",
            tripDate = "2026-06-01",
            entryTime = (string?)null,
            exitTime = (string?)null,
            results = "Survey aborted; water rising.",
            caveIds = Array.Empty<Guid>(),
            participants = new object[] { new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null } },
            proposers = new object[] { new { caverId = (Guid?)null, newCaverName = "Ana Ionescu" } },
            visibility = "authenticated",
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("type").GetString().ShouldBe("exploration");
        updated.GetProperty("entryTime").ValueKind.ShouldBe(JsonValueKind.Null);
        updated.GetProperty("proposers").GetArrayLength().ShouldBe(1);

        // Validation: an unknown proposer user, and a proposer carrying both identities, are rejected.
        (await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Bad proposer {marker}",
            tripDate = "2026-06-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            proposers = new object[] { new { caverId = (Guid?)Guid.NewGuid(), newCaverName = (string?)null } },
            visibility = "private",
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Ambiguous proposer {marker}",
            tripDate = "2026-06-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            proposers = new object[] { new { caverId = (Guid?)outsiderCaverId, newCaverName = "Also named" } },
            visibility = "private",
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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

        // The paged list is redacted the same way as the detail view.
        var listed = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?search=Rigging day {marker}");
        listed.GetProperty("items").EnumerateArray().Single()
            .GetProperty("caveIds").GetArrayLength().ShouldBe(0);

        // Cave-filtered trip lists behave as if nothing were linked.
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?caveId={caveId}"))
            .GetProperty("items").GetArrayLength().ShouldBe(0);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?caveId={caveId}"))
            .GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Editing_a_trip_preserves_hidden_protected_cave_links()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync($"Protected {marker}", "authenticated", locationProtected: true);
        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Trip {marker}",
            tripDate = "2026-05-05",
            caveIds = new[] { caveId },
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        var tripId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The outsider can Write the trip but not view the cave's exact location, so they see
        // the cave link redacted (empty caveIds).
        await GrantTripAsync(tripId, outsiderId, AccessAction.Read | AccessAction.Write);
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"))
            .GetProperty("caveIds").GetArrayLength().ShouldBe(0);

        // Editing the title while echoing the redacted (empty) cave list must not drop the link.
        (await outsider.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Trip {marker} edited",
            tripDate = "2026-05-05",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var asOwner = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        asOwner.GetProperty("caveIds").GetArrayLength().ShouldBe(1);
        asOwner.GetProperty("caveIds").EnumerateArray().Single().GetGuid().ShouldBe(caveId);
        asOwner.GetProperty("title").GetString().ShouldBe($"Trip {marker} edited");
    }

    [Fact]
    public async Task Editing_a_trip_does_not_duplicate_child_history_events()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync($"Trip Cave {marker}", "authenticated");
        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Trip {marker}",
            tripDate = "2026-05-05",
            caveIds = new[] { caveId },
            participants = new[] { new { caverId = (Guid?)null, newCaverName = "Guest" } },
            visibility = "authenticated",
        });
        var tripId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Edit the title twice, resubmitting the same cave link and participant.
        for (var i = 0; i < 2; i++)
        {
            (await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
            {
                title = $"Trip {marker} v{i}",
                tripDate = "2026-05-05",
                caveIds = new[] { caveId },
                participants = new[] { new { caverId = (Guid?)null, newCaverName = "Guest" } },
                visibility = "authenticated",
            })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Unchanged children must be reconciled (diffed), not delete-all/recreate-all: exactly
        // one 'created' event each and no phantom re-creations or lost deletes in the timeline.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tripIdStr = tripId.ToString();
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == "TripLogCave" && a.RootEntityId == tripIdStr && a.Action == AuditActions.Created))
            .ShouldBe(1);
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == "TripLogParticipant" && a.RootEntityId == tripIdStr && a.Action == AuditActions.Created))
            .ShouldBe(1);
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == "TripLogCave" && a.RootEntityId == tripIdStr && a.Action == AuditActions.Deleted))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Tags_filter_lists_and_both_map_paths()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var tagged = await CreateCaveAsync($"Tagged {marker}", "authenticated");
        var untagged = await CreateCaveAsync($"Untagged {marker}", "authenticated");
        var taggedEntrance = await CreateEntranceAsync(tagged, 25.31, 45.31);
        await CreateEntranceAsync(untagged, 25.32, 45.32);

        // Tag with diacritics → slug is normalized; tagging twice is idempotent.
        var tagName = $"Zonă Verticală {marker}";
        var slug = $"zona-verticala-{marker}";
        var tagging = await TagAsync(owner, tagName, "feature", tagged);
        tagging.StatusCode.ShouldBe(HttpStatusCode.Created, await tagging.Content.ReadAsStringAsync());
        var taggingDto = await tagging.Content.ReadFromJsonAsync<JsonElement>();
        taggingDto.GetProperty("tag").GetProperty("slug").GetString().ShouldBe(slug);
        taggingDto.GetProperty("entityType").GetString().ShouldBe("feature");
        taggingDto.GetProperty("entityId").GetGuid().ShouldBe(tagged);
        (await TagAsync(owner, tagName, "feature", tagged)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The map layers filter the ENTRANCE features by their own tags — an entrance is a
        // feature in its own right, so it carries the tags that place it on the map.
        (await TagAsync(owner, tagName, "feature", taggedEntrance)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The catalog finds it (accent-insensitive) and the entity lists it.
        var tags = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/tags?search=zona verticala {marker}");
        tags.EnumerateArray().Count(x => x.GetProperty("slug").GetString() == slug).ShouldBe(1);
        var taggings = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/taggings/?entityType=feature&entityId={tagged}");
        taggings.GetArrayLength().ShouldBe(1);
        var taggingId = taggings[0].GetProperty("id").GetInt64();

        // Viewer role: readable entity but not writable → cannot tag or untag.
        (await TagAsync(viewer, tagName, "feature", untagged)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/taggings/{taggingId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Cave list filter.
        var list = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/caves?tag={slug}");
        var ids = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(tagged);
        ids.ShouldNotContain(untagged);

        // The cross-kind feature list honors the same tag over the supertype.
        var features = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/features?tag={slug}&pageSize=50");
        var featureIds = features.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        featureIds.ShouldContain(tagged);
        featureIds.ShouldContain(taggedEntrance);

        // Map: point path (zoom 14) and clustered SQL path (zoom 7) both honor the tag.
        var points = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/map/cave-entrances?bbox=25.2,45.2,25.4,45.4&zoom=14&tag={slug}");
        points.GetProperty("features").GetArrayLength().ShouldBe(1);
        points.GetProperty("features")[0].GetProperty("properties").GetProperty("id").GetGuid()
            .ShouldBe(taggedEntrance);
        var clusters = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/map/cave-entrances?bbox=25.2,45.2,25.4,45.4&zoom=7&tag={slug}");
        clusters.GetProperty("features").EnumerateArray()
            .Sum(f => f.GetProperty("properties").GetProperty("count").GetInt32()).ShouldBe(1);

        // Owner removes the cave's tag; the cave filter empties (the entrance keeps its own).
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

        // Feature rows are typed by kind; the qualified name selects exactly one kind.
        var kindName = Uri.EscapeDataString(FeatureAudit.TypeName(FeatureKind.Cave));
        var audit = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/audit?entityType={kindName}&entityId={caveId}");
        var items = audit.GetProperty("items").EnumerateArray().ToList();
        items.ShouldContain(x => x.GetProperty("action").GetString() == AuditActions.Created);
        items[0].GetProperty("userName").GetString().ShouldNotBeNullOrWhiteSpace();

        // The bare word selects the whole feature world, whatever the row's kind.
        var everyKind = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/audit?entityType={FeatureAudit.RootName}&entityId={caveId}");
        var kinds = everyKind.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("entityType").GetString()).ToList();
        kinds.Count.ShouldBe(items.Count);
        kinds.ShouldAllBe(t => t == FeatureAudit.TypeName(FeatureKind.Cave));
    }

    /// <summary>
    /// Pins who a trip write tells, and how often. Announcing a trip tells the people on it once;
    /// saving it again with the same people tells nobody a second time, and the author is never
    /// told about their own trip.
    /// <para>
    /// The distinction between "listed" and "newly listed" is worth a test of its own because it
    /// is not visible in the shape of the code that makes it: reconciling the roster works out who
    /// had no row already, and that answer is what the notification is sent to. A rewrite that
    /// loses it fails in one of two directions — everybody named is mailed again on every save, or
    /// nobody is ever mailed at all — and neither one fails a build or shows up in a response.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_participant_is_told_once_and_saving_the_trip_again_tells_nobody()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ownerCaverId = await RosterHelper.CaverIdForAsync(factory, ownerId);
        var laterUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-late-{marker}@t.local");
        var laterCaverId = await RosterHelper.CaverIdForAsync(factory, laterUserId);

        // Created with one registered participant beside a guest and the author themselves.
        var create = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Told once {marker}",
            tripDate = "2026-08-01",
            caveIds = Array.Empty<Guid>(),
            participants = new object[]
            {
                new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)ownerCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)null, newCaverName = $"Guest {marker}" },
            },
            visibility = "authenticated",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var tripId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // A new trip is a draft, and a draft tells nobody anything: the author is still deciding
        // what it says.
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(0);

        var publish = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/publish");
        publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);

        // Nobody tells the author about the trip they just wrote, and a guest with no account has
        // nowhere to be told.
        (await TripNotificationsForAsync(ownerId)).ShouldBe(0);

        // Saved again with the same people: the roster is unchanged, so there is nothing to say.
        var resave = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Told once {marker} (edited)",
            tripDate = "2026-08-01",
            caveIds = Array.Empty<Guid>(),
            participants = new object[]
            {
                new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)ownerCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)null, newCaverName = $"Guest {marker}" },
            },
            visibility = "authenticated",
        });
        resave.StatusCode.ShouldBe(HttpStatusCode.OK, await resave.Content.ReadAsStringAsync());
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);

        // The positive half: silence on a re-save is not silence in general. Somebody genuinely
        // added to the roster is told, and the people who were already on it still are not.
        var extended = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Told once {marker} (extended)",
            tripDate = "2026-08-01",
            caveIds = Array.Empty<Guid>(),
            participants = new object[]
            {
                new { caverId = (Guid?)outsiderCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)ownerCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)laterCaverId, newCaverName = (string?)null },
                new { caverId = (Guid?)null, newCaverName = $"Guest {marker}" },
            },
            visibility = "authenticated",
        });
        extended.StatusCode.ShouldBe(HttpStatusCode.OK, await extended.Content.ReadAsStringAsync());
        (await TripNotificationsForAsync(laterUserId)).ShouldBe(1);
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);
        (await TripNotificationsForAsync(ownerId)).ShouldBe(0);
    }

    /// <summary>
    /// The lifecycle happy path: a trip is written as a draft, announced once the write-up is
    /// ready, and can be taken back for more work. The day it first went out is recorded and stays
    /// recorded through a withdrawal and a second announcement — withdrawing something to correct
    /// it and announcing it again is one announcement, not two.
    /// </summary>
    [Fact]
    public async Task A_trip_is_written_as_a_draft_announced_and_can_be_taken_back()
    {
        var tripId = await CreateTripAsync($"Lifecycle {Guid.NewGuid():N}");

        var draft = await ReadTripAsync(owner, tripId);
        draft.GetProperty("state").GetString().ShouldBe("draft");
        draft.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        (await TransitionAsync(owner, tripId, "publish")).GetProperty("state").GetString().ShouldBe("published");

        // Read back rather than believed from the write's own answer: the stored value is what
        // every later reader gets, and it is the one that has to stay put.
        var firstAnnouncement = (await ReadTripAsync(owner, tripId)).GetProperty("publishedAt").GetDateTimeOffset();

        var withdrawn = await TransitionAsync(owner, tripId, "unpublish");
        withdrawn.GetProperty("state").GetString().ShouldBe("draft");
        (await ReadTripAsync(owner, tripId)).GetProperty("publishedAt").GetDateTimeOffset()
            .ShouldBe(firstAnnouncement);

        (await TransitionAsync(owner, tripId, "publish")).GetProperty("state").GetString().ShouldBe("published");
        var announced = await ReadTripAsync(owner, tripId);
        announced.GetProperty("state").GetString().ShouldBe("published");
        announced.GetProperty("publishedAt").GetDateTimeOffset().ShouldBe(firstAnnouncement);
    }

    /// <summary>
    /// A trip only moves the way the lifecycle allows, and a move it does not make is refused by
    /// name rather than quietly ignored. A trip called off is not announced from where it stands:
    /// it is reinstated first, so declaring that something did not happen and announcing it are
    /// two decisions and never one click.
    /// </summary>
    [Fact]
    public async Task A_move_the_lifecycle_does_not_allow_is_refused()
    {
        var tripId = await CreateTripAsync($"Refusals {Guid.NewGuid():N}");

        // A draft was never announced, so there is nothing to take back.
        await RefusedTransitionAsync(tripId, "unpublish");

        (await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Announcing what is already announced is not a move either.
        await RefusedTransitionAsync(tripId, "publish");

        await SetTripStateAsync(tripId, ActivityState.Cancelled);
        await RefusedTransitionAsync(tripId, "publish");

        // The positive half: a trip called off comes back through the same door a withdrawn one
        // does, and is announced from there.
        (await TransitionAsync(owner, tripId, "unpublish")).GetProperty("state").GetString().ShouldBe("draft");
        (await TransitionAsync(owner, tripId, "publish")).GetProperty("state").GetString().ShouldBe("published");
    }

    /// <summary>
    /// Announcing a trip is a write on it: signing in is not enough, being able to read it is not
    /// enough, and only somebody who may write it may put it out.
    /// </summary>
    [Fact]
    public async Task Only_somebody_who_may_write_the_trip_may_announce_it()
    {
        var tripId = await CreateTripAsync($"Who may announce {Guid.NewGuid():N}");
        var url = $"/api/v1/trip-logs/{tripId}/publish";

        using var anonymous = factory.CreateClient();
        (await anonymous.PostWithIfMatchAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A signed-in stranger with no grant on a private trip is not told one exists.
        (await outsider.PostWithIfMatchAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Reading it is not writing it: now they know it is there and still may not announce it.
        await GrantTripAsync(tripId, outsiderId, AccessAction.Read);
        (await outsider.PostWithIfMatchAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await owner.PostWithIfMatchAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Announcing carries the same precondition a full edit does: what goes out is the version the
    /// person announcing it read, not whatever the row has become since.
    /// </summary>
    [Fact]
    public async Task Announcing_a_trip_is_checked_against_the_version_the_caller_loaded()
    {
        var tripId = await CreateTripAsync($"Precondition {Guid.NewGuid():N}");
        var url = $"/api/v1/trip-logs/{tripId}/publish";

        var bare = await owner.PostAsync(url, null);
        bare.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await ProblemCodeAsync(bare)).ShouldBe("concurrency.if_match_required");

        var stale = await owner.PostWithIfMatchAsync(url, "\"0\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemCodeAsync(stale)).ShouldBe("concurrency.version_mismatch");

        var loaded = await owner.GetAsync($"/api/v1/trip-logs/{tripId}");
        var etag = loaded.Headers.ETag!.ToString();
        (await owner.PostWithIfMatchAsync(url, etag)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A draft is not a security boundary. What may be read is decided by visibility and the
    /// access entries and by nothing else — a draft left public is public, and that is correct,
    /// because two rules deciding who may read a row is how the two come to disagree.
    /// </summary>
    [Fact]
    public async Task A_draft_is_readable_by_whoever_its_visibility_admits()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var open = await CreateTripAsync($"Open draft {marker}", "public");
        var closed = await CreateTripAsync($"Closed draft {marker}");

        // A Viewer with no grant of any kind on either trip — the seeded editor group reads past
        // visibility by design, so proving anything about withholding needs somebody who does not.
        (await outsider.GetAsync($"/api/v1/trip-logs/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/trip-logs/{closed}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/?search=draft {marker}");
        var titles = listed.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString()).ToList();
        titles.ShouldContain($"Open draft {marker}");
        titles.ShouldNotContain($"Closed draft {marker}");

        // Both are still drafts: what was shown was shown while unannounced.
        (await ReadTripAsync(owner, open)).GetProperty("state").GetString().ShouldBe("draft");
    }

    /// <summary>
    /// Announcing re-asks who may currently read the trip rather than trusting the roster written
    /// while it was a draft. A name goes on a trip long before its visibility settles, and telling
    /// somebody about a trip they cannot open hands them its title and date for nothing.
    /// </summary>
    [Fact]
    public async Task Announcing_tells_only_the_people_who_may_read_the_trip()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var shut = await CreateTripAsync($"Shut out {marker}", "private", outsiderCaverId);
        (await TransitionAsync(owner, shut, "publish")).GetProperty("state").GetString().ShouldBe("published");
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(0);

        // The positive half, with the one thing that differs changed and nothing else: the same
        // person, on the same kind of trip, once they may read it.
        var opened = await CreateTripAsync($"Let in {marker}", "private", outsiderCaverId);
        await GrantTripAsync(opened, outsiderId, AccessAction.Read);
        (await TransitionAsync(owner, opened, "publish")).GetProperty("state").GetString().ShouldBe("published");
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);
    }

    /// <summary>
    /// The same rule on the ordinary edit path: putting a name on a trip already announced tells
    /// that person only if they may read the trip. Naming somebody on a trip that is closed to
    /// them is not a way to send them its title and date.
    /// </summary>
    [Fact]
    public async Task Adding_somebody_to_an_announced_trip_tells_them_only_if_they_may_read_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var shut = await CreateTripAsync($"Edited shut {marker}");
        (await TransitionAsync(owner, shut, "publish")).GetProperty("state").GetString().ShouldBe("published");
        await AddParticipantAsync(shut, $"Edited shut {marker}", outsiderCaverId);
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(0);

        // The positive half, with the one thing that differs changed and nothing else: the same
        // person, added the same way to the same kind of trip, once they may read it.
        var opened = await CreateTripAsync($"Edited open {marker}");
        await GrantTripAsync(opened, outsiderId, AccessAction.Read);
        (await TransitionAsync(owner, opened, "publish")).GetProperty("state").GetString().ShouldBe("published");
        await AddParticipantAsync(opened, $"Edited open {marker}", outsiderCaverId);
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);
    }

    /// <summary>
    /// A trip taken back for a correction and announced again tells its people again — chosen, not
    /// inherited. While a trip is back in draft its edits notify nobody, so announcing only the
    /// first time would leave out everybody added in between, who are exactly the people the
    /// announcement is for. The date of the first announcement stays where it is either way.
    /// </summary>
    [Fact]
    public async Task Announcing_a_corrected_trip_tells_its_people_again()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var laterUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-corr-{marker}@t.local");
        var laterCaverId = await RosterHelper.CaverIdForAsync(factory, laterUserId);

        var tripId = await CreateTripAsync($"Corrected {marker}", "authenticated", outsiderCaverId);
        (await TransitionAsync(owner, tripId, "publish")).GetProperty("state").GetString().ShouldBe("published");
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(1);

        // Taken back for more work, and somebody else put on the trip while it is a draft: that
        // edit tells nobody, which is what makes the second announcement the only chance they get.
        (await TransitionAsync(owner, tripId, "unpublish")).GetProperty("state").GetString().ShouldBe("draft");
        await AddParticipantAsync(tripId, $"Corrected {marker}", outsiderCaverId, laterCaverId);
        (await TripNotificationsForAsync(laterUserId)).ShouldBe(0);

        (await TransitionAsync(owner, tripId, "publish")).GetProperty("state").GetString().ShouldBe("published");
        (await TripNotificationsForAsync(laterUserId)).ShouldBe(1);
        (await TripNotificationsForAsync(outsiderId)).ShouldBe(2);
    }

    // ---- helpers ----

    /// <summary>Rewrites a trip's roster through the ordinary edit path, keeping its visibility.</summary>
    private async Task AddParticipantAsync(Guid tripId, string title, params Guid[] caverIds)
    {
        var current = await ReadTripAsync(owner, tripId);
        var response = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title,
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = caverIds.Select(id => new { caverId = (Guid?)id, newCaverName = (string?)null }).ToArray(),
            visibility = current.GetProperty("visibility").GetString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A trip owned by the Editor, optionally with one person on it.</summary>
    private async Task<Guid> CreateTripAsync(string title, string visibility = "private", Guid? participantCaverId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = participantCaverId is null
                ? Array.Empty<object>()
                : [new { caverId = participantCaverId, newCaverName = (string?)null }],
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadTripAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> TransitionAsync(HttpClient client, Guid tripId, string move)
    {
        var response = await client.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/{move}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>Asserts a move is refused, and refused under the one code that names the reason.</summary>
    private async Task RefusedTransitionAsync(Guid tripId, string move)
    {
        var response = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/{move}");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(response)).ShouldBe(ActivityStates.TripLogTransitionInvalidCode);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();

    /// <summary>Puts a trip into a state no endpoint reaches yet, to test the moves out of it.</summary>
    private async Task SetTripStateAsync(Guid tripId, ActivityState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trip = await db.TripLogs.FirstAsync(t => t.Id == tripId);
        trip.State = state;
        await db.SaveChangesAsync();
    }

    /// <summary>How many trip-participation notifications an account has been queued, ever.</summary>
    private async Task<int> TripNotificationsForAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationOutbox.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.Category == NotificationCategory.TripParticipation);
    }

    private static object TripBody(string title, Guid[] caveIds) => new
    {
        title,
        tripDate = "2026-07-01",
        caveIds,
        participants = Array.Empty<object>(),
        visibility = "private",
    };

    /// <summary>Tags a target named in the two-world vocabulary ("feature" + a feature id, or an entity name).</summary>
    private static Task<HttpResponseMessage> TagAsync(HttpClient client, string tagName, string entityType, Guid entityId) =>
        client.PostAsJsonAsync("/api/v1/taggings/", new { tagName, entityType, entityId });

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

    private async Task GrantTripAsync(Guid tripId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId (ScopeFeatureId is
            // reserved for the feature-domain FK).
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Adds an entrance and returns its own feature id.</summary>
    private async Task<Guid> CreateEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
