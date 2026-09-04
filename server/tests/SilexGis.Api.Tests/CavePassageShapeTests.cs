// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
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
/// The two read surfaces that answer how big a cave's passages are and what kind of cave their
/// shape suggests.
///
/// <para>
/// The load-bearing test in here is the null one. Everything about the volume rests on the claim
/// that a station whose surveyor never reached a wall leaves the estimate rather than contributing
/// a flattened sliver of it — and a volume that counts an unmeasured wall as zero is wrong in one
/// direction always and looks entirely plausible. So the fixture measures some walls and not
/// others, and what is asserted is that the unmeasured passage is missing from both the volume and
/// the length the volume claims to describe.
/// </para>
/// <para>
/// The other is that a maze and a branchwork of the same size receive different suggestions, and
/// that in both cases the label is the head of the scores the trace produced. A label the trace
/// does not support is the failure the whole design exists to prevent, and it cannot be caught by
/// looking at either half alone.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CavePassageShapeTests : IAsyncLifetime, IDisposable
{
    private const double OriginLongitude = 25.5;
    private const double OriginLatitude = 45.5;

    /// <summary>How this format writes "the surveyor did not reach this wall".</summary>
    private const double NotMeasured = -1d;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public CavePassageShapeTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // The queue lives in the container every test class shares; a drain started here would
            // claim work another class queued and fail it against storage this host does not have.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cps-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cps-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cps-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"cps-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The test the whole null path is worth: measured passage is measured, unmeasured passage is
    /// absent, and the answer says which is which.
    /// </summary>
    [Fact]
    public async Task A_station_missing_a_wall_leaves_the_volume_rather_than_counting_as_nothing()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, PartlyMeasuredTraverse());
        await RunQueuedGraphJobAsync(modelId);

        var answer = await CrossSectionAsync(owner, caveId);
        answer.GetProperty("basis").GetString().ShouldBe("surveyFlags");
        answer.GetProperty("hasReadings").GetBoolean().ShouldBeTrue();

        var summary = answer.GetProperty("summary");

        // Four stations were surveyed. Three carry a reading, and only two of those measured all
        // four walls — the third measured one side and no ceiling, which is a reading and is not a
        // cross-section.
        summary.GetProperty("stationCount").GetInt32().ShouldBe(3);
        summary.GetProperty("widthStationCount").GetInt32().ShouldBe(2);
        summary.GetProperty("areaStationCount").GetInt32().ShouldBe(2);

        // The fixture's two complete stations are four metres wide and two metres tall, so the
        // cross-section is a quarter-pi of eight and the median width is exactly four.
        summary.GetProperty("width").GetProperty("median").GetDouble().ShouldBe(4d, 1e-9);
        summary.GetProperty("height").GetProperty("median").GetDouble().ShouldBe(2d, 1e-9);

        var volume = summary.GetProperty("volume");

        // Three legs of fifty metres were offered. Exactly one of them has a cross-section at both
        // ends, so exactly one contributed — and the volume is that one leg's length times the
        // ellipse the two identical ends describe.
        volume.GetProperty("legCount").GetInt32().ShouldBe(3);
        volume.GetProperty("measuredLegCount").GetInt32().ShouldBe(1);
        volume.GetProperty("lengthM").GetDouble().ShouldBe(150d, 1d);
        volume.GetProperty("measuredLengthM").GetDouble().ShouldBe(50d, 1d);
        volume.GetProperty("volumeM3").GetDouble().ShouldBe(Math.PI / 4d * 8d * 50d, 1d);

        // And the figure that says how much of the cave that describes is a third of it, not all of
        // it. Without this the number above is unreadable: it is the same number whether the survey
        // measured every wall or two of them.
        volume.GetProperty("lengthFraction").GetDouble().ShouldBe(1d / 3d, 0.02);
    }

    /// <summary>
    /// A cave whose walls nobody measured is not a cave whose passages have no size.
    /// </summary>
    [Fact]
    public async Task A_survey_with_no_wall_distances_says_so_rather_than_reporting_nothing()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, Grid(3, unmeasured: true));
        await RunQueuedGraphJobAsync(modelId);

        var answer = await CrossSectionAsync(owner, caveId);
        answer.GetProperty("basis").GetString().ShouldBe("surveyFlags");
        answer.GetProperty("hasReadings").GetBoolean().ShouldBeFalse();
        answer.GetProperty("summary").ValueKind.ShouldBe(JsonValueKind.Null);

        // The pattern still has a network to read, so the two questions are answered
        // independently: not knowing how wide the passage is says nothing about its shape.
        var pattern = await PatternAsync(owner, caveId);
        pattern.GetProperty("network").GetProperty("cyclomaticNumber").GetInt32()
            .ShouldBeGreaterThan(0);
        pattern.GetProperty("suggestion").GetProperty("caveats").EnumerateArray()
            .Select(c => c.GetString()).ShouldContain("noCrossSections");
    }

    /// <summary>
    /// The suggestion the batch exists for: two caves of the same size and opposite shape, and a
    /// label that follows from the trace rather than sitting beside it.
    /// </summary>
    [Fact]
    public async Task A_maze_and_a_branchwork_are_suggested_differently_and_each_label_follows_its_trace()
    {
        var mazeCave = await CreateCaveAsync();
        var mazeModel = await UploadLocalLoxAsync(mazeCave, Grid(4, unmeasured: true));
        await RunQueuedGraphJobAsync(mazeModel);

        var branchCave = await CreateCaveAsync();
        var branchModel = await UploadLocalLoxAsync(branchCave, Branchwork());
        await RunQueuedGraphJobAsync(branchModel);

        var maze = await PatternAsync(owner, mazeCave);
        var branchwork = await PatternAsync(owner, branchCave);

        var mazeLabel = maze.GetProperty("suggestion").GetProperty("pattern").GetString();
        var branchLabel = branchwork.GetProperty("suggestion").GetProperty("pattern").GetString();
        mazeLabel.ShouldNotBe(branchLabel);

        // The network the rules read really is the opposite shape in each, so the difference above
        // is a reading of the caves rather than of two arbitrary answers.
        maze.GetProperty("network").GetProperty("cyclomaticNumber").GetInt32().ShouldBeGreaterThan(5);
        branchwork.GetProperty("network").GetProperty("cyclomaticNumber").GetInt32().ShouldBe(0);
        branchwork.GetProperty("network").GetProperty("extremityCount").GetInt32()
            .ShouldBeGreaterThan(maze.GetProperty("network").GetProperty("extremityCount").GetInt32());

        // The ring-closure rule is fed by the request path and not only by a hand-built figure. A
        // rule the shipped route can never assess is a constant wearing a rule's name, and only a
        // check through the route says which of the two this is.
        var ringOutcome = (JsonElement answer) => answer.GetProperty("suggestion").GetProperty("rules")
            .EnumerateArray().Single(r => r.GetProperty("rule").GetString() == "networkRings")
            .GetProperty("outcome").GetString();
        maze.GetProperty("network").GetProperty("clustering").GetDouble().ShouldBeGreaterThan(0);
        ringOutcome(maze).ShouldBe("fired");

        // And shown staying silent on a cave whose passages meet once and never again, which is the
        // difference between a rule and a figure that is always true.
        branchwork.GetProperty("network").GetProperty("clustering").GetDouble().ShouldBe(0);
        ringOutcome(branchwork).ShouldBe("didNotFire");

        // And each label is the head of its own scores. A label the trace does not support is the
        // one failure this design exists to prevent, and nothing but this comparison catches it.
        foreach (var answer in new[] { maze, branchwork })
        {
            var suggestion = answer.GetProperty("suggestion");
            var scores = suggestion.GetProperty("scores").EnumerateArray().ToList();
            scores.ShouldNotBeEmpty();
            suggestion.GetProperty("pattern").GetString()
                .ShouldBe(scores[0].GetProperty("kind").GetString());

            // Every rule is reported, whether or not it fired. A rule never shown staying silent is
            // not a rule, it is a constant.
            var rules = suggestion.GetProperty("rules").EnumerateArray().ToList();
            rules.Count.ShouldBe(10);
            rules.ShouldContain(r => r.GetProperty("outcome").GetString() == "fired");
            rules.ShouldContain(r => r.GetProperty("outcome").GetString() == "didNotFire");

            // The figures a rule read travel with it, so a reader can disagree with the rule rather
            // than with the verdict.
            rules.ShouldAllBe(r => r.GetProperty("figures").GetArrayLength() > 0);
        }
    }

    /// <summary>
    /// A wall distance is a measurement taken at a cave coordinate, so both routes are withheld
    /// from a caller who may read the cave but not place it exactly — and refused as "no such
    /// cave", not as "you may not".
    /// </summary>
    [Fact]
    public async Task A_reader_who_may_not_place_the_cave_is_told_there_is_no_such_cave()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var modelId = await UploadLocalLoxAsync(caveId, PartlyMeasuredTraverse());
        await RunQueuedGraphJobAsync(modelId);

        // The positive half, in the same test: the figures exist and the cave's owner reads them.
        (await CrossSectionAsync(owner, caveId)).GetProperty("hasReadings").GetBoolean()
            .ShouldBeTrue();
        (await PatternAsync(owner, caveId)).GetProperty("basis").GetString().ShouldBe("surveyFlags");

        // The viewer may read the cave itself.
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Not a 403. A refusal that distinguishes "you may not" from "there is nothing here" tells
        // somebody being kept from a hidden cave both that it exists and that it has been surveyed.
        foreach (var route in new[] { "cross-section", "pattern" })
        {
            var refused = await viewer.GetAsync($"/api/v1/caves/{caveId}/{route}");
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Code(refused)).ShouldBe("cave.not_found");

            // Word for word what a cave that was never created answers.
            var absent = await viewer.GetAsync($"/api/v1/caves/{Guid.NewGuid()}/{route}");
            absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Code(absent)).ShouldBe("cave.not_found");
        }
    }

    /// <summary>A cave the caller cannot see at all answers the same way.</summary>
    [Fact]
    public async Task A_cave_outside_the_readers_visibility_is_refused_identically()
    {
        var caveId = await CreateCaveAsync(visibility: "private");
        var modelId = await UploadLocalLoxAsync(caveId, PartlyMeasuredTraverse());
        await RunQueuedGraphJobAsync(modelId);

        // The owner's answer, so a route that answered nobody could not pass this.
        (await CrossSectionAsync(owner, caveId)).GetProperty("hasReadings").GetBoolean()
            .ShouldBeTrue();

        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        foreach (var route in new[] { "cross-section", "pattern" })
        {
            var refused = await viewer.GetAsync($"/api/v1/caves/{caveId}/{route}");
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Code(refused)).ShouldBe("cave.not_found");
        }
    }

    [Fact]
    public async Task Neither_route_answers_without_a_caller_or_without_a_cave()
    {
        var caveId = await CreateCaveAsync();

        foreach (var route in new[] { "cross-section", "pattern" })
        {
            (await anonymous.GetAsync($"/api/v1/caves/{caveId}/{route}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // The route constraint accepts the all-zero guid as a well-formed one, so the refusal
            // has to come from the validator rather than from the router.
            var empty = await owner.GetAsync($"/api/v1/caves/{Guid.Empty}/{route}");
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Code(empty)).ShouldBe("validation.failed");
        }
    }

    private async Task<JsonElement> CrossSectionAsync(HttpClient client, Guid caveId) =>
        await OkAsync(client, $"/api/v1/caves/{caveId}/cross-section");

    private async Task<JsonElement> PatternAsync(HttpClient client, Guid caveId) =>
        await OkAsync(client, $"/api/v1/caves/{caveId}/pattern");

    private static async Task<JsonElement> OkAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>
    /// Four stations in a line fifty metres apart, with the walls measured at the first two, half
    /// measured at the third and never measured at the fourth.
    /// </summary>
    /// <remarks>
    /// The three cases are all here on purpose. The complete pair is the only piece of passage that
    /// can be given a volume; the half-measured station is a reading that exists, is stored, and
    /// still yields no cross-section; and the unmeasured station carries the sentinel that has to
    /// stay absent rather than become a zero. A fixture with only the first and last would pass an
    /// implementation that counted any stored reading as a measured one.
    /// </remarks>
    private static byte[] PartlyMeasuredTraverse()
    {
        var stations = new List<CaveStation>();
        for (uint i = 0; i < 4; i++)
        {
            stations.Add(new CaveStation
            {
                Id = i + 1,
                Name = $"T{i}",
                Position = new CaveVector3(i * 50d, 0, 0),
            });
        }

        // Two metres to each side and one to the ceiling and the floor: four metres wide, two tall.
        var whole = new CaveLrud(2, 2, 1, 1);
        var half = new CaveLrud(2, NotMeasured, NotMeasured, NotMeasured);
        var none = new CaveLrud(NotMeasured, NotMeasured, NotMeasured, NotMeasured);

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = stations,
            Shots =
            [
                new CaveShot { FromStationId = 1, ToStationId = 2, FromLrud = whole, ToLrud = whole },
                new CaveShot { FromStationId = 2, ToStationId = 3, FromLrud = whole, ToLrud = half },
                new CaveShot { FromStationId = 3, ToStationId = 4, FromLrud = half, ToLrud = none },
            ],
        });
    }

    /// <summary>
    /// A square grid of passages: every interior station has four ways out and the network closes
    /// on itself repeatedly. Level, because a maze cut on joints does not descend.
    /// </summary>
    private static byte[] Grid(int side, bool unmeasured)
    {
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        var ids = new uint[side, side];
        uint next = 1;

        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
            {
                ids[x, y] = next++;
                stations.Add(new CaveStation
                {
                    Id = ids[x, y],
                    Name = $"G{x}_{y}",
                    Position = new CaveVector3(x * 30d, y * 30d, 0),
                });
            }
        }

        var walls = unmeasured ? (CaveLrud?)null : new CaveLrud(1, 1, 1, 1);
        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
            {
                if (x + 1 < side)
                {
                    shots.Add(new CaveShot
                    {
                        FromStationId = ids[x, y],
                        ToStationId = ids[x + 1, y],
                        FromLrud = walls,
                        ToLrud = walls,
                    });
                }

                if (y + 1 < side)
                {
                    shots.Add(new CaveShot
                    {
                        FromStationId = ids[x, y],
                        ToStationId = ids[x, y + 1],
                        FromLrud = walls,
                        ToLrud = walls,
                    });
                }
            }
        }

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = stations,
            Shots = shots,
        });
    }

    /// <summary>
    /// A trunk descending steeply with a side passage off every station: no loops, many dead ends,
    /// and the same order of size as the grid beside it.
    /// </summary>
    private static byte[] Branchwork()
    {
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        uint next = 1;

        var trunk = new uint[8];
        for (var i = 0; i < trunk.Length; i++)
        {
            trunk[i] = next++;
            stations.Add(new CaveStation
            {
                Id = trunk[i],
                Name = $"B{i}",
                Position = new CaveVector3(i * 30d, 0, -i * 20d),
            });

            if (i > 0)
            {
                shots.Add(new CaveShot { FromStationId = trunk[i - 1], ToStationId = trunk[i] });
            }
        }

        for (var i = 1; i < trunk.Length; i++)
        {
            var id = next++;
            stations.Add(new CaveStation
            {
                Id = id,
                Name = $"S{i}",
                Position = new CaveVector3(i * 30d, 25d, (-i * 20d) - 15d),
            });
            shots.Add(new CaveShot { FromStationId = trunk[i], ToStationId = id });
        }

        return LoxWriter.Write(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = stations,
            Shots = shots,
        });
    }

    private async Task<Guid> CreateCaveAsync(
        bool locationProtected = false,
        string visibility = "authenticated")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Shape Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadLocalLoxAsync(Guid caveId, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "Local.lox" } };
        form.Add(new StringContent(
            OriginLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "originLongitude");
        form.Add(new StringContent(
            OriginLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "originLatitude");
        form.Add(new StringContent("1000"), "originHeightM");

        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Runs what one upload queued, the way the background worker would.</summary>
    private async Task RunQueuedGraphJobAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyGraph);

        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph && j.Status == ProcessingJobStatus.Queued)
            .ToListAsync();

        // This model's job and no other: the queue is shared with every class in the collection.
        var mine = queued.Where(j =>
            JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId).ToList();
        mine.ShouldHaveSingleItem();

        foreach (var job in mine)
        {
            job.Status = ProcessingJobStatus.Succeeded;
            await handler.ExecuteAsync(job, CancellationToken.None);
        }

        await db.SaveChangesAsync();
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
