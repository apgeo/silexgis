// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The walls a line plot's own reading builds, from upload to the file a 3D scene fetches: that
/// they are drawn where the survey measured them and nowhere else, that they stand on the stations
/// stored beside them, that a second reading replaces them, that failing to make them costs the
/// reading nothing, and that a cave whose location is protected gives them to nobody it gives its
/// record to nobody.
/// </summary>
public sealed class SurveyWallsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double OriginLongitude = 25.2;
    private const double OriginLatitude = 45.5;
    private const double OriginHeightM = 1200;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    /// <summary>Switched on by the one test that needs storing a mesh to fail.</summary>
    private bool refuseMeshes;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private long caveTypeId;

    public SurveyWallsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                // The queue lives in the container every test class shares, so a drain started
                // here would claim work another class queued and fail it against storage this
                // host does not have. This class runs its own jobs, one at a time.
                JobWorkers.RemoveFrom(services);

                // The real store, with one thing it can be told to refuse. Everything else about
                // storing a file stays the real wiring.
                var real = services.Single(s => s.ServiceType == typeof(IFileStore));
                services.Remove(real);
                services.AddSingleton<IFileStore>(provider => new MeshRefusingFileStore(
                    (IFileStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!),
                    () => refuseMeshes));
            });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"swl-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"swl-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"swl-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"swl-read-{suffix}@t.local");
    }

    [Fact]
    public async Task A_survey_is_walled_along_the_legs_it_measured_and_along_no_others()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Partly.lox", PartlyMeasured());

        // Nothing has been read yet, so nothing is drawable and the record does not pretend.
        var pending = await GetModelAsync(owner, modelId);
        pending.GetProperty("meshUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        pending.GetProperty("triangleCount").ValueKind.ShouldBe(JsonValueKind.Null);

        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("format").GetString().ShouldBe("lox");

        // One leg of the three was measured at both ends: sixteen faces round it and eight closing
        // each end. The splay beside it carries dimensions too, and gets nothing.
        read.GetProperty("triangleCount").GetInt32().ShouldBe(32);
        read.GetProperty("meshSizeBytes").GetInt64().ShouldBeGreaterThan(0);

        // Delivered exactly as an uploaded wall mesh's conversion is: a signed URL that needs no
        // session, answering with a glTF binary.
        using var anonymous = factory.CreateClient();
        var glb = await anonymous.GetByteArrayAsync(read.GetProperty("meshUrl").GetString()!);
        Encoding.ASCII.GetString(glb, 0, 4).ShouldBe("glTF");

        var stations = await StationsAsync(modelId);
        var vertices = WorldVertices(glb, read);

        // The tube's axis runs through the two stations it joins, as they are stored: each end is
        // closed about the station itself, so the station is a vertex of the mesh — to a
        // centimetre, which is finer than the survey.
        NearestVertexM(vertices, stations["A"]).ShouldBeLessThan(0.01);
        NearestVertexM(vertices, stations["B"]).ShouldBeLessThan(0.01);

        // And nothing is drawn at the stations nobody measured the walls of. C is eight metres
        // past B; were its leg walled, C would be a vertex too.
        NearestVertexM(vertices, stations["C"]).ShouldBeGreaterThan(5);
        NearestVertexM(vertices, stations["D"]).ShouldBeGreaterThan(5);
        NearestVertexM(vertices, stations["W"]).ShouldBeGreaterThan(2);
    }

    [Fact]
    public async Task Walls_of_a_survey_on_a_national_grid_stand_on_the_stations_stored_from_it()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // The same survey written in UTM zone 35N, well east of the zone's central meridian. This
        // format cannot name its own grid, so the upload does; nothing is said about where the
        // zero point is, because on a grid the file's own coordinates answer that.
        var content = new ByteArrayContent(PartlyMeasured(eastingM: 360_000, northingM: 5_042_000));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { content, "file", "OnTheGrid.lox" },
            { new StringContent("32635"), "sourceEpsg" },
            { new StringContent("1200"), "originHeightM" },
        };
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("anchorLongitude").GetDouble().ShouldBe(25.209, tolerance: 0.01);
        read.GetProperty("anchorLatitude").GetDouble().ShouldBe(45.519, tolerance: 0.01);

        using var anonymous = factory.CreateClient();
        var glb = await anonymous.GetByteArrayAsync(read.GetProperty("meshUrl").GetString()!);
        var stations = await StationsAsync(modelId);
        var vertices = WorldVertices(glb, read);

        // Grid north is more than a degree off true north here. The stations were turned onto
        // true north as they were stored; walls left on the grid, or turned twice, would miss A by
        // a third of a metre at this distance from the anchor. They miss it by nothing.
        NearestVertexM(vertices, stations["A"]).ShouldBeLessThan(0.01);
        NearestVertexM(vertices, stations["B"]).ShouldBeLessThan(0.01);
        NearestVertexM(vertices, stations["C"]).ShouldBeGreaterThan(5);
    }

    [Fact]
    public async Task A_survey_carrying_its_compilers_wall_surfaces_is_drawn_with_those()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Drawn.lox", WithWallSurface());
        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());

        // The two faces the file states, and not the thirty-two a tube round its measured leg
        // would be: the surfaces are the compiler's own model of the walls, and they win outright.
        read.GetProperty("triangleCount").GetInt32().ShouldBe(2);

        using var anonymous = factory.CreateClient();
        var local = SurveyFileSamples.GlbVertices(
            await anonymous.GetByteArrayAsync(read.GetProperty("meshUrl").GetString()!));

        // The file is in plain metres about its own zero, so the surface's corners come out where
        // the file put them.
        local.Count.ShouldBe(4);
        local.Any(v => Near(v, (4, 3, 0))).ShouldBeTrue();
        local.Any(v => Near(v, (0, 3, 2))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_survey_with_nothing_measured_is_read_and_simply_has_no_walls()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Bare.lox", NothingMeasured());
        await RunQueuedGraphJobAsync(modelId);

        // Not a failure and not a warning: a line plot with no wall surfaces and no passage
        // dimensions is the commonest kind there is.
        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        read.GetProperty("meshUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        read.GetProperty("triangleCount").ValueKind.ShouldBe(JsonValueKind.Null);
        read.GetProperty("meshSizeBytes").ValueKind.ShouldBe(JsonValueKind.Null);

        (await StationsAsync(modelId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Cross_sections_in_the_other_format_become_tubes_of_the_size_they_state()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // Three stations in a line due east, each measured half a metre to either wall and a
        // quarter of a metre to roof and floor.
        var modelId = await UploadLocalAsync(caveId, "Measured.3d", SurveyFileSamples.Survex3d(
            [new("a", 0, 0, 0), new("b", 10, 0, 0), new("c", 20, 0, 0)],
            [new((0, 0, 0), (10, 0, 0)), new((10, 0, 0), (20, 0, 0))],
            [[new("a", 0.5, 0.5, 0.25, 0.25), new("b", 0.5, 0.5, 0.25, 0.25), new("c", 0.5, 0.5, 0.25, 0.25)]]));
        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("format").GetString().ShouldBe("survex3d");

        // Three rings, two stretches between them, two closed ends.
        read.GetProperty("triangleCount").GetInt32().ShouldBe(48);

        using var anonymous = factory.CreateClient();
        var local = SurveyFileSamples.GlbVertices(
            await anonymous.GetByteArrayAsync(read.GetProperty("meshUrl").GetString()!));

        // The radius of every ring is the one that was measured. A tube of one metre around each
        // leg — which is what the reader's own mesher makes of this format, because it never looks
        // at these numbers — would stand twice as far out sideways and four times as far up.
        local.Max(v => Math.Abs(v.North)).ShouldBe(0.5, tolerance: 1e-5);
        local.Max(v => Math.Abs(v.Up)).ShouldBe(0.25, tolerance: 1e-5);
        local.Min(v => v.East).ShouldBe(0, tolerance: 1e-5);
        local.Max(v => v.East).ShouldBe(20, tolerance: 1e-5);
    }

    [Fact]
    public async Task Reading_the_file_again_replaces_its_walls_instead_of_adding_a_second_mesh()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Partly.lox", PartlyMeasured());
        await RunQueuedGraphJobAsync(modelId);

        var first = await MeshOfAsync(modelId);
        first.MeshFileId.ShouldNotBeNull();
        File.Exists(first.MeshPath).ShouldBeTrue();

        // Read again — after a crash, or by hand.
        await RunGraphJobAsync(modelId);

        var second = await MeshOfAsync(modelId);
        second.MeshFileId.ShouldNotBeNull();
        second.MeshFileId.ShouldNotBe(first.MeshFileId);

        // One mesh derived from the upload, and it is the one the model names. The first is gone
        // as a row and as bytes: left behind, every reading would strand another copy of the cave.
        second.DerivedFileCount.ShouldBe(1);
        File.Exists(first.MeshPath).ShouldBeFalse();
        File.Exists(second.MeshPath).ShouldBeTrue();
        StoredMeshCount().ShouldBe(1);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("triangleCount").GetInt32().ShouldBe(32);
        using var anonymous = factory.CreateClient();
        var glb = await anonymous.GetByteArrayAsync(read.GetProperty("meshUrl").GetString()!);
        Encoding.ASCII.GetString(glb, 0, 4).ShouldBe("glTF");
    }

    [Fact]
    public async Task Walls_that_cannot_be_stored_cost_the_survey_none_of_its_reading()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Partly.lox", PartlyMeasured());

        refuseMeshes = true;

        // Not expected to throw: the job is the reading, and the reading worked.
        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        read.GetProperty("meshUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        read.GetProperty("triangleCount").ValueKind.ShouldBe(JsonValueKind.Null);

        // Everything the file is uploaded for is there.
        (await StationsAsync(modelId)).Count.ShouldBe(5);
        (await PageAsync(owner, $"/api/v1/survey-models/{modelId}/shots?pageSize=100"))
            .GetProperty("totalItems").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task Walls_that_cannot_be_replaced_are_kept_and_the_reading_still_lands()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalAsync(caveId, "Partly.lox", PartlyMeasured());
        await RunQueuedGraphJobAsync(modelId);
        var first = await MeshOfAsync(modelId);

        // Something else has come to depend on the mesh the first reading built — here a second
        // record naming it as its own, which the database will not let be orphaned. So the old mesh
        // cannot be removed, and the statement that tries is refused in the middle of the
        // transaction that is carrying the new stations and legs.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var upload = await db.SurveyModels.Where(m => m.Id == modelId).Select(m => m.FileId).SingleAsync();
            db.SurveyModels.Add(new SurveyModel
            {
                CaveFeatureId = caveId,
                Name = "Holds the mesh",
                FileId = upload,
                Format = SurveyModelFormat.Stl,
                ConvertedFileId = first.MeshFileId,
            });
            await db.SaveChangesAsync();
        }

        await RunGraphJobAsync(modelId);

        // The reading committed: it is ready, it failed nothing, and its rows are one reading's
        // worth rather than none or two.
        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StationsAsync(modelId)).Count.ShouldBe(5);

        // And the model still has the mesh it had, which was built from these same bytes.
        var after = await MeshOfAsync(modelId);
        after.MeshFileId.ShouldBe(first.MeshFileId);
        after.DerivedFileCount.ShouldBe(1);
        File.Exists(first.MeshPath).ShouldBeTrue();
        read.GetProperty("triangleCount").GetInt32().ShouldBe(32);

        // The mesh the second reading built and could not record is not left in the store.
        StoredMeshCount().ShouldBe(1);
    }

    [Fact]
    public async Task Walls_of_a_cave_whose_location_is_protected_are_withheld_with_the_rest_of_its_record()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var modelId = await UploadLocalAsync(caveId, "Partly.lox", PartlyMeasured());
        await RunQueuedGraphJobAsync(modelId);

        // The positive half of the same question: the walls are there, and the cave's owner is
        // given them.
        var mine = await GetModelAsync(owner, modelId);
        mine.GetProperty("meshUrl").GetString().ShouldNotBeNullOrEmpty();
        mine.GetProperty("anchorLongitude").GetDouble().ShouldBe(OriginLongitude);

        // A viewer with no grant on the cave reads the cave and none of its models. The mesh holds
        // only offsets, but the record that carries its URL also carries the point those offsets
        // are measured from, so the whole record stays shut: no row in the list, nothing by id.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        listed.GetArrayLength().ShouldBe(0);
        (await reader.GetAsync($"/api/v1/survey-models/{modelId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- fixtures -----------------------------------------------------------

    /// <summary>
    /// A survey of three legs and one shot at the wall, in which only the first leg had its walls
    /// measured at both ends.
    ///
    /// <para>
    /// A to B is measured at both ends, differently, and not along an axis — so a mesh that stood
    /// on its stations by accident of symmetry would not pass. B to C is measured where it starts
    /// and not where it ends. C to D was never measured. B to W is flagged a shot at the wall and
    /// carries dimensions all the same, because a file may.
    /// </para>
    /// </summary>
    private static byte[] PartlyMeasured(double eastingM = 0, double northingM = 0)
    {
        const uint loxSplayBit = 16;
        var atA = new CaveLrud(Left: 1.5, Right: 0.5, Up: 2, Down: 0.3);
        var atB = new CaveLrud(Left: 0.8, Right: 1.2, Up: 1, Down: 1);
        var unmeasured = new CaveLrud(-1, -1, -1, -1);

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(eastingM, northingM, 0) },
                new CaveStation { Id = 2, Name = "B", Position = new CaveVector3(eastingM + 12, northingM + 5, -3) },
                new CaveStation { Id = 3, Name = "C", Position = new CaveVector3(eastingM + 20, northingM + 5, -3) },
                new CaveStation { Id = 4, Name = "D", Position = new CaveVector3(eastingM + 31, northingM + 9, -4) },
                new CaveStation { Id = 5, Name = "W", Position = new CaveVector3(eastingM + 12, northingM + 9, -3) },
            ],
            Shots =
            [
                Shot(1, 2, atA, atB),
                Shot(2, 3, atB, unmeasured),
                Shot(3, 4, unmeasured, unmeasured),
                Shot(2, 5, atB, atB, rawFlags: loxSplayBit),
            ],
        });
    }

    /// <summary>
    /// One measured leg, and beside it a wall surface of two faces as the compiler would have
    /// written one.
    /// </summary>
    private static byte[] WithWallSurface()
    {
        var measured = new CaveLrud(1, 1, 1, 1);
        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, Name = "B", Position = new CaveVector3(10, 0, 0) },
            ],
            Shots = [Shot(1, 2, measured, measured)],
            Scraps =
            [
                new CaveScrap
                {
                    Id = 1,
                    Points =
                    [
                        new CaveVector3(0, 0, 0), new CaveVector3(4, 0, 0),
                        new CaveVector3(4, 3, 0), new CaveVector3(0, 3, 2),
                    ],
                    Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3)],
                },
            ],
        });
    }

    /// <summary>A traverse of two legs with no wall surface and no dimension anywhere in it.</summary>
    private static byte[] NothingMeasured() =>
        LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, Name = "B", Position = new CaveVector3(10, 0, 0) },
                new CaveStation { Id = 3, Name = "C", Position = new CaveVector3(20, 4, -2) },
            ],
            Shots = [Shot(1, 2, null, null), Shot(2, 3, null, null)],
        });

    private static CaveShot Shot(uint from, uint to, CaveLrud? atStart, CaveLrud? atEnd, uint rawFlags = 0) =>
        new()
        {
            FromStationId = from,
            ToStationId = to,
            FromLrud = atStart,
            ToLrud = atEnd,
            RawFlags = rawFlags,
            SectionType = CaveShotSection.Oval,
        };

    // ---- reading the mesh back ----------------------------------------------

    /// <summary>
    /// Every vertex of a delivered mesh as a position in the world, worked out from the anchor the
    /// model's own record publishes — which is all a 3D scene has to place it by.
    ///
    /// <para>
    /// The metres-to-degrees step is written out here from the ellipsoid rather than borrowed from
    /// the code that placed the stations, so that agreeing with the stored stations is evidence
    /// and not an echo.
    /// </para>
    /// </summary>
    private static List<(double Longitude, double Latitude, double AltitudeM)> WorldVertices(
        byte[] glb, JsonElement model)
    {
        var longitude = model.GetProperty("anchorLongitude").GetDouble();
        var latitude = model.GetProperty("anchorLatitude").GetDouble();
        var height = model.GetProperty("anchorHeightM").GetDouble();
        var (metresPerDegreeEast, metresPerDegreeNorth) = MetresPerDegree(latitude);

        return [.. SurveyFileSamples.GlbVertices(glb).Select(v => (
            longitude + (v.East / metresPerDegreeEast),
            latitude + (v.North / metresPerDegreeNorth),
            height + v.Up))];
    }

    /// <summary>How far the nearest vertex is from a stored station, in metres.</summary>
    private static double NearestVertexM(
        List<(double Longitude, double Latitude, double AltitudeM)> vertices,
        (double Longitude, double Latitude, double AltitudeM) station)
    {
        var (metresPerDegreeEast, metresPerDegreeNorth) = MetresPerDegree(station.Latitude);
        return vertices.Min(v =>
        {
            var east = (v.Longitude - station.Longitude) * metresPerDegreeEast;
            var north = (v.Latitude - station.Latitude) * metresPerDegreeNorth;
            var up = v.AltitudeM - station.AltitudeM;
            return Math.Sqrt((east * east) + (north * north) + (up * up));
        });
    }

    /// <summary>
    /// The length of a degree of longitude and of latitude on the WGS 84 ellipsoid at a latitude,
    /// from its radii of curvature there.
    /// </summary>
    private static (double East, double North) MetresPerDegree(double latitudeDeg)
    {
        const double equatorialRadiusM = 6_378_137.0;
        const double flattening = 1 / 298.257223563;
        const double eccentricitySquared = flattening * (2 - flattening);

        var latitude = latitudeDeg * Math.PI / 180;
        var w = 1 - (eccentricitySquared * Math.Sin(latitude) * Math.Sin(latitude));
        var primeVertical = equatorialRadiusM / Math.Sqrt(w);
        var meridian = equatorialRadiusM * (1 - eccentricitySquared) / (w * Math.Sqrt(w));

        return (Math.PI / 180 * primeVertical * Math.Cos(latitude), Math.PI / 180 * meridian);
    }

    private static bool Near((double East, double North, double Up) vertex, (double East, double North, double Up) expected) =>
        Math.Abs(vertex.East - expected.East) < 1e-5
        && Math.Abs(vertex.North - expected.North) < 1e-5
        && Math.Abs(vertex.Up - expected.Up) < 1e-5;

    /// <summary>The stations of a model as its owner is given them, by name.</summary>
    private async Task<Dictionary<string, (double Longitude, double Latitude, double AltitudeM)>> StationsAsync(Guid modelId)
    {
        var page = await PageAsync(owner, $"/api/v1/survey-models/{modelId}/stations?pageSize=100");
        return page.GetProperty("items").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => (
                s.GetProperty("longitude").GetDouble(),
                s.GetProperty("latitude").GetDouble(),
                s.GetProperty("altitudeM").GetDouble()));
    }

    /// <summary>
    /// Which mesh a model names, where its bytes are, and how many files in all are derived from
    /// the model's upload — read off the rows, because "there is only one" is a fact about the
    /// database that no response states.
    /// </summary>
    private async Task<(Guid? MeshFileId, string? MeshPath, int DerivedFileCount)> MeshOfAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();

        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == modelId);
        var derived = await db.StoredFiles.AsNoTracking()
            .Where(f => f.ConvertedFromFileId == model.FileId)
            .ToListAsync();
        var mesh = derived.SingleOrDefault(f => f.Id == model.ConvertedFileId);

        return (
            model.ConvertedFileId,
            mesh is null ? null : store.GetAbsolutePath(mesh.StoragePath),
            derived.Count);
    }

    /// <summary>How many mesh files this class's store is holding, whatever any row says.</summary>
    private int StoredMeshCount() =>
        Directory.EnumerateFiles(filesRoot, "*.glb", SearchOption.AllDirectories).Count();

    // ---- driving the application --------------------------------------------

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Walls Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a survey in plain metres with the position of its zero point declared.</summary>
    private async Task<Guid> UploadLocalAsync(Guid caveId, string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { content, "file", fileName },
            { new StringContent(OriginLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)), "originLongitude" },
            { new StringContent(OriginLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)), "originLatitude" },
            { new StringContent(OriginHeightM.ToString(System.Globalization.CultureInfo.InvariantCulture)), "originHeightM" },
        };

        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<JsonElement> GetModelAsync(HttpClient client, Guid id) =>
        PageAsync(client, $"/api/v1/survey-models/{id}");

    /// <summary>
    /// Runs what one upload queued, the way the background worker would — that model's job and no
    /// other, because the queue is shared with every other test class and their files are not here.
    /// </summary>
    private async Task RunQueuedGraphJobAsync(Guid modelId)
    {
        var mine = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyGraph))
            .Where(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId)
            .ToList();
        mine.ShouldHaveSingleItem();

        await QueuedJob.RunAsync(factory.Services, mine[0].Id);
    }

    /// <summary>Runs the reading again for one model, as a re-run after a crash would.</summary>
    private async Task RunGraphJobAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyGraph);

        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyGraph,
                Payload = JsonSerializer.Serialize(
                    new SurveyGraphPayload(modelId), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    /// <summary>
    /// The file store, able to refuse one thing: a mesh. Storing anything else — the upload itself
    /// above all — goes to the real store untouched.
    /// </summary>
    private sealed class MeshRefusingFileStore(IFileStore inner, Func<bool> refusing) : IFileStore
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default) =>
            refusing() && extension == ".glb"
                ? throw new IOException("No space left on device")
                : inner.SaveAsync(content, extension, ct);

        public Task<long> AppendAsync(string storagePath, Stream content, CancellationToken ct = default) =>
            inner.AppendAsync(storagePath, content, ct);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct = default) =>
            inner.DeleteAsync(storagePath, ct);

        public string GetAbsolutePath(string storagePath) => inner.GetAbsolutePath(storagePath);
    }
}
