// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;

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

    public MapViewTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mv-own-{suffix}@t.local");
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
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/map-views/");
        var items = list.EnumerateArray().ToList();
        items.Single(x => x.GetProperty("isHome").GetBoolean())
            .GetProperty("id").GetGuid().ShouldBe(second);
        _ = first;

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
