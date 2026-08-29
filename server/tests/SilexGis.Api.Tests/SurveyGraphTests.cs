// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading an uploaded line-plot survey into station and shot rows: what arriving queues, what the
/// job stores, that the stored flags are the file's own answer rather than an inference from the
/// shape of the network, that running the job twice leaves one set of rows, and that a caller who
/// may not place the cave is told about none of it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SurveyGraphTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private long caveTypeId;

    public SurveyGraphTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // The queue lives in the container every test class shares, so a drain started here
            // would claim work another class queued and fail it against storage this host does not
            // have. This class runs its own jobs, deliberately, one at a time.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sgr-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sgr-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sgr-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sgr-read-{suffix}@t.local");
    }

    [Fact]
    public async Task A_line_plot_waits_on_arrival_and_becomes_stations_and_shots_when_the_job_runs()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // A real export in a projected national grid, which this format states itself, so the
        // upload is asked nothing and the file places itself.
        var modelId = await UploadAsync(caveId, "P8_Master.3d", Survex3dFixture());

        var pending = await GetModelAsync(owner, modelId);
        pending.GetProperty("status").GetString().ShouldBe("pending");
        pending.GetProperty("droppedShotCount").ValueKind.ShouldBe(JsonValueKind.Null);
        (await QueuedGraphJobCountAsync(modelId)).ShouldBe(1);

        await RunQueuedGraphJobAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("anchorLongitude").GetDouble().ShouldBeInRange(-10, 5);
        read.GetProperty("anchorLatitude").GetDouble().ShouldBeInRange(45, 62);

        // What the file holds, and what matching leg endpoints to stations by exact coordinate
        // equality could not keep. Both counts are recorded rather than left to be inferred.
        read.GetProperty("droppedShotCount").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        read.GetProperty("mergedStationCount").GetInt32().ShouldBeGreaterThanOrEqualTo(0);

        var stations = await PageAsync(owner, $"/api/v1/survey-models/{modelId}/stations?pageSize=500");
        stations.GetProperty("totalItems").GetInt32().ShouldBe(393);

        var shots = await PageAsync(owner, $"/api/v1/survey-models/{modelId}/shots?pageSize=500");
        shots.GetProperty("totalItems").GetInt32().ShouldBe(389);

        var first = stations.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        first.GetProperty("altitudeM").ValueKind.ShouldBe(JsonValueKind.Number);
    }

    [Fact]
    public async Task The_stored_splay_flag_is_what_the_file_said_and_not_what_the_network_looks_like()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, DisagreeingSplays());
        await RunQueuedGraphJobAsync(modelId);

        var shots = (await PageAsync(owner, $"/api/v1/survey-models/{modelId}/shots?pageSize=100"))
            .GetProperty("items").EnumerateArray().ToList();
        shots.Count.ShouldBe(5);

        var splays = shots
            .Where(s => s.GetProperty("isSplay").GetBoolean())
            .Select(s => $"{s.GetProperty("fromStationName").GetString()}-{s.GetProperty("toStationName").GetString()}")
            .ToList();

        // The file flagged exactly one leg a splay, and it is the one hanging alone off B.
        splays.ShouldBe(["B-P"]);

        // The fixture is built so that guessing from the shape of the network gives a different
        // answer, in both directions. A lone loose shot at a station reads as the tip of a passage
        // that ends there, so the guess keeps B-P — the one leg the file calls a splay; and two
        // loose shots at one station read as a fan, so the guess drops C-Q and C-R — the two legs
        // the file calls traverse. Without a disagreement like this the assertion above would pass
        // for a reading that never opened the file's flags at all.
        var guessed = CenterlineSkeleton.Build(AsLines(shots));
        var guessedLength = Enumerable.Range(0, guessed.NumGeometries)
            .Sum(i => guessed.GetGeometryN(i).Length);
        var surveyed = shots.Sum(s => s.GetProperty("lengthM").GetDouble());
        guessedLength.ShouldBeLessThan(surveyed);

        // And the counters say the network lost nothing: every leg the file calls traverse found
        // both of its stations.
        (await GetModelAsync(owner, modelId)).GetProperty("droppedShotCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Reading_the_same_file_twice_leaves_one_set_of_rows()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, DisagreeingSplays());

        await RunQueuedGraphJobAsync(modelId);
        var afterFirst = await CountRowsAsync(modelId);

        // A job is re-run after a crash, or by hand; the file has not changed, so neither has the
        // answer. Rows appended a second time would double every count computed over them.
        await RunGraphJobAsync(modelId);
        (await CountRowsAsync(modelId)).ShouldBe(afterFirst);
        afterFirst.ShouldBe((Stations: 6, Shots: 5));

        // And one centerline, rewritten rather than joined by a second claiming the same survey.
        (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_survey_becomes_a_centerline_whose_skeleton_follows_the_files_flags()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, DisagreeingSplays());
        await RunQueuedGraphJobAsync(modelId);

        var centerline = (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();
        centerline.GetProperty("source").GetString().ShouldBe("extracted");
        centerline.GetProperty("surveyModelId").GetGuid().ShouldBe(modelId);

        // The cave had no centerline before this one, so the survey becomes its shape on the map.
        centerline.GetProperty("isDefault").GetBoolean().ShouldBeTrue();

        // The geometry is the survey as measured: every leg the file drew, splays included.
        centerline.GetProperty("pathCount").GetInt32().ShouldBe(5);

        // The published length is not that geometry's length. It is how much passage the survey
        // found, so the one leg the file calls a wall shot is left out of it — which is the answer
        // every other centerline here gives, since one drawn by hand has no wall shots to leave
        // out. A whole-system export is mostly wall shots, so measuring the geometry instead would
        // put a 15 km cave on the page as a 240 km one.
        var legs = (await PageAsync(owner, $"/api/v1/survey-models/{modelId}/shots?pageSize=100"))
            .GetProperty("items").EnumerateArray().ToList();
        var measured = legs.Sum(s => s.GetProperty("lengthM").GetDouble());
        var passage = legs
            .Where(s => !s.GetProperty("isSplay").GetBoolean())
            .Sum(s => s.GetProperty("lengthM").GetDouble());

        var published = (double)centerline.GetProperty("lengthM").GetDecimal();
        published.ShouldBe(passage, tolerance: 0.5);
        published.ShouldBeLessThan(measured);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.Centerlines.AsNoTracking()
            .FirstAsync(c => c.SurveyModelId == modelId);

        // Four legs are passage by the file's own flags, and they sew into A-B-C plus the two
        // legs off C: three polylines. The guess, shown the same five legs, keeps only B-C —
        // it reads the splay at B as a passage tip and the two real legs at C as a fan — and
        // would store one. Three is the file's answer; one would be the guess's.
        stored.Skeleton.ShouldNotBeNull();
        stored.Skeleton.NumGeometries.ShouldBe(3);
        stored.SkeletonPathCount.ShouldBe(3);

        var surveyed = (MultiLineString)(await db.Features.AsNoTracking()
            .FirstAsync(f => f.Id == stored.Id)).Geom!;
        surveyed.NumGeometries.ShouldBe(5);
        CenterlineSkeleton.Build(surveyed).NumGeometries.ShouldBe(1);

        stored.Source.ShouldBe(CenterlineSource.Extracted);
    }

    [Fact]
    public async Task A_survey_of_a_cave_a_viewer_may_not_place_has_nothing_to_read()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var modelId = await UploadLocalLoxAsync(caveId, DisagreeingSplays());
        await RunQueuedGraphJobAsync(modelId);

        // The positive half of the same question, in the same test: the rows are there, and the
        // owner of the cave reads them.
        var mine = await PageAsync(owner, $"/api/v1/survey-models/{modelId}/stations");
        mine.GetProperty("totalItems").GetInt32().ShouldBe(6);

        // A genuine Viewer with no grant at all. The cave is readable to them and its stations are
        // its location, so the answer is an empty page rather than a refusal — and it is the same
        // empty page a survey model that does not exist gives, so it says nothing about which
        // caves are being kept from them.
        (await reader.GetAsync($"/api/v1/survey-models/{modelId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var withheld = await PageAsync(reader, $"/api/v1/survey-models/{modelId}/stations");
        withheld.GetProperty("totalItems").GetInt32().ShouldBe(0);
        withheld.GetProperty("items").GetArrayLength().ShouldBe(0);

        var noShots = await PageAsync(reader, $"/api/v1/survey-models/{modelId}/shots");
        noShots.GetProperty("totalItems").GetInt32().ShouldBe(0);

        var absent = await PageAsync(reader, $"/api/v1/survey-models/{Guid.NewGuid()}/stations");
        absent.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_survey_nobody_can_read_says_so_in_words_its_uploader_can_act_on()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // A file whose name claims a format its bytes are not.
        var modelId = await UploadLocalLoxAsync(caveId, [1, 0, 0, 0, 9, 9, 9, 9]);
        await RunQueuedGraphJobAsync(modelId, expectFailure: true);

        var failed = await GetModelAsync(owner, modelId);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("processingError").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_reading_the_database_refuses_leaves_the_survey_recorded_as_failed()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // A file this application cannot store: two different stations under one name, which the
        // station rows refuse because the name is what identifies them. The point is not the
        // collision — it is that the refusal arrives from the save, after a whole reading has been
        // staged, which is the one moment where recording the failure can itself fail.
        var modelId = await UploadLocalLoxAsync(caveId, CollidingStationNames());
        await RunQueuedGraphJobAsync(modelId, expectFailure: true);

        // Not still being read. A model left saying that is polled for ever by whoever uploaded it
        // and re-queued by every restart, with nothing anywhere saying why.
        var failed = await GetModelAsync(owner, modelId);
        failed.GetProperty("status").GetString().ShouldBe("failed", failed.ToString());

        // In our words, not the database's: nothing the uploader did produced this, so they are
        // told what is theirs to know and no more.
        failed.GetProperty("processingError").GetString().ShouldBe("The survey could not be read.");

        // And nothing half-written survives the attempt.
        (await CountRowsAsync(modelId)).ShouldBe((Stations: 0, Shots: 0));
        (await CenterlinesOfAsync(caveId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dimension_the_surveyor_did_not_measure_is_stored_as_nothing()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, MeasuredWalls());
        await RunQueuedGraphJobAsync(modelId);

        var reading = (await ReadingsOfAsync(modelId)).ShouldHaveSingleItem();
        reading.StationName.ShouldBe("A");

        reading.LeftM.ShouldBe(1.5);
        reading.RightM.ShouldBe(2.0);

        // The one that matters. The file says this was never measured, and it says so with a
        // negative number; stored as the number it would be a wall a metre behind the station, and
        // every width, height and volume computed over the cave afterwards would be wrong while
        // looking entirely like data.
        reading.UpM.ShouldBeNull();

        // And a genuine zero is not the same statement: the station stands against the floor. A rule
        // that treated the two alike would erase a measurement somebody took.
        reading.DownM.ShouldBe(0);

        reading.Section.ShouldBe(SurveySectionShape.Oval);

        // The leg this was measured along, named by an id the database assigned in the same save.
        reading.ShotId.ShouldNotBeNull();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.SurveyShots.AnyAsync(s => s.Id == reading.ShotId)).ShouldBeTrue();
        }

        // Read again — after a crash, or by hand — and there is still one reading, not two. The
        // readings have to be cleared before the legs are, because the readings the other format
        // states name no leg and so would not follow the legs out.
        await RunGraphJobAsync(modelId);
        (await ReadingsOfAsync(modelId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Cross_sections_keyed_by_station_alone_are_stored_with_no_leg_named()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // A real export in the other of the two formats, which states its own grid and so is asked
        // nothing. It carries its passage dimensions as runs of cross-sections keyed by station
        // name, with no leg anywhere in them.
        var modelId = await UploadAsync(caveId, "P8_Master.3d", Survex3dFixture());
        await RunQueuedGraphJobAsync(modelId);

        var readings = await ReadingsOfAsync(modelId);
        readings.Count.ShouldBe(155);

        // Nullable because of exactly this: the relationship does not exist in this format, and
        // filling it in would mean guessing which of a station's legs a reading belonged to.
        readings.ShouldAllBe(r => r.ShotId == null);
        readings.ShouldAllBe(r => r.Section == null);

        // Every reading stands at a station this same read stored, which is what makes the name a
        // usable key rather than a label nothing can be joined on.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stations = await db.SurveyStations
            .Where(s => s.SurveyModelId == modelId)
            .Select(s => s.Name)
            .ToListAsync();
        readings.Select(r => r.StationName).Except(stations).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_survey_takes_the_centerline_that_was_read_out_of_it()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, DisagreeingSplays());
        await RunQueuedGraphJobAsync(modelId);
        (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();

        (await owner.DeleteAsync($"/api/v1/survey-models/{modelId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // The extracted line is the file's own line work and nothing else; with the file gone it is
        // a machine-made shape nobody can trace to a survey, and it was the cave's shape on the map.
        (await CenterlinesOfAsync(caveId)).ShouldBeEmpty();

        // Which is what deleting a survey is usually for: uploading the corrected export. The new
        // reading becomes the cave's shape, rather than sitting beside a stale line that kept it.
        var again = await UploadLocalLoxAsync(caveId, DisagreeingSplays());
        await RunQueuedGraphJobAsync(again);

        var replacement = (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();
        replacement.GetProperty("surveyModelId").GetGuid().ShouldBe(again);
        replacement.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_survey_in_plain_metres_with_no_position_given_is_not_placed_somewhere()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // This format never states a coordinate system, so a survey written about a fixed station
        // has only what the uploader says. Nothing was said here.
        using var form = BuildForm("Unplaced.lox", DisagreeingSplays());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await RunQueuedGraphJobAsync(modelId, expectFailure: true);

        var failed = await GetModelAsync(owner, modelId);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("processingError").GetString().ShouldNotBeNull().ShouldContain("zero point");
    }

    /// <summary>
    /// A survey whose real splay flags contradict what the shape of its network suggests, in both
    /// directions.
    ///
    /// <para>
    /// A-B-C is passage. One leg B-P is flagged a splay and is the only loose shot at B, which is
    /// what the tip of a dead-end passage looks like. Two legs C-Q and C-R are flagged nothing at
    /// all and are both loose at C, which is what a fan of wall shots looks like. So a reading that
    /// inferred splays from the network would get both wrong, and a fixture where the two agree
    /// would not tell the two readings apart.
    /// </para>
    /// </summary>
    private static byte[] DisagreeingSplays()
    {
        const uint loxSplayBit = 16;

        (uint Id, string Name, double X, double Y)[] stations =
        [
            (1, "A", 0, 0), (2, "B", 10, 0), (3, "C", 20, 0),
            (4, "P", 10, 3), (5, "Q", 25, 4), (6, "R", 25, -4),
        ];

        (uint From, uint To, uint RawFlags)[] shots =
        [
            (1, 2, 0), (2, 3, 0), (2, 4, loxSplayBit), (3, 5, 0), (3, 6, 0),
        ];

        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                .. stations.Select(s => new CaveStation
                {
                    Id = s.Id,
                    Name = s.Name,
                    Position = new CaveVector3(s.X, s.Y, 0),
                }),
            ],
            Shots =
            [
                .. shots.Select(s => new CaveShot
                {
                    FromStationId = s.From,
                    ToStationId = s.To,
                    RawFlags = s.RawFlags,
                    Flags = s.RawFlags == loxSplayBit ? CaveShotFlags.Splay : CaveShotFlags.None,
                }),
            ],
        };

        return LoxWriter.Write(model);
    }

    /// <summary>
    /// A survey with the passage measured at one end of its one leg and not at the other, carrying
    /// all three cases at once: two wall distances measured, one measured as zero because the
    /// station stands against the floor, and one the surveyor never took.
    /// </summary>
    private static byte[] MeasuredWalls()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, Name = "B", Position = new CaveVector3(10, 0, 0) },
            ],
            Shots =
            [
                new CaveShot
                {
                    FromStationId = 1,
                    ToStationId = 2,
                    SectionType = CaveShotSection.Oval,

                    // -1 is how this format writes "not measured". The other format writes a
                    // different negative number for the same statement, which is why what is read
                    // is the sign and not the value.
                    FromLrud = new CaveLrud(Left: 1.5, Right: 2.0, Up: -1, Down: 0),
                    ToLrud = new CaveLrud(-1, -1, -1, -1),
                },
            ],
        };

        return LoxWriter.Write(model);
    }

    /// <summary>
    /// A survey naming two different stations the same thing — a file this application will read
    /// happily and then refuse to store, because a station's name is its identity within the model.
    /// </summary>
    private static byte[] CollidingStationNames()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 1, Name = "A", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, Name = "A", Position = new CaveVector3(10, 0, 0) },
            ],
            Shots = [new CaveShot { FromStationId = 1, ToStationId = 2 }],
        };

        return LoxWriter.Write(model);
    }

    /// <summary>The legs as drawn, which is all a reading with no flags to consult would have.</summary>
    private static MultiLineString AsLines(IEnumerable<JsonElement> shots) =>
        new(
            [
                .. shots.Select(s => new LineString(
                [
                    new CoordinateZ(
                        s.GetProperty("fromLongitude").GetDouble(),
                        s.GetProperty("fromLatitude").GetDouble(),
                        s.GetProperty("fromAltitudeM").GetDouble()),
                    new CoordinateZ(
                        s.GetProperty("toLongitude").GetDouble(),
                        s.GetProperty("toLatitude").GetDouble(),
                        s.GetProperty("toAltitudeM").GetDouble()),
                ])
                { SRID = 4326 }),
            ])
        { SRID = 4326 };

    private static byte[] Survex3dFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P8_Master.3d"));

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Graph Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadAsync(
        Guid caveId, string fileName, byte[] bytes, Action<MultipartFormDataContent>? declare = null)
    {
        using var form = BuildForm(fileName, bytes);
        declare?.Invoke(form);
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a survey in plain metres with the position of its zero point declared.</summary>
    private Task<Guid> UploadLocalLoxAsync(Guid caveId, byte[] bytes) =>
        UploadAsync(caveId, "Local.lox", bytes, form =>
        {
            form.Add(new StringContent("25.2"), "originLongitude");
            form.Add(new StringContent("45.5"), "originLatitude");
            form.Add(new StringContent("1200"), "originHeightM");
        });

    private static async Task<JsonElement> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> GetModelAsync(HttpClient client, Guid id) =>
        await PageAsync(client, $"/api/v1/survey-models/{id}");

    private async Task<int> QueuedGraphJobCountAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph && j.Status == ProcessingJobStatus.Queued)
            .ToListAsync();
        return queued.Count(j =>
            JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId);
    }

    /// <summary>The centerlines of a cave as its owner sees them.</summary>
    private async Task<List<JsonElement>> CenterlinesOfAsync(Guid caveId)
    {
        var response = await owner.GetAsync($"/api/v1/caves/{caveId}/centerlines");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    /// <summary>
    /// The passage dimensions as the database ends up holding them.
    ///
    /// <para>
    /// Read off the rows and not off anything rendered, deliberately. The fact under test is that a
    /// dimension the surveyor never measured is stored as nothing at all, and a rendered blank and a
    /// rendered zero are the same handful of pixels — a screen cannot tell the two apart, and the
    /// difference between them is a passage that exists and a passage that does not.
    /// </para>
    /// </summary>
    private async Task<List<SurveyLrud>> ReadingsOfAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyLruds
            .Where(l => l.SurveyModelId == modelId)
            .OrderBy(l => l.StationName)
            .ToListAsync();
    }

    private async Task<(int Stations, int Shots)> CountRowsAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (
            await db.SurveyStations.CountAsync(s => s.SurveyModelId == modelId),
            await db.SurveyShots.CountAsync(s => s.SurveyModelId == modelId));
    }

    /// <summary>
    /// Runs what one upload queued, the way the background worker would.
    ///
    /// <para>
    /// One model's job and not every queued one: the queue lives in the container every test class
    /// shares, and a class holds its stored files under a directory of its own — so running another
    /// class's job here reads its file out of a directory this host does not have, and fails a
    /// reading that was never this test's to run.
    /// </para>
    /// </summary>
    private async Task RunQueuedGraphJobAsync(Guid modelId, bool expectFailure = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyGraph);

        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph && j.Status == ProcessingJobStatus.Queued)
            .ToListAsync();

        var mine = queued.Where(j =>
            JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId).ToList();
        mine.ShouldHaveSingleItem();

        foreach (var job in mine)
        {
            job.Status = ProcessingJobStatus.Succeeded;
            try
            {
                await handler.ExecuteAsync(job, CancellationToken.None);
            }
            catch (Exception) when (expectFailure)
            {
                // The handler records the reason on the model and rethrows so the worker can record
                // the failure too; what this test is checking is the record it left behind.
                job.Status = ProcessingJobStatus.Failed;
            }
        }

        await db.SaveChangesAsync();
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
}
