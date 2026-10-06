// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The wall meshes of the caves in a view: which mesh answers for a cave, which caves are in the
/// answer at all, in what order, and how many.
/// </summary>
/// <remarks>
/// <para>
/// Every test works in a box of its own, drawn somewhere on open ocean at random. The database is
/// the one every class shares and a box is a query over all of it, so a fixed position would count
/// whatever another class — or another test here — had anchored nearby.
/// </para>
/// <para>
/// Two hosts over that one database, because one of the things asserted is a limit and a limit is
/// only visible from a host that sets it below what the test can afford to create.
/// </para>
/// </remarks>
public sealed class CaveMeshesInViewTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>Half the side of a test's box, in degrees: a little over two kilometres.</summary>
    private const double HalfBox = 0.02;

    private const int CappedCaves = 2;
    private const int CappedMinZoom = 16;
    private const long CappedBytes = 4096;

    private readonly SilexGisApiFactory factory;
    private readonly SilexGisApiFactory capped;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient cappedOwner = null!;
    private Guid readerId;
    private long caveTypeId;
    private long karstAreaTypeId;

    public CaveMeshesInViewTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        var storage = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        };

        // No background worker in either host: the queue lives in the shared database, and a
        // drain started here would claim conversions another class queued and fail them against
        // storage this host does not have. Each test runs the jobs it queued itself.
        factory = new SilexGisApiFactory(postgres.ConnectionString, storage, JobWorkers.RemoveFrom);
        capped = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(storage)
            {
                ["Map:MeshesInViewMaxCaves"] = CappedCaves.ToString(CultureInfo.InvariantCulture),
                ["Map:MeshesInViewMinZoom"] = CappedMinZoom.ToString(CultureInfo.InvariantCulture),
                ["Map:MeshesInViewMaxBytes"] = CappedBytes.ToString(CultureInfo.InvariantCulture),
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cmv-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cmv-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cmv-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cmv-read-{suffix}@t.local");
        cappedOwner = await AuthHelper.BearerClientAsync(capped, $"cmv-own-{suffix}@t.local");
    }

    [Fact]
    public async Task Converted_meshes_in_the_box_answer_nearest_first_with_what_a_scene_budgets_by()
    {
        var spot = Spot.Somewhere();

        // Created far-to-near on purpose, so that an answer in creation order would be wrong.
        var outside = await CreateCaveWithMeshAsync(spot.North(0.05));
        var far = await CreateCaveWithMeshAsync(spot.North(0.012), heightM: 1420);
        var near = await CreateCaveWithMeshAsync(spot.North(0.001), heightM: 980);
        // A mesh that has been uploaded and placed but not converted: the anchor is on the row
        // already, and there is still nothing to draw.
        var pending = await CreateCaveWithMeshAsync(spot.North(-0.002), convert: false);

        var answer = await MeshesAsync(owner, spot);

        answer.Total.ShouldBe(2);
        answer.CaveIds.ShouldBe([near.CaveId, far.CaveId]);
        answer.CaveIds.ShouldNotContain(outside.CaveId);
        answer.CaveIds.ShouldNotContain(pending.CaveId);

        var first = answer.Items[0];
        first.GetProperty("surveyModelId").GetGuid().ShouldBe(near.ModelId);
        first.GetProperty("caveName").GetString().ShouldBe(near.CaveName);
        first.GetProperty("modelName").GetString().ShouldBe("Walls");
        first.GetProperty("anchorLongitude").GetDouble().ShouldBe(spot.Longitude, tolerance: 1e-9);
        first.GetProperty("anchorLatitude").GetDouble().ShouldBe(spot.Latitude + 0.001, tolerance: 1e-9);
        first.GetProperty("anchorHeightM").GetDouble().ShouldBe(980);
        first.GetProperty("triangleCount").GetInt32().ShouldBe(2);
        answer.Items[1].GetProperty("anchorHeightM").GetDouble().ShouldBe(1420);

        // The size is the converted file's own length — what there is to fetch — and agrees with
        // what the cave's own route says about the same mesh.
        var single = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{near.ModelId}");
        var sizeBytes = first.GetProperty("sizeBytes").GetInt64();
        sizeBytes.ShouldBeGreaterThan(0);
        sizeBytes.ShouldBe(single.GetProperty("meshSizeBytes").GetInt64());

        // And the address is one a scene can fetch with nothing but the address.
        using var anonymous = factory.CreateClient();
        var glb = await anonymous.GetByteArrayAsync(first.GetProperty("meshUrl").GetString()!);
        Encoding.ASCII.GetString(glb, 0, 4).ShouldBe("glTF");
        glb.LongLength.ShouldBe(sizeBytes);

        // Asked again, the same view answers the same.
        (await MeshesAsync(owner, spot)).CaveIds.ShouldBe(answer.CaveIds);
    }

    [Fact]
    public async Task The_mesh_chosen_as_current_answers_for_its_cave_whichever_was_uploaded_last()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateCaveWithMeshAsync(spot.North(0.001));
        var newer = await UploadMeshAsync(cave.CaveId, spot.North(0.002), heightM: 1000);
        await RunQueuedMeshJobAsync(newer);

        // The first mesh keeps the mark: a second one does not take over by arriving.
        (await MeshesAsync(owner, spot)).ModelIds.ShouldBe([cave.ModelId]);

        // Chosen by hand, the newer mesh answers: the mark decides and the upload order does not.
        (await owner.PutAsync($"/api/v1/survey-models/{newer}/current", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await MeshesAsync(owner, spot);
        answer.ModelIds.ShouldBe([newer]);
        // One mesh per cave: the cave is counted once, however many meshes it holds.
        answer.Total.ShouldBe(1);
    }

    /// <summary>
    /// The order of preference in full: the current wall mesh, else the current line plot where it
    /// has a mesh, else the newest model that has one.
    /// </summary>
    /// <remarks>
    /// The middle arm cannot be reached through an upload yet — nothing gives a line plot a mesh —
    /// so the rows are put into that state directly. The rule is asked of the stored columns, and
    /// those are what this arranges.
    /// </remarks>
    [Fact]
    public async Task A_cave_is_drawn_by_its_current_wall_mesh_then_its_current_line_plot_then_its_newest_mesh()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateCaveWithMeshAsync(spot.North(0.001));
        var oldMesh = cave.ModelId;
        var linePlot = await UploadLinePlotAsync(cave.CaveId);
        var newMesh = await UploadMeshAsync(cave.CaveId, spot.North(0.001), heightM: 1000);
        await RunQueuedMeshJobAsync(newMesh);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var meshFile = await db.SurveyModels.Where(m => m.Id == oldMesh)
                .Select(m => m.ConvertedFileId).SingleAsync();
            await db.SurveyModels.Where(m => m.Id == linePlot).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ConvertedFileId, meshFile)
                .SetProperty(m => m.Anchor, new Point(spot.Longitude, spot.Latitude + 0.001) { SRID = 4326 })
                .SetProperty(m => m.AnchorHeightM, 1000d));
        }

        // Both marks are held — by the first wall mesh and by the line plot — and the wall mesh wins.
        (await MeshesAsync(owner, spot)).ModelIds.ShouldBe([oldMesh]);

        // No current wall mesh: the current line plot answers, though a newer mesh exists.
        await SetAsync(oldMesh, isCurrent: false);
        (await MeshesAsync(owner, spot)).ModelIds.ShouldBe([linePlot]);

        // Nothing marked at all: the newest model that has a mesh.
        await SetAsync(linePlot, isCurrent: false);
        (await MeshesAsync(owner, spot)).ModelIds.ShouldBe([newMesh]);

        // A model with a file but no complete anchor has nothing to be placed by, and stops
        // counting as having a mesh.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyModels.Where(m => m.Id == newMesh).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AnchorHeightM, (double?)null));
        }

        (await MeshesAsync(owner, spot)).ModelIds.ShouldBe([linePlot]);
    }

    [Fact]
    public async Task A_cave_whose_current_mesh_is_anchored_outside_the_box_is_not_answered_by_an_older_one_inside()
    {
        var spot = Spot.Somewhere();
        var cave = await CreateCaveWithMeshAsync(spot.North(0.001));
        // The corrected export moved the cave out of this view, and was chosen as the cave's walls.
        var moved = await UploadMeshAsync(cave.CaveId, spot.North(0.2), heightM: 1000);
        await RunQueuedMeshJobAsync(moved);
        (await owner.PutAsync($"/api/v1/survey-models/{moved}/current", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await MeshesAsync(owner, spot);

        answer.Items.ShouldBeEmpty();
        answer.Total.ShouldBe(0);
    }

    [Fact]
    public async Task A_location_protected_cave_is_absent_and_uncounted_until_its_exact_location_is_granted()
    {
        var spot = Spot.Somewhere();
        var open = await CreateCaveWithMeshAsync(spot.North(0.004));
        var closed = await CreateCaveWithMeshAsync(spot.North(0.001), locationProtected: true);

        // The reader may read the protected cave itself; what is closed to them is where it is.
        (await reader.GetAsync($"/api/v1/caves/{closed.CaveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withheld = await MeshesAsync(reader, spot);
        withheld.CaveIds.ShouldBe([open.CaveId]);
        // Not counted either: a total of two beside one item would say a cave is being kept back.
        withheld.Total.ShouldBe(1);

        // Whoever owns it never lost sight of it.
        (await MeshesAsync(owner, spot)).CaveIds.ShouldBe([closed.CaveId, open.CaveId]);

        await GrantExactViewAsync(closed.CaveId);

        var granted = await MeshesAsync(reader, spot);
        granted.CaveIds.ShouldBe([closed.CaveId, open.CaveId]);
        granted.Total.ShouldBe(2);
    }

    [Fact]
    public async Task Protection_inherited_from_an_area_withholds_the_meshes_of_the_caves_inside_it()
    {
        var spot = Spot.Somewhere();
        var areaId = await CreateProtectedAreaAsync();
        var inside = await CreateCaveWithMeshAsync(spot.North(0.001), parentId: areaId);

        (await MeshesAsync(reader, spot)).Total.ShouldBe(0);

        // A grant on the cave is not a grant on the root above it.
        await GrantExactViewAsync(inside.CaveId);
        (await MeshesAsync(reader, spot)).Total.ShouldBe(0);

        await GrantExactViewAsync(areaId);
        (await MeshesAsync(reader, spot)).CaveIds.ShouldBe([inside.CaveId]);
    }

    [Fact]
    public async Task A_private_cave_and_a_deleted_one_are_absent_and_uncounted()
    {
        var spot = Spot.Somewhere();
        var shared = await CreateCaveWithMeshAsync(spot.North(0.001));
        var hidden = await CreateCaveWithMeshAsync(spot.North(0.002), visibility: "private");
        var doomed = await CreateCaveWithMeshAsync(spot.North(0.003));

        // The positive half first: all three are there for their owner, so what the reader is
        // refused below is the visibility rule and not a query that finds nothing here.
        (await MeshesAsync(owner, spot)).CaveIds.ShouldBe([shared.CaveId, hidden.CaveId, doomed.CaveId]);

        var outsider = await MeshesAsync(reader, spot);
        outsider.CaveIds.ShouldBe([shared.CaveId, doomed.CaveId]);
        outsider.Total.ShouldBe(2);

        (await owner.DeleteAsync($"/api/v1/caves/{doomed.CaveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterwards = await MeshesAsync(owner, spot);
        afterwards.CaveIds.ShouldBe([shared.CaveId, hidden.CaveId]);
        afterwards.Total.ShouldBe(2);
    }

    [Fact]
    public async Task The_cap_keeps_the_nearest_caves_and_still_says_how_many_there_were()
    {
        var spot = Spot.Somewhere();
        var third = await CreateCaveWithMeshAsync(spot.North(0.009));
        var first = await CreateCaveWithMeshAsync(spot.North(0.001));
        var second = await CreateCaveWithMeshAsync(spot.North(-0.004));

        // An installation with room for all three answers with all three, which is what makes the
        // two below a limit rather than the whole of what was there.
        (await MeshesAsync(owner, spot)).CaveIds.ShouldBe([first.CaveId, second.CaveId, third.CaveId]);

        var answer = await MeshesAsync(cappedOwner, spot);

        answer.CaveIds.ShouldBe([first.CaveId, second.CaveId]);
        answer.Total.ShouldBe(3);
    }

    [Fact]
    public async Task The_limits_a_scene_works_within_are_published_and_follow_the_configuration()
    {
        var shipped = await owner.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        shipped.GetProperty("meshesInViewMinZoom").GetInt32().ShouldBe(14);
        shipped.GetProperty("meshesInViewMaxCaves").GetInt32().ShouldBe(12);
        shipped.GetProperty("meshesInViewMaxBytes").GetInt64().ShouldBe(64L * 1024 * 1024);

        var configured = await cappedOwner.GetFromJsonAsync<JsonElement>("/api/v1/map/config");
        configured.GetProperty("meshesInViewMinZoom").GetInt32().ShouldBe(CappedMinZoom);
        configured.GetProperty("meshesInViewMaxCaves").GetInt32().ShouldBe(CappedCaves);
        configured.GetProperty("meshesInViewMaxBytes").GetInt64().ShouldBe(CappedBytes);
    }

    [Fact]
    public async Task The_route_refuses_a_caller_with_no_account_and_a_box_it_cannot_read()
    {
        var spot = Spot.Somewhere();

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/map/cave-meshes?bbox={spot.Box}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        foreach (var malformed in new[] { "not-a-box", "8.0,40.0", "8.0,40.0,east,41.0" })
        {
            var refused = await owner.GetAsync($"/api/v1/map/cave-meshes?bbox={malformed}");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("map.invalid_bbox");
        }
    }

    // ---- helpers ----

    /// <summary>The middle of one test's box, and positions measured from it.</summary>
    private sealed record Spot(double Longitude, double Latitude)
    {
        /// <summary>A point on open ocean nobody else is using.</summary>
        public static Spot Somewhere() => new(
            Math.Round(-170d + (Random.Shared.NextDouble() * 60d), 6),
            Math.Round(-60d + (Random.Shared.NextDouble() * 30d), 6));

        /// <summary>The same meridian, this many degrees of latitude further north.</summary>
        public Spot North(double degrees) => new(Longitude, Latitude + degrees);

        public string Box => string.Join(
            ',',
            new[] { Longitude - HalfBox, Latitude - HalfBox, Longitude + HalfBox, Latitude + HalfBox }
                .Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
    }

    private sealed record MeshCave(Guid CaveId, string CaveName, Guid ModelId);

    private sealed record Answer(IReadOnlyList<JsonElement> Items, int Total)
    {
        public IReadOnlyList<Guid> CaveIds => [.. Items.Select(i => i.GetProperty("caveId").GetGuid())];

        public IReadOnlyList<Guid> ModelIds => [.. Items.Select(i => i.GetProperty("surveyModelId").GetGuid())];
    }

    private static async Task<Answer> MeshesAsync(HttpClient client, Spot spot)
    {
        var response = await client.GetAsync($"/api/v1/map/cave-meshes?bbox={spot.Box}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Answer([.. body.GetProperty("items").EnumerateArray()], body.GetProperty("total").GetInt32());
    }

    /// <summary>A cave with one wall mesh anchored at the given position, converted unless told otherwise.</summary>
    private async Task<MeshCave> CreateCaveWithMeshAsync(
        Spot at,
        double heightM = 1000,
        string visibility = "authenticated",
        bool locationProtected = false,
        Guid? parentId = null,
        bool convert = true)
    {
        var name = $"Mesh Cave {Guid.NewGuid():N}"[..30];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var caveId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var modelId = await UploadMeshAsync(caveId, at, heightM);
        if (convert)
        {
            await RunQueuedMeshJobAsync(modelId);
        }

        return new MeshCave(caveId, name, modelId);
    }

    /// <summary>Uploads a mesh in plain metres about the given position, and leaves its conversion queued.</summary>
    private async Task<Guid> UploadMeshAsync(Guid caveId, Spot at, double heightM)
    {
        using var form = BuildForm("Walls.stl", LocalStl());
        form.Add(new StringContent(at.Longitude.ToString("R", CultureInfo.InvariantCulture)), "originLongitude");
        form.Add(new StringContent(at.Latitude.ToString("R", CultureInfo.InvariantCulture)), "originLatitude");
        form.Add(new StringContent(heightM.ToString("R", CultureInfo.InvariantCulture)), "originHeightM");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A line plot, stored unopened: its reading is queued and these tests never run it.</summary>
    private async Task<Guid> UploadLinePlotAsync(Guid caveId)
    {
        var bytes = Encoding.ASCII.GetBytes($"LOX-FIXTURE-{Guid.NewGuid():N}").Concat(new byte[128]).ToArray();
        using var form = BuildForm("Lines.lox", bytes);
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task SetAsync(Guid modelId, bool isCurrent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyModels.Where(m => m.Id == modelId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsCurrent, isCurrent));
    }

    /// <summary>
    /// Runs the one conversion an upload queued, the way the background worker would. One model's
    /// job and not every queued one: the queue is shared with every other class, whose files this
    /// host cannot read.
    /// </summary>
    private async Task RunQueuedMeshJobAsync(Guid modelId)
    {
        var mine = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyMesh))
            .Where(j => JsonSerializer.Deserialize<SurveyMeshPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId)
            .ToList();
        mine.ShouldNotBeEmpty();

        foreach (var queued in mine)
        {
            await QueuedJob.RunAsync(factory.Services, queued.Id);
        }
    }

    /// <summary>Grants the reader Read + ViewExactLocation on one feature, whatever its kind.</summary>
    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    /// <summary>A location-protected karst area: a protection root that is not a cave.</summary>
    private async Task<Guid> CreateProtectedAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Protected Karst {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = (object?)null,
            description = (string?)null,
            locationProtected = true,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Two triangles, twenty metres across, in plain metres about their own zero point.</summary>
    private static byte[] LocalStl()
    {
        (float X, float Y, float Z)[][] triangles =
        [
            [(-10, -10, 0), (10, -10, 0), (-10, 10, -5)],
            [(10, -10, 0), (10, 10, -5), (-10, 10, -5)],
        ];

        var bytes = new byte[84 + (triangles.Length * 50)];
        BitConverter.TryWriteBytes(bytes.AsSpan(80), triangles.Length);
        for (var t = 0; t < triangles.Length; t++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                // Twelve bytes of exporter face normal are left zero; the reader recomputes it.
                var at = 84 + (t * 50) + 12 + (corner * 12);
                BitConverter.TryWriteBytes(bytes.AsSpan(at), triangles[t][corner].X);
                BitConverter.TryWriteBytes(bytes.AsSpan(at + 4), triangles[t][corner].Y);
                BitConverter.TryWriteBytes(bytes.AsSpan(at + 8), triangles[t][corner].Z);
            }
        }

        return bytes;
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        capped.Dispose();
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
