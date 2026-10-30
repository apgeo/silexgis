// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using NPOI.XSSF.Extractor;
using NPOI.XSSF.UserModel;
using Shouldly;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Position conditions and cave conditions on the worlds other than features — photographs, survey
/// models, trips, georeferenced maps and geodata files — each decided on what its reader is shown.
/// </summary>
/// <remarks>
/// <para>
/// The attacks are the feature world's: a radius or an outline moved across a protected cave's
/// true position, a negation, and a refusal compared with the refusal for something that does not
/// exist. The owner, who places their own protected caves exactly, is the control in each test; the
/// viewer reads the protected caves but holds no grant to place them.
/// </para>
/// <para>
/// Each test works in a spot of its own, in a stretch of the globe no other filter test uses.
/// </para>
/// </remarks>
public sealed class FilterKindPositionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double GridMeters = 5000d;

    private static readonly JsonSerializerOptions Wire = BuildWire();

    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid ownerId;
    private long caveTypeId;
    private long entranceTypeId;
    private string tag = null!;

    public FilterKindPositionTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    private static double Cell => LocationProtection.CellDegrees(GridMeters);

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"kp-own-{tag}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"kp-view-{tag}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"kp-own-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"kp-view-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    // ---------- photographs: the cave condition ----------

    [Fact]
    public async Task Pictures_of_a_cave_the_reader_may_not_place_are_refused_in_the_words_of_a_missing_cave()
    {
        var spot = Spot.Pick();
        var (guarded, _) = await CaveWithEntranceAsync("Guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (open, _) = await CaveWithEntranceAsync("Open", spot.TrueLon + (2 * Cell), spot.TrueLat, locationProtected: false);
        var ofGuarded = await PhotographAsync("guarded", fix: null, hangsOn: guarded);
        var ofOpen = await PhotographAsync("open", fix: null, hangsOn: open);

        // The owner places the guarded cave exactly: the condition answers for them.
        (await IdsAsync(owner, Photographs(CaveIs(guarded)))).ShouldBe([ofGuarded]);
        (await IdsAsync(owner, Photographs(CaveIn(guarded, open)))).ShouldBe([ofGuarded, ofOpen], ignoreOrder: true);

        // The viewer reads the picture and the cave, and may not place the cave.
        (await IdsAsync(viewer, Photographs(Titled()))).ShouldContain(ofGuarded, "the picture itself is the viewer's to see");
        (await IdsAsync(viewer, Photographs(CaveIs(open)))).ShouldBe([ofOpen]);

        // Asked of the guarded cave, the whole filter is refused, exactly as for a cave that does
        // not exist — negated or alongside a cave the viewer may place, too.
        var missing = await RefusalAsync(viewer, Photographs(CaveIs(Guid.NewGuid())));
        missing.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        (await RefusalAsync(viewer, Photographs(CaveIs(guarded)))).ShouldBe(missing);
        (await RefusalAsync(viewer, Photographs(new NotNode(CaveIs(guarded))))).ShouldBe(missing);
        (await RefusalAsync(viewer, Photographs(CaveIn(open, guarded)))).ShouldBe(missing);

        // Something that is not a cave, or not an identifier at all, reads the same.
        (await RefusalAsync(viewer, Photographs(new ConditionNode(
            PhotographFilterFields.Cave, FilterOp.Equals, [new IdValue("not-an-id")])))).ShouldBe(missing);

        // Whether there is a cave at all is not a question the condition takes.
        var empty = await RefusalAsync(viewer, Photographs(new ConditionNode(PhotographFilterFields.Cave, FilterOp.IsEmpty, [])));
        empty.Code.ShouldBe("filter.invalid");
    }

    // ---------- photographs: the position condition ----------

    [Fact]
    public async Task A_picture_answers_a_position_condition_only_by_a_fix_its_reader_is_shown()
    {
        var spot = Spot.Pick();
        var (guarded, _) = await CaveWithEntranceAsync("Fixed guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var hidden = await PhotographAsync("hidden-fix", fix: (spot.TrueLon, spot.TrueLat), hangsOn: guarded);
        var loose = (spot.TrueLon + (3 * Cell), spot.TrueLat);
        var shown = await PhotographAsync("shown-fix", fix: loose, hangsOn: null);
        var unplaced = await PhotographAsync("no-fix", fix: null, hangsOn: null);

        var atHidden = new PointValue(spot.TrueLon, spot.TrueLat);
        var atShown = new PointValue(loose.Item1, loose.Item2);

        // The owner is shown the guarded picture's fix, so for them the radius is exact.
        (await IdsAsync(owner, Photographs(Titled(), Within(300, atHidden)))).ShouldBe([hidden]);

        // The viewer is not: at any radius around the fix, the picture answers as one with no fix.
        foreach (var radius in new[] { 50d, 300d, 3_000d })
        {
            var near = await IdsAsync(viewer, Photographs(Titled(), Within(radius, atHidden)));
            near.ShouldNotContain(hidden);
            var notNear = await IdsAsync(viewer, Photographs(Titled(), new NotNode(Within(radius, atHidden))));
            notNear.Contains(hidden).ShouldBe(notNear.Contains(unplaced), "a withheld fix and no fix answer alike");
            notNear.ShouldContain(hidden);
        }

        (await IdsAsync(viewer, Photographs(Titled(), Inside(Square(spot.TrueLon, spot.TrueLat, 0.5 * Cell))))).ShouldNotContain(hidden);

        // A picture hanging on nothing is shown its fix by everybody who reads it.
        (await IdsAsync(viewer, Photographs(Titled(), Within(300, atShown)))).ShouldBe([shown]);
        (await IdsAsync(viewer, Photographs(Titled(), Inside(Square(loose.Item1, loose.Item2, 0.01))))).ShouldBe([shown]);
    }

    // ---------- survey models ----------

    [Fact]
    public async Task A_survey_model_is_found_by_its_cave_and_by_where_its_cave_is()
    {
        var spot = Spot.Pick();
        var (open, _) = await CaveWithEntranceAsync("Surveyed open", spot.TrueLon, spot.TrueLat, locationProtected: false);
        var (guarded, _) = await CaveWithEntranceAsync("Surveyed guarded", spot.TrueLon + (2 * Cell), spot.TrueLat, locationProtected: true);
        var ofOpen = await SurveyModelAsync(open);
        var ofGuarded = await SurveyModelAsync(guarded);

        (await IdsAsync(viewer, Models(CaveIs(open)))).ShouldBe([ofOpen]);
        (await IdsAsync(owner, Models(CaveIs(guarded)))).ShouldBe([ofGuarded]);

        // A cave the viewer may not place has no models for them, exactly as one that does not exist.
        (await IdsAsync(viewer, Models(CaveIs(guarded)))).ShouldBeEmpty();
        (await IdsAsync(viewer, Models(CaveIs(Guid.NewGuid())))).ShouldBeEmpty();

        // Where the model is, is where its cave is shown to be.
        var here = new PointValue(spot.TrueLon, spot.TrueLat);
        (await IdsAsync(viewer, Models(Titled("surveyModel"), Within(200, here)))).ShouldBe([ofOpen]);
        (await IdsAsync(viewer, Models(Titled("surveyModel"), Within(200, new PointValue(spot.TrueLon + 1, spot.TrueLat))))).ShouldBeEmpty();
        (await IdsAsync(viewer, Models(Titled("surveyModel"), Inside(Square(spot.TrueLon, spot.TrueLat, 0.001))))).ShouldBe([ofOpen]);
        (await IdsAsync(owner, Models(Titled("surveyModel"), Within(200, new PointValue(spot.TrueLon + (2 * Cell), spot.TrueLat)))))
            .ShouldBe([ofGuarded]);
    }

    // ---------- trips ----------

    [Fact]
    public async Task A_trip_answers_a_position_condition_through_the_caves_its_list_shows_its_reader()
    {
        var spot = Spot.Pick();
        var (open, _) = await CaveWithEntranceAsync("Visited open", spot.TrueLon, spot.TrueLat, locationProtected: false);
        var guardedLon = spot.TrueLon + (2 * Cell);
        var (guarded, _) = await CaveWithEntranceAsync("Visited guarded", guardedLon, spot.TrueLat, locationProtected: true);
        var toOpen = await TripAsync("to open", open);
        var toGuarded = await TripAsync("to guarded", guarded);

        var atOpen = new PointValue(spot.TrueLon, spot.TrueLat);
        var atGuarded = new PointValue(guardedLon, spot.TrueLat);

        (await IdsAsync(viewer, Trips(Titled("tripLog"), Within(300, atOpen)))).ShouldBe([toOpen]);
        (await IdsAsync(owner, Trips(Titled("tripLog"), Within(300, atGuarded)))).ShouldBe([toGuarded]);

        // The viewer reads the trip but its list withholds the guarded cave, so nothing about that
        // cave's position — true or shown — makes the trip answer, and its negation does not move.
        (await IdsAsync(viewer, Trips(Titled("tripLog")))).ShouldContain(toGuarded);
        foreach (var radius in new[] { 300d, 3_000d, 20_000d })
        {
            (await IdsAsync(viewer, Trips(Titled("tripLog"), Within(radius, atGuarded)))).ShouldNotContain(toGuarded);
            (await IdsAsync(viewer, Trips(Titled("tripLog"), new NotNode(Within(radius, atGuarded))))).ShouldContain(toGuarded);
        }

        (await IdsAsync(viewer, Trips(Titled("tripLog"), Inside(Square(spot.TrueLon, spot.TrueLat, 0.01))))).ShouldBe([toOpen]);
    }

    // ---------- footprints ----------

    [Fact]
    public async Task A_map_and_a_geodata_file_answer_by_their_footprint()
    {
        var spot = Spot.Pick();
        var (map, file) = await FootprintsAsync(spot.TrueLon, spot.TrueLat, half: 0.01);
        var centre = new PointValue(spot.TrueLon, spot.TrueLat);
        var farAway = new PointValue(spot.TrueLon + 1, spot.TrueLat);

        foreach (var (world, id) in new[] { ("georeferencedMap", map), ("geofile", file) })
        {
            (await IdsAsync(viewer, About(world, Titled(world), Within(100, centre)))).ShouldBe([id]);
            (await IdsAsync(viewer, About(world, Titled(world), Within(100, farAway)))).ShouldBeEmpty();
            (await IdsAsync(viewer, About(world, Titled(world), Inside(Square(spot.TrueLon, spot.TrueLat, 0.02))))).ShouldBe([id]);
            // Inside means all of it: an outline over half the footprint does not hold it.
            (await IdsAsync(viewer, About(world, Titled(world), Inside(Square(spot.TrueLon + 0.01, spot.TrueLat, 0.01))))).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Every_world_with_a_position_measures_from_a_cave_through_the_one_placement_gate()
    {
        var spot = Spot.Pick();
        var (guarded, _) = await CaveWithEntranceAsync("Anchor guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (open, _) = await CaveWithEntranceAsync("Anchor open", spot.TrueLon + (3 * Cell), spot.TrueLat, locationProtected: false);
        var ofOpen = await SurveyModelAsync(open);

        var missing = await RefusalAsync(viewer, Models(Within(500, new ObjectValue("feature", Guid.NewGuid()))));
        missing.Code.ShouldBe(SpatialConditions.AnchorNotFoundCode);
        foreach (var world in new[] { "photograph", "surveyModel", "tripLog", "georeferencedMap", "geofile" })
        {
            (await RefusalAsync(viewer, About(world, Within(500, new ObjectValue("feature", guarded))))).ShouldBe(missing);
        }

        (await IdsAsync(viewer, Models(Titled("surveyModel"), Within(100, new ObjectValue("feature", open))))).ShouldBe([ofOpen]);
    }


    // ---------- exports on the filter ----------

    [Fact]
    public async Task The_exports_answer_an_anchor_on_a_withheld_cave_exactly_as_for_one_that_does_not_exist()
    {
        var spot = Spot.Pick();
        var (guarded, _) = await CaveWithEntranceAsync("Export anchor guarded", spot.TrueLon, spot.TrueLat, locationProtected: true);
        var (open, _) = await CaveWithEntranceAsync("Export anchor open", spot.TrueLon + (3 * Cell), spot.TrueLat, locationProtected: false);

        foreach (var path in new[] { "/api/v1/geofiles/export?format=geojson&", "/api/v1/trip-logs/export?" })
        {
            FilterDocument Doc(Guid anchor) => path.Contains("geofiles")
                ? About("geofile", Titled("geofile"), Within(500, new ObjectValue("feature", anchor)))
                : Trips(Titled("tripLog"), Within(500, new ObjectValue("feature", anchor)));

            async Task<(HttpStatusCode, string?)> AskAsync(HttpClient client, Guid anchor)
            {
                var response = await client.GetAsync($"{path}filter={FilterToken.Encode(Doc(anchor))}");
                var body = await response.Content.ReadAsStringAsync();
                return (response.StatusCode, response.StatusCode == HttpStatusCode.BadRequest
                    ? JsonNode.Parse(body)!["code"]?.GetValue<string>() : null);
            }

            var missing = await AskAsync(viewer, Guid.NewGuid());
            missing.Item1.ShouldBe(HttpStatusCode.BadRequest);
            missing.Item2.ShouldBe(SpatialConditions.AnchorNotFoundCode);
            (await AskAsync(viewer, guarded)).ShouldBe(missing);

            // The twins: the owner places the guarded cave, and the viewer places the open one.
            (await AskAsync(owner, guarded)).Item1.ShouldBe(HttpStatusCode.OK);
            (await AskAsync(viewer, open)).Item1.ShouldBe(HttpStatusCode.OK);
        }
    }


    [Fact]
    public async Task The_trip_export_decides_a_position_condition_through_the_caves_the_trip_list_shows_its_reader()
    {
        var spot = Spot.Pick();
        var (open, _) = await CaveWithEntranceAsync("Exported open", spot.TrueLon, spot.TrueLat, locationProtected: false);
        var guardedLon = spot.TrueLon + (2 * Cell);
        var (guarded, _) = await CaveWithEntranceAsync("Exported guarded", guardedLon, spot.TrueLat, locationProtected: true);
        await TripAsync("to open", open);
        await TripAsync("to guarded", guarded);

        var atOpen = new PointValue(spot.TrueLon, spot.TrueLat);
        var atGuarded = new PointValue(guardedLon, spot.TrueLat);
        var toOpen = $"Trip to open {tag}";
        var toGuarded = $"Trip to guarded {tag}";

        // The controls: the owner places the guarded cave exactly, and the viewer places the open one.
        (await TripSheetAsync(owner, Trips(Titled("tripLog"), Within(300, atGuarded)))).ShouldContain(toGuarded);
        var viewerAtOpen = await TripSheetAsync(viewer, Trips(Titled("tripLog"), Within(300, atOpen)));
        viewerAtOpen.ShouldContain(toOpen);
        viewerAtOpen.ShouldNotContain(toGuarded);

        // The viewer reads the guarded trip, so the file holds it without a condition; no radius
        // around the guarded cave's true position, small or large, makes it match, and the negation
        // does not move either.
        (await TripSheetAsync(viewer, Trips(Titled("tripLog")))).ShouldContain(toGuarded);
        foreach (var radius in new[] { 300d, 3_000d, 20_000d })
        {
            (await TripSheetAsync(viewer, Trips(Titled("tripLog"), Within(radius, atGuarded)))).ShouldNotContain(toGuarded);
            (await TripSheetAsync(viewer, Trips(Titled("tripLog"), new NotNode(Within(radius, atGuarded))))).ShouldContain(toGuarded);
        }
    }

    [Fact]
    public async Task The_geofile_list_export_holds_the_rows_of_the_files_the_filter_leaves_and_the_reader_may_read()
    {
        var spot = Spot.Pick();
        var (_, shown) = await FootprintsAsync(spot.TrueLon, spot.TrueLat, half: 0.01);
        Guid hidden;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var shownFile = await db.Geofiles.SingleAsync(g => g.Id == shown);
            var secret = GeofileWorldFixture.Add(db, $"Hidden geodata {tag}", ownerId, Visibility.Private);
            secret.Bbox = shownFile.Bbox;
            secret.FeatureCount = 1;
            shownFile.FeatureCount = 1;
            hidden = secret.Id;
            var geometries = new GeometryFactory(new PrecisionModel(), 4326);
            foreach (var (file, label) in new[] { (shown, "row-shown"), (hidden, "row-hidden") })
            {
                db.GeofileFeatures.Add(new GeofileFeature
                {
                    GeofileId = file,
                    Geom = geometries.CreatePoint(new Coordinate(spot.TrueLon, spot.TrueLat)),
                    Properties = $"{{\"label\":\"{label}\"}}",
                });
            }

            await db.SaveChangesAsync();
        }

        var centre = new PointValue(spot.TrueLon, spot.TrueLat);
        var far = new PointValue(spot.TrueLon + 1, spot.TrueLat);

        // The owner reads both files, so the viewer's smaller answer is the audience walk and not a
        // fixture that wrote one file.
        var ownerFile = await GeofileExportAsync(owner, About("geofile", Titled("geofile"), Within(100, centre)));
        ownerFile.ShouldContain("row-shown");
        ownerFile.ShouldContain("row-hidden");
        ownerFile.ShouldContain($"Hidden geodata {tag}");

        var viewerFile = await GeofileExportAsync(viewer, About("geofile", Titled("geofile"), Within(100, centre)));
        viewerFile.ShouldContain("row-shown");
        viewerFile.ShouldContain($"Geodata {tag}");
        viewerFile.ShouldNotContain("row-hidden");
        viewerFile.ShouldNotContain($"Hidden geodata {tag}");

        // A footprint nowhere near the question leaves an empty file, not an unfiltered one.
        (await GeofileExportAsync(owner, About("geofile", Titled("geofile"), Within(100, far)))).ShouldNotContain("row-shown");
    }

    [Fact]
    public async Task The_list_exports_refuse_a_filter_they_cannot_run_and_never_answer_it_as_everything()
    {
        var readable = FilterToken.Encode(About("geofile", Titled("geofile")));
        var aboutPhotographs = FilterToken.Encode(About("photograph", Titled()));
        var tripsReadable = FilterToken.Encode(Trips(Titled("tripLog")));

        var cases = new (string Path, string Query, HttpStatusCode Status, string Code)[]
        {
            ("/api/v1/geofiles/export?format=geojson", "filter=eyJ2ZXJzaW9uIjox", HttpStatusCode.BadRequest, SurfaceFilters.MalformedCode),
            ("/api/v1/geofiles/export?format=geojson", $"filter={aboutPhotographs}", HttpStatusCode.BadRequest, SurfaceFilters.WrongWorldCode),
            ("/api/v1/geofiles/export?format=geojson", $"filter={readable}&savedFilterId={Guid.NewGuid()}", HttpStatusCode.BadRequest, SurfaceFilters.AmbiguousCode),
            ("/api/v1/geofiles/export?format=geojson", $"savedFilterId={Guid.NewGuid()}", HttpStatusCode.NotFound, SurfaceFilters.SavedNotFoundCode),
            ("/api/v1/geofiles/export?format=nope", "", HttpStatusCode.BadRequest, "export.format_unsupported"),
            ("/api/v1/trip-logs/export?", "filter=eyJ2ZXJzaW9uIjox", HttpStatusCode.BadRequest, SurfaceFilters.MalformedCode),
            ("/api/v1/trip-logs/export?", $"filter={readable}", HttpStatusCode.BadRequest, SurfaceFilters.WrongWorldCode),
            ("/api/v1/trip-logs/export?", $"filter={tripsReadable}&savedFilterId={Guid.NewGuid()}", HttpStatusCode.BadRequest, SurfaceFilters.AmbiguousCode),
            ("/api/v1/trip-logs/export?", $"savedFilterId={Guid.NewGuid()}", HttpStatusCode.NotFound, SurfaceFilters.SavedNotFoundCode),
        };
        foreach (var (path, query, status, code) in cases)
        {
            var response = await viewer.GetAsync($"{path}&{query}");
            response.StatusCode.ShouldBe(status, $"{path} {query}");
            (await response.Content.ReadAsStringAsync()).ShouldContain(code, customMessage: $"{path} {query}");
        }

        // A saved filter its author keeps private is the same refusal as one that is not there; the
        // author's own export is the positive twin.
        var saved = await owner.PostAsJsonAsync("/api/v1/saved-filters", new
        {
            name = $"Private trips {tag}",
            document = Trips(Titled("tripLog")),
            visibility = "private",
        }, Wire);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var id = (await saved.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        (await owner.GetAsync($"/api/v1/trip-logs/export?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/trip-logs/export?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.GetAsync($"/api/v1/geofiles/export?format=geojson&filter={readable}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/geofiles/export?format=geojson&filter={readable}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/trip-logs/export?filter={tripsReadable}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_geofile_list_export_past_the_row_cap_is_refused_rather_than_cut_short()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var big = GeofileWorldFixture.Add(db, $"Huge {tag}", ownerId, Visibility.Authenticated);
            big.FeatureCount = 300_000;
            await db.SaveChangesAsync();
        }

        var token = FilterToken.Encode(About("geofile", Titled("geofile")));
        var response = await viewer.GetAsync($"/api/v1/geofiles/export?format=geojson&filter={token}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("export.too_many_geofiles");
    }

    private static async Task<string> GeofileExportAsync(HttpClient client, FilterDocument document)
    {
        var response = await client.GetAsync($"/api/v1/geofiles/export?format=geojson&filter={FilterToken.Encode(document)}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    private static async Task<string> TripSheetAsync(HttpClient client, FilterDocument document)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/export?filter={FilterToken.Encode(document)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XSSFWorkbook(stream);
        return new XSSFExcelExtractor(workbook).Text;
    }

    // ---------- the radius ceiling ----------

    [Fact]
    public async Task The_published_radius_ceiling_is_the_installation_setting_and_the_one_enforced()
    {
        var shipped = await viewer.GetFromJsonAsync<JsonNode>("/api/v1/filters/vocabulary");
        shipped!["limits"]!["maxRadiusMeters"]!.GetValue<double>().ShouldBe(SpatialConditions.DefaultMaxRadiusMeters);
        var refused = await RefusalAsync(viewer, Models(Within(300_000, new PointValue(10, 10))));
        refused.Code.ShouldBe("filter.invalid");

        using var raised = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["Filters:MaxRadiusMeters"] = "500000" },
            ownHost: true);
        using var client = await AuthHelper.BearerClientAsync(raised, $"kp-view-{tag}@t.local");
        var published = await client.GetFromJsonAsync<JsonNode>("/api/v1/filters/vocabulary");
        published!["limits"]!["maxRadiusMeters"]!.GetValue<double>().ShouldBe(500_000);
        (await IdsAsync(client, Models(Within(300_000, new PointValue(10, 10))))).ShouldBeEmpty();
        (await RefusalAsync(client, Models(Within(500_001, new PointValue(10, 10))))).Code.ShouldBe("filter.invalid");
    }

    // ---------- helpers ----------

    private sealed record Spot(double TrueLon, double TrueLat)
    {
        public static Spot Pick()
        {
            // A stretch of the Pacific no other filter test uses, with every protected position three
            // tenths of a cell off its lattice point.
            var nearLon = -170 + (Random.Shared.NextDouble() * 40);
            var nearLat = 5 + (Random.Shared.NextDouble() * 20);
            var snappedLon = Math.Round(nearLon / Cell, MidpointRounding.AwayFromZero) * Cell;
            var snappedLat = Math.Round(nearLat / Cell, MidpointRounding.AwayFromZero) * Cell;
            return new Spot(snappedLon - (0.3 * Cell), snappedLat + (0.1 * Cell));
        }
    }

    private ConditionNode Titled(string world = "photograph") => world switch
    {
        "photograph" => new(PhotographFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]),
        "tripLog" => new(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]),
        _ => new(CommonFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
    };

    private static ConditionNode CaveIs(Guid cave) =>
        new(PhotographFilterFields.Cave, FilterOp.Equals, [new IdValue(cave.ToString())]);

    private static ConditionNode CaveIn(params Guid[] caves) =>
        new(PhotographFilterFields.Cave, FilterOp.In, [.. caves.Select(c => new IdValue(c.ToString()))]);

    private static ConditionNode Within(double radius, FilterValue anchor) =>
        new(SpatialConditions.PositionField, FilterOp.Within, [new NumberValue(radius), anchor]);

    private static ConditionNode Inside(FilterValue outline) =>
        new(SpatialConditions.PositionField, FilterOp.Inside, [outline]);

    private static PolygonValue Square(double lon, double lat, double half) => new(
    [
        [[lon - half, lat - half], [lon + half, lat - half], [lon + half, lat + half], [lon - half, lat + half], [lon - half, lat - half]],
    ]);

    private static FilterDocument About(string world, params FilterNode[] conditions) => new()
    {
        Scope = [new WorldScope(world, conditions.Length == 1 ? conditions[0] : new AllOfNode([.. conditions]))],
    };

    private static FilterDocument Photographs(params FilterNode[] conditions) => About("photograph", conditions);

    private static FilterDocument Models(params FilterNode[] conditions) => About("surveyModel", conditions);

    private static FilterDocument Trips(params FilterNode[] conditions) => About("tripLog", conditions);

    private static async Task<HashSet<Guid>> IdsAsync(HttpClient client, FilterDocument document)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(document, PageSize: 100), Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonNode.Parse(payload)!["worlds"]![0]!["hits"]!.AsArray().Select(h => h!["id"]!.GetValue<Guid>())];
    }

    private sealed record Refusal(int Status, string? Code, string? Detail);

    private static async Task<Refusal> RefusalAsync(HttpClient client, FilterDocument document)
    {
        var response = await client.PostAsJsonAsync("/api/v1/filters/query", new FilterQueryRequest(document), Wire);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        var body = JsonNode.Parse(payload)!;
        return new Refusal((int)response.StatusCode, body["code"]?.GetValue<string>(), body["detail"]?.GetValue<string>());
    }

    private async Task<(Guid CaveId, Guid EntranceId)> CaveWithEntranceAsync(
        string name, double lon, double lat, bool locationProtected)
    {
        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {tag}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();

        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var entrancePayload = await entrance.Content.ReadAsStringAsync();
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, entrancePayload);
        return (caveId, JsonNode.Parse(entrancePayload)!["id"]!.GetValue<Guid>());
    }

    /// <summary>A picture every signed-in account may read, with a fix or none, hanging on a feature or nothing.</summary>
    private async Task<Guid> PhotographAsync(string name, (double Lon, double Lat)? fix, Guid? hangsOn)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = new Document { Title = $"Picture {name} {tag}", OwnerUserId = ownerId, Visibility = Visibility.Authenticated };
        db.Documents.Add(document);
        var version = new DocumentVersion { DocumentId = document.Id, VersionNumber = 1, IsCurrent = true, UploadedBy = ownerId };
        db.DocumentVersions.Add(version);
        var file = new StoredFile
        {
            DocumentVersionId = version.Id,
            StoragePath = Guid.NewGuid().ToString("N"),
            OriginalName = "picture.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Kind = FileKind.Image,
            Geom = fix is { } p ? new Point(p.Lon, p.Lat) { SRID = 4326 } : null,
        };
        db.StoredFiles.Add(file);
        if (hangsOn is { } feature)
        {
            db.Attachments.Add(new Attachment { FileId = file.Id, FeatureId = feature });
        }

        await db.SaveChangesAsync();
        return document.Id;
    }

    private async Task<Guid> SurveyModelAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cave = await db.Features.SingleAsync(f => f.Id == caveId);
        var model = SurveyModelWorldFixture.Add(db, cave, ownerId, $"Model {tag}");
        await db.SaveChangesAsync();
        return model.Id;
    }

    private async Task<Guid> TripAsync(string title, Guid caveId)
    {
        Guid caver;
        using (var scope = factory.Services.CreateScope())
        {
            caver = (await RosterHelper.CaverForAsync(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(), ownerId)).Id;
        }

        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Trip {title} {tag}",
            tripDate = "2026-05-12",
            caveIds = new[] { caveId },
            participants = new[] { new { caverId = caver } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonNode.Parse(payload)!["id"]!.GetValue<Guid>();
    }

    private async Task<(Guid Map, Guid File)> FootprintsAsync(double lon, double lat, double half)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var factoryOf = new GeometryFactory(new PrecisionModel(), 4326);
        Polygon Box() => factoryOf.CreatePolygon(
        [
            new Coordinate(lon - half, lat - half), new Coordinate(lon + half, lat - half),
            new Coordinate(lon + half, lat + half), new Coordinate(lon - half, lat + half),
            new Coordinate(lon - half, lat - half),
        ]);

        var map = new GeoreferencedMap
        {
            Name = $"Map {tag}",
            FileId = ConformanceSeeds.File(db, ownerId, FileKind.Raster, "tif").Id,
            OwnerUserId = ownerId,
            Visibility = Visibility.Authenticated,
            Bbox = Box(),
        };
        db.GeoreferencedMaps.Add(map);
        var geofile = GeofileWorldFixture.Add(db, $"Geodata {tag}", ownerId, Visibility.Authenticated);
        geofile.Bbox = Box();
        await db.SaveChangesAsync();
        return (map.Id, geofile.Id);
    }

    private static JsonSerializerOptions BuildWire()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
