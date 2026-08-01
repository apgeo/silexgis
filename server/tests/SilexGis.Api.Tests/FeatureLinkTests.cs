// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Typed feature links: full-replace validation, and the locating-link redaction that
/// carries the old cave-link protection semantics — a link between a record with exact
/// coordinates and a protected cave vanishes, in both directions and on every read
/// path, for callers without the exact-location permission.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureLinkTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private Guid outsiderId;
    private long sinkholeTypeId;
    private long caveTypeId;

    public FeatureLinkTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"flown-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"flout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"flown-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"flout-{suffix}@t.local");
    }

    [Fact]
    public async Task Link_replace_validates_and_serves_both_directions()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var featureA = await CreateFeatureAsync(owner, $"Link A {marker}", "authenticated", 25.61, 45.51);
        var featureB = await CreateFeatureAsync(owner, $"Link B {marker}", "authenticated", 25.62, 45.52);
        var privateFeature = await CreateFeatureAsync(owner, $"Link Priv {marker}", "private", 25.63, 45.53);

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync($"/api/v1/features/{featureA}/links"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Write guard: readable-but-not-writable is 403, unreadable is an undisclosing 404.
        (await outsider.PutAsJsonAsync($"/api/v1/features/{featureA}/links", Links()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PutAsJsonAsync($"/api/v1/features/{privateFeature}/links", Links()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await PutLinksExpectingAsync(owner, featureA, "feature.link_self",
            new { toId = featureA, linkKindCode = "related", note = (string?)null });
        await PutLinksExpectingAsync(owner, featureA, "feature.link_kind_unknown",
            new { toId = featureB, linkKindCode = "no_such_kind", note = (string?)null });
        await PutLinksExpectingAsync(owner, featureA, "feature.link_duplicate",
            new { toId = featureB, linkKindCode = "related", note = (string?)null },
            new { toId = featureB, linkKindCode = "related", note = "twice" });
        await PutLinksExpectingAsync(owner, featureA, "feature.link_target_not_found",
            new { toId = Guid.NewGuid(), linkKindCode = "related", note = (string?)null });

        // A target the caller cannot read is reported exactly like a missing one.
        var outsiderPrivate = await CreateFeatureAsync(outsider, $"Foreign priv {marker}", "private", 25.64, 45.54);
        await PutLinksExpectingAsync(owner, featureA, "feature.link_target_not_found",
            new { toId = outsiderPrivate, linkKindCode = "related", note = (string?)null });

        // A valid replace; the row serves from both endpoints, direction preserved.
        var put = await owner.PutAsJsonAsync($"/api/v1/features/{featureA}/links",
            Links(new { toId = featureB, linkKindCode = "related", note = "pair" }));
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var view = (await put.Content.ReadFromJsonAsync<JsonArray>())!;
        view.Single()!["toId"]!.GetValue<Guid>().ShouldBe(featureB);
        view.Single()!["linkKindCode"]!.GetValue<string>().ShouldBe("related");
        view.Single()!["note"]!.GetValue<string>().ShouldBe("pair");

        var reverse = (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureB}/links"))!;
        reverse.Single()!["fromId"]!.GetValue<Guid>().ShouldBe(featureA);

        // Locating links to UNPROTECTED endpoints are not redacted for anyone.
        var outsiderView = (await outsider.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureA}/links"))!;
        outsiderView.Count.ShouldBe(1);

        // Full replace with an empty set removes the visible link.
        (await owner.PutAsJsonAsync($"/api/v1/features/{featureA}/links", Links()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureA}/links"))!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Locating_link_to_a_protected_cave_is_redacted_and_survives_blind_replaces()
    {
        // A location-protected but otherwise visible cave, linked from a feature with
        // exact coordinates: the link must vanish for callers without the
        // exact-location permission, or the feature would give the cave away.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Linked Protected Cave {marker}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var lon = 25.4611;
        var lat = 45.5322;
        var featureId = await CreateFeatureAsync(owner, $"Sinkhole above protected {marker}", "authenticated", lon, lat);

        var link = await owner.PutAsJsonAsync($"/api/v1/features/{featureId}/links",
            Links(new { toId = caveId, linkKindCode = "associated_cave", note = (string?)null }));
        link.StatusCode.ShouldBe(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        (await link.Content.ReadFromJsonAsync<JsonArray>())!.Single()!["toId"]!.GetValue<Guid>().ShouldBe(caveId);

        // Owner (may view exact location) keeps the link, from both endpoints.
        (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureId}/links"))!.Count.ShouldBe(1);
        (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{caveId}/links"))!
            .Single()!["fromId"]!.GetValue<Guid>().ShouldBe(featureId);

        // Outsider can read both records but not the exact cave location: the link is
        // redacted in both directions.
        (await outsider.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureId}/links"))!.Count.ShouldBe(0);
        (await outsider.GetFromJsonAsync<JsonArray>($"/api/v1/features/{caveId}/links"))!.Count.ShouldBe(0);

        // The feature's own read paths stay exact (it is unprotected) and carry no cave
        // reference of any kind — the association lives only in the links endpoint.
        var detail = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/features/{featureId}"))!;
        detail["feature"]!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(lon);
        detail["feature"]!.AsObject().ContainsKey("caveId").ShouldBeFalse();

        var listItem = (await outsider.GetFromJsonAsync<JsonObject>(
            $"/api/v1/features?search=Sinkhole above protected {marker}"))!
            ["items"]!.AsArray().Single(f => f!["id"]!.GetValue<Guid>() == featureId)!;
        listItem.AsObject().ContainsKey("caveId").ShouldBeFalse();

        var bbox = $"{lon - 0.01},{lat - 0.01},{lon + 0.01},{lat + 0.01}";
        var mapFeature = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/map/features?bbox={bbox}"))!
            ["features"]!.AsArray().Single(f => f!["properties"]!["id"]!.GetValue<Guid>() == featureId)!;
        mapFeature["properties"]!.AsObject().ContainsKey("caveId").ShouldBeFalse();
        mapFeature["properties"]!["approximate"]!.GetValue<bool>().ShouldBeFalse();
        mapFeature["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(lon);

        // A caller with Write but no exact view replaces the link set from their redacted
        // view — the hidden locating link must be preserved verbatim, not silently severed.
        await GrantAsync(owner, featureId, outsiderId, "Read,Write");
        var plainTarget = await CreateFeatureAsync(owner, $"Plain target {marker}", "authenticated", 25.47, 45.54);
        var blindReplace = await outsider.PutAsJsonAsync($"/api/v1/features/{featureId}/links",
            Links(new { toId = plainTarget, linkKindCode = "related", note = (string?)null }));
        blindReplace.StatusCode.ShouldBe(HttpStatusCode.OK, await blindReplace.Content.ReadAsStringAsync());

        // The grantee still sees only what they may; the owner sees the cave link intact.
        (await blindReplace.Content.ReadFromJsonAsync<JsonArray>())!
            .Single()!["toId"]!.GetValue<Guid>().ShouldBe(plainTarget);
        var ownerLinks = (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureId}/links"))!;
        ownerLinks.Count.ShouldBe(2);
        ownerLinks.Select(l => l!["toId"]!.GetValue<Guid>()).ShouldContain(caveId);
        ownerLinks.Single(l => l!["toId"]!.GetValue<Guid>() == caveId)!
            ["linkKindCode"]!.GetValue<string>().ShouldBe("associated_cave");
    }

    private static object Links(params object[] links) => new { links };

    private async Task<Guid> CreateFeatureAsync(
        HttpClient client, string name, string visibility, double lon, double lat)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private static async Task PutLinksExpectingAsync(
        HttpClient client, Guid featureId, string code, params object[] links)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/features/{featureId}/links", new { links });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>().ShouldBe(code);
    }

    private static async Task GrantAsync(HttpClient granter, Guid featureId, Guid subjectId, string permissions)
    {
        var response = await granter.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/acl", new
        {
            entries = new[] { new { subjectKind = "user", subjectId, permissions } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
