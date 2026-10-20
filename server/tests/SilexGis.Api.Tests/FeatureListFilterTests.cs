// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
/// The cross-kind feature list reading one filter document — the list, its count, and the feature
/// export in every format narrowed by the same conditions through the same seam.
/// </summary>
/// <remarks>
/// <para>
/// The reader is a Viewer throughout, because the seeded Editors group reads past visibility at the
/// widest scope and a test run only as an Editor would prove nothing about which rows a filter can
/// reach. The author is an Editor so the fixture can be built through the ordinary routes.
/// </para>
/// <para>
/// The fixture is a handful of sinkholes differing in one respect each from the ones the filter is
/// after (type, tag, visibility), so a condition dropped anywhere along the way lets exactly one of
/// them through and the assertion names which. One match has no position at all: the list shows it
/// and the export cannot, which is the one difference between the two the test allows.
/// </para>
/// </remarks>
public sealed class FeatureListFilterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double OpenLon = 25.41873;
    private const double OpenLat = 45.58219;
    private const double ProtectedLon = 25.51436;
    private const double ProtectedLat = 45.49127;

    private readonly SilexGisApiFactory factory;
    private string tag = null!;
    private HttpClient author = null!;
    private HttpClient reader = null!;
    private long sinkholeTypeId;
    private long fractureTypeId;

    public FeatureListFilterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"flf-author-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"flf-reader-{tag}@t.local");
        author = await AuthHelper.BearerClientAsync(factory, $"flf-author-{tag}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"flf-reader-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        fractureTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync();
    }

    // ---------- the list ----------

    [Fact]
    public async Task A_filter_narrows_the_feature_list_and_its_count_to_matching_rows_the_reader_may_see()
    {
        var seeded = await SeedAsync();

        var page = await ListAsync(reader, $"filter={Token(SinkholesTagged())}");

        // Exactly the three that match every condition and that a Viewer may read. The private one
        // matches too — the author's own answer below proves the filter is not what keeps it out —
        // and it is neither listed nor counted.
        Ids(page).ShouldBe(
            [seeded["match"], seeded["match-two"], seeded["no-position"]], ignoreOrder: true);
        page.GetProperty("totalItems").GetInt32().ShouldBe(3);

        var ownersPage = await ListAsync(author, $"filter={Token(SinkholesTagged())}");
        Ids(ownersPage).ShouldBe(
            [seeded["match"], seeded["match-two"], seeded["no-position"], seeded["private"]],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Pages_are_taken_from_the_narrowed_set_and_the_total_does_not_move_between_them()
    {
        var seeded = await SeedAsync();
        var token = Token(SinkholesTagged());

        var pages = new List<Guid>();
        for (var number = 1; number <= 3; number++)
        {
            var page = await ListAsync(reader, $"filter={token}&page={number}&pageSize=1");
            page.GetProperty("totalItems").GetInt32().ShouldBe(3);
            pages.AddRange(Ids(page));
        }

        pages.ShouldBe([seeded["match"], seeded["match-two"], seeded["no-position"]], ignoreOrder: true);
        pages.Distinct().Count().ShouldBe(3, "a row seen on two pages means another was skipped");
    }

    [Fact]
    public async Task A_saved_filter_runs_on_the_list_and_the_export_by_its_id_under_the_runners_own_access()
    {
        var seeded = await SeedAsync();
        var saved = await author.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Tagged sinkholes {tag}",
            document = SinkholesTagged(),
            visibility = "private",
        }, FilterDocument.SerializerOptions);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Ids(await ListAsync(author, $"savedFilterId={id}")).ShouldBe(
            [seeded["match"], seeded["match-two"], seeded["no-position"], seeded["private"]],
            ignoreOrder: true);
        (await ExportedNamesAsync(author, $"savedFilterId={id}")).ShouldBe(
            [Name("match"), Name("match-two"), Name("private")], ignoreOrder: true);

        // Private to its author, so to anybody else there is no such filter — and the refusal is the
        // one an invented id gets.
        var others = await reader.GetAsync($"/api/v1/features?savedFilterId={id}");
        others.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await others.Content.ReadAsStringAsync()).ShouldContain("saved_filter.not_found");
    }

    [Fact]
    public async Task The_list_and_its_export_are_refused_to_a_caller_who_is_not_signed_in()
    {
        using var anonymous = factory.CreateClient();
        var token = Token(SinkholesTagged());

        (await anonymous.GetAsync($"/api/v1/features?filter={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/export/features?format=geojson&filter={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- the export agrees with the list ----------

    [Fact]
    public async Task The_export_holds_exactly_the_rows_the_list_shows_that_have_a_position()
    {
        var seeded = await SeedAsync();
        var token = Token(SinkholesTagged());
        var listed = Ids(await ListAsync(reader, $"filter={token}"));

        // The file carries no ids, so rows are matched by name — unique per row and per test run, so
        // the same name is the same row. The only listed row allowed to be missing is the one with
        // no position, which a vector row cannot carry.
        var namesById = seeded.ToDictionary(p => p.Value, p => Name(p.Key));
        var expected = listed.Where(id => id != seeded["no-position"]).Select(id => namesById[id]).ToList();
        expected.Count.ShouldBe(2);

        (await ExportedNamesAsync(reader, $"filter={token}")).ShouldBe(expected, ignoreOrder: true);

        // Every format reads the same narrowing; the text formats are checked by what they name.
        foreach (var format in new[] { "csv", "kml", "gpx" })
        {
            var response = await reader.GetAsync($"/api/v1/export/features?format={format}&filter={token}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, format);
            var text = await response.Content.ReadAsStringAsync();
            foreach (var name in expected)
            {
                text.ShouldContain(name, customMessage: format);
            }

            text.ShouldNotContain(Name("other-type"), customMessage: format);
            text.ShouldNotContain(Name("untagged"), customMessage: format);
            text.ShouldNotContain(Name("private"), customMessage: format);
        }

        var shapefile = await reader.GetAsync($"/api/v1/export/features?format=shapefile&filter={token}");
        shapefile.StatusCode.ShouldBe(HttpStatusCode.OK);
        shapefile.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
    }

    [Fact]
    public async Task The_list_and_the_export_refuse_a_filter_they_cannot_read_or_find_rather_than_answering_everything()
    {
        await SeedAsync();
        var cutShort = "eyJ2ZXJzaW9uIjox";
        var holed = Token(new FilterDocument { Scope = [new WorldScope("feature", new AllOfNode(null!))] });
        var unknownField = Token(new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new ConditionNode("altitude", FilterOp.GreaterThan, [new NumberValue(0)])),
            ],
        });
        var aboutTrips = Token(new FilterDocument { Scope = [new WorldScope("tripLog", null)] });

        foreach (var route in new[] { "/api/v1/features?pageSize=100", "/api/v1/export/features?format=geojson" })
        {
            foreach (var token in new[] { cutShort, holed })
            {
                await ShouldRefuseAsync(
                    await reader.GetAsync($"{route}&filter={token}"), HttpStatusCode.BadRequest, "filter.malformed");
            }

            await ShouldRefuseAsync(
                await reader.GetAsync($"{route}&filter={unknownField}"), HttpStatusCode.BadRequest, "filter.invalid");
            await ShouldRefuseAsync(
                await reader.GetAsync($"{route}&filter={aboutTrips}"), HttpStatusCode.BadRequest, "filter.wrong_world");
            await ShouldRefuseAsync(
                await reader.GetAsync($"{route}&filter={Token(SinkholesTagged())}&savedFilterId={Guid.NewGuid()}"),
                HttpStatusCode.BadRequest, "filter.ambiguous");
            await ShouldRefuseAsync(
                await reader.GetAsync($"{route}&savedFilterId={Guid.NewGuid()}"),
                HttpStatusCode.NotFound, "saved_filter.not_found");
        }
    }

    /// <summary>
    /// A refusal, and nothing of the list in it: a page or a file of everything answered for a filter
    /// that could not be read is the failure these refusals exist to prevent.
    /// </summary>
    private async Task ShouldRefuseAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
        body.ShouldNotContain(Name("match"));
    }

    // ---------- location protection ----------

    [Fact]
    public async Task A_protected_feature_the_filter_finds_is_exported_on_the_grid_never_at_its_position()
    {
        await SeedAsync();
        var protectedName = Name("protected");
        // At a surveyed point of its own, so "the file does not contain this number" cannot be failed
        // by an open row entitled to publish the same coordinates.
        var protectedId = await CreateAsync(
            "protected", sinkholeTypeId, tagged: true, locationProtected: true,
            geometry: Point(ProtectedLon, ProtectedLat));

        var token = Token(SinkholesTagged());

        // Listed and counted like any other row the reader may read, with an approximate position.
        var page = await ListAsync(reader, $"filter={token}");
        var row = page.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == protectedId);
        row.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var response = await reader.GetAsync($"/api/v1/export/features?format=geojson&filter={token}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var raw = await response.Content.ReadAsStringAsync();
        var file = JsonDocument.Parse(raw).RootElement;
        var features = file.GetProperty("features").EnumerateArray().ToList();

        var snapped = features.Single(f => f.GetProperty("properties").GetProperty("name").GetString() == protectedName);
        var coordinates = snapped.GetProperty("geometry").GetProperty("coordinates");
        snapped.GetProperty("properties").GetProperty("approximate").GetString().ShouldBe("yes");
        Math.Abs(coordinates[0].GetDouble() - ProtectedLon).ShouldBeGreaterThan(1e-6);
        Math.Abs(coordinates[1].GetDouble() - ProtectedLat).ShouldBeGreaterThan(1e-6);
        raw.ShouldNotContain(ProtectedLon.ToString(CultureInfo.InvariantCulture));
        raw.ShouldNotContain(ProtectedLat.ToString(CultureInfo.InvariantCulture));

        // The positive twin in the same file: a row nobody protects is written where it really is,
        // so the snapping above is about protection and not about how the file writes points.
        var open = features.Single(f => f.GetProperty("properties").GetProperty("name").GetString() == Name("match"));
        var openCoordinates = open.GetProperty("geometry").GetProperty("coordinates");
        openCoordinates[0].GetDouble().ShouldBe(OpenLon, 1e-9);
        openCoordinates[1].GetDouble().ShouldBe(OpenLat, 1e-9);
    }

    [Fact]
    public async Task A_protected_line_the_filter_finds_is_listed_but_left_out_of_the_readers_file()
    {
        await SeedAsync();
        var lineName = Name("protected-line");
        await CreateAsync("protected-line", fractureTypeId, tagged: true, locationProtected: true, geometry: Line());

        // Every tagged generic row of this run, whatever its type.
        var token = Token(new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode(
                [
                    new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("generic")]),
                    new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue(TagSlug)]),
                    new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
                ])),
            ],
        });

        // A line or a polygon gives its course away however coarse the grid, so a reader without
        // exact view never gets it in a file. The unprotected line of the same type in the same
        // answer is the positive case: lines are exported, this one is held back for its protection.
        var exported = await ExportedNamesAsync(reader, $"filter={token}");
        exported.ShouldContain(Name("other-type"));
        exported.ShouldNotContain(lineName);
        var csv = await reader.GetAsync($"/api/v1/export/features?format=csv&filter={token}");
        (await csv.Content.ReadAsStringAsync()).ShouldNotContain(lineName);
    }

    // ---------- fixture ----------

    /// <summary>Tagged sinkholes of this run: kind, type, tag and name — the list's controls.</summary>
    private FilterDocument SinkholesTagged() => new()
    {
        Scope =
        [
            new WorldScope("feature", new AllOfNode(
            [
                new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("generic")]),
                new ConditionNode(
                    FeatureFilterFields.TypeId, FilterOp.Equals,
                    [new IdValue(sinkholeTypeId.ToString(CultureInfo.InvariantCulture))]),
                new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue(TagSlug)]),
                // Scoped to this class's rows; the suite shares one database.
                new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
            ])),
        ],
    };

    private string TagSlug => $"flf-{tag}";

    private string Name(string what) => $"FLF {what} {tag}";

    private async Task<Dictionary<string, Guid>> SeedAsync() => new()
    {
        ["match"] = await CreateAsync("match", sinkholeTypeId, tagged: true),
        ["match-two"] = await CreateAsync("match-two", sinkholeTypeId, tagged: true),
        ["no-position"] = await CreateAsync("no-position", sinkholeTypeId, tagged: true, withoutGeometry: true),
        ["other-type"] = await CreateAsync("other-type", fractureTypeId, tagged: true, geometry: Line()),
        ["untagged"] = await CreateAsync("untagged", sinkholeTypeId, tagged: false),
        ["private"] = await CreateAsync("private", sinkholeTypeId, tagged: true, visibility: "private"),
    };

    private async Task<Guid> CreateAsync(
        string what,
        long featureTypeId,
        bool tagged,
        string visibility = "authenticated",
        bool locationProtected = false,
        object? geometry = null,
        bool withoutGeometry = false)
    {
        var created = await author.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Name(what),
            featureTypeId,
            geometry = withoutGeometry ? null : geometry ?? Point(OpenLon, OpenLat),
            visibility,
            locationProtected,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

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

        return id;
    }

    private static object Point(double lon, double lat) => new { type = "Point", coordinates = new[] { lon, lat } };

    private static object Line() => new
    {
        type = "LineString",
        coordinates = new[] { new[] { 25.44, 45.52 }, new[] { 25.46, 45.54 } },
    };

    private static string Token(FilterDocument document) => FilterToken.Encode(document);

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        var paged = query.Contains("pageSize=", StringComparison.Ordinal) ? query : $"pageSize=100&{query}";
        var response = await client.GetAsync($"/api/v1/features?{paged}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<string?>> ExportedNamesAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/export/features?format=geojson&{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var file = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return
        [
            .. file.GetProperty("features").EnumerateArray()
                .Select(f => f.GetProperty("properties").GetProperty("name").GetString()),
        ];
    }

    private static List<Guid> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
