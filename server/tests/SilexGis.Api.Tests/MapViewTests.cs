// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Saved map views: CRUD with visibility, single home view per user, share-token
/// lifecycle, the anonymous shared endpoint exposing name+config only, and explicit
/// grants on a view.
/// </summary>
public sealed class MapViewTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private Guid outsiderId;
    private Guid ownerId;

    public MapViewTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mv-own-{suffix}@t.local");
        // A regular user: Editors hold domain-wide map-view rights now, so the private-view
        // invisibility below needs a caller with nothing but grants.
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mv-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"mv-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"mv-out-{suffix}@t.local");
    }

    [Fact]
    public async Task Views_cover_crud_home_uniqueness_share_and_anonymous_access()
    {
        // Create two views; the second claims "home" and must steal it from the first.
        var first = await CreateAsync("Overview", isHome: true);
        var second = await CreateAsync("Detail area", isHome: true);
        // Asked of this test's own views only: an Editor's list holds every Editor's views, so
        // a home another test sets would otherwise be counted here.
        (await HomesAsync(owner, first, second)).ShouldBe([second]);

        // Private views are invisible to others.
        (await ViewIdsAsync(outsider)).ShouldNotContain(second);

        // Share: mint once, reuse on repeat; the anonymous page gets name+config ONLY.
        var share1 = await owner.PostAsync($"/api/v1/map-views/{second}/share", null);
        share1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await share1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shareToken").GetGuid();
        var share2 = await owner.PostAsync($"/api/v1/map-views/{second}/share", null);
        (await share2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shareToken").GetGuid().ShouldBe(token);

        using var anonymous = factory.CreateClient();
        var shared = await anonymous.GetAsync($"/api/v1/shared/views/{token}");
        shared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await shared.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("name").GetString().ShouldBe("Detail area");
        payload.GetProperty("camera").GetProperty("zoom").GetDouble().ShouldBe(12);
        payload.GetProperty("camera").GetProperty("longitude").GetDouble().ShouldBe(25.4);
        payload.GetProperty("camera").GetProperty("latitude").GetDouble().ShouldBe(45.5);

        // What the page draws and nothing else: the stored document is the owner's working state —
        // the files and rasters they had on, their tag filter, their 3D camera — and none of it is
        // sent, not even under another name.
        payload.EnumerateObject().Select(x => x.Name).Order()
            .ShouldBe(new[] { "background", "camera", "name", "overlays", "reach" });
        var text = payload.GetRawText();
        foreach (var privateMember in new[] { ViewGeofileId, ViewRasterId, ViewTagSlug, "camera3d", "geofileIds", "tagFilter", "configVersion" })
        {
            text.ShouldNotContain(privateMember, Case.Insensitive);
        }

        // Anonymous callers still cannot browse the API surface.
        (await anonymous.GetAsync("/api/v1/map-views/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/shared/views/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Others cannot share/delete a private view; revoking kills the link.
        (await outsider.PostAsync($"/api/v1/map-views/{second}/share", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/map-views/{second}/share")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"/api/v1/shared/views/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Delete.
        (await owner.DeleteAsync($"/api/v1/map-views/{second}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A saved view carries its own access control, so an explicit grant on it is what
    /// makes a private view reachable for someone else — and a Read grant stays a Read
    /// grant: it opens the list, never the edit.
    /// </summary>
    [Fact]
    public async Task Acl_grants_make_a_private_view_readable_for_the_grantee()
    {
        var viewId = await CreateAsync("Granted area", isHome: false);
        (await ViewIdsAsync(outsider)).ShouldNotContain(viewId);
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed by grantee", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await ReplaceViewAccessRulesAsync(owner, viewId, [(outsiderId, AccessAction.Read)]);

        (await ViewIdsAsync(outsider)).ShouldContain(viewId);
        // Read is not Write: the view now exists for the grantee, so refusal is 403, not 404.
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed by grantee", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Write closes the gap; revoking everything takes the view away again.
        await ReplaceViewAccessRulesAsync(
            owner, viewId, [(outsiderId, AccessAction.Read | AccessAction.Write)]);
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed by grantee", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await ReplaceViewAccessRulesAsync(owner, viewId, []);
        (await ViewIdsAsync(outsider)).ShouldNotContain(viewId);
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed again", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Who may change, remove or publish a view, pinned route by route: a caller with Read only
    /// is told the view exists but may not be changed or published (403) and is not told it
    /// can be deleted or unpublished (404); a caller with no grant learns nothing (404). Each
    /// refusal is followed by the owner doing the same thing, so the refusal is about the
    /// caller and not about a route that refuses everybody.
    /// </summary>
    [Fact]
    public async Task Only_the_rights_a_caller_holds_open_each_write_on_a_view()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mv-read-{suffix}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"mv-read-{suffix}@t.local");
        var view = await CreateAsync("Matrix view", isHome: false);
        await ReplaceViewAccessRulesAsync(owner, view, [(readerId, AccessAction.Read)]);
        (await ViewIdsAsync(reader)).ShouldContain(view);
        (await ViewIdsAsync(outsider)).ShouldNotContain(view);

        async Task RefusedAsync(HttpClient caller, HttpStatusCode expected, string what)
        {
            (await caller.PutAsJsonAsync($"/api/v1/map-views/{view}", ViewBody("Renamed", isHome: false)))
                .StatusCode.ShouldBe(expected, $"{what} PUT");
            (await caller.PostAsync($"/api/v1/map-views/{view}/share", null))
                .StatusCode.ShouldBe(expected, $"{what} share");
            (await caller.DeleteAsync($"/api/v1/map-views/{view}/share"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{what} unshare");
            (await caller.DeleteAsync($"/api/v1/map-views/{view}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{what} DELETE");
        }

        await RefusedAsync(reader, HttpStatusCode.Forbidden, "Read grantee");
        await RefusedAsync(outsider, HttpStatusCode.NotFound, "no grant");

        // Nothing the refused calls asked for happened.
        var stored = (await ViewsAsync(owner)).Single(v => v.GetProperty("id").GetGuid() == view);
        stored.GetProperty("name").GetString().ShouldBe("Matrix view");
        (stored.TryGetProperty("shareToken", out var token) && token.ValueKind != JsonValueKind.Null).ShouldBeFalse();

        (await owner.PutAsJsonAsync($"/api/v1/map-views/{view}", ViewBody("Renamed", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/map-views/{view}/share", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // A Read grantee still cannot unpublish what the owner published.
        (await reader.DeleteAsync($"/api/v1/map-views/{view}/share")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/map-views/{view}/share")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/map-views/{view}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ViewIdsAsync(owner)).ShouldNotContain(view);
    }

    [Fact]
    public async Task A_view_with_no_name_a_name_too_long_or_a_config_that_is_not_an_object_is_refused()
    {
        var name = $"Valid {Guid.NewGuid():N}"[..20];
        var view = await CreateAsync(name, isHome: false);

        var bodies = new (string What, JsonObject Body)[]
        {
            ("an empty name", Body(string.Empty, new JsonObject { ["zoom"] = 12 })),
            ("a 201-character name", Body(new string('n', 201), new JsonObject { ["zoom"] = 12 })),
            ("a config that is an array", Body(name, new JsonArray(1, 2))),
        };
        foreach (var (what, body) in bodies)
        {
            (await owner.PostAsJsonAsync("/api/v1/map-views/", body)).StatusCode
                .ShouldBe(HttpStatusCode.BadRequest, $"create with {what}");
            (await owner.PutAsJsonAsync($"/api/v1/map-views/{view}", body)).StatusCode
                .ShouldBe(HttpStatusCode.BadRequest, $"update with {what}");
        }

        // A name exactly at the limit is accepted, and the refused updates changed nothing.
        (await owner.PostAsJsonAsync("/api/v1/map-views/", Body(new string('n', 200), new JsonObject { ["zoom"] = 12 })))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ViewsAsync(owner)).Single(v => v.GetProperty("id").GetGuid() == view)
            .GetProperty("name").GetString().ShouldBe(name);

        static JsonObject Body(string name, JsonNode config) => new()
        {
            ["name"] = name,
            ["description"] = null,
            ["config"] = config,
            ["isHome"] = false,
            ["cavingGroupId"] = null,
            ["visibility"] = "private",
        };
    }

    [Fact]
    public async Task A_view_bound_to_a_caving_group_the_caller_is_not_in_is_refused()
    {
        // Two real clubs: the owner belongs to one and not to the other, so the refusal below is
        // about membership, not about a group that does not exist.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid ownClub, otherClub;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var own = new CavingGroup { Name = $"MV own club {suffix}", Slug = $"mv-own-club-{suffix}" };
            var other = new CavingGroup { Name = $"MV other club {suffix}", Slug = $"mv-other-club-{suffix}" };
            db.CavingGroups.AddRange(own, other);
            await db.SaveChangesAsync();
            await RosterHelper.AddMemberAsync(db, own.Id, ownerId);
            ownClub = own.Id;
            otherClub = other.Id;
        }

        var name = $"Bound {suffix}";
        var body = FilteredViewBody(name, null);

        // Create: the club the owner is not in is refused and nothing is stored; the owner's own
        // club is accepted.
        body["cavingGroupId"] = otherClub.ToString();
        var refused = await owner.PostAsJsonAsync("/api/v1/map-views/", body);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe(CavingGroupBindingRules.ForbiddenCode);
        (await ViewsAsync(owner)).ShouldNotContain(v => v.GetProperty("name").GetString() == name);

        body["cavingGroupId"] = ownClub.ToString();
        var created = await owner.PostAsJsonAsync("/api/v1/map-views/", body);
        var createdText = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, createdText);
        var id = JsonDocument.Parse(createdText).RootElement.GetProperty("id").GetGuid();

        // Update: re-binding to the club the owner is not in is refused and the stored group
        // stays; an update that keeps the owner's own club goes through.
        body["cavingGroupId"] = otherClub.ToString();
        var rebind = await owner.PutAsJsonAsync($"/api/v1/map-views/{id}", body);
        rebind.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rebind.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe(CavingGroupBindingRules.ForbiddenCode);
        (await ViewsAsync(owner)).Single(v => v.GetProperty("id").GetGuid() == id)
            .GetProperty("cavingGroupId").GetGuid().ShouldBe(ownClub);

        body["cavingGroupId"] = ownClub.ToString();
        body["description"] = "kept in its club";
        var kept = await owner.PutAsJsonAsync($"/api/v1/map-views/{id}", body);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// One home per owner holds when the owner saves several homes at the same moment — two tabs,
    /// a double click — not only when the saves arrive one after the other.
    /// </summary>
    [Fact]
    public async Task Homes_saved_at_the_same_moment_still_leave_the_owner_one_home()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ids = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => CreateAsync($"Parallel home {i} {suffix}", isHome: true)));

        (await HomesAsync(owner, ids)).Count.ShouldBe(1);

        // And moving home in parallel afterwards keeps it at one as well.
        await Task.WhenAll(ids.Select(async (id, i) =>
        {
            var moved = await owner.PutAsJsonAsync($"/api/v1/map-views/{id}", ViewBody($"Parallel home {i} {suffix}", isHome: true));
            moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        }));
        (await HomesAsync(owner, ids)).Count.ShouldBe(1);
    }

    /// <summary>
    /// "Home" is the owner's choice and theirs alone: a grantee with Write who edits someone
    /// else's view cannot make it the owner's home, cannot take the owner's home away by
    /// sending <c>isHome:false</c>, and does not lose their own home by editing another
    /// person's view with <c>isHome:true</c>.
    /// </summary>
    [Fact]
    public async Task A_grantee_editing_a_view_changes_nobodys_home()
    {
        var ownersHome = await CreateAsync("Owner home", isHome: true);
        var shared = await CreateAsync("Owner shared", isHome: false);
        var granteesHome = await CreateAsAsync(outsider, "Grantee home", isHome: true);

        // Positive case first: the grantee really can edit the shared view, so whatever the
        // homes say afterwards is the edit's doing, not a refusal's.
        await ReplaceViewAccessRulesAsync(owner, shared, [(outsiderId, AccessAction.Read | AccessAction.Write)]);
        var asHome = await outsider.PutAsJsonAsync($"/api/v1/map-views/{shared}", ViewBody("Renamed as home", isHome: true));
        asHome.StatusCode.ShouldBe(HttpStatusCode.OK, await asHome.Content.ReadAsStringAsync());

        (await HomesAsync(owner, ownersHome, shared)).ShouldBe([ownersHome]);
        (await HomesAsync(outsider, granteesHome, shared)).ShouldBe([granteesHome]);

        var notHome = await outsider.PutAsJsonAsync($"/api/v1/map-views/{shared}", ViewBody("Renamed again", isHome: false));
        notHome.StatusCode.ShouldBe(HttpStatusCode.OK, await notHome.Content.ReadAsStringAsync());
        await ReplaceViewAccessRulesAsync(owner, ownersHome, [(outsiderId, AccessAction.Read | AccessAction.Write)]);
        var ownersHomeEdited = await outsider.PutAsJsonAsync($"/api/v1/map-views/{ownersHome}", ViewBody("Owner home renamed", isHome: false));
        ownersHomeEdited.StatusCode.ShouldBe(HttpStatusCode.OK, await ownersHomeEdited.Content.ReadAsStringAsync());

        (await HomesAsync(owner, ownersHome, shared)).ShouldBe([ownersHome]);
        (await HomesAsync(outsider, granteesHome)).ShouldBe([granteesHome]);

        // The owner still moves their own home, and moving it clears only their own.
        var moved = await owner.PutAsJsonAsync($"/api/v1/map-views/{shared}", ViewBody("Owner shared", isHome: true));
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await HomesAsync(owner, ownersHome, shared)).ShouldBe([shared]);
        (await HomesAsync(outsider, granteesHome)).ShouldBe([granteesHome]);
    }

    /// <summary>
    /// A view is shown to whoever it is shared with, so it never stores a place somebody typed into
    /// its filter — a point, an outline, or the place nearest first was measured from — whether it
    /// is created that way or updated to it, and a refused update leaves the view as it was. A
    /// filter naming no place of its own is stored whole, and a view naming a saved filter keeps
    /// the id.
    /// </summary>
    [Fact]
    public async Task A_filter_holding_a_place_is_refused_on_create_and_update_and_any_other_filter_is_kept()
    {
        var deep = new ConditionNode(FeatureFilterFields.Depth, FilterOp.GreaterOrEqual, [new NumberValue(100)]);
        var plain = new FilterDocument { Scope = [new WorldScope("feature", deep)] };
        var placed = new (string What, FilterDocument Document)[]
        {
            ("a typed point", new FilterDocument
            {
                Scope =
                [
                    new WorldScope("feature", new AllOfNode(
                    [
                        deep,
                        new ConditionNode(
                            FeatureFilterFields.Position,
                            FilterOp.Within,
                            [new NumberValue(500), new PointValue(PlacedLon, PlacedLat)]),
                    ])),
                ],
            }),
            ("a pasted outline", new FilterDocument
            {
                Scope =
                [
                    new WorldScope("feature", new NotNode(new ConditionNode(
                        FeatureFilterFields.Position,
                        FilterOp.Inside,
                        [new PolygonValue([[[25.4, 45.5], [PlacedLon, 45.5], [PlacedLon, PlacedLat], [25.4, 45.5]]])]))),
                ],
            }),
            ("nearest first's place", plain with { Sort = SortKey.Proximity, Anchor = new PointAnchor(PlacedLon, PlacedLat) }),
        };
        var name = $"Placed {Guid.NewGuid():N}"[..24];

        foreach (var (what, document) in placed)
        {
            await ShouldBeRefusedAsync(await owner.PostAsJsonAsync("/api/v1/map-views/", FilteredViewBody(name, document)), what);
        }

        (await ViewsAsync(owner)).ShouldNotContain(v => v.GetProperty("name").GetString() == name);

        var created = await owner.PostAsJsonAsync("/api/v1/map-views/", FilteredViewBody(name, plain));
        var createdPayload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, createdPayload);
        var id = JsonDocument.Parse(createdPayload).RootElement.GetProperty("id").GetGuid();

        foreach (var (what, document) in placed)
        {
            await ShouldBeRefusedAsync(await owner.PutAsJsonAsync($"/api/v1/map-views/{id}", FilteredViewBody(name, document)), what);
        }

        var stored = (await ViewsAsync(owner)).Single(v => v.GetProperty("id").GetGuid() == id).GetProperty("config");
        var storedFilter = FilterDocument.Deserialize(stored.GetProperty("filterDocument").GetRawText()).ShouldNotBeNull();
        FilterDocument.Serialize(storedFilter).ShouldBe(FilterDocument.Serialize(plain));
        stored.GetRawText().ShouldNotContain(PlacedLat.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var savedFilterId = Guid.NewGuid();
        var bySaved = await owner.PostAsJsonAsync("/api/v1/map-views/", FilteredViewBody($"{name} saved", null, savedFilterId));
        var bySavedPayload = await bySaved.Content.ReadAsStringAsync();
        bySaved.StatusCode.ShouldBe(HttpStatusCode.Created, bySavedPayload);
        JsonDocument.Parse(bySavedPayload).RootElement.GetProperty("config").GetProperty("savedFilterId").GetGuid()
            .ShouldBe(savedFilterId);
    }

    private const double PlacedLon = 25.4321;
    private const double PlacedLat = 45.5432;

    private static JsonObject FilteredViewBody(string name, FilterDocument? document, Guid? savedFilterId = null)
    {
        var config = new JsonObject
        {
            ["configVersion"] = 1,
            ["zoom"] = 12,
            ["center"] = new JsonArray(25.4, 45.5),
            ["entrancesVisible"] = true,
            ["surfaceFeaturesVisible"] = true,
            ["tagFilter"] = null,
        };
        if (document is not null)
        {
            config["filterDocument"] = JsonNode.Parse(FilterDocument.Serialize(document));
        }

        if (savedFilterId is { } id)
        {
            config["savedFilterId"] = id.ToString();
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = null,
            ["config"] = config,
            ["isHome"] = false,
            ["cavingGroupId"] = null,
            ["visibility"] = "private",
        };
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string what)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{what}: {payload}");
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()
            .ShouldBe(SavedFilterRules.AnchorRefusedCode, what);
    }

    private static async Task<List<JsonElement>> ViewsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/map-views/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.Clone().EnumerateArray()];
    }

    private const string ViewGeofileId = "6f1c1d36-0d7e-4a3b-9a55-3f0b6b0c2a11";
    private const string ViewRasterId = "a2b4c6d8-1e3f-4a5b-8c7d-9e0f1a2b3c4d";
    private const string ViewTagSlug = "private-leads-only";

    private static object ViewBody(string name, bool isHome) => new
    {
        name,
        description = (string?)null,
        config = new
        {
            configVersion = 1,
            zoom = 12,
            center = new[] { 25.4, 45.5 },
            geofileIds = new[] { ViewGeofileId },
            rasters = new[] { new { id = ViewRasterId, opacity = 0.5 } },
            tagFilter = ViewTagSlug,
            camera3d = new { longitude = 25.41, latitude = 45.51, height = 1500, heading = 10, pitch = -30, roll = 0 },
        },
        isHome,
        cavingGroupId = (Guid?)null,
        visibility = "private",
    };

    private async Task<Guid> CreateAsync(string name, bool isHome)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/map-views/", ViewBody(name, isHome));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAsAsync(HttpClient client, string name, bool isHome)
    {
        var response = await client.PostAsJsonAsync("/api/v1/map-views/", ViewBody(name, isHome));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Which of the named views the caller's list marks as home, in the order named.</summary>
    private static async Task<List<Guid>> HomesAsync(HttpClient client, params Guid[] among) =>
        [.. (await ViewsAsync(client))
            .Where(x => x.GetProperty("isHome").GetBoolean())
            .Select(x => x.GetProperty("id").GetGuid())
            .Where(among.Contains)
            .OrderBy(id => Array.IndexOf(among, id))];

    private static async Task<List<Guid>> ViewIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/map-views/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())];
    }

    /// <summary>Replaces the direct access rules on a saved view (the "mapView" target).</summary>
    private static async Task ReplaceViewAccessRulesAsync(
        HttpClient client, Guid viewId, (Guid SubjectId, AccessAction Actions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/mapView/{viewId}/access", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.SubjectId,
                effect = "allow",
                actions = e.Actions.ToString().Replace(" ", string.Empty),
                scopeKind = "object",
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
