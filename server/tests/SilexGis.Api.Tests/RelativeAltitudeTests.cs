// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// A line plot in local metres uploaded with a position for its zero point but no altitude for it.
///
/// <para>
/// Such a file is written about a station fixed at height zero, so read without the height its
/// heights are measured from a plane at 0 m and are not altitudes. The survey is still read and its
/// shape still measured, but every answer that sets its heights against something measured from sea
/// level — the terrain over it, a spring's altitude, another cave — has to refuse, because the
/// difference it would report is the missing height and not a fact about the cave. Each assertion
/// that something is refused sits beside the same question asked of a survey that was given its
/// height, which is answered.
/// </para>
/// </summary>
public sealed class RelativeAltitudeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string RelativeCode = "cave.altitudes_relative";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private long caveTypeId;
    private long springCaveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    public RelativeAltitudeTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // No drain in this host: one would claim the jobs these tests queue and run them on its
            // own schedule, a second writer beside the tests that run each job by hand.
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rel-own-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            // Overburden reads ground heights, which are gated by a right of their own that an
            // Editor does not hold by default. Without it the route refuses before it ever looks at
            // the survey, and the refusal asserted below would be the wrong one.
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = ownerId,
                Domain = AccessDomain.Terrain,
                Actions = AccessAction.Read,
                Effect = AccessEffect.Allow,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();

            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            springCaveTypeId = await db.CaveTypes
                .Where(t => t.Code == CaveTypeSeeds.SpringCave).Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"rel-own-{suffix}@t.local");
    }

    [Fact]
    public async Task A_survey_without_its_zero_point_height_keeps_its_shape_and_refuses_every_sea_level_comparison()
    {
        // A spring in the same area, which the owner may place, so the placed cave has a spring
        // line to draw and the relative one is refused one rather than merely having none.
        var area = await CreateAreaAsync();
        var spring = await CreateCaveAsync(area, springCaveTypeId);
        await AddEntranceAsync(spring, 800m, longitude: 25.1990);

        var relativeCave = await CreateCaveAsync(area);
        var relativeModel = await UploadLocalLoxAsync(relativeCave, heightM: null, longitude: 25.2000);
        await RunGraphJobAsync(relativeModel);

        var placedCave = await CreateCaveAsync(area);
        var placedModel = await UploadLocalLoxAsync(placedCave, heightM: 1200, longitude: 25.2010);
        await RunGraphJobAsync(placedModel);

        var otherPlacedCave = await CreateCaveAsync();
        var otherPlacedModel = await UploadLocalLoxAsync(otherPlacedCave, heightM: 1150, longitude: 25.2020);
        await RunGraphJobAsync(otherPlacedModel);

        // The reading still places the file at 0 m, as before, and says so on the model.
        (await ModelFlagAsync(relativeModel)).ShouldBe((true, 0d));
        (await ModelFlagAsync(placedModel)).ShouldBe((false, 1200d));
        (await ModelJsonAsync(relativeCave, relativeModel)).GetProperty("altitudesRelative").GetBoolean().ShouldBeTrue();
        (await ModelJsonAsync(placedCave, placedModel)).GetProperty("altitudesRelative").GetBoolean().ShouldBeFalse();

        // Overburden: terrain altitude minus a height about the survey's own zero would be the
        // terrain altitude itself, give or take the cave's relief — refused with the reason.
        var refused = await owner.GetAsync($"/api/v1/caves/{relativeCave}/overburden");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(RelativeCode);

        var answered = await owner.GetAsync($"/api/v1/caves/{placedCave}/overburden");
        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());

        // Hypsometry: the shape is still there — the file's passage spans ten metres of height,
        // A at 0 and B, C at +10, so the histogram exists — but it is flagged, and no spring line
        // (an altitude from sea level) is drawn across it.
        var relativeBands = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{relativeCave}/hypsometry");
        relativeBands.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        relativeBands.GetProperty("altitudesRelative").GetBoolean().ShouldBeTrue();
        relativeBands.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Object);
        relativeBands.GetProperty("springAltitudesM").GetArrayLength().ShouldBe(0);

        var placedBands = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{placedCave}/hypsometry");
        placedBands.GetProperty("altitudesRelative").GetBoolean().ShouldBeFalse();
        placedBands.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Object);
        placedBands.GetProperty("springAltitudesM").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        // The vertical gap to a neighbour: between the relative cave and a placed one it would be
        // the 1200 m nobody declared, so it is an absence with its own name; between two placed
        // caves it is measured. The two placed ones share a shape about zero planes 50 m apart,
        // the higher one's whole passage lies 40 m or more above the lower one's (1200..1210
        // against 1150..1160), so whichever two ends the shortest line joins, the vertical part of
        // it is at least that.
        var mixed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/caves/{relativeCave}/closest-approach/{placedCave}");
        mixed.GetProperty("absence").GetString().ShouldBe("altitudesRelative");
        mixed.GetProperty("distanceM").ValueKind.ShouldBe(JsonValueKind.Null);
        mixed.GetProperty("verticalDistanceM").ValueKind.ShouldBe(JsonValueKind.Null);

        var both = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/caves/{placedCave}/closest-approach/{otherPlacedCave}");
        both.GetProperty("absence").GetString().ShouldBe("none");
        both.GetProperty("verticalDistanceM").GetDouble().ShouldBeGreaterThanOrEqualTo(40 - 0.01);

        // The nearest-pairs table over a box holding all three: the two placed caves are paired,
        // and no pair names the relative one, whose heights would be the gap between two datums.
        var table = await owner.GetFromJsonAsync<JsonElement>(
            "/api/v1/caves/closest-approaches?west=25.19&south=45.49&east=25.215&north=45.51&maxDistanceM=2000");
        var pairs = table.GetProperty("pairs").EnumerateArray()
            .Select(p => (A: p.GetProperty("caveAId").GetGuid(), B: p.GetProperty("caveBId").GetGuid()))
            .ToList();
        pairs.ShouldContain(p => (p.A == placedCave && p.B == otherPlacedCave) || (p.A == otherPlacedCave && p.B == placedCave));
        pairs.ShouldNotContain(p => p.A == relativeCave || p.B == relativeCave);
    }

    [Fact]
    public async Task A_relative_survey_whose_first_leg_is_level_at_its_zero_point_still_has_its_shape_measured()
    {
        // The first leg runs level along the zero plane, A (0) to B (0), then the passage falls:
        // B to C 20 m down and C to D 20 m more. Judged leg by leg, the first one looks like a
        // plan drawing; judged for the survey, it is a level leg of a cave with a 40 m relief.
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, heightM: null, longitude: 25.2040, LevelFirstSurvey());
        await RunGraphJobAsync(modelId);
        (await ModelFlagAsync(modelId)).AltitudesRelative.ShouldBeTrue();

        var bands = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/hypsometry");
        bands.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        bands.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Object);

        var orientation = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/orientation");
        orientation.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        orientation.GetProperty("dip").ValueKind.ShouldBe(JsonValueKind.Object);

        var profile = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/depth-profile");
        profile.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();

        // The level leg itself: no rise, a dip of exactly 0 and a mid-height of 0 — not nulls.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var legs = await SilexGis.Infrastructure.Surveys.SurveySegmentSql.ForModelAsync(db, modelId, CancellationToken.None);
        var level = legs.Single(l => l.FromStationName == "A" || l.ToStationName == "A");
        level.HasZ.ShouldBeTrue();
        level.DeltaZM.ShouldBe(0d);
        level.DipDegrees.ShouldBe(0d);
        level.MidZM.ShouldBe(0d);
        legs.ShouldAllBe(l => l.HasZ);
    }

    [Fact]
    public async Task System_figures_leave_out_the_survey_comparison_for_a_relative_survey_and_keep_the_entrance_figures()
    {
        // The rising survey spans 0..10 m about its zero point. Placed at 1200 m it spans
        // 1200..1210 m, so under an entrance at 1205 m the total depth is 1205 - 1200 = 5 m and the
        // height above that entrance 1210 - 1205 = 5 m. Left relative, the same subtraction would
        // answer 1205 - 0 = 1205 m of depth, which is the missing datum and nothing else.
        var placedCave = await CreateCaveAsync();
        await AddEntranceAsync(placedCave, 1205m, longitude: 25.2050);
        await RunGraphJobAsync(await UploadLocalLoxAsync(placedCave, heightM: 1200, longitude: 25.2050));

        var relativeCave = await CreateCaveAsync();
        await AddEntranceAsync(relativeCave, 1205m, longitude: 25.2060);
        await RunGraphJobAsync(await UploadLocalLoxAsync(relativeCave, heightM: null, longitude: 25.2060));

        var placed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{placedCave}/system-figures");
        placed.GetProperty("altitudesRelative").GetBoolean().ShouldBeFalse();
        placed.GetProperty("figures").GetProperty("totalDepthM").GetDouble().ShouldBe(5, 0.01);
        placed.GetProperty("figures").GetProperty("heightAboveLowestEntranceM").GetDouble().ShouldBe(5, 0.01);
        placed.GetProperty("surveyLowestM").GetDouble().ShouldBe(1200, 0.01);

        var relative = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{relativeCave}/system-figures");
        relative.GetProperty("altitudesRelative").GetBoolean().ShouldBeTrue();
        relative.GetProperty("hasAltitudes").GetBoolean().ShouldBeTrue();
        relative.GetProperty("figures").GetProperty("totalDepthM").ValueKind.ShouldBe(JsonValueKind.Null);
        relative.GetProperty("figures").GetProperty("heightAboveLowestEntranceM").ValueKind.ShouldBe(JsonValueKind.Null);
        relative.GetProperty("surveyHighestM").ValueKind.ShouldBe(JsonValueKind.Null);
        relative.GetProperty("surveyLowestM").ValueKind.ShouldBe(JsonValueKind.Null);
        relative.GetProperty("figures").GetProperty("highestEntranceM").GetDouble().ShouldBe(1205, 0.01);
    }

    [Fact]
    public async Task Reading_the_file_again_keeps_it_relative_and_declaring_the_height_makes_its_altitudes_real()
    {
        var caveId = await CreateCaveAsync();
        var modelId = await UploadLocalLoxAsync(caveId, heightM: null, longitude: 25.2030);
        var firstJob = await RunGraphJobAsync(modelId);
        (await ModelFlagAsync(modelId)).ShouldBe((true, 0d));

        // A second reading of the same declaration finds the 0 the first one stored: the flag,
        // not the stored height, is what remembers that no height was ever given.
        await QueuedJob.RunAgainAsync(factory.Services, firstJob);
        (await ModelFlagAsync(modelId)).ShouldBe((true, 0d));

        var placed = await owner.PutAsJsonAsync($"/api/v1/survey-models/{modelId}/placement", new
        {
            originLongitude = 25.2030,
            originLatitude = 45.5,
            originHeightM = 900d,
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());

        // Declared but not yet read again: the stored legs are still measured from 0 m, so the
        // model still says its heights are relative and overburden still refuses.
        (await ModelFlagAsync(modelId)).ShouldBe((true, 900d));
        var early = await owner.GetAsync($"/api/v1/caves/{caveId}/overburden");
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict, await early.Content.ReadAsStringAsync());
        (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(RelativeCode);

        await RunGraphJobAsync(modelId);

        (await ModelFlagAsync(modelId)).ShouldBe((false, 900d));
        var overburden = await owner.GetAsync($"/api/v1/caves/{caveId}/overburden");
        overburden.StatusCode.ShouldBe(HttpStatusCode.OK, await overburden.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_projected_survey_uploaded_without_a_height_keeps_the_heights_it_carries()
    {
        // A file on a projected grid states where it is itself, heights included, so leaving the
        // height out is not the local-coordinates case and the file is read as it always was.
        var caveId = await CreateCaveAsync();
        var form = BuildForm(RisingSurvey(eastOffset: 500_000, northOffset: 5_000_000));
        form.Add(new StringContent("32635"), "sourceEpsg");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await RunGraphJobAsync(modelId);

        (await ModelFlagAsync(modelId)).AltitudesRelative.ShouldBeFalse();
    }

    private async Task<(bool AltitudesRelative, double? AnchorHeightM)> ModelFlagAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == modelId);
        return (model.AltitudesRelative, model.AnchorHeightM);
    }

    private async Task<JsonElement> ModelJsonAsync(Guid caveId, Guid modelId)
    {
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        var items = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("items");
        return items.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == modelId);
    }

    private async Task<Guid> CreateCaveAsync(Guid? parent = null, long? typeId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Relative Cave {Guid.NewGuid():N}"[..30],
            caveTypeId = typeId ?? caveTypeId,
            parentId = parent,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Relative Area {Guid.NewGuid():N}"[..36],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.19, 45.49 }, new[] { 25.215, 45.49 },
                        new[] { 25.215, 45.51 }, new[] { 25.19, 45.51 },
                        new[] { 25.19, 45.49 },
                    },
                },
            },
            locationProtected = false,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddEntranceAsync(Guid caveId, decimal altitude, double longitude)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { longitude, 45.5 } },
            altitude,
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Uploads <see cref="RisingSurvey"/> in plain metres with its zero point at the given longitude,
    /// and its height only when one is given.
    /// </summary>
    private async Task<Guid> UploadLocalLoxAsync(Guid caveId, double? heightM, double longitude, byte[]? file = null)
    {
        using var form = BuildForm(file ?? RisingSurvey(eastOffset: 0, northOffset: 0));
        form.Add(new StringContent(longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), "originLongitude");
        form.Add(new StringContent("45.5"), "originLatitude");
        if (heightM is { } height)
        {
            form.Add(new StringContent(height.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), "originHeightM");
        }

        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildForm(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", "Rising.lox" } };
    }

    /// <summary>
    /// Runs the newest reading queued for this model, the way the background worker would, and
    /// answers its job id.
    /// </summary>
    private async Task<long> RunGraphJobAsync(Guid modelId)
    {
        // This model's job and no other: a class keeps its stored files under a directory of its
        // own, so running another model's job reads a file this host does not have.
        var newest = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyGraph))
            .First(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId);

        await QueuedJob.RunAsync(factory.Services, newest.Id);
        return newest.Id;
    }

    /// <summary>
    /// Four stations: A at the fixed zero, B a hundred metres east of it and level (so the first
    /// leg lies on the zero plane at both ends), C a hundred metres north of B and 20 m lower, D a
    /// hundred metres east of C and 20 m lower again.
    /// </summary>
    private static byte[] LevelFirstSurvey()
    {
        (uint Id, string Name, double X, double Y, double Z)[] stations =
        [
            (1, "A", 0, 0, 0),
            (2, "B", 100, 0, 0),
            (3, "C", 100, 100, -20),
            (4, "D", 200, 100, -40),
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
                    Position = new CaveVector3(s.X, s.Y, s.Z),
                }),
            ],
            Shots =
            [
                new CaveShot { FromStationId = 1, ToStationId = 2 },
                new CaveShot { FromStationId = 2, ToStationId = 3 },
                new CaveShot { FromStationId = 3, ToStationId = 4 },
            ],
        };

        return LoxWriter.Write(model);
    }

    /// <summary>
    /// Three stations: A at the fixed zero, B a hundred metres east and ten metres up, C a hundred
    /// metres north of B and level. The first leg rises, so no leg of this file lies on the zero
    /// plane at both ends.
    /// </summary>
    private static byte[] RisingSurvey(double eastOffset, double northOffset)
    {
        (uint Id, string Name, double X, double Y, double Z)[] stations =
        [
            (1, "A", eastOffset, northOffset, 0),
            (2, "B", eastOffset + 100, northOffset, 10),
            (3, "C", eastOffset + 100, northOffset + 100, 10),
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
                    Position = new CaveVector3(s.X, s.Y, s.Z),
                }),
            ],
            Shots =
            [
                new CaveShot { FromStationId = 1, ToStationId = 2 },
                new CaveShot { FromStationId = 2, ToStationId = 3 },
            ],
        };

        return LoxWriter.Write(model);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
