// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// The relations graph is a second view of reads that already exist, never a new disclosure:
/// for each category, and for an administrator, the fixtures' owner, a member of the caving
/// group some rows are shared with, and a plain viewer outside it who is also kept out of some
/// rows, the graph's neighbours equal what the relation's own route answers the same caller.
/// Asked around an ordinary cave and around a location-protected one, where the routes' rules
/// differ most.
/// </summary>
public sealed class GraphParityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private GraphTestKit kit = null!;
    private Guid cave;
    private Guid guarded;
    private Guid groupChild;
    private Guid groupNear;

    public GraphParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        kit = await GraphTestKit.CreateAsync(factory, "gxp");
        var m = Guid.NewGuid().ToString("N")[..8];

        var region = await kit.FeatureAsync($"Graf region {m}");
        cave = await kit.CaveAsync($"Graf parity cave {m}");
        guarded = await kit.CaveAsync($"Graf parity guarded {m}", locationProtected: true);
        var setParents = await kit.Owner.PutAsJsonAsync(
            $"/api/v1/features/{cave}/parents", new { parents = new[] { new { parentId = region, isPrimary = true } } });
        setParents.EnsureSuccessStatusCode();

        await kit.EntranceAsync(cave, $"Graf door {m}");
        await kit.CenterlineAsync(cave, $"graf-parity-{m}.gpx");
        await kit.EntranceAsync(guarded, $"Graf guarded door {m}");
        await kit.CenterlineAsync(guarded, $"graf-guarded-{m}.gpx");

        var openChild = await kit.FeatureAsync($"Graf open child {m}", parentId: cave);
        var hiddenChild = await kit.FeatureAsync($"Graf hidden child {m}", parentId: cave);
        await kit.DenyViewerAsync(hiddenChild);
        var guardedChild = await kit.FeatureAsync($"Graf guarded child {m}", parentId: guarded);
        groupChild = await kit.FeatureAsync($"Graf group child {m}", parentId: cave);
        await kit.ShareWithGroupAsync(GroupShared.Feature, groupChild);
        groupNear = await kit.FeatureAsync($"Graf group near {m}");
        await kit.ShareWithGroupAsync(GroupShared.Feature, groupNear);

        var near = await kit.FeatureAsync($"Graf near {m}");
        var hiddenNear = await kit.FeatureAsync($"Graf hidden near {m}");
        await kit.DenyViewerAsync(hiddenNear);
        await kit.FeatureLinksAsync(
            cave, (near, "related"), (hiddenNear, "same_system"), (guarded, "hydro_connection"), (groupNear, "related"));
        await kit.FeatureLinksAsync(near, (guarded, "associated_cave"));

        await kit.ResLinkAsync(cave, near);
        await kit.ResLinkAsync(cave, groupNear);
        await kit.ResLinkAsync(cave, hiddenNear, openChild);
        await kit.ResLinkAsync(cave, guarded);
        await kit.ResLinkAsync(guarded, guardedChild);
        await kit.ResLinkAsync(guarded, hiddenChild, near);
    }

    public static TheoryData<string, bool> Viewers => new()
    {
        { "admin", false }, { "owner", false }, { "member", false }, { "viewer", false },
        { "admin", true }, { "owner", true }, { "member", true }, { "viewer", true },
    };

    private HttpClient As(string who) => who switch
    {
        "admin" => kit.Admin,
        "owner" => kit.Owner,
        "member" => kit.Member,
        _ => kit.Viewer,
    };

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Resource_links_are_the_links_panel(string who, bool aroundGuarded)
    {
        var client = As(who);
        var seed = aroundGuarded ? guarded : cave;
        var panel = await GraphTestKit.BodyAsync(client, $"/api/v1/reslinks/for-target?type=feature&id={seed}&pageSize=500");
        var expected = new HashSet<string>();
        foreach (var link in panel.GetProperty("items").EnumerateArray())
        {
            var hub = $"resLink:{link.GetProperty("id").GetGuid()}";
            foreach (var member in link.GetProperty("members").EnumerateArray())
            {
                var target = member.GetProperty("display").ValueKind == JsonValueKind.Null
                    ? $"restricted:{member.GetProperty("id").GetGuid()}"
                    : $"{member.GetProperty("targetType").GetString()}:{member.GetProperty("targetId").GetGuid()}";
                expected.Add($"{hub}>{target}");
            }
        }

        var graph = await GraphTestKit.GraphAsync(client, "feature", seed, "resLink");
        var actual = GraphTestKit.Edges(graph, "resLink")
            .Select(e => $"{e.GetProperty("source").GetString()}>{e.GetProperty("target").GetString()}")
            .ToHashSet();
        actual.ShouldBe(expected, ignoreOrder: true);
        GraphTestKit.Count(graph, "resLink").GetProperty("total").GetInt32()
            .ShouldBe(panel.GetProperty("totalItems").GetInt32());

        // The member reads the group's row through membership; the viewer outside the group
        // meets the same member as a placeholder.
        if (!aroundGuarded)
        {
            expected.Any(e => e.EndsWith($">feature:{groupNear}", StringComparison.Ordinal)).ShouldBe(who != "viewer");
        }
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Feature_links_are_the_feature_links_route(string who, bool aroundGuarded)
    {
        var client = As(who);
        var seed = aroundGuarded ? guarded : cave;
        var route = await GraphTestKit.BodyAsync(client, $"/api/v1/features/{seed}/links");
        var expected = route.EnumerateArray()
            .Select(l => $"feature:{l.GetProperty("fromId").GetGuid()}>feature:{l.GetProperty("toId").GetGuid()}"
                + $"|{l.GetProperty("linkKindCode").GetString()}")
            .ToHashSet();

        var graph = await GraphTestKit.GraphAsync(client, "feature", seed, "featureLink");
        var actual = GraphTestKit.Edges(graph, "featureLink")
            .Select(e => $"{e.GetProperty("source").GetString()}>{e.GetProperty("target").GetString()}"
                + $"|{e.GetProperty("relation").GetString()}")
            .ToHashSet();
        actual.ShouldBe(expected, ignoreOrder: true);
        GraphTestKit.Count(graph, "featureLink").GetProperty("total").GetInt32().ShouldBe(expected.Count);
        if (!aroundGuarded)
        {
            expected.Any(e => e.Contains($"feature:{groupNear}", StringComparison.Ordinal)).ShouldBe(who != "viewer");
        }
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Containment_is_the_parents_children_entrances_and_centerlines_routes(string who, bool aroundGuarded)
    {
        var client = As(who);
        var seed = aroundGuarded ? guarded : cave;
        var parents = (await GraphTestKit.BodyAsync(client, $"/api/v1/features/{seed}/parents")).EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid());
        var children = (await GraphTestKit.BodyAsync(client, $"/api/v1/features/{seed}/children?pageSize=500"))
            .GetProperty("items").EnumerateArray()
            .Where(c => c.GetProperty("kind").GetString() is not ("caveEntrance" or "centerline"))
            .Select(c => c.GetProperty("id").GetGuid());
        var entrances = (await GraphTestKit.BodyAsync(client, $"/api/v1/caves/{seed}/entrances")).EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid());
        var centerlines = (await GraphTestKit.BodyAsync(client, $"/api/v1/caves/{seed}/centerlines")).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid());
        var expected = parents.Select(p => $"parent:{p}")
            .Concat(children.Select(c => $"parent:{c}"))
            .Concat(entrances.Select(e => $"entrance:{e}"))
            .Concat(centerlines.Select(c => $"centerline:{c}"))
            .ToHashSet();

        var seedKey = GraphTestKit.FeatureKey(seed);
        var graph = await GraphTestKit.GraphAsync(client, "feature", seed, "hierarchy");
        var actual = GraphTestKit.Edges(graph, "hierarchy")
            .Select(e =>
            {
                var far = e.GetProperty("source").GetString() == seedKey
                    ? e.GetProperty("target").GetString()!
                    : e.GetProperty("source").GetString()!;
                return $"{e.GetProperty("relation").GetString()}:{far["feature:".Length..]}";
            })
            .ToHashSet();
        actual.ShouldBe(expected, ignoreOrder: true);
        GraphTestKit.Count(graph, "hierarchy").GetProperty("total").GetInt32().ShouldBe(expected.Count);

        // The fixture reaches every relation for the people who may see it all, so equality
        // above is not two empty sets agreeing.
        if (who is "admin" or "owner")
        {
            expected.Count(k => k.StartsWith("entrance:", StringComparison.Ordinal)).ShouldBe(1);
            expected.Count(k => k.StartsWith("centerline:", StringComparison.Ordinal)).ShouldBe(1);
            expected.Count(k => k.StartsWith("parent:", StringComparison.Ordinal)).ShouldBe(aroundGuarded ? 1 : 4);
        }

        // The member reaches the group's child through membership. Whether the viewer is told
        // of it too is the children route's own rule (a child is read beneath its parent), so
        // the viewer's answer is held to the route by the equality above, not asserted here.
        if (!aroundGuarded && who != "viewer")
        {
            expected.ShouldContain($"parent:{groupChild}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await kit.DisposeAsync();
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
