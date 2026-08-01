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
[Collection(PostgresCollection.Name)]
public sealed class MapViewTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private Guid outsiderId;

    public MapViewTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
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
        payload.GetProperty("config").GetProperty("zoom").GetInt32().ShouldBe(12);
        payload.TryGetProperty("ownerUserId", out _).ShouldBeFalse(); // nothing beyond name+config

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

        await ReplaceViewAclAsync(owner, viewId, [(outsiderId, AccessAction.Read)]);

        (await ViewIdsAsync(outsider)).ShouldContain(viewId);
        // Read is not Write: the view now exists for the grantee, so refusal is 403, not 404.
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed by grantee", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Write closes the gap; revoking everything takes the view away again.
        await ReplaceViewAclAsync(
            owner, viewId, [(outsiderId, AccessAction.Read | AccessAction.Write)]);
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed by grantee", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await ReplaceViewAclAsync(owner, viewId, []);
        (await ViewIdsAsync(outsider)).ShouldNotContain(viewId);
        (await outsider.PutAsJsonAsync($"/api/v1/map-views/{viewId}", ViewBody("Renamed again", isHome: false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object ViewBody(string name, bool isHome) => new
    {
        name,
        description = (string?)null,
        config = new { configVersion = 1, zoom = 12, center = new[] { 25.4, 45.5 } },
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

    /// <summary>Replaces the grants on a saved view (the "mapView" ACL target).</summary>
    private static async Task ReplaceViewAclAsync(
        HttpClient client, Guid viewId, (Guid SubjectId, AccessAction Permissions)[] entries)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/mapView/{viewId}/acl", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.SubjectId,
                permissions = e.Permissions.ToString().Replace(" ", string.Empty),
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
