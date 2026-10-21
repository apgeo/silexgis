// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Npgsql;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// CRS definitions resolved from the PROJ database that ships with the application. This exists
/// so the vendored 3D survey viewer never has to reach a public web service to find out what
/// coordinate system a survey is in.
/// </summary>
public sealed class CrsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient reader = null!;

    public CrsTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var email = $"crs-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        reader = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Theory]
    [InlineData(4326)]
    [InlineData(3857)]
    // Romanian Stereo70. The vendored viewer hard-codes only a handful of systems and this is not
    // one of them, so it is the code that used to leave a survey unreferenced on an offline
    // installation — the whole reason this endpoint exists.
    [InlineData(31700)]
    public async Task A_known_code_resolves_to_a_proj4_definition_the_viewer_can_use_as_is(int srid)
    {
        var response = await reader.GetAsync($"/api/v1/crs/{srid}.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        var body = await response.Content.ReadAsStringAsync();
        // The consumer hands the body straight to a proj4 parser, so it must be the definition
        // itself — no JSON envelope, no leading whitespace, nothing to unwrap.
        body.ShouldStartWith("+proj=");
        body.Trim().ShouldBe(body);
    }

    [Fact]
    public async Task A_definition_is_immutable_so_it_is_answered_identically_and_marked_cacheable()
    {
        var first = await reader.GetAsync("/api/v1/crs/31700.proj4");
        var second = await reader.GetAsync("/api/v1/crs/31700.proj4");

        (await first.Content.ReadAsStringAsync()).ShouldBe(await second.Content.ReadAsStringAsync());
        // Private, not public: the request carried a bearer token, so a shared cache must not
        // keep the answer under a key that ignores it.
        var cacheControl = second.Headers.CacheControl.ShouldNotBeNull();
        cacheControl.Private.ShouldBeTrue();
        cacheControl.MaxAge.ShouldNotBeNull().TotalDays.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task An_esri_authority_code_still_resolves_even_though_the_authority_is_lost()
    {
        // Survey files name a system as "epsg:31700" or "esri:102008", but the viewer that asks
        // for a definition passes on only the number — the authority never reaches this endpoint.
        // 102008 (North America Albers Equal Area Conic) is an ESRI code with no EPSG meaning, so
        // resolving it proves such a survey keeps the georeferencing it used to get from the
        // public web service this endpoint replaced.
        var response = await reader.GetAsync("/api/v1/crs/102008.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("+proj=aea");
    }

    [Fact]
    public async Task An_epsg_code_wins_over_an_esri_code_of_the_same_number()
    {
        // The two registers overlap at the low numbers and EPSG is the authoritative one, so an
        // ambiguous number must resolve the EPSG way.
        var response = await reader.GetAsync("/api/v1/crs/4326.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("+proj=longlat");
    }

    [Fact]
    public async Task A_code_the_database_does_not_know_is_a_clean_not_found()
    {
        var response = await reader.GetAsync("/api/v1/crs/999998.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("crs.not_found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000000)]
    public async Task A_nonsense_code_is_rejected_rather_than_looked_up(int srid)
    {
        var response = await reader.GetAsync($"/api/v1/crs/{srid}.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("crs.code_invalid");
    }

    [Fact]
    public async Task A_route_that_is_not_an_integer_code_never_reaches_the_handler()
    {
        // The route constraint, not the handler, is what keeps a path segment from being used as
        // an unbounded key into the definition cache.
        (await reader.GetAsync("/api/v1/crs/abc.proj4")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_endpoint_needs_a_bearer_token()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/crs/4326.proj4")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Stereo 70: one definition, shared with the client's coordinate formats ----------------
    //
    // The grid's definition and the points that pin it live in one file at the root of the working
    // tree, which the client's own test reads as well. Both sides are held to that file rather than
    // to each other, so neither can drift without a test on its side failing.

    private sealed record ReferencePoint(double Lon, double Lat, double Easting, double Northing);

    private sealed record Stereo70Contract(
        int[] Codes, string Definition, ReferencePoint[] ReferencePoints, double ToleranceMetres);

    private static readonly Lazy<Stereo70Contract> Stereo70 = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "server", "SilexGis.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not find the working tree above the test assembly.");
        var path = Path.Combine(directory.FullName, "contract", "crs", "stereo70.json");
        return JsonSerializer.Deserialize<Stereo70Contract>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    });

    [Theory]
    [InlineData(3844)]
    [InlineData(31700)]
    public async Task Both_Stereo70_codes_answer_the_one_definition_the_client_formats_use(int srid)
    {
        Stereo70.Value.Codes.ShouldContain(srid);

        var body = await reader.GetStringAsync($"/api/v1/crs/{srid}.proj4");

        // Exactly the shared definition. What went wrong before is that PROJ exports this grid
        // without its datum shift, because it now chooses the transformation separately from the
        // definition; a viewer handed such a string puts every point about 125 m from where the
        // database and the rest of the application put it.
        body.ShouldBe(Stereo70.Value.Definition);
    }

    [Fact]
    public async Task The_reference_points_are_where_the_database_puts_them()
    {
        // The pinned points are only worth pinning to if they are the database's own answer, so
        // they are asked of the database every run rather than trusted as written down.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var point in Stereo70.Value.ReferencePoints)
        {
            await using var command = new NpgsqlCommand(
                "select st_x(g), st_y(g) from (select st_transform(st_setsrid(st_makepoint(@lon, @lat), 4326), 3844) g) t",
                connection);
            command.Parameters.AddWithValue("lon", point.Lon);
            command.Parameters.AddWithValue("lat", point.Lat);
            await using var row = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            (await row.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
            Math.Sqrt(Math.Pow(row.GetDouble(0) - point.Easting, 2) + Math.Pow(row.GetDouble(1) - point.Northing, 2))
                .ShouldBeLessThan(Stereo70.Value.ToleranceMetres, $"at {point.Lon}, {point.Lat}");
        }
    }

    [Theory]
    [InlineData(3844)]
    [InlineData(31700)]
    public async Task The_registry_answer_projects_the_reference_points_where_the_database_does(int srid)
    {
        GdalRuntime.Configure();
        var definition = await reader.GetStringAsync($"/api/v1/crs/{srid}.proj4");

        using var grid = new SpatialReference("");
        grid.ImportFromProj4(definition).ShouldBe(0);
        using var wgs84 = new SpatialReference("");
        wgs84.ImportFromEPSG(4326).ShouldBe(0);
        grid.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        wgs84.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var forward = new CoordinateTransformation(wgs84, grid);

        foreach (var point in Stereo70.Value.ReferencePoints)
        {
            var projected = new double[3];
            forward.TransformPoint(projected, point.Lon, point.Lat, 0);
            Math.Sqrt(Math.Pow(projected[0] - point.Easting, 2) + Math.Pow(projected[1] - point.Northing, 2))
                .ShouldBeLessThan(Stereo70.Value.ToleranceMetres, $"at {point.Lon}, {point.Lat}");
        }
    }

    [Theory]
    [InlineData(3844)]
    [InlineData(31700)]
    public void A_Stereo70_survey_is_placed_where_the_reference_points_say(int srid)
    {
        // The projector is what places an uploaded survey: its anchor, from which every station
        // of the model and the 3D scene's position for it follow. It has to agree with the same
        // definition the viewer is handed, or the survey and its own map disagree by the shift.
        var projector = new ProjCoordinateProjector();
        foreach (var point in Stereo70.Value.ReferencePoints)
        {
            var placed = projector.ToWgs84(srid, point.Easting, point.Northing).ShouldNotBeNull();
            MetresBetween(placed.Longitude, placed.Latitude, point.Lon, point.Lat)
                .ShouldBeLessThan(Stereo70.Value.ToleranceMetres, $"at {point.Easting}, {point.Northing}");
        }
    }

    private static double MetresBetween(double lon1, double lat1, double lon2, double lat2)
    {
        // Over centimetres an equirectangular approximation is exact enough.
        const double metresPerDegree = 6_371_000 * Math.PI / 180;
        var east = (lon1 - lon2) * Math.Cos(lat2 * Math.PI / 180) * metresPerDegree;
        var north = (lat1 - lat2) * metresPerDegree;
        return Math.Sqrt(east * east + north * north);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }
}
