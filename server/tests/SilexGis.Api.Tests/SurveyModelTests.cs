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

namespace SilexGis.Api.Tests;

/// <summary>
/// 3D survey models end-to-end: upload/list/get/update/delete under the cave's access rules, the
/// anonymous delivery URL, and the location-protection rule — a model is withheld entirely
/// unless the caller may view the exact location of every protected feature above its cave,
/// whether that root is the cave itself or an area containing it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SurveyModelTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;
    private long karstAreaTypeId;

    public SurveyModelTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"svm-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"svm-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"svm-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"svm-read-{suffix}@t.local");
    }

    [Fact]
    public async Task Survey_model_lifecycle_with_anonymous_delivery()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var bytes = FakeLox();

        // Wrong extension is rejected; the reader (no Write on the cave) may not upload.
        using (var badForm = BuildForm("model.zip", bytes))
        {
            (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", badForm))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var readerForm = BuildForm("model.lox", bytes))
        {
            (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-models", readerForm))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var form = BuildForm("Pestera Mare.lox", bytes);
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var model = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        model.GetProperty("name").GetString().ShouldBe("Pestera Mare");
        model.GetProperty("format").GetString().ShouldBe("lox");
        // A survey model hangs off the cave's FEATURE id — the cave is a feature like any other.
        model.GetProperty("caveId").GetGuid().ShouldBe(caveId);
        var id = model.GetProperty("id").GetGuid();

        // Any cave reader sees the model in the list and gets a working delivery URL.
        var list = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        list.GetArrayLength().ShouldBe(1);
        var modelUrl = list[0].GetProperty("modelUrl").GetString()!;
        using var anonymous = factory.CreateClient();
        var streamed = await anonymous.GetByteArrayAsync(modelUrl);
        streamed.ShouldBe(bytes);

        // Single GET carries an ETag; metadata update honors If-Match.
        var single = await reader.GetAsync($"/api/v1/survey-models/{id}");
        single.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = single.Headers.ETag.ShouldNotBeNull().Tag;

        using (var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/survey-models/{id}"))
        {
            stale.Headers.TryAddWithoutValidation("If-Match", "\"12345678\"");
            stale.Content = JsonContent.Create(new { name = "Renamed", description = (string?)null, surveyedAt = (string?)null });
            (await owner.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        }

        using (var fresh = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/survey-models/{id}"))
        {
            fresh.Headers.TryAddWithoutValidation("If-Match", etag);
            fresh.Content = JsonContent.Create(new { name = "Main survey", description = "2024 resurvey", surveyedAt = "2024-08-15" });
            var updated = await owner.SendAsync(fresh);
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        }

        // The reader may not update or delete; the owner may.
        (await reader.PutAsJsonAsync($"/api/v1/survey-models/{id}",
            new { name = "Nope", description = (string?)null, surveyedAt = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Models_of_protected_caves_are_withheld_without_exact_location()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        var id = await UploadAsync(caveId, "secret-system.3d");

        // The cave itself stays readable, but its models are the location: empty list, 404 by id.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var hidden = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        hidden.GetArrayLength().ShouldBe(0);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Write paths do not disclose it either: 404 rather than 403.
        (await reader.PutAsJsonAsync($"/api/v1/survey-models/{id}",
            new { name = "Nope", description = (string?)null, surveyedAt = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner sees everything.
        var owned = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        owned.GetArrayLength().ShouldBe(1);

        // An explicit ViewExactLocation grant on the protected root flips them visible.
        await GrantExactViewAsync(caveId);

        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        granted.GetArrayLength().ShouldBe(1);
        granted[0].GetProperty("format").GetString().ShouldBe("survex3d");
        granted[0].GetProperty("caveId").GetGuid().ShouldBe(caveId);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_protected_parent_area_withholds_the_models_of_the_caves_inside_it()
    {
        // The protection root need not be the cave: an unprotected cave inside a protected
        // karst area inherits the protection, and only a grant on the AREA lifts it.
        var areaId = await CreateProtectedAreaAsync();
        var caveId = await CreateCaveAsync(
            visibility: "authenticated", locationProtected: false, parentId: areaId);
        var id = await UploadAsync(caveId, "inside-area.lox");

        var cave = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}");
        cave.GetProperty("locationProtected").GetBoolean().ShouldBeFalse();
        cave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models"))
            .GetArrayLength().ShouldBe(0);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A grant on the cave is not a grant on the root above it, so it reveals nothing.
        await GrantExactViewAsync(caveId);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models"))
            .GetArrayLength().ShouldBe(0);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The grant on the area does.
        await GrantExactViewAsync(areaId);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models"))
            .GetArrayLength().ShouldBe(1);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The row owner never lost sight of their own cave under a protected area.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models"))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Private_cave_models_are_not_disclosed_to_outsiders()
    {
        var caveId = await CreateCaveAsync(visibility: "private", locationProtected: false);
        var id = await UploadAsync(caveId, "private.lox");

        // Existence non-disclosure: 404 (not 403) on every path.
        (await reader.GetAsync($"/api/v1/caves/{caveId}/survey-models")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var outsiderForm = BuildForm("intrusion.lox", FakeLox());
        (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-models", outsiderForm))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_cave_takes_its_survey_models_out_of_every_read_path()
    {
        // Cave deletion is soft; the model rows survive it in storage, but a deleted cave
        // resolves nowhere, so nothing hanging off it may be read or written any more.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var id = await UploadAsync(caveId, "doomed.lox");

        (await owner.DeleteAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.GetAsync($"/api/v1/caves/{caveId}/survey-models")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    /// <summary>Uploads one survey model as the owner and returns its id.</summary>
    private async Task<Guid> UploadAsync(Guid caveId, string fileName)
    {
        using var form = BuildForm(fileName, FakeLox());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
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

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Survey Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            parentId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_wall_mesh_is_placed_in_the_world_by_the_conversion_it_queues()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        // A mesh in a projected national grid — coordinates that say where they are, in a system
        // the file itself has no way to name.
        using var form = BuildForm("Walls.stl", ProjectedStl());
        form.Add(new StringContent("32635"), "sourceEpsg");
        form.Add(new StringContent("1100"), "originHeightM");

        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var model = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        model.GetProperty("format").GetString().ShouldBe("stl");
        // Nothing is drawable yet, and the response says so rather than implying otherwise.
        model.GetProperty("status").GetString().ShouldBe("pending");
        model.GetProperty("meshUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = model.GetProperty("id").GetGuid();

        await RunQueuedMeshJobsAsync();

        var converted = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{id}");
        converted.GetProperty("status").GetString().ShouldBe("ready");
        converted.GetProperty("meshUrl").GetString().ShouldNotBeNullOrEmpty();
        // Read as zone 35N these coordinates are in the Romanian Carpathians. Read as its western
        // neighbour they are in Serbia, which is why the system is declared rather than guessed.
        converted.GetProperty("anchorLongitude").GetDouble().ShouldBe(25.209, tolerance: 0.01);
        converted.GetProperty("anchorLatitude").GetDouble().ShouldBe(45.519, tolerance: 0.01);
        converted.GetProperty("anchorHeightM").GetDouble().ShouldBe(1100);
        converted.GetProperty("triangleCount").GetInt32().ShouldBe(2);

        // The converted mesh is delivered by the same signed, anonymous URL as everything else.
        using var anonymous = factory.CreateClient();
        var glb = await anonymous.GetByteArrayAsync(converted.GetProperty("meshUrl").GetString()!);
        Encoding.ASCII.GetString(glb, 0, 4).ShouldBe("glTF");
    }

    [Fact]
    public async Task A_wall_mesh_with_nothing_to_place_it_by_is_refused()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        // No coordinate system and no position: there is no answer to where this cave is, and
        // storing it anyway would leave a model that nothing can ever draw.
        using var form = BuildForm("Walls.stl", ProjectedStl());
        form.Add(new StringContent("1100"), "originHeightM");

        var refused = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("survey_model.origin_invalid");
    }

    [Fact]
    public async Task A_wall_mesh_that_cannot_be_read_says_why_instead_of_staying_silent()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var form = BuildForm("Walls.stl", FakeLox()); // not an STL at all
        form.Add(new StringContent("25.4472"), "originLongitude");
        form.Add(new StringContent("45.5312"), "originLatitude");
        form.Add(new StringContent("1100"), "originHeightM");
        var id = JsonDocument.Parse(
                await (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form))
                    .Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        await RunQueuedMeshJobsAsync(expectFailure: true);

        var failed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/survey-models/{id}");
        failed.GetProperty("status").GetString().ShouldBe("failed");
        // The reason is the uploader's to act on — this is a file they can re-export.
        failed.GetProperty("processingError").GetString().ShouldNotBeNullOrEmpty();
    }

    /// <summary>Runs whatever the upload queued, the way the background worker would.</summary>
    private async Task RunQueuedMeshJobsAsync(bool expectFailure = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyMesh);

        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.SurveyMesh && j.Status == ProcessingJobStatus.Queued)
            .ToListAsync();

        foreach (var job in queued)
        {
            try
            {
                await handler.ExecuteAsync(job, CancellationToken.None);
            }
            catch (Exception) when (expectFailure)
            {
                // The handler records the reason on the model and rethrows so the worker can retry
                // it; what this test is checking is the record it left behind.
            }
        }
    }

    /// <summary>Two triangles in UTM zone 35N, over Piatra Craiului.</summary>
    private static byte[] ProjectedStl()
    {
        (float X, float Y, float Z)[][] triangles =
        [
            [(359994, 5042094, 0), (360292, 5042094, 0), (359994, 5042171, 0)],
            [(360292, 5042094, 0), (360292, 5042171, 0), (359994, 5042171, 0)],
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

    /// <summary>Opaque bytes are fine — the server stores survey files without parsing them.</summary>
    private static byte[] FakeLox() =>
        Encoding.ASCII.GetBytes($"LOX-FIXTURE-{Guid.NewGuid():N}").Concat(new byte[128]).ToArray();

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
