// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Entity history end-to-end over the feature supertype: one merged event per feature edit
/// (the aggregate is one thing to a reader), kind-qualified event types, descendant roll-up
/// through the containment closure at any depth, the protection-of-history redaction that
/// mirrors the live DTO masking — including its "current protection state governs" rule —
/// and the soft-delete forensics a timeline exists to show.
/// </summary>
public sealed class HistoryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, owns everything seeded here — always exact
    private HttpClient editor = null!;   // Editor granted Read+Write, but NOT ViewExactLocation
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private Guid editorId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    private const double ExactLon = 25.46110;
    private const double ExactLat = 45.53220;
    private const string SecretAddress = "Str. Secreta 5";
    private const string MovedSecretAddress = "Str. Secreta 7";

    public HistoryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hown-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hedit-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"hout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"hown-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"hedit-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"hout-{suffix}@t.local");
    }

    [Fact]
    public async Task Feature_edit_is_one_kind_qualified_event_carrying_both_halves_of_the_aggregate()
    {
        var caveId = await CreateCaveAsync(owner, protectedLocation: false);

        // One save touching the feature row (name, description) and the cave row (region):
        // a reader edits "the cave", so the timeline must show one event, not two.
        await UpdateCaveAsync(
            owner, caveId, name: "Merged edit", protectedLocation: false,
            description: "Both halves", region: "Bihor");

        var (_, events) = await HistoryAsync(owner, "feature", caveId);

        // Kind-qualified vocabulary throughout — a bare "Cave"/"Feature" row would mean the
        // supertype and subtype audited separately.
        events.Select(EntityType).ShouldAllBe(t => t.StartsWith("Feature:", StringComparison.Ordinal));

        var created = events
            .Where(e => Action(e) == "created" && EntityId(e) == caveId)
            .ToList();
        created.Count.ShouldBe(1);
        EntityType(created[0]).ShouldBe("Feature:Cave");
        HasChange(created[0], "Name").ShouldBeTrue();       // supertype half
        HasChange(created[0], "CaveTypeId").ShouldBeTrue(); // subtype half

        var edits = events
            .Where(e => Action(e) == "updated" && EntityId(e) == caveId && HasChange(e, "Description"))
            .ToList();
        edits.Count.ShouldBe(1);
        EntityType(edits[0]).ShouldBe("Feature:Cave");
        NewText(edits[0], "Description").ShouldBe("Both halves");
        NewText(edits[0], "Name").ShouldBe("Merged edit");
        NewText(edits[0], "Region").ShouldBe("Bihor");
    }

    [Fact]
    public async Task Parent_timelines_roll_up_descendants_through_the_closure()
    {
        var areaId = await CreateAreaAsync(owner, protectedLocation: false);
        var caveId = await CreateCaveAsync(owner, protectedLocation: false, parentId: areaId);
        var entranceId = await CreateEntranceAsync(owner, caveId, ExactLon, ExactLat);
        var unrelatedCaveId = await CreateCaveAsync(owner, protectedLocation: false);

        // One level down: the entrance's creation is part of its cave's story.
        var (_, caveEvents) = await HistoryAsync(owner, "feature", caveId);
        caveEvents.ShouldContain(e => EntityType(e) == "Feature:CaveEntrance" && EntityId(e) == entranceId);

        // Two levels down: the closure — not a pointer stamped at write time — decides scope,
        // so the entrance under the cave under the area shows in the area's timeline too.
        var (_, areaEvents) = await HistoryAsync(owner, "feature", areaId);
        areaEvents.ShouldContain(e =>
            EntityType(e) == "Feature:Generic" && EntityId(e) == areaId && Action(e) == "created");
        areaEvents.ShouldContain(e => EntityType(e) == "Feature:Cave" && EntityId(e) == caveId);
        areaEvents.ShouldContain(e => EntityType(e) == "Feature:CaveEntrance" && EntityId(e) == entranceId);

        // …and nothing outside the subtree leaks in.
        areaEvents.ShouldNotContain(e => EntityId(e) == unrelatedCaveId);
    }

    [Fact]
    public async Task Timeline_requires_authentication_and_never_discloses_unreadable_entities()
    {
        var privateCaveId = await CreateCaveAsync(owner, protectedLocation: false, visibility: "private");

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync($"/api/v1/history?entityType=feature&entityId={privateCaveId}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // A row the caller may not read answers exactly like one that never existed.
        foreach (var id in new[] { Guid.NewGuid(), privateCaveId })
        {
            var response = await outsider.GetAsync($"/api/v1/history?entityType=feature&entityId={id}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(response)).ShouldBe("history.entity_not_found");
        }
    }

    [Fact]
    public async Task Protected_timeline_hides_coordinates_and_address_until_exact_view_is_granted()
    {
        var caveId = await CreateCaveAsync(owner, protectedLocation: true, closestAddress: SecretAddress);
        var entranceId = await CreateEntranceAsync(owner, caveId, ExactLon, ExactLat);
        await GrantAsync(caveId, editorId, AccessAction.Read | AccessAction.Write);

        await UpdateCaveAsync(
            owner, caveId, name: "Protected cave", protectedLocation: true,
            description: "Owner note", closestAddress: MovedSecretAddress);
        await UpdateEntranceAsync(owner, entranceId, ExactLon + 0.001, ExactLat + 0.001, "moved");

        // ---- owner (exact): the timeline carries the entrance WKT and the address verbatim.
        var (ownerBody, _) = await HistoryAsync(owner, "feature", caveId);
        ownerBody.ShouldContain("POINT");
        ownerBody.ShouldContain("Secreta");

        // ---- editor (no exact view): the location-revealing values are removed and named.
        var (editorBody, editorEvents) = await HistoryAsync(editor, "feature", caveId);
        editorBody.ShouldNotContain("POINT");
        editorBody.ShouldNotContain("Secreta");

        var entranceUpdate = editorEvents.Single(e =>
            EntityId(e) == entranceId && Action(e) == "updated");
        EntityType(entranceUpdate).ShouldBe("Feature:CaveEntrance");
        Redacted(entranceUpdate).ShouldContain("Geom");
        HasChange(entranceUpdate, "Geom").ShouldBeFalse();
        // The event itself (who changed what, when) is activity metadata, not location data.
        entranceUpdate.GetProperty("userId").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // The birth coordinates in the created snapshot are governed by the same rule.
        var entranceCreate = editorEvents.Single(e =>
            EntityId(e) == entranceId && Action(e) == "created");
        Redacted(entranceCreate).ShouldContain("Geom");

        var caveEdit = editorEvents.Single(e =>
            EntityId(e) == caveId && Action(e) == "updated" && HasChange(e, "Description"));
        Redacted(caveEdit).ShouldContain("ClosestAddress");
        HasChange(caveEdit, "ClosestAddress").ShouldBeFalse();

        // ---- granting ViewExactLocation lifts the redaction retroactively.
        await GrantAsync(caveId, editorId, AccessAction.ViewExactLocation);
        var (grantedBody, _) = await HistoryAsync(editor, "feature", caveId);
        grantedBody.ShouldContain("POINT");
        grantedBody.ShouldContain("Secreta");
    }

    [Fact]
    public async Task Re_parenting_under_a_protected_root_hides_the_coordinate_history_retroactively()
    {
        var caveId = await CreateCaveAsync(owner, protectedLocation: false);
        var entranceId = await CreateEntranceAsync(owner, caveId, ExactLon, ExactLat);

        // Nothing above the cave is protected yet, so its coordinates are public record.
        var (before, _) = await HistoryAsync(outsider, "feature", caveId);
        before.ShouldContain("POINT");

        var areaId = await CreateAreaAsync(owner, protectedLocation: true);
        var reparent = await owner.PutWithIfMatchAsync($"/api/v1/features/{caveId}/parents", new
        {
            parents = new[] { new { parentId = areaId, isPrimary = true } },
        });
        reparent.StatusCode.ShouldBe(HttpStatusCode.OK, await reparent.Content.ReadAsStringAsync());

        // Redaction follows the CURRENT protected-ancestor set: a root two levels up now
        // vetoes exact view, and the history written before the move is covered by it.
        var (after, afterEvents) = await HistoryAsync(outsider, "feature", caveId);
        after.ShouldNotContain("POINT");
        Redacted(afterEvents.Single(e => EntityId(e) == entranceId && Action(e) == "created"))
            .ShouldContain("Geom");

        // The row's own owner keeps exact view of their cave under a protected area.
        var (ownerBody, _) = await HistoryAsync(owner, "feature", caveId);
        ownerBody.ShouldContain("POINT");
    }

    [Fact]
    public async Task Soft_deletion_events_survive_in_the_parent_timeline()
    {
        var areaId = await CreateAreaAsync(owner, protectedLocation: false);
        var caveId = await CreateCaveAsync(owner, protectedLocation: false, parentId: areaId);
        var entranceId = await CreateEntranceAsync(owner, caveId, ExactLon, ExactLat);

        var deleted = await owner.DeleteAsync($"/api/v1/caves/{caveId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // The deletion of a subtree is precisely the event a timeline exists to show, so the
        // stamped rows stay in the parent's scope with their kind-qualified types.
        var (_, areaEvents) = await HistoryAsync(owner, "feature", areaId);
        areaEvents.ShouldContain(e =>
            Action(e) == "deleted" && EntityType(e) == "Feature:Cave" && EntityId(e) == caveId);
        areaEvents.ShouldContain(e =>
            Action(e) == "deleted" && EntityType(e) == "Feature:CaveEntrance" && EntityId(e) == entranceId);

        // The deleted row itself is no longer addressable — same answer as a row that never was.
        var gone = await owner.GetAsync($"/api/v1/history?entityType=feature&entityId={caveId}");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(gone)).ShouldBe("history.entity_not_found");
    }

    /// <summary>
    /// Naming a cave on a trip is recorded as an association, and an association is recorded in
    /// the timeline of what it names rather than the one doing the naming. So the trip's own
    /// timeline never carries the cave's id — for anybody, including the caller who may place
    /// the cave perfectly well — and there is nothing there left to redact.
    ///
    /// Asserted for both callers together because "the id is absent" is satisfied just as well
    /// by a timeline that came back empty, and the events counted beside it are what refuses
    /// that reading: the trip was written, its writing was recorded, and the cave is still not
    /// in it.
    /// </summary>
    [Fact]
    public async Task A_trips_timeline_does_not_name_the_caves_the_trip_names()
    {
        var caveId = await CreateCaveAsync(owner, protectedLocation: true, closestAddress: SecretAddress);
        var tripId = await CreateTripAsync(owner, caveId);

        var (ownerBody, ownerEvents) = await HistoryAsync(owner, "tripLog", tripId);
        ownerEvents.ShouldContain(e => EntityType(e) == "TripLog" && Action(e) == "created");
        ownerBody.ShouldNotContain(caveId.ToString());

        var (outsiderBody, outsiderEvents) = await HistoryAsync(outsider, "tripLog", tripId);
        outsiderEvents.ShouldContain(e => EntityType(e) == "TripLog" && Action(e) == "created");
        outsiderBody.ShouldNotContain(caveId.ToString());

        // The record of the naming lives on the cave, where the cave's own rules govern it.
        var (_, caveEvents) = await HistoryAsync(owner, "feature", caveId);
        caveEvents.ShouldContain(e => EntityType(e) == "ResLinkMember" && Action(e) == "created");
    }

    /// <summary>
    /// The locating-link rule the timeline composes for feature-link rows: either endpoint
    /// hidden removes both ids (the pairing itself is what discloses the protected one).
    /// Exercised directly because a feature-link audit row carries no parent pointer, so no
    /// timeline currently serves one — the rule must still be pinned, or the redaction would
    /// be silently lost the day those rows do surface.
    /// </summary>
    [Fact]
    public void Locating_link_history_drops_the_endpoint_ids_of_a_hidden_feature()
    {
        var visibleId = Guid.CreateVersion7();
        var protectedId = Guid.CreateVersion7();

        var hiddenTarget = HistoryProtection.Redact(
            nameof(FeatureLink), LinkChanges(visibleId, protectedId), governingHidden: false,
            id => id == protectedId, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: _ => false);
        var hiddenTargetChanges = hiddenTarget.Changes;
        hiddenTarget.Redacted.ShouldContain(nameof(FeatureLink.ToId));
        hiddenTargetChanges.ShouldNotBeNull();
        hiddenTargetChanges!.ContainsKey(nameof(FeatureLink.ToId)).ShouldBeFalse();
        // The note is not location data; only the endpoints go.
        hiddenTargetChanges.ContainsKey("Note").ShouldBeTrue();

        // Redaction runs in both directions — the source endpoint discloses just as much.
        var hiddenSource = HistoryProtection.Redact(
            nameof(FeatureLink), LinkChanges(protectedId, visibleId), governingHidden: false,
            id => id == protectedId, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: _ => false);
        var hiddenSourceChanges = hiddenSource.Changes;
        hiddenSource.Redacted.ShouldContain(nameof(FeatureLink.FromId));
        hiddenSourceChanges.ShouldNotBeNull();
        hiddenSourceChanges!.ContainsKey(nameof(FeatureLink.FromId)).ShouldBeFalse();

        // Same row, nothing hidden: the removal is driven by the protection predicate, not
        // by the property name.
        var visible = HistoryProtection.Redact(
            nameof(FeatureLink), LinkChanges(visibleId, protectedId), governingHidden: false, _ => false, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: _ => false);
        var visibleChanges = visible.Changes;
        visible.Redacted.ShouldBeEmpty();
        visibleChanges.ShouldNotBeNull();
        visibleChanges!.ContainsKey(nameof(FeatureLink.FromId)).ShouldBeTrue();
        visibleChanges.ContainsKey(nameof(FeatureLink.ToId)).ShouldBeTrue();
    }

    // ---- event accessors

    private static string EntityType(JsonElement e) => e.GetProperty("entityType").GetString()!;

    private static string Action(JsonElement e) => e.GetProperty("action").GetString()!;

    private static Guid? EntityId(JsonElement e) =>
        Guid.TryParse(e.GetProperty("entityId").GetString(), out var id) ? id : null;

    private static bool HasChange(JsonElement e, string property)
    {
        var changes = e.GetProperty("changes");
        return changes.ValueKind == JsonValueKind.Object && changes.TryGetProperty(property, out _);
    }

    private static string? NewText(JsonElement e, string property) =>
        e.GetProperty("changes").GetProperty(property).GetProperty("new").GetString();

    private static string[] Redacted(JsonElement e) =>
        [.. e.GetProperty("redactedProperties").EnumerateArray().Select(x => x.GetString()!)];

    /// <summary>A feature-link audit diff as the interceptor writes one (ids as strings).</summary>
    private static JsonObject LinkChanges(Guid fromId, Guid toId) => new()
    {
        [nameof(FeatureLink.FromId)] = new JsonObject { ["old"] = null, ["new"] = fromId.ToString() },
        [nameof(FeatureLink.ToId)] = new JsonObject { ["old"] = null, ["new"] = toId.ToString() },
        ["Note"] = new JsonObject { ["old"] = null, ["new"] = "sump connection" },
    };

    // ---- API helpers

    private async Task<(string Body, List<JsonElement> Events)> HistoryAsync(
        HttpClient client, string entityType, Guid entityId)
    {
        var response = await client.GetAsync(
            $"/api/v1/history?entityType={entityType}&entityId={entityId}&pageSize=200");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var items = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().ToList();
        return (body, items);
    }

    private async Task<Guid> CreateCaveAsync(
        HttpClient client,
        bool protectedLocation,
        string? closestAddress = null,
        Guid? parentId = null,
        string visibility = "authenticated")
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"History cave {Guid.NewGuid():N}",
            caveTypeId,
            visibility,
            locationProtected = protectedLocation,
            closestAddress,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
        });
        return await CreatedIdAsync(response);
    }

    /// <summary>A karst area — a generic feature that can hold caves and carry the protection root.</summary>
    private async Task<Guid> CreateAreaAsync(HttpClient client, bool protectedLocation)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Karst area {Guid.NewGuid():N}",
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 },
                        new[] { 25.60, 45.50 },
                        new[] { 25.60, 45.60 },
                        new[] { 25.40, 45.60 },
                        new[] { 25.40, 45.50 },
                    },
                },
            },
            locationProtected = protectedLocation,
            visibility = "authenticated",
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateEntranceAsync(HttpClient client, Guid caveId, double lon, double lat)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = "Main entrance",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            altitude = 900m,
            positionQuality = "Gps",
        });
        return await CreatedIdAsync(response);
    }

    private static async Task<Guid> CreateTripAsync(HttpClient client, Guid caveId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"History trip {Guid.NewGuid():N}",
            tripDate = "2026-07-01",
            caveIds = new[] { caveId },
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        return await CreatedIdAsync(response);
    }

    private async Task UpdateCaveAsync(
        HttpClient client,
        Guid caveId,
        string name,
        bool protectedLocation,
        string? description = null,
        string? closestAddress = null,
        string? region = null)
    {
        var response = await client.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", new
        {
            name,
            caveTypeId,
            visibility = "authenticated",
            locationProtected = protectedLocation,
            closestAddress,
            description,
            region,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task UpdateEntranceAsync(
        HttpClient client, Guid entranceId, double lon, double lat, string description)
    {
        var response = await client.PutWithIfMatchAsync($"/api/v1/cave-entrances/{entranceId}", new
        {
            name = "Main entrance",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            description,
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        return root.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Grants (ORs in) actions on a feature; one direct object-scope allow entry
    /// per subject per target — the same shape the ACL endpoint writes.</summary>
    private async Task GrantAsync(Guid featureId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = await db.AccessEntries.FirstOrDefaultAsync(e =>
            e.SubjectKind == AccessSubjectKind.User && e.SubjectId == userId
            && e.Domain == AccessDomain.Features && e.ScopeKind == AccessScopeKind.Object
            && e.ScopeFeatureId == featureId && e.Effect == AccessEffect.Allow);
        if (entry is null)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = userId,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Features,
                Actions = actions,
                ScopeKind = AccessScopeKind.Object,
                ScopeFeatureId = featureId,
            });
        }
        else
        {
            entry.Actions |= actions;
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        editor.Dispose();
        outsider.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
