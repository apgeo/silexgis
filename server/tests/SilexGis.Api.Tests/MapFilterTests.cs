// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The map's two data layers reading one filter document — the entrance layer in both of its
/// branches (clusters and points) and the feature layer — through the same seam the cave registry
/// uses, after visibility and after the window has decided membership.
/// </summary>
/// <remarks>
/// <para>
/// The runner of a filter is a Viewer, because the seeded Editors group reads past visibility at the
/// widest scope and a filter run only by an Editor would prove nothing about which rows it can
/// reach. The author is an Editor so the fixture can be built through the ordinary routes; both are
/// members of one club, so a filter the author shares with the club can be run by the clubmate.
/// </para>
/// <para>
/// Every filter here names this class's run (a name condition), because the suite shares one
/// database and the cluster branch counts every entrance in its window.
/// </para>
/// </remarks>
public sealed class MapFilterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The shipped location-protection grid.</summary>
    private const double GridMeters = 5000d;

    /// <summary>Where this class's unprotected fixture stands, away from every other class's.</summary>
    private const double BaseLon = 25.9137;
    private const double BaseLat = 44.6281;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The wire's own conventions, so a document is written here the way a browser writes it.</summary>
    private static readonly JsonSerializerOptions Wire = FilterDocument.SerializerOptions;

    private readonly SilexGisApiFactory factory;
    private string tag = null!;
    private Guid cavingGroupId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private HttpClient author = null!;
    private HttpClient clubmate = null!;
    private HttpClient outsider = null!;

    public MapFilterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        var authorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mf-author-{tag}@t.local");
        var clubmateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mf-club-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mf-out-{tag}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Map Filter Club {tag}", Slug = $"map-filter-club-{tag}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, authorId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);

            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        author = await AuthHelper.BearerClientAsync(factory, $"mf-author-{tag}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"mf-club-{tag}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"mf-out-{tag}@t.local");
    }

    // ---------- the entrance layer ----------

    /// <summary>
    /// A filter saved from the cave registry says "caves"; the entrance layer draws a cave by its
    /// entrances, so the caves the filter keeps keep their entrances — in the points branch and in
    /// the clusters' counts alike, and only among the caves the runner may see.
    /// </summary>
    [Theory]
    [InlineData(14)]
    [InlineData(6)]
    public async Task A_cave_filter_keeps_the_entrances_of_the_caves_it_selects_that_the_runner_may_see(int zoom)
    {
        await SeedCavesAsync();
        var token = Token(BihorCaves());

        // The clubmate sees the matching cave and not the private one, which matches too: the
        // author's own answer below is what proves the filter is not what keeps it out.
        (await CountAsync(clubmate, FixtureWindow, zoom, $"&filter={token}")).ShouldBe(1);
        (await CountAsync(author, FixtureWindow, zoom, $"&filter={token}")).ShouldBe(2);

        // Unfiltered, the clubmate sees the cave elsewhere as well: the filter is what removed it.
        (await CountAsync(clubmate, FixtureWindow, zoom, string.Empty)).ShouldBeGreaterThanOrEqualTo(2);

        if (zoom >= 12)
        {
            var points = await EntrancesAsync(clubmate, FixtureWindow, zoom, $"&filter={token}");
            points["features"]!.AsArray()
                .Select(f => f!["properties"]!["caveName"]!.GetValue<string>())
                .ShouldBe([Name("match")]);
        }
    }

    [Fact]
    public async Task A_filter_naming_entrances_themselves_keeps_those_entrances()
    {
        await SeedCavesAsync();

        // A cave whose name says nothing of this run, with an entrance whose own name does.
        await CreateCaveAsync(
            $"MF lone {Guid.NewGuid():N}"[..30], "Alba", BaseLon, BaseLat + 0.003, entranceName: $"Gura {tag}");
        var entranceKind = new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode(
                [
                    new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("caveEntrance")]),
                    new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
                ])),
            ],
        };

        // The entrance matches by its own row; the readable caves named for this run bring their
        // (unnamed) entrances in through the cave arm, and the lone cave's entrance does not come
        // in through that arm, its cave not matching.
        var own = await EntrancesAsync(clubmate, FixtureWindow, 14, $"&filter={Token(entranceKind)}");
        own["features"]!.AsArray().Select(f => f!["properties"]!["name"]!.GetValue<string>())
            .ShouldBe([$"Gura {tag}"]);
        (await CountAsync(clubmate, FixtureWindow, 6, $"&filter={Token(entranceKind)}")).ShouldBe(1);
        (await CountAsync(clubmate, FixtureWindow, 14, $"&filter={Token(NamedCaves(tag))}")).ShouldBe(2);
    }

    [Fact]
    public async Task A_saved_filter_shared_with_a_club_runs_under_each_runners_own_access()
    {
        await SeedCavesAsync();
        var id = await SaveAsync(BihorCaves(), shareWithClub: true);

        foreach (var zoom in new[] { 6, 14 })
        {
            (await CountAsync(author, FixtureWindow, zoom, $"&savedFilterId={id}")).ShouldBe(2);
            (await CountAsync(clubmate, FixtureWindow, zoom, $"&savedFilterId={id}")).ShouldBe(1);
        }

        var features = await FeaturesAsync(clubmate, FixtureWindow, $"&kinds=caveEntrance&savedFilterId={id}");
        features["features"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_saved_filter_the_caller_cannot_read_is_refused_never_run_unfiltered()
    {
        await SeedCavesAsync();
        var privateId = await SaveAsync(BihorCaves(), shareWithClub: false);
        var clubId = await SaveAsync(BihorCaves(), shareWithClub: true);

        // The positive case: the clubmate runs the club's filter.
        (await CountAsync(clubmate, FixtureWindow, 14, $"&savedFilterId={clubId}")).ShouldBe(1);

        // Not shared with them: no such filter, the same answer an invented id gets, on every route
        // and in both branches.
        foreach (var (client, id) in new[] { (clubmate, privateId), (outsider, clubId), (clubmate, Guid.NewGuid()) })
        {
            foreach (var zoom in new[] { 6, 14 })
            {
                var response = await client.GetAsync(
                    $"/api/v1/map/cave-entrances?bbox={FixtureWindow}&zoom={zoom}&savedFilterId={id}");
                await ShouldRefuseAsync(response, HttpStatusCode.NotFound, "saved_filter.not_found");
            }

            await ShouldRefuseAsync(
                await client.GetAsync($"/api/v1/map/features?bbox={FixtureWindow}&savedFilterId={id}"),
                HttpStatusCode.NotFound,
                "saved_filter.not_found");
        }
    }

    [Fact]
    public async Task A_filter_that_cannot_be_read_is_refused_on_both_layers()
    {
        foreach (var path in new[]
        {
            $"/api/v1/map/cave-entrances?bbox={FixtureWindow}&zoom=6",
            $"/api/v1/map/cave-entrances?bbox={FixtureWindow}&zoom=14",
            $"/api/v1/map/features?bbox={FixtureWindow}",
        })
        {
            await ShouldRefuseAsync(
                await clubmate.GetAsync($"{path}&filter=eyJ2ZXJzaW9uIjox"),
                HttpStatusCode.BadRequest,
                "filter.malformed");
            await ShouldRefuseAsync(
                await clubmate.GetAsync($"{path}&filter={Token(BihorCaves())}&savedFilterId={Guid.NewGuid()}"),
                HttpStatusCode.BadRequest,
                "filter.ambiguous");
            await ShouldRefuseAsync(
                await clubmate.GetAsync($"{path}&filter={Token(new FilterDocument { Scope = [new WorldScope("tripLog", null)] })}"),
                HttpStatusCode.BadRequest,
                "filter.wrong_world");
        }
    }

    [Fact]
    public async Task An_anonymous_caller_is_turned_away_with_or_without_a_filter()
    {
        using var anonymous = factory.CreateClient();
        var token = Token(BihorCaves());

        (await anonymous.GetAsync($"/api/v1/map/cave-entrances?bbox={FixtureWindow}&zoom=6&filter={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/map/cave-entrances?bbox={FixtureWindow}&zoom=14&savedFilterId={Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/map/features?bbox={FixtureWindow}&filter={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- the feature layer ----------

    [Fact]
    public async Task The_feature_layer_keeps_the_features_a_filter_selects()
    {
        var kept = await CreateAreaAsync(Name("area kept"), BaseLon + 0.01, BaseLat + 0.01);
        var dropped = await CreateAreaAsync($"MF other area {Guid.NewGuid():N}"[..30], BaseLon + 0.02, BaseLat + 0.01);
        var named = new FilterDocument
        {
            Scope = [new WorldScope("feature", new ConditionNode(
                FeatureFilterFields.Name, FilterOp.Contains, [new TextValue($"area kept {tag}")]))],
        };

        var unfiltered = Ids(await FeaturesAsync(clubmate, FixtureWindow, string.Empty));
        unfiltered.ShouldContain(kept);
        unfiltered.ShouldContain(dropped);

        Ids(await FeaturesAsync(clubmate, FixtureWindow, $"&filter={Token(named)}")).ShouldBe([kept]);

        // A cave filter on the feature layer keeps no generic feature — it is about caves — and,
        // asked for entrances, keeps the entrances of the caves it selects.
        await SeedCavesAsync();
        Ids(await FeaturesAsync(clubmate, FixtureWindow, $"&filter={Token(BihorCaves())}")).ShouldBeEmpty();
        Ids(await FeaturesAsync(clubmate, FixtureWindow, $"&kinds=caveEntrance&filter={Token(BihorCaves())}"))
            .Count.ShouldBe(1);
    }

    // ---------- the window still decides on the shown position ----------

    /// <summary>
    /// The position-oracle probe with a filter applied: two windows whose moving edge sits either
    /// side of a protected entrance's true position, inside one protection cell. For the caller
    /// shown the snapped point the two answers are the same; for the owner, who places it exactly,
    /// they differ. The filter is what makes the count this one cave's alone, which is the setting
    /// in which a probe would be sharpest.
    /// </summary>
    [Theory]
    [InlineData(6, 26.5, 45.7)]
    [InlineData(14, 27.5, 46.3)]
    public async Task With_a_filter_on_a_window_edge_crossing_a_protected_entrance_inside_one_cell_changes_nothing(
        int zoom, double nearLon, double nearLat)
    {
        var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
        var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
        var lon = snappedLon - (0.3 * Cell);
        var lat = snappedLat + (0.1 * Cell);
        var step = 0.05 * Cell;

        var name = Name($"guarded {zoom}");
        await CreateCaveAsync(name, "Bihor", lon, lat, locationProtected: true);
        var filter = $"&filter={Token(NamedCaves(name))}";

        var westInside = Window(lon - step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var westOutside = Window(lon + step, snappedLat - 0.4, snappedLon + 0.4, snappedLat + 0.4);
        var eastInside = Window(snappedLon - 0.4, snappedLat - 0.4, lon + step, snappedLat + 0.4);
        var eastOutside = Window(snappedLon - 0.4, snappedLat - 0.4, lon - step, snappedLat + 0.4);

        var clubWestInside = await CountAsync(clubmate, westInside, zoom, filter);
        var clubWestOutside = await CountAsync(clubmate, westOutside, zoom, filter);
        clubWestInside.ShouldBe(clubWestOutside, "the west edge crossed the true position and the answer moved");
        clubWestInside.ShouldBe(1, "the snapped point is inside both windows");

        var clubEastInside = await CountAsync(clubmate, eastInside, zoom, filter);
        var clubEastOutside = await CountAsync(clubmate, eastOutside, zoom, filter);
        clubEastInside.ShouldBe(clubEastOutside, "the east edge crossed the true position and the answer moved");
        clubEastInside.ShouldBe(0, "the snapped point is outside both windows");

        // The control: the author places the entrance exactly, so for them the window is exact.
        (await CountAsync(author, westInside, zoom, filter)).ShouldBe(1);
        (await CountAsync(author, westOutside, zoom, filter)).ShouldBe(0);
        (await CountAsync(author, eastInside, zoom, filter)).ShouldBe(1);
        (await CountAsync(author, eastOutside, zoom, filter)).ShouldBe(0);
    }

    // ---------- fixture ----------

    private static string FixtureWindow => Window(BaseLon - 0.1, BaseLat - 0.1, BaseLon + 0.1, BaseLat + 0.1);

    private static string Window(double west, double south, double east, double north) =>
        string.Join(',', new[] { west, south, east, north }.Select(v => v.ToString("R", Invariant)));

    private string Name(string what) => $"MF {what} {tag}";

    /// <summary>The cave registry's spelling: caves in Bihor, scoped to this run by name.</summary>
    private FilterDocument BihorCaves() => new()
    {
        Scope =
        [
            new WorldScope("feature", new AllOfNode(
            [
                new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
                new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, [new TextValue("bihor")]),
                new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
            ])),
        ],
    };

    private static FilterDocument NamedCaves(string text) => new()
    {
        Scope =
        [
            new WorldScope("feature", new AllOfNode(
            [
                new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
                new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(text)]),
            ])),
        ],
    };

    private async Task SeedCavesAsync()
    {
        await CreateCaveAsync(Name("match"), "Bihor Nord", BaseLon, BaseLat);
        await CreateCaveAsync(Name("elsewhere"), "Alba", BaseLon + 0.003, BaseLat);
        await CreateCaveAsync(Name("private"), "Munții Bihor", BaseLon - 0.003, BaseLat, visibility: "private");
    }

    private async Task CreateCaveAsync(
        string name,
        string region,
        double lon,
        double lat,
        bool locationProtected = false,
        string visibility = "authenticated",
        string? entranceName = null)
    {
        var created = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            region,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await author.PostAsJsonAsync($"/api/v1/caves/{id}/entrances", new
        {
            name = entranceName,
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateAreaAsync(string name, double lon, double lat)
    {
        var ring = new[]
        {
            new[] { lon, lat }, new[] { lon + 0.002, lat }, new[] { lon + 0.002, lat + 0.002 },
            new[] { lon, lat + 0.002 }, new[] { lon, lat },
        };
        var response = await author.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            visibility = "authenticated",
            locationProtected = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SaveAsync(FilterDocument document, bool shareWithClub)
    {
        var saved = await author.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Map filter {Guid.NewGuid():N}"[..30],
            document,
            cavingGroupId = shareWithClub ? cavingGroupId : (Guid?)null,
            visibility = shareWithClub ? "cavingGroup" : "private",
        }, Wire);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Token(FilterDocument document) => FilterToken.Encode(document);

    private static async Task<JsonObject> EntrancesAsync(HttpClient client, string bbox, int zoom, string extra)
    {
        var response = await client.GetAsync($"/api/v1/map/cave-entrances?bbox={bbox}&zoom={zoom}{extra}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsObject();
    }

    private static async Task<JsonObject> FeaturesAsync(HttpClient client, string bbox, string extra)
    {
        var response = await client.GetAsync($"/api/v1/map/features?bbox={bbox}{extra}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsObject();
    }

    private static List<Guid> Ids(JsonObject collection) =>
        [.. collection["features"]!.AsArray().Select(f => f!["properties"]!["id"]!.GetValue<Guid>())];

    /// <summary>Entrances in the window: a cluster contributes its count, a point one.</summary>
    private static async Task<int> CountAsync(HttpClient client, string bbox, int zoom, string extra)
    {
        var body = await EntrancesAsync(client, bbox, zoom, extra);
        return body["features"]!.AsArray().Sum(f =>
            f!["properties"]!["cluster"]?.GetValue<bool>() == true
                ? f["properties"]!["count"]!.GetValue<int>()
                : 1);
    }

    private static async Task ShouldRefuseAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        payload.ShouldContain(code);
    }

    public Task DisposeAsync()
    {
        author.Dispose();
        clubmate.Dispose();
        outsider.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
