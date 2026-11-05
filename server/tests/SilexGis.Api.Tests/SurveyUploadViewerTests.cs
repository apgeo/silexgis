// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a page of one upload's results reads beside its status: the compiler's figures and the
/// upload's own survey measured, signed in and through a public link; the survey's line work in the
/// world's coordinates, signed in on the line's own exact-position check and through the public
/// link as what the upload's own files place; and the way from a public link to the same upload in
/// the application, for a signed-in reader the upload answers.
/// </summary>
/// <remarks>
/// The survey is genuine TopoDroid output of an invented survey, compiled by a stand-in compiler
/// whose log carries invented statistics. The line placed in the world and the legs measured are
/// invented too. Nothing here is a real cave.
/// </remarks>
public sealed class SurveyUploadViewerTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Uploads = "/api/v1/survey-uploads";
    private const string Public = "/api/v1/public/survey-uploads";

    /// <summary>Where the invented line is drawn: an exact position nobody without the right may be handed.</summary>
    private const double Lon = 25.4611;
    private const double Lat = 45.5322;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private Guid ownerId;
    private HttpClient reader = null!;
    private Guid readerId;

    public SurveyUploadViewerTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-suview-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler>(
                    new FakeSurveyCompiler { StatisticsBlock = SurveyCompilationLogTests.InventedStatistics }));
            });
    }

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"suview-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"suview-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"suview-r-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"suview-r-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    // ---- figures ----

    [Fact]
    public async Task The_figures_are_everything_the_compilers_log_states()
    {
        var (id, _) = await SendCompiledAsync($"Figuri {suffix}");

        var response = await owner.GetAsync($"{Uploads}/{id}/figures");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var root = JsonDocument.Parse(text).RootElement;
        var figures = root.GetProperty("compiler");

        figures.GetProperty("outcome").GetString().ShouldBe("succeeded");
        figures.GetProperty("stationCount").GetInt32().ShouldBe(42);
        figures.GetProperty("shotCount").GetInt32().ShouldBe(57);
        figures.GetProperty("totalLengthM").GetDouble().ShouldBe(312.40);
        figures.GetProperty("loopCount").GetInt32().ShouldBe(2);
        root.GetProperty("placed").GetBoolean().ShouldBeFalse();
        // The stand-in compiler's model is not a file that can be read, so nothing was measured.
        root.GetProperty("analyses").ValueKind.ShouldBe(JsonValueKind.Null);

        // Each extent with the stations at its ends and their absolute values, and each loop with
        // the stations it runs through — what the log the reader may already download says.
        var vertical = figures.GetProperty("verticalRange");
        vertical.GetProperty("lengthM").GetDouble().ShouldBe(61.20);
        vertical.GetProperty("fromStation").GetString().ShouldBe("1.17@inventata");
        vertical.GetProperty("fromValueM").GetDouble().ShouldBe(1234.50);
        vertical.GetProperty("toStation").GetString().ShouldBe("1.23@inventata");
        vertical.GetProperty("toValueM").GetDouble().ShouldBe(1173.30);
        figures.GetProperty("northSouthRange").GetProperty("fromValueM").GetDouble().ShouldBe(5040123.00);
        figures.GetProperty("eastWestRange").GetProperty("fromValueM").GetDouble().ShouldBe(412345.00);
        var loop = figures.GetProperty("loops")[0];
        loop.GetProperty("stations").GetString().ShouldBe("1.1@inventata - 1.2@inventata - 1.1@inventata");
        loop.GetProperty("errorYM").GetDouble().ShouldBe(0.2);
    }

    [Fact]
    public async Task A_run_with_no_log_has_no_figures()
    {
        var (id, _) = await SendCompiledAsync($"Necompilata {suffix}", pipeline: new { compile = false });

        var figures = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}/figures");
        figures.GetProperty("compiler").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_signed_in_routes_refuse_a_stranger_an_anonymous_caller_and_an_access_key()
    {
        var (id, _) = await SendCompiledAsync($"Straina {suffix}");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Uploads}/{id}/figures")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Uploads}/by-public-link/{await TokenAsync(id)}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A key opens the device's routes and no other: these serve the web's pages.
        var key = await AuthHelper.CreateAccessKeyAsync(owner);
        using var device = AuthHelper.AccessKeyClient(factory, key);
        var keyed = await device.GetAsync($"{Uploads}/{id}/figures");
        keyed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(keyed)).ShouldBe("auth.access_key_not_accepted");
        (await device.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Somebody who may not read the cave is told the upload does not exist, as on every upload route.
        var stranger = await reader.GetAsync($"{Uploads}/{id}/figures");
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(stranger)).ShouldBe("survey_upload.not_found");
        (await reader.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"{Uploads}/{Guid.NewGuid()}/figures")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reader_who_may_read_the_cave_sees_the_figures_and_one_who_may_not_place_it_does_not()
    {
        var (id, caveId) = await SendCompiledAsync($"Citita {suffix}");
        await PlaceLineAsync(id, caveId);
        await AllowAsync(readerId, AccessAction.Read);

        // Read on an unprotected cave is the whole of the survey terms.
        (await reader.GetAsync($"{Uploads}/{id}/figures")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Protected, and the reader without the exact position: the upload is not there for them.
        await SetProtectedAsync(caveId, true);
        (await reader.GetAsync($"{Uploads}/{id}/figures")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var geodata = await reader.GetAsync($"{Uploads}/{id}/geodata");
        geodata.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await geodata.Content.ReadAsStringAsync()).ShouldNotContain(Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)[..5]);

        // The owner, who places it, still does.
        (await owner.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- public figures ----

    [Fact]
    public async Task A_public_link_opens_its_figures_for_anybody_and_an_invented_or_withdrawn_one_for_nobody()
    {
        var (id, _) = await SendCompiledAsync($"Publica {suffix}");
        var token = await TokenAsync(id);

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"{Public}/{token}/figures");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var compiler = JsonDocument.Parse(text).RootElement.GetProperty("compiler");
        compiler.GetProperty("totalLengthM").GetDouble().ShouldBe(312.40);
        // The whole figures, as the log the link already serves states them.
        compiler.GetProperty("verticalRange").GetProperty("fromStation").GetString().ShouldBe("1.17@inventata");
        compiler.GetProperty("verticalRange").GetProperty("fromValueM").GetDouble().ShouldBe(1234.50);
        JsonDocument.Parse(text).RootElement.GetProperty("placed").GetBoolean().ShouldBeFalse();
        text.ShouldNotContain(id.ToString(), Case.Insensitive);

        var invented = await anonymous.GetAsync($"{Public}/not-a-real-token-{suffix}/figures");
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(invented)).ShouldBe("survey_upload.public_link_not_found");

        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await anonymous.GetAsync($"{Public}/{token}/figures");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(withdrawn)).ShouldBe("survey_upload.public_link_not_found");
    }

    [Fact]
    public async Task A_public_link_serves_the_line_its_files_place_in_every_offered_format()
    {
        var (id, caveId) = await SendCompiledAsync($"Harta publica {suffix}");
        await PlaceLineAsync(id, caveId);
        var token = await TokenAsync(id);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetFromJsonAsync<JsonElement>($"{Public}/{token}/figures")).GetProperty("placed").GetBoolean().ShouldBeTrue();

        var geojson = await anonymous.GetAsync($"{Public}/{token}/geodata");
        geojson.StatusCode.ShouldBe(HttpStatusCode.OK, await geojson.Content.ReadAsStringAsync());
        geojson.Content.Headers.ContentType!.MediaType.ShouldBe("application/geo+json");
        var feature = JsonDocument.Parse(await geojson.Content.ReadAsStringAsync()).RootElement
            .GetProperty("features").EnumerateArray().Single();
        var first = feature.GetProperty("geometry").GetProperty("coordinates")[0][0];
        first[0].GetDouble().ShouldBe(Lon, 1e-9);
        first[1].GetDouble().ShouldBe(Lat, 1e-9);
        feature.GetProperty("properties").GetProperty("name").GetString().ShouldBe($"Harta publica {suffix}");

        var kml = await anonymous.GetAsync($"{Public}/{token}/geodata?format=kml");
        kml.StatusCode.ShouldBe(HttpStatusCode.OK);
        kml.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.google-earth.kml+xml");
        kml.Content.Headers.ContentDisposition!.FileNameStar.ShouldEndWith(".kml");

        var gpx = await anonymous.GetAsync($"{Public}/{token}/geodata?format=gpx");
        gpx.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await gpx.Content.ReadAsStringAsync()).ShouldContain("<gpx", Case.Insensitive);

        var unsupported = await anonymous.GetAsync($"{Public}/{token}/geodata?format=dwg");
        unsupported.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unsupported)).ShouldBe("survey_upload.geodata_format_unsupported");

        // Nothing about the cave's own record rides along: not its id, not a page in the application.
        (await geojson.Content.ReadAsStringAsync()).ShouldNotContain(caveId.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task A_public_links_line_work_opens_and_refuses_exactly_as_its_page_does()
    {
        var (id, caveId) = await SendCompiledAsync($"Linie publica {suffix}");
        await PlaceLineAsync(id, caveId);
        var token = await TokenAsync(id);
        using var anonymous = factory.CreateClient();

        // An invented token is refused before its format is judged, so a bad format says nothing.
        var invented = await anonymous.GetAsync($"{Public}/invented-{suffix}/geodata?format=dwg");
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(invented)).ShouldBe("survey_upload.public_link_not_found");

        // An ordinary link's line work stops with its page the moment the cave is protected, and
        // comes back with it.
        await SetProtectedAsync(caveId, true);
        var protectedCave = await anonymous.GetAsync($"{Public}/{token}/geodata");
        protectedCave.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(protectedCave)).ShouldBe("survey_upload.public_link_not_found");
        (await anonymous.GetAsync($"{Public}/{token}/figures")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await SetProtectedAsync(caveId, false);
        (await anonymous.GetAsync($"{Public}/{token}/geodata")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await anonymous.GetAsync($"{Public}/{token}/geodata?format=kml");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(withdrawn)).ShouldBe("survey_upload.public_link_not_found");

        var (orgId, orgCave) = await SendCompiledAsync($"Linie organizatie {suffix}", publicLink: new { audience = "organization" });
        await PlaceLineAsync(orgId, orgCave);
        var orgToken = await TokenAsync(orgId);
        var refused = await anonymous.GetAsync($"{Public}/{orgToken}/geodata");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).ShouldBe("survey_upload.public_link_sign_in_required");
        (await reader.GetAsync($"{Public}/{orgToken}/geodata")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_public_link_to_a_run_that_placed_no_line_says_so()
    {
        var (id, _) = await SendCompiledAsync($"Publica neplasata {suffix}");
        var token = await TokenAsync(id);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"{Public}/{token}/geodata?format=gpx");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("survey_upload.not_georeferenced");
    }

    [Fact]
    public async Task An_organization_links_figures_ask_an_anonymous_caller_to_sign_in()
    {
        var (id, _) = await SendCompiledAsync($"Organizatie {suffix}", publicLink: new { audience = "organization" });
        var token = await TokenAsync(id);

        using var anonymous = factory.CreateClient();
        var refused = await anonymous.GetAsync($"{Public}/{token}/figures");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).ShouldBe("survey_upload.public_link_sign_in_required");

        (await reader.GetAsync($"{Public}/{token}/figures")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- the upload's own survey measured ----

    [Fact]
    public async Task The_analyses_measure_the_uploads_own_survey_not_the_one_the_cave_is_drawn_from()
    {
        // Two uploads of one cave. The first is the cave's current survey: one short leg.
        // The second, not current, runs further north and deeper over two legs.
        var (firstId, caveId) = await SendCompiledAsync($"Prima {suffix}");
        var (secondId, _) = await SendCompiledAsync($"A doua {suffix}", cave: new { id = caveId });
        var firstModel = await SeedLegsAsync(firstId, isCurrent: true, (0.0001, 0.0, -2.0));
        var secondModel = await SeedLegsAsync(secondId, isCurrent: false, (0.0, 0.0003, -10.0), (0.0, 0.0003, -10.0));
        await SeedTopologyAsync(secondModel, nodes: 3);

        var cave = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/statistics");
        cave.GetProperty("surveyModelId").GetGuid().ShouldBe(firstModel);
        var caveLength = cave.GetProperty("indices").GetProperty("totalLengthM").GetDouble();

        var analyses = (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{secondId}/figures")).GetProperty("analyses");
        analyses.GetProperty("surveyModelId").GetGuid().ShouldBe(secondModel);
        analyses.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        var length = analyses.GetProperty("statistics").GetProperty("indices").GetProperty("totalLengthM").GetDouble();
        length.ShouldBeGreaterThan(60);
        length.ShouldBeGreaterThan(caveLength * 4);
        analyses.GetProperty("statistics").GetProperty("indices").GetProperty("verticalExtentM").GetDouble().ShouldBe(20, 0.01);
        analyses.GetProperty("orientation").GetProperty("segmentCount").GetInt32().ShouldBe(2);
        analyses.GetProperty("orientation").GetProperty("dip").ValueKind.ShouldBe(JsonValueKind.Object);
        analyses.GetProperty("depthProfile").GetProperty("deepestM").GetDouble().ShouldBe(20, 0.01);
        analyses.GetProperty("depthProfile").GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(0);
        analyses.GetProperty("topology").GetProperty("nodeCount").GetInt32().ShouldBe(3);

        // The first upload's own page measures the first upload, which has no network figures.
        var own = (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{firstId}/figures")).GetProperty("analyses");
        own.GetProperty("surveyModelId").GetGuid().ShouldBe(firstModel);
        own.GetProperty("statistics").GetProperty("indices").GetProperty("totalLengthM").GetDouble().ShouldBe(caveLength, 0.01);
        own.GetProperty("topology").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_public_link_and_a_reader_without_the_exact_position_get_the_analyses_as_the_page_does()
    {
        var (id, caveId) = await SendCompiledAsync($"Analize publice {suffix}");
        var model = await SeedLegsAsync(id, isCurrent: false, (0.0002, 0.0, -5.0));
        var token = await TokenAsync(id);

        using var anonymous = factory.CreateClient();
        var shown = (await anonymous.GetFromJsonAsync<JsonElement>($"{Public}/{token}/figures")).GetProperty("analyses");
        shown.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        shown.GetProperty("depthProfile").GetProperty("deepestM").GetDouble().ShouldBe(5, 0.01);

        // A signed-in reader who may not see the cave's survey is told the upload does not exist.
        (await reader.GetAsync($"{Uploads}/{id}/figures")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the link's figures close with its page when the cave is protected.
        await SetProtectedAsync(caveId, true);
        (await anonymous.GetAsync($"{Public}/{token}/figures")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- line work ----

    [Fact]
    public async Task The_line_work_comes_in_every_offered_format_placed_where_the_survey_was_placed()
    {
        var (id, caveId) = await SendCompiledAsync($"Geodate {suffix}");
        await PlaceLineAsync(id, caveId);
        (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}/figures")).GetProperty("placed").GetBoolean().ShouldBeTrue();

        var geojson = await owner.GetAsync($"{Uploads}/{id}/geodata");
        geojson.StatusCode.ShouldBe(HttpStatusCode.OK, await geojson.Content.ReadAsStringAsync());
        geojson.Content.Headers.ContentType!.MediaType.ShouldBe("application/geo+json");
        var collection = JsonDocument.Parse(await geojson.Content.ReadAsStringAsync()).RootElement;
        var feature = collection.GetProperty("features").EnumerateArray().Single();
        feature.GetProperty("geometry").GetProperty("type").GetString().ShouldBe("MultiLineString");
        var first = feature.GetProperty("geometry").GetProperty("coordinates")[0][0];
        first[0].GetDouble().ShouldBe(Lon, 1e-9);
        first[1].GetDouble().ShouldBe(Lat, 1e-9);
        feature.GetProperty("properties").GetProperty("name").GetString().ShouldBe($"Geodate {suffix}");

        var kml = await owner.GetAsync($"{Uploads}/{id}/geodata?format=kml");
        kml.StatusCode.ShouldBe(HttpStatusCode.OK);
        kml.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.google-earth.kml+xml");
        (await kml.Content.ReadAsStringAsync()).ShouldContain("<kml", Case.Insensitive);
        kml.Content.Headers.ContentDisposition!.FileNameStar.ShouldEndWith(".kml");

        var gpx = await owner.GetAsync($"{Uploads}/{id}/geodata?format=GPX");
        gpx.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await gpx.Content.ReadAsStringAsync()).ShouldContain("<gpx", Case.Insensitive);

        var unsupported = await owner.GetAsync($"{Uploads}/{id}/geodata?format=dwg");
        unsupported.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unsupported)).ShouldBe("survey_upload.geodata_format_unsupported");
    }

    [Fact]
    public async Task A_run_that_placed_no_line_says_so()
    {
        var (id, _) = await SendCompiledAsync($"Neplasata {suffix}");

        var response = await owner.GetAsync($"{Uploads}/{id}/geodata?format=kml");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("survey_upload.not_georeferenced");
    }

    [Fact]
    public async Task A_line_under_a_protection_of_its_own_is_withheld_as_if_never_placed()
    {
        var (id, caveId) = await SendCompiledAsync($"Linie protejata {suffix}");
        var lineId = await PlaceLineAsync(id, caveId);
        await AllowAsync(readerId, AccessAction.Read);
        (await reader.GetAsync($"{Uploads}/{id}/geodata")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The cave stays open; the line itself is protected and the reader holds no exact position.
        await SetProtectedAsync(lineId, true);
        var withheld = await reader.GetAsync($"{Uploads}/{id}/geodata");
        withheld.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(withheld)).ShouldBe("survey_upload.not_georeferenced");
        // And the page is told there is nothing to place, as for a run that placed nothing.
        (await reader.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}/figures")).GetProperty("placed").GetBoolean().ShouldBeFalse();
        (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}/figures")).GetProperty("placed").GetBoolean().ShouldBeTrue();
    }

    // ---- from a public link into the application ----

    [Fact]
    public async Task A_signed_in_reader_of_a_public_link_is_shown_the_upload_in_the_application_only_when_it_answers_them()
    {
        var (id, caveId) = await SendCompiledAsync($"In aplicatie {suffix}");
        var token = await TokenAsync(id);

        var found = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/by-public-link/{token}");
        found.GetProperty("statusPageUrl").GetString().ShouldEndWith($"/uploads/{id}");
        found.GetProperty("caveUrl").GetString().ShouldBe($"/caves/{caveId}");

        // Holding the address opens the page, not the cave: a reader the upload does not answer is
        // told the link does not exist, as an invented one is.
        var stranger = await reader.GetAsync($"{Uploads}/by-public-link/{token}");
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(stranger)).ShouldBe("survey_upload.public_link_not_found");
        (await owner.GetAsync($"{Uploads}/by-public-link/invented-{suffix}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await AllowAsync(readerId, AccessAction.Read);
        (await reader.GetAsync($"{Uploads}/by-public-link/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Uploads}/by-public-link/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    /// <summary>Sends a compiled survey of a cave the upload makes, runs it, and answers the upload and its cave.</summary>
    private async Task<(Guid Id, Guid CaveId)> SendCompiledAsync(string title, object? publicLink = null, object? pipeline = null, object? cave = null)
    {
        var files = new (string Name, byte[] Bytes)[]
        {
            ("pestera_inventata.zip", Fixture("pestera_inventata.zip")),
            ("pestera_inventata.th", Fixture("pestera_inventata.th")),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")),
            ("pestera_inventata-1s.th2", Fixture("pestera_inventata-1s.th2")),
        };
        var opened = await owner.PostAsJsonAsync(Uploads, new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = title,
            surveyName = "pestera_inventata",
            cave,
            publicLink,
            pipeline,
            files = files.Select(f => new
            {
                name = f.Name,
                size = f.Bytes.LongLength,
                role = f.Name.EndsWith(".zip", StringComparison.Ordinal) ? "archive"
                    : f.Name.EndsWith(".th2", StringComparison.Ordinal) ? "therionSketch"
                    : "therionSource",
            }),
        });
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        var caveId = body.GetProperty("cave").GetProperty("id").GetGuid();

        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await owner.PutAsync($"{Uploads}/{id}/files/{Uri.EscapeDataString(name)}", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var complete = await owner.PostAsync($"{Uploads}/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        return (id, caveId);
    }

    /// <summary>
    /// Writes the line the run's model would have been read into, as the reading does once a
    /// survey can be placed; the stand-in compiler's model is not a file that can be read.
    /// </summary>
    private async Task<Guid> PlaceLineAsync(Guid uploadId, Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var modelId = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == uploadId && f.Output == "lox" && f.SurveyModelId != null)
            .Select(f => f.SurveyModelId!.Value)
            .SingleAsync();
        var geometries = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var path = geometries.CreateLineString([new Coordinate(Lon, Lat), new Coordinate(Lon + 0.001, Lat + 0.0005)]);
        var feature = new Feature { Name = "pestera_inventata", Geom = geometries.CreateMultiLineString([path]) };
        await writer.CreateCenterlineAsync(
            feature,
            new Centerline { CaveFeatureId = caveId, SurveyModelId = modelId, PathCount = 1, Source = CenterlineSource.Extracted },
            firstBecomesDefault: false);
        await db.SaveChangesAsync();
        return feature.Id;
    }

    /// <summary>
    /// Writes the legs the run's compiled model would have been read into, each leg given as its
    /// step east and north in degrees and its rise in metres from where the last one ended, and
    /// marks the model read. The stand-in compiler's model is not a file that can be read.
    /// </summary>
    private async Task<Guid> SeedLegsAsync(Guid uploadId, bool isCurrent, params (double East, double North, double Rise)[] legs)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var modelId = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == uploadId && f.Output == "lox" && f.SurveyModelId != null)
            .Select(f => f.SurveyModelId!.Value)
            .SingleAsync();
        await db.SurveyModels.Where(m => m.Id == modelId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, SurveyModelStatus.Ready).SetProperty(m => m.IsCurrent, isCurrent));

        var geometries = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var (x, y, z) = (Lon, Lat, 1000.0);
        var station = 0;
        foreach (var (east, north, rise) in legs)
        {
            var from = new CoordinateZ(x, y, z);
            (x, y, z) = (x + east, y + north, z + rise);
            db.SurveyShots.Add(new SurveyShot
            {
                SurveyModelId = modelId,
                FromStationName = $"{station}",
                ToStationName = $"{station + 1}",
                Geom = geometries.CreateLineString([from, new CoordinateZ(x, y, z)]),
                LengthM = 1,
            });
            station++;
        }

        await db.SaveChangesAsync();
        return modelId;
    }

    private async Task SeedTopologyAsync(Guid modelId, int nodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.SurveyTopologies.Add(new SurveyTopology
        {
            SurveyModelId = modelId,
            NodeCount = nodes,
            EdgeCount = nodes - 1,
            ComponentCount = 1,
            ComputedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync(Guid id)
    {
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        return status.GetProperty("publicLink").GetProperty("url").GetString()!["/p/survey/".Length..];
    }

    private async Task AllowAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Features,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Protects or unprotects a feature itself, both the flag and the derived column a write would set.</summary>
    private async Task SetProtectedAsync(Guid featureId, bool on)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => f.Id == featureId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.LocationProtected, on).SetProperty(f => f.IsProtectedEffective, on));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] Fixture(string name) =>
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));

    private async Task RunPipelineAsync(Guid uploadId)
    {
        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var queued = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline && j.Status == ProcessingJobStatus.Queued)
                .ToListAsync();
            jobId = queued.Single(j =>
                JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == uploadId).Id;
        }

        await QueuedJob.RunAsync(factory.Services, jobId);
    }
}
