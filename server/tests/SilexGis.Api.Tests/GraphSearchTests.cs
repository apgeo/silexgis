// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The relations graph's seed search: who may ask, what is refused and with which code, that an
/// item the caller may not read is never offered while the same item is offered to someone who
/// may, and that every hit is an item the neighbourhood route answers for.
/// </summary>
public sealed class GraphSearchTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private GraphTestKit kit = null!;

    public GraphSearchTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync() => kit = await GraphTestKit.CreateAsync(factory, "gxs");

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var token = Token();
        await kit.FeatureAsync($"Graf {token}");
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/graph/search?q={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The same question signed in is answered.
        (await SearchAsync(kit.Viewer, $"q={token}")).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("q=x&types=nonsense", "graph.type.unknown")]
    [InlineData("q=x&types=feature,nonsense", "graph.type.unknown")]
    [InlineData("q=x&types=resLink", "graph.type.unknown")]
    [InlineData("q=x&types=file", "graph.type.unknown")]
    [InlineData("q=x&limit=0", "graph.paging.invalid")]
    [InlineData("q=x&limit=21", "graph.paging.invalid")]
    [InlineData("q={long}", "graph.query.too_long")]
    public async Task Each_invalid_request_is_refused_with_its_code(string query, string code)
    {
        var response = await kit.Viewer.GetAsync(
            $"/api/v1/graph/search?{query.Replace("{long}", new string('a', 201))}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Blank_text_finds_nothing()
    {
        (await SearchAsync(kit.Admin, "q=%20%20")).ShouldBeEmpty();
        (await SearchAsync(kit.Admin, string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_feature_the_caller_may_not_read_is_never_offered()
    {
        var token = Token();
        var open = await kit.FeatureAsync($"Graf deschis {token}");
        var denied = await kit.FeatureAsync($"Graf refuzat {token}");
        await kit.DenyViewerAsync(denied);
        var unlisted = await kit.FeatureAsync($"Graf privat {token}", visibility: "private");

        // Positive control: the owner is offered all three.
        var owners = Ids(await SearchAsync(kit.Owner, $"q={token}&types=feature"));
        owners.ShouldBe([open.ToString(), denied.ToString(), unlisted.ToString()], ignoreOrder: true);

        // The viewer is offered only the one it may read: neither the explicitly denied one nor
        // the private one, whichever kinds are asked for.
        Ids(await SearchAsync(kit.Viewer, $"q={token}&types=feature")).ShouldBe([open.ToString()]);
        Ids(await SearchAsync(kit.Viewer, $"q={token}")).ShouldBe([open.ToString()]);

        // And what the search would not offer, the neighbourhood route would not answer either.
        (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={denied}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tag_is_offered_only_to_a_caller_who_may_read_the_tag_catalogue()
    {
        var token = Token();
        var feature = await kit.FeatureAsync($"Graf etichetat {token}");
        var tagName = $"graf-eticheta-{token}";
        var tagged = await kit.Owner.PostAsJsonAsync(
            "/api/v1/taggings", new { tagName, entityType = "feature", entityId = feature });
        tagged.IsSuccessStatusCode.ShouldBeTrue(await tagged.Content.ReadAsStringAsync());

        // Positive control: with the catalogue's ordinary Read the viewer is offered the tag,
        // under the key its node carries in a neighbourhood.
        var before = await SearchAsync(kit.Viewer, $"q={tagName}&types=tag");
        before.Count.ShouldBe(1);
        before[0].GetProperty("type").GetString().ShouldBe("tag");
        before[0].GetProperty("title").GetString().ShouldBe(tagName);
        before[0].GetProperty("key").GetString().ShouldBe($"tag:{before[0].GetProperty("id").GetString()}");

        // With an explicit deny on reading tags, the catalogue refuses the viewer, and the search
        // offers no tag name — while the owner is still offered it.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = kit.ViewerId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Tags,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        (await kit.Viewer.GetAsync("/api/v1/tags")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SearchAsync(kit.Viewer, $"q={tagName}&types=tag")).ShouldBeEmpty();
        (await SearchAsync(kit.Owner, $"q={tagName}&types=tag")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Kinds_and_limit_narrow_the_hits_and_every_hit_opens_as_a_seed()
    {
        var token = Token();
        var features = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            features.Add(await kit.FeatureAsync($"Graf {i} {token}"));
        }

        var group = await IdOfAsync(await kit.Admin.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = $"Clubul Graf {token}",
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        }));

        var all = await SearchAsync(kit.Admin, $"q={token}");
        Ids(all).ShouldBe([.. features.Select(f => f.ToString()), group.ToString()], ignoreOrder: true);

        // Asking for one kind answers only that kind; the limit caps each kind.
        (await SearchAsync(kit.Admin, $"q={token}&types=cavingGroup"))
            .Select(h => h.GetProperty("type").GetString()).ShouldBe(["cavingGroup"]);
        (await SearchAsync(kit.Admin, $"q={token}&types=feature&limit=2")).Count.ShouldBe(2);
        var capped = await SearchAsync(kit.Admin, $"q={token}&limit=1");
        capped.Select(h => h.GetProperty("type").GetString()).ShouldBe(["feature", "cavingGroup"]);

        // Features come in title order, and each hit is answered by the neighbourhood route
        // under its own type and id, with the same key.
        Ids(await SearchAsync(kit.Admin, $"q={token}&types=feature"))
            .ShouldBe([.. features.Select(f => f.ToString())]);
        foreach (var hit in all)
        {
            var graph = await GraphTestKit.GraphAsync(
                kit.Admin, $"type={hit.GetProperty("type").GetString()}&id={hit.GetProperty("id").GetString()}");
            graph.GetProperty("seed").GetProperty("key").GetString().ShouldBe(hit.GetProperty("key").GetString());
            graph.GetProperty("seed").GetProperty("title").GetString().ShouldBe(hit.GetProperty("title").GetString());
        }
    }

    private static string Token() => $"gxs{Guid.NewGuid():N}"[..14];

    private static List<string> Ids(IEnumerable<JsonElement> hits) =>
        [.. hits.Select(h => h.GetProperty("id").GetString()!)];

    private static async Task<List<JsonElement>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/graph/search?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.Clone().EnumerateArray()];
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await kit.DisposeAsync();
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
