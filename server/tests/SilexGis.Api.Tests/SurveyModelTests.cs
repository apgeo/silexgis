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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// 3D survey models end-to-end: upload/list/get/update/delete under the cave's ACL, the
/// anonymous delivery URL, and the location-protection rule — models of a protected cave
/// are withheld entirely unless the caller may view the exact location.
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
        using var form = BuildForm("secret-system.3d", FakeLox());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // The cave itself stays readable, but its models are the location: empty list, 404 by id.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var hidden = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        hidden.GetArrayLength().ShouldBe(0);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner sees everything.
        var owned = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        owned.GetArrayLength().ShouldBe(1);

        // An explicit ViewExactLocation ACL grant flips the models visible for the reader.
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/cave/{caveId}/acl", new
        {
            entries = new[] { new { subjectKind = "user", subjectId = readerId, permissions = "read, viewExactLocation" } },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());

        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-models");
        granted.GetArrayLength().ShouldBe(1);
        granted[0].GetProperty("format").GetString().ShouldBe("survex3d");
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Private_cave_models_are_not_disclosed_to_outsiders()
    {
        var caveId = await CreateCaveAsync(visibility: "private", locationProtected: false);
        using var form = BuildForm("private.lox", FakeLox());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Existence non-disclosure: 404 (not 403) on every path.
        (await reader.GetAsync($"/api/v1/caves/{caveId}/survey-models")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.DeleteAsync($"/api/v1/survey-models/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var outsiderForm = BuildForm("intrusion.lox", FakeLox());
        (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-models", outsiderForm))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Survey Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
