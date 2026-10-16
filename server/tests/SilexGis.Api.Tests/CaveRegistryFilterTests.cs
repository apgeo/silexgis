// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The cave registry reading one filter document — the list, its count and its pages, the vector
/// export and the interchange export all narrowed by the same conditions through the same seam.
/// </summary>
/// <remarks>
/// <para>
/// The reader is a Viewer throughout, because the seeded Editors group reads past visibility at the
/// widest scope and a test run only as an Editor would prove nothing about which rows a filter can
/// reach. The author is an Editor so the fixture can be built through the ordinary routes.
/// </para>
/// <para>
/// The fixture is six caves differing in one respect each from the one the filter is after, so a
/// condition dropped anywhere along the way lets exactly one of them through and the assertion names
/// which.
/// </para>
/// </remarks>
public sealed class CaveRegistryFilterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double ExactLon = 22.61873;
    private const double ExactLat = 46.58219;
    private const double ProtectedLon = 22.71436;
    private const double ProtectedLat = 46.49127;

    private readonly SilexGisApiFactory factory;
    private string tag = null!;
    private HttpClient author = null!;
    private HttpClient reader = null!;

    public CaveRegistryFilterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"crf-author-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"crf-reader-{tag}@t.local");
        author = await AuthHelper.BearerClientAsync(factory, $"crf-author-{tag}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"crf-reader-{tag}@t.local");
    }

    // ---------- the list ----------

    [Fact]
    public async Task A_filter_narrows_the_list_and_its_count_to_matching_caves_the_reader_may_see()
    {
        await SeedAsync();

        var page = await ListAsync(reader, Token(RegionTagAndLength()));

        // Exactly the two that match every condition and that a Viewer may read. The private one
        // matches too — the author's own answer below proves the filter is not what keeps it out —
        // and it is neither listed nor counted: a total that included it would say there is a cave
        // here this reader is not shown.
        Names(page).ShouldBe([Name("match"), Name("at-the-minimum")], ignoreOrder: true);
        page.GetProperty("totalItems").GetInt32().ShouldBe(2);

        var ownersPage = await ListAsync(author, Token(RegionTagAndLength()));
        Names(ownersPage).ShouldBe(
            [Name("match"), Name("at-the-minimum"), Name("private")], ignoreOrder: true);
    }

    [Fact]
    public async Task Pages_are_taken_from_the_narrowed_set_and_the_total_does_not_move_between_them()
    {
        await SeedAsync();
        var token = Token(RegionTagAndLength());

        var first = await ListAsync(reader, token, "&page=1&pageSize=1&sort=name");
        var second = await ListAsync(reader, token, "&page=2&pageSize=1&sort=name");

        first.GetProperty("totalItems").GetInt32().ShouldBe(2);
        second.GetProperty("totalItems").GetInt32().ShouldBe(2);
        Names(first).Concat(Names(second))
            .ShouldBe([Name("at-the-minimum"), Name("match")]);
    }

    [Fact]
    public async Task A_link_that_cannot_be_read_is_refused_rather_than_answered_with_everything()
    {
        var response = await reader.GetAsync("/api/v1/caves?filter=eyJ2ZXJzaW9uIjox");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.malformed");
    }

    /// <summary>
    /// Links whose document parses but has a member left out, which the reader fills with null.
    /// Each used to reach the validator and fault there, so the answer was a server error.
    /// </summary>
    public static TheoryData<string> LinksWithAHole => new()
    {
        Token(new FilterDocument { Scope = [new WorldScope("feature", new AllOfNode(null!))] }),
        Token(new FilterDocument
        {
            Scope = [new WorldScope("feature", new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, null!))],
        }),
        Token(new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new ConditionNode(
                    FeatureFilterFields.Region, FilterOp.Contains, [new TextValue(null!)])),
            ],
        }),
    };

    [Theory]
    [MemberData(nameof(LinksWithAHole))]
    public async Task A_link_with_a_member_missing_is_refused_as_unreadable_rather_than_failing(string token)
    {
        var response = await reader.GetAsync($"/api/v1/caves?filter={token}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.malformed");
    }

    [Fact]
    public async Task A_filter_naming_a_position_or_an_altitude_is_refused_not_ignored()
    {
        // Neither is in the vocabulary, and a condition on one must not be dropped quietly: dropped,
        // it would answer a wider question than the one asked; obeyed, it would be a way to ask
        // where a protected cave is.
        foreach (var field in new[] { FeatureFilterFields.Position, "altitude", "geom" })
        {
            var document = new FilterDocument
            {
                Scope = [new WorldScope("feature", new ConditionNode(field, FilterOp.LessThan, [new NumberValue(800)]))],
            };

            var response = await reader.GetAsync($"/api/v1/caves?filter={Token(document)}");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await response.Content.ReadAsStringAsync()).ShouldContain("filter.invalid");
        }
    }

    [Fact]
    public async Task A_filter_about_another_kind_of_thing_is_refused_here()
    {
        var aboutTrips = new FilterDocument { Scope = [new WorldScope("tripLog", null)] };

        var response = await reader.GetAsync($"/api/v1/caves?filter={Token(aboutTrips)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.wrong_world");
    }

    [Fact]
    public async Task A_filter_and_a_saved_filter_together_are_refused_as_ambiguous()
    {
        var response = await reader.GetAsync(
            $"/api/v1/caves?filter={Token(RegionTagAndLength())}&savedFilterId={Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.ambiguous");
    }

    [Fact]
    public async Task A_saved_filter_runs_on_the_list_by_its_id_under_the_runners_own_access()
    {
        await SeedAsync();
        var saved = await author.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Bihor long ones {tag}",
            document = RegionTagAndLength(),
            visibility = "private",
        }, Wire);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ownersPage = await ListAsync(author, null, $"&pageSize=100&savedFilterId={id}");
        Names(ownersPage).ShouldBe(
            [Name("match"), Name("at-the-minimum"), Name("private")], ignoreOrder: true);

        // Private to its author, so to anybody else there is no such filter — and the refusal is
        // the one an invented id gets.
        var others = await reader.GetAsync($"/api/v1/caves?savedFilterId={id}");
        var invented = await reader.GetAsync($"/api/v1/caves?savedFilterId={Guid.NewGuid()}");
        others.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await others.Content.ReadAsStringAsync()).ShouldContain("saved_filter.not_found");
    }

    // ---------- the exports agree with the list ----------

    [Fact]
    public async Task The_vector_export_holds_exactly_the_caves_the_list_shows()
    {
        await SeedAsync();
        var token = Token(RegionTagAndLength());
        var listed = Names(await ListAsync(reader, token));

        // Every format reads the same narrowing; GeoJSON is the one whose rows are easy to name.
        var response = await reader.GetAsync($"/api/v1/export/caves?format=geojson&filter={token}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var file = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var exported = file.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("name").GetString())
            .ToList();

        exported.ShouldBe(listed, ignoreOrder: true);

        var csv = await reader.GetAsync($"/api/v1/export/caves?format=csv&filter={token}");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await csv.Content.ReadAsStringAsync();
        foreach (var name in listed)
        {
            text.ShouldContain(name!);
        }

        text.ShouldNotContain(Name("short"));
        text.ShouldNotContain(Name("elsewhere"));
        text.ShouldNotContain(Name("untagged"));
    }

    [Fact]
    public async Task The_interchange_export_and_its_preview_hold_exactly_the_caves_the_list_shows()
    {
        await SeedAsync();
        var token = Token(RegionTagAndLength());
        var listed = Names(await ListAsync(reader, token));

        var preview = await reader.PostAsJsonAsync("/api/v1/export/caves/karstlink/preview", new { filter = token });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("caveCount").GetInt32()
            .ShouldBe(listed.Count);

        var response = await reader.PostAsJsonAsync(
            "/api/v1/export/caves/karstlink", new { filter = token, treatmentForAll = "grid_position" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var exported = document.GetProperty("@graph").EnumerateArray()
            .Where(n => n.TryGetProperty("name", out _))
            .Select(n => n.GetProperty("name").GetString())
            .Where(n => n!.EndsWith(tag, StringComparison.Ordinal))
            .ToList();

        exported.ShouldBe(listed, ignoreOrder: true);
    }

    [Fact]
    public async Task Every_export_refuses_a_filter_it_cannot_read_or_find_rather_than_writing_the_whole_registry()
    {
        await SeedAsync();
        var cutShort = "eyJ2ZXJzaW9uIjox";
        var holed = Token(new FilterDocument { Scope = [new WorldScope("feature", new AllOfNode(null!))] });
        var unknown = Guid.NewGuid();

        foreach (var token in new[] { cutShort, holed })
        {
            await ShouldRefuseAsync(
                await reader.GetAsync($"/api/v1/export/caves?format=geojson&filter={token}"),
                HttpStatusCode.BadRequest, "filter.malformed");
            await ShouldRefuseAsync(
                await reader.PostAsJsonAsync("/api/v1/export/caves/karstlink/preview", new { filter = token }),
                HttpStatusCode.BadRequest, "filter.malformed");
            await ShouldRefuseAsync(
                await reader.PostAsJsonAsync(
                    "/api/v1/export/caves/karstlink", new { filter = token, treatmentForAll = "grid_position" }),
                HttpStatusCode.BadRequest, "filter.malformed");
        }

        await ShouldRefuseAsync(
            await reader.GetAsync($"/api/v1/export/caves?format=geojson&savedFilterId={unknown}"),
            HttpStatusCode.NotFound, "saved_filter.not_found");
        await ShouldRefuseAsync(
            await reader.PostAsJsonAsync("/api/v1/export/caves/karstlink/preview", new { savedFilterId = unknown }),
            HttpStatusCode.NotFound, "saved_filter.not_found");
        await ShouldRefuseAsync(
            await reader.PostAsJsonAsync(
                "/api/v1/export/caves/karstlink", new { savedFilterId = unknown, treatmentForAll = "grid_position" }),
            HttpStatusCode.NotFound, "saved_filter.not_found");
    }

    /// <summary>
    /// A refusal, and nothing of the registry in it: a file of everything answered for a filter that
    /// could not be read is the failure these refusals exist to prevent.
    /// </summary>
    private async Task ShouldRefuseAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
        body.ShouldNotContain(Name("match"));
    }

    [Fact]
    public async Task A_protected_cave_the_filter_finds_is_exported_on_the_grid_never_at_its_position()
    {
        await SeedAsync();
        var protectedName = Name("protected");
        // At a surveyed point of its own, so "the file does not contain this number" cannot be
        // failed by an open cave entitled to publish the same coordinates.
        await CreateCaveAsync(
            protectedName, "Bihor de jos", 800, tagged: true, locationProtected: true,
            lon: ProtectedLon, lat: ProtectedLat);

        var token = Token(RegionTagAndLength());

        // Listed and counted like any other cave the reader may read, with an approximate position.
        var page = await ListAsync(reader, token);
        var row = page.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("name").GetString() == protectedName);
        row.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var response = await reader.GetAsync($"/api/v1/export/caves?format=geojson&filter={token}");
        var file = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var feature = file.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("properties").GetProperty("name").GetString() == protectedName);
        var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");

        feature.GetProperty("properties").GetProperty("approximate").GetString().ShouldBe("yes");
        Math.Abs(coordinates[0].GetDouble() - ProtectedLon).ShouldBeGreaterThan(1e-6);
        Math.Abs(coordinates[1].GetDouble() - ProtectedLat).ShouldBeGreaterThan(1e-6);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(
            ProtectedLon.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_box_on_the_list_follows_the_published_point_of_a_protected_cave_never_its_surveyed_one()
    {
        var name = Name("boxed");
        await CreateCaveAsync(name, "Bihor", 800, tagged: false, locationProtected: true);
        double grid;
        using (var scope = factory.Services.CreateScope())
        {
            grid = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<SilexGis.Api.Common.AccessOptions>>()
                .Value.LocationGridMeters;
        }

        var exact = new NetTopologySuite.Geometries.Point(ExactLon, ExactLat) { SRID = 4326 };
        var published = SilexGis.Domain.Geo.LocationProtection.Snap(exact, grid);
        published.Distance(exact).ShouldBeGreaterThan(0.002, "the fixture needs the two points apart");

        // A tiny box around the surveyed point and nowhere near the published one. Answered from the
        // surveyed point, the count would say the cave is in it — and moving the edges would walk
        // the cave onto the map. Answered from the published point, it is not there.
        var aroundTheSurvey = Box(ExactLon, ExactLat);
        var aroundThePublished = Box(published.X, published.Y);

        (await CountAsync(reader, name, aroundTheSurvey)).ShouldBe(0);
        (await CountAsync(reader, name, aroundThePublished)).ShouldBe(1);

        // A cave nobody protects is narrowed by where it really is, which is what makes the two
        // answers above about protection rather than about a box that matches nothing.
        var open = Name("boxed-open");
        await CreateCaveAsync(open, "Bihor", 800, tagged: false);
        (await CountAsync(reader, open, aroundTheSurvey)).ShouldBe(1);
    }

    private static string Box(double lon, double lat) =>
        FormattableString.Invariant($"{lon - 0.0005},{lat - 0.0005},{lon + 0.0005},{lat + 0.0005}");

    private static async Task<int> CountAsync(HttpClient client, string name, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/caves?search={Uri.EscapeDataString(name)}&bbox={bbox}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalItems").GetInt32();
    }

    // ---------- fixture ----------

    /// <summary>
    /// "In Bihor, tagged, at least 300 m long" — the three narrowings the old export dropped — plus
    /// the cave kind, which a document saved from the registry carries so it means the same thing
    /// wherever it is run.
    /// </summary>
    private FilterDocument RegionTagAndLength() => new()
    {
        Scope =
        [
            new WorldScope("feature", new AllOfNode(
            [
                new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
                new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, [new TextValue("bihor")]),
                new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue(TagSlug)]),
                // "At least", spelled the way the registry spells it: no operator says it directly.
                new AnyOfNode(
                [
                    new ConditionNode(FeatureFilterFields.SurveyedLength, FilterOp.GreaterThan, [new NumberValue(300)]),
                    new ConditionNode(FeatureFilterFields.SurveyedLength, FilterOp.Equals, [new NumberValue(300)]),
                ]),
                // Scoped to this class's caves; the suite shares one database.
                new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
            ])),
        ],
    };

    private string TagSlug => $"kf-{tag}";

    private string Name(string what) => $"KF {what} {tag}";

    private async Task SeedAsync()
    {
        await CreateCaveAsync(Name("match"), "Bihor Nord", 500, tagged: true);
        await CreateCaveAsync(Name("at-the-minimum"), "Munții Bihor", 300, tagged: true);
        await CreateCaveAsync(Name("short"), "Bihor", 50, tagged: true);
        await CreateCaveAsync(Name("elsewhere"), "Alba", 900, tagged: true);
        await CreateCaveAsync(Name("untagged"), "Bihor", 900, tagged: false);
        await CreateCaveAsync(Name("private"), "Bihor", 700, tagged: true, visibility: "private");
    }

    private async Task CreateCaveAsync(
        string name,
        string region,
        decimal length,
        bool tagged,
        string visibility = "authenticated",
        bool locationProtected = false,
        double lon = ExactLon,
        double lat = ExactLat)
    {
        long caveTypeId;
        long entranceTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        var created = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            region,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            surveyedLength = length,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await author.PostAsJsonAsync($"/api/v1/caves/{id}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());

        if (tagged)
        {
            var tagging = await author.PostAsJsonAsync("/api/v1/taggings", new
            {
                tagName = TagSlug,
                entityType = "feature",
                entityId = id,
            });
            tagging.IsSuccessStatusCode.ShouldBeTrue(await tagging.Content.ReadAsStringAsync());
        }
    }

    private static string Token(FilterDocument document) => FilterToken.Encode(document);

    private static async Task<JsonElement> ListAsync(
        HttpClient client, string? token, string extra = "&pageSize=100")
    {
        var query = token is null ? extra.TrimStart('&') : $"filter={token}{extra}";
        var response = await client.GetAsync($"/api/v1/caves?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())];

    /// <summary>The wire's own conventions, so a document is written here the way a browser writes it.</summary>
    private static readonly JsonSerializerOptions Wire = FilterDocument.SerializerOptions;

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
