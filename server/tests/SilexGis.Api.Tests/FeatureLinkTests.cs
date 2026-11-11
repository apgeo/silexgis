// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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
/// Typed feature links: full-replace validation, and the locating-link redaction that
/// carries the old cave-link protection semantics — a link between a record with exact
/// coordinates and a protected cave vanishes, in both directions and on every read
/// path, for callers without the exact-location permission.
/// </summary>
public sealed class FeatureLinkTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private Guid ownerId;
    private Guid outsiderId;
    private long sinkholeTypeId;
    private long caveTypeId;

    public FeatureLinkTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"flown-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"flout-{suffix}@t.local");

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

        // A target the caller cannot read is reported exactly like a missing one. Editors
        // read every content row now, so an object-scope deny is what puts the target out
        // of this caller's sight.
        var unreadable = await CreateFeatureAsync(owner, $"Unreadable target {marker}", "private", 25.64, 45.54);
        await DenyReadAsync(ownerId, unreadable);
        (await owner.GetAsync($"/api/v1/features/{unreadable}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await PutLinksExpectingAsync(owner, featureA, "feature.link_target_not_found",
            new { toId = unreadable, linkKindCode = "related", note = (string?)null });

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

    [Fact]
    public async Task A_replace_keeps_the_links_the_caller_was_never_shown()
    {
        // The editor reads A's links, changes them and sends the whole list back. A link to a
        // feature they cannot read, and a link to a deleted feature, were never in what they read,
        // so they cannot have meant to remove them — and could not name them to keep them either.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var source = await CreateFeatureAsync(owner, $"Keep A {marker}", "authenticated", 25.71, 45.61);
        var shown = await CreateFeatureAsync(owner, $"Keep shown {marker}", "authenticated", 25.72, 45.62);
        var dropped = await CreateFeatureAsync(owner, $"Keep dropped {marker}", "authenticated", 25.73, 45.63);
        var unseen = await CreateFeatureAsync(owner, $"Keep unseen {marker}", "authenticated", 25.74, 45.64);
        var retired = await CreateFeatureAsync(owner, $"Keep retired {marker}", "authenticated", 25.75, 45.65);
        var added = await CreateFeatureAsync(owner, $"Keep added {marker}", "authenticated", 25.76, 45.66);

        var initial = await owner.PutAsJsonAsync($"/api/v1/features/{source}/links", Links(
            new { toId = shown, linkKindCode = "related", note = (string?)null },
            new { toId = dropped, linkKindCode = "related", note = (string?)null },
            new { toId = unseen, linkKindCode = "related", note = (string?)null },
            new { toId = retired, linkKindCode = "related", note = (string?)null }));
        initial.StatusCode.ShouldBe(HttpStatusCode.OK, await initial.Content.ReadAsStringAsync());

        (await owner.DeleteAsync($"/api/v1/features/{retired}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DenyReadAsync(ownerId, unseen);

        // What the editor is shown: the two readable links, not the unseen or the retired one.
        var view = (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{source}/links"))!
            .Select(l => l!["toId"]!.GetValue<Guid>()).ToList();
        view.ShouldBe([shown, dropped], ignoreOrder: true);

        // A round trip that drops one readable link and adds another.
        var replaced = await owner.PutAsJsonAsync($"/api/v1/features/{source}/links", Links(
            new { toId = shown, linkKindCode = "related", note = "still here" },
            new { toId = added, linkKindCode = "related", note = (string?)null }));
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.FeatureLinks.AsNoTracking()
            .Where(l => l.FromId == source).Select(l => l.ToId).ToListAsync();
        stored.ShouldBe([shown, added, unseen, retired], ignoreOrder: true);
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

    /// <summary>Takes one feature out of one user's sight, whatever their role grants.</summary>
    private async Task DenyReadAsync(Guid subjectId, Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = subjectId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task GrantAsync(HttpClient granter, Guid featureId, Guid subjectId, string actions)
    {
        var response = await granter.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId, effect = "allow", actions, scopeKind = "object" },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        await Task.CompletedTask;
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
