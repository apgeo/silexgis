// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// The relations-graph route as a route: who may ask, what is refused and with which code, that
/// an unreadable item answers exactly like a missing one, that one category pages without
/// overlap under a stable total, that totals count only what the caller may see, and that no
/// answer carries a position under any name.
/// </summary>
public sealed class GraphNeighbourhoodTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private GraphTestKit kit = null!;

    public GraphNeighbourhoodTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync() => kit = await GraphTestKit.CreateAsync(factory, "gxn");

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var cave = await kit.CaveAsync($"Graf anon {Guid.NewGuid():N}");
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={cave}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The same question signed in is answered.
        (await GraphTestKit.GraphAsync(kit.Viewer, "feature", cave))
            .GetProperty("seed").GetProperty("key").GetString().ShouldBe(GraphTestKit.FeatureKey(cave));
    }

    [Theory]
    [InlineData("id={id}", "graph.type.unknown")]
    [InlineData("type=nonsense&id={id}", "graph.type.unknown")]
    [InlineData("type=file&id={id}", "graph.type.unknown")]
    [InlineData("type=restricted&id={id}", "graph.type.unknown")]
    [InlineData("type=feature", "graph.id.invalid")]
    [InlineData("type=feature&id=not-a-uuid", "graph.id.invalid")]
    [InlineData("type=feature&id=00000000-0000-0000-0000-000000000000", "graph.id.invalid")]
    [InlineData("type=tag&id={id}", "graph.id.invalid")]
    [InlineData("type=tag&id=-3", "graph.id.invalid")]
    [InlineData("type=feature&id={id}&categories=resLink,nonsense", "graph.category.unknown")]
    [InlineData("type=feature&id={id}&category=nonsense&offset=0", "graph.category.unknown")]
    [InlineData("type=feature&id={id}&offset=2", "graph.paging.invalid")]
    [InlineData("type=feature&id={id}&category=resLink&offset=-1", "graph.paging.invalid")]
    [InlineData("type=feature&id={id}&limit=0", "graph.paging.invalid")]
    [InlineData("type=feature&id={id}&limit=201", "graph.paging.invalid")]
    public async Task Each_invalid_request_is_refused_with_its_code(string query, string code)
    {
        var response = await kit.Viewer.GetAsync(
            $"/api/v1/graph/neighbourhood?{query.Replace("{id}", Guid.NewGuid().ToString())}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task An_unreadable_item_answers_exactly_like_a_missing_one()
    {
        var hidden = await kit.FeatureAsync($"Graf hidden {Guid.NewGuid():N}");
        await kit.DenyViewerAsync(hidden);

        // Positive control: the item exists and its owner may ask about it.
        (await GraphTestKit.GraphAsync(kit.Owner, "feature", hidden))
            .GetProperty("seed").GetProperty("id").GetString().ShouldBe(hidden.ToString());

        var unreadable = await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={hidden}");
        var missing = await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={Guid.NewGuid()}");
        unreadable.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unreadableBody = JsonDocument.Parse(await unreadable.Content.ReadAsStringAsync()).RootElement;
        var missingBody = JsonDocument.Parse(await missing.Content.ReadAsStringAsync()).RootElement;
        unreadableBody.GetProperty("code").GetString().ShouldBe("graph.seed.not_found");
        missingBody.GetProperty("code").GetString().ShouldBe("graph.seed.not_found");
        Names(unreadableBody).ShouldBe(Names(missingBody));

        // A type the item is not is the same miss: a cave asked for as a trip names nothing.
        (await kit.Owner.GetAsync($"/api/v1/graph/neighbourhood?type=tripLog&id={hidden}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task One_category_pages_without_overlap_under_a_stable_total()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var hub = await kit.FeatureAsync($"Graf hub {marker}");
        var targets = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            targets.Add(await kit.FeatureAsync($"Graf spoke {i} {marker}"));
        }

        await kit.FeatureLinksAsync(hub, [.. targets.Select(t => (t, "related"))]);
        var hubKey = GraphTestKit.FeatureKey(hub);

        var first = await GraphTestKit.GraphAsync(kit.Viewer, $"type=feature&id={hub}&categories=featureLink&limit=2");
        GraphTestKit.Count(first, "featureLink").GetProperty("shown").GetInt32().ShouldBe(2);
        GraphTestKit.Count(first, "featureLink").GetProperty("total").GetInt32().ShouldBe(5);

        var seen = GraphTestKit.Neighbours(first, "featureLink", hubKey).ToList();
        for (var offset = 2; offset < 6; offset += 2)
        {
            var next = await GraphTestKit.GraphAsync(
                kit.Viewer, $"type=feature&id={hub}&category=featureLink&offset={offset}&limit=2");
            GraphTestKit.Count(next, "featureLink").GetProperty("total").GetInt32().ShouldBe(5);
            next.GetProperty("counts").GetArrayLength().ShouldBe(1);
            seen.AddRange(GraphTestKit.Neighbours(next, "featureLink", hubKey));
        }

        seen.Count.ShouldBe(5);
        seen.ToHashSet().ShouldBe(targets.Select(GraphTestKit.FeatureKey).ToHashSet(), ignoreOrder: true);

        // Ordered by far-end title: spoke 0 and 1 come first.
        GraphTestKit.Neighbours(first, "featureLink", hubKey)
            .ShouldBe([GraphTestKit.FeatureKey(targets[0]), GraphTestKit.FeatureKey(targets[1])], ignoreOrder: true);
    }

    [Fact]
    public async Task Totals_count_only_what_the_caller_may_see()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var seed = await kit.FeatureAsync($"Graf counted {marker}");
        var open = await kit.FeatureAsync($"Graf open {marker}");
        var hidden = await kit.FeatureAsync($"Graf withheld {marker}");
        await kit.DenyViewerAsync(hidden);
        await kit.FeatureLinksAsync(seed, (open, "related"), (hidden, "related"));

        var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", seed, "featureLink");
        GraphTestKit.Count(owners, "featureLink").GetProperty("total").GetInt32().ShouldBe(2);

        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", seed, "featureLink");
        GraphTestKit.Count(viewers, "featureLink").GetProperty("total").GetInt32().ShouldBe(1);
        GraphTestKit.Count(viewers, "featureLink").GetProperty("shown").GetInt32().ShouldBe(1);
        GraphTestKit.HasNode(viewers, GraphTestKit.FeatureKey(hidden)).ShouldBeFalse();
        GraphTestKit.HasNode(viewers, GraphTestKit.FeatureKey(open)).ShouldBeTrue();
    }

    [Fact]
    public async Task Categories_narrow_the_answer_and_a_category_that_does_not_apply_is_absent()
    {
        var cave = await kit.CaveAsync($"Graf narrowed {Guid.NewGuid():N}");
        var narrowed = await GraphTestKit.GraphAsync(kit.Viewer, "feature", cave, "featureLink,hierarchy");
        narrowed.GetProperty("counts").EnumerateArray().Select(c => c.GetProperty("category").GetString())
            .ShouldBe(["featureLink", "hierarchy"], ignoreOrder: true);

        // Feature links and containment are relations of features alone: a link hub asked
        // about answers only its resource-link family.
        var other = await kit.FeatureAsync($"Graf linked {Guid.NewGuid():N}");
        var link = await kit.ResLinkAsync(cave, other);
        var hub = await GraphTestKit.GraphAsync(kit.Viewer, "resLink", link);
        hub.GetProperty("counts").EnumerateArray().Select(c => c.GetProperty("category").GetString())
            .ShouldBe(["resLink"]);
        hub.GetProperty("seed").GetProperty("expandable").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task No_answer_carries_a_position_under_any_name()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var cave = await kit.CaveAsync($"Graf positioned {marker}");
        await kit.EntranceAsync(cave, $"Graf door {marker}");
        await kit.CenterlineAsync(cave, $"graf-line-{marker}.gpx");
        var child = await kit.FeatureAsync($"Graf child {marker}", parentId: cave);
        var linked = await kit.FeatureAsync($"Graf neighbour {marker}");
        await kit.FeatureLinksAsync(linked, (cave, "related"));
        await kit.ResLinkAsync(cave, child, linked);

        var answer = await GraphTestKit.GraphAsync(kit.Owner, "feature", cave);
        // The answer is populated in all three families, so the scan below reads real rows.
        GraphTestKit.Count(answer, "resLink").GetProperty("total").GetInt32().ShouldBe(1);
        GraphTestKit.Count(answer, "featureLink").GetProperty("total").GetInt32().ShouldBe(1);
        GraphTestKit.Count(answer, "hierarchy").GetProperty("total").GetInt32().ShouldBe(3);

        string[] forbidden =
        [
            "geometry", "geom", "lat", "lon", "lng", "latitude", "longitude", "x", "y", "z",
            "coordinates", "bbox", "extent", "distance", "point", "position", "altitude", "elevation",
        ];
        var names = Names(answer);
        foreach (var name in forbidden)
        {
            names.ShouldNotContain(name);
        }

        // Nothing that looks like a coordinate pair either: no number in the answer but counts.
        Numbers(answer).ShouldAllBe(n => n == Math.Floor(n) && n >= 0);
    }

    private static HashSet<string> Names(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    names.Add(p.Name);
                    Walk(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }

        Walk(element);
        return names;
    }

    private static List<double> Numbers(JsonElement element)
    {
        var numbers = new List<double>();
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    numbers.Add(e.GetDouble());
                    break;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        Walk(p.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        Walk(element);
        return numbers;
    }

    public async ValueTask DisposeAsync()
    {
        await kit.DisposeAsync();
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
