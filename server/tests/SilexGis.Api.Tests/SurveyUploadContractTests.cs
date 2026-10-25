// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The survey-upload exchange exactly as a field device has it with the server, recorded under
/// <c>contract/topodroid-sync/v1/</c> and compared byte for byte on every run.
/// </summary>
/// <remarks>
/// <para>
/// The survey tool on the other side of these routes is written by people who read the recorded
/// files and the protocol document, not this suite. So the recordings are the promise: a normal run
/// fails on any difference from what is committed, and only a run with
/// <c>SILEXGIS_CONTRACT_RECORD=1</c> rewrites them — a diff in that rewrite is a contract change and
/// is reviewed as one. Behaviour is asserted by the route and pipeline suites; here each exchange is
/// driven only as far as it takes to reach the answer being recorded.
/// </para>
/// <para>
/// The device carries nothing but a personal access key and the contract header, as the survey
/// tool does. Both archives are invented and committed as files, so their bytes and the digests
/// the recordings show never change: one is genuine survey-tool output of an invented survey, the
/// other an archive without its manifest.
/// </para>
/// </remarks>
public sealed class SurveyUploadContractTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Uploads = "/api/v1/survey-uploads";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient web = null!;
    private HttpClient device = null!;
    private string deviceKey = null!;
    private Guid ownerId;

    public SurveyUploadContractTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supct-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var email = $"supct-{suffix}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        web = await AuthHelper.BearerClientAsync(factory, email);
        deviceKey = await AuthHelper.CreateAccessKeyAsync(web, "TopoDroid test phone");
        device = AuthHelper.AccessKeyClient(factory, deviceKey);
        device.DefaultRequestHeaders.Add("X-SilexGis-Contract", "1");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        device?.Dispose();
        web?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task A_survey_sent_watched_sent_again_and_listed_is_the_recorded_exchange()
    {
        var archive = ArchiveFile("pestera_inventata.zip");
        var broken = ArchiveFile("no_manifest.zip");
        var title = $"Pestera Inventata {suffix}";

        await Fixture().AssertAsync(
            "01-capabilities", $"GET {Uploads}/capabilities", await device.GetAsync($"{Uploads}/capabilities"));

        // No cave named: the server makes one from the survey's title.
        var openBody = Json(new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = title,
            surveyName = "pestera_inventata",
            description = "Invented survey for the recorded contract",
            files = new[] { new { name = "pestera_inventata.zip", size = archive.LongLength, role = "archive" } },
            pipeline = new { compile = true },
        });
        var opened = await device.PostAsync(Uploads, JsonContent(openBody));
        var first = await BodyAsync(opened, HttpStatusCode.Created);
        var uploadId = first.GetProperty("id").GetGuid();
        var caveId = first.GetProperty("cave").GetProperty("id").GetGuid();
        var surveyId = first.GetProperty("caveSurvey").GetProperty("id").GetGuid();
        Guid? nextId = null;
        var archived = new List<(Guid Id, string Label)>();
        ContractFixture Named()
        {
            // The uploader's label is made from their id when they have set no name, so it varies
            // with the id and is named the way the id is.
            var fixture = Fixture()
                .Name(caveId, "cave").Name(surveyId, "survey").Name(uploadId, "upload").Name(ownerId, "uploader")
                .Literal(ProfileProtection.AnonymousLabelPrefix + ownerId.ToString("N")[..8], "uploader-label")
                .Literal(suffix, "marker");
            foreach (var (id, label) in archived)
            {
                fixture.Name(id, label);
            }

            return nextId is { } next ? fixture.Name(next, "next-upload") : fixture;
        }

        await Named().AssertWithStatusAsync("02-open", $"POST {Uploads}", openBody, opened);

        var put = await PutAsync(device, uploadId, "pestera_inventata.zip", archive);
        var stored = await BodyAsync(put, HttpStatusCode.OK);
        archived.Add((stored.GetProperty("sourceId").GetGuid(), "archive-source"));
        archived.Add((stored.GetProperty("documentId").GetGuid(), "archive-document"));
        await Named().AssertAsync(
            "03-put-file", $"PUT {Uploads}/{uploadId}/files/pestera_inventata.zip", put);

        var completed = await device.PostAsync($"{Uploads}/{uploadId}/complete", null);
        await Named().AssertWithStatusAsync("04-complete", $"POST {Uploads}/{uploadId}/complete", null, completed);

        await Named().AssertAsync("05-status-queued", $"GET {Uploads}/{uploadId}", await device.GetAsync($"{Uploads}/{uploadId}"));

        await RunPipelineAsync(uploadId);
        await Named().AssertAsync("06-status-succeeded", $"GET {Uploads}/{uploadId}", await device.GetAsync($"{Uploads}/{uploadId}"));

        // The same survey again, by the id the server answered for it: its next version.
        var nextBody = Json(new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            caveSurvey = new { id = surveyId },
            surveyTitle = title,
            surveyName = "pestera_inventata",
            files = new[] { new { name = "no_manifest.zip", size = broken.LongLength, role = "archive" } },
        });
        var reopened = await device.PostAsync(Uploads, JsonContent(nextBody));
        nextId = (await BodyAsync(reopened, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Named().AssertWithStatusAsync("07-open-next-version", $"POST {Uploads}", nextBody, reopened);

        var storedNext = await BodyAsync(await PutAsync(device, nextId.Value, "no_manifest.zip", broken), HttpStatusCode.OK);
        archived.Add((storedNext.GetProperty("sourceId").GetGuid(), "next-archive-source"));
        archived.Add((storedNext.GetProperty("documentId").GetGuid(), "next-archive-document"));
        (await device.PostAsync($"{Uploads}/{nextId}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Should.ThrowAsync<InvalidOperationException>(RunPipelineAsync(nextId.Value));
        await Named().AssertAsync("08-status-failed", $"GET {Uploads}/{nextId}", await device.GetAsync($"{Uploads}/{nextId}"));

        await Named().AssertAsync(
            "09-list", $"GET {Uploads}?caveSurveyId={surveyId}", await device.GetAsync($"{Uploads}?caveSurveyId={surveyId}"));

        var rerunBody = Json(new { pipeline = new { compile = false } });
        var rerun = await device.PostAsync($"{Uploads}/{nextId}/rerun", JsonContent(rerunBody));
        await Named().AssertWithStatusAsync("10-rerun", $"POST {Uploads}/{nextId}/rerun", rerunBody, rerun);

        // The refusals a device meets once an upload has moved on.
        await Named().AssertRefusalAsync(
            "11-errors/not-receiving", $"DELETE {Uploads}/{uploadId}", await device.DeleteAsync($"{Uploads}/{uploadId}"));
        await Named().AssertRefusalAsync(
            "11-errors/run-in-progress",
            $"POST {Uploads}/{nextId}/rerun",
            rerunBody,
            await device.PostAsync($"{Uploads}/{nextId}/rerun", JsonContent(rerunBody)));
    }

    [Fact]
    public async Task Every_refusal_a_device_can_meet_before_its_files_are_in_is_the_recorded_one()
    {
        var archive = ArchiveFile("pestera_inventata.zip");
        var unknown = Guid.CreateVersion7();
        ContractFixture Named(params (Guid Id, string Label)[] ids)
        {
            var fixture = Fixture().Name(unknown, "unknown").Literal(suffix, "marker");
            foreach (var (id, label) in ids)
            {
                fixture.Name(id, label);
            }

            return fixture;
        }

        // Signing in and the key's reach.
        using (var stranger = AuthHelper.AccessKeyClient(factory, "sgx_" + new string('A', 43)))
        {
            await Named().AssertRefusalAsync(
                "11-errors/unauthenticated", $"GET {Uploads}/capabilities", await stranger.GetAsync($"{Uploads}/capabilities"));
        }

        await Named().AssertRefusalAsync(
            "11-errors/access-key-not-accepted", "GET /api/v1/me/access-keys", await device.GetAsync("/api/v1/me/access-keys"));

        // A build speaking a contract version this server does not serve.
        using (var later = AuthHelper.AccessKeyClient(factory, deviceKey))
        {
            later.DefaultRequestHeaders.Add("X-SilexGis-Contract", "2");
            await Named().AssertRefusalAsync(
                "11-errors/contract-unsupported", $"GET {Uploads}/{unknown}", await later.GetAsync($"{Uploads}/{unknown}"));
        }

        // Opening.
        await RefuseOpenAsync("11-errors/source-unsupported", Named(), Open(archive, source: "compass"));
        await RefuseOpenAsync("11-errors/cave-ambiguous", Named(), Open(archive, cave: new { id = unknown }, newCave: new { name = "Alta" }));
        await RefuseOpenAsync("11-errors/cave-not-found", Named(), Open(archive, cave: new { id = unknown }));
        await RefuseOpenAsync("11-errors/survey-not-found", Named(), Open(archive, caveSurvey: new { id = unknown }));
        await RefuseOpenAsync(
            "11-errors/files-invalid",
            Named(),
            Open(archive, files: [new { name = "a.zip", size = 10L, role = "archive" }, new { name = "b.zip", size = 10L, role = "archive" }]));
        await RefuseOpenAsync("11-errors/output-unknown", Named(), Open(archive, outputs: ["lox", "pdf3d"]));

        // A new cave the device describes: a group cave for an account in no group, and a cave
        // type the installation no longer has.
        await RefuseOpenAsync("11-errors/visibility-invalid", Named(), Open(archive, newCave: new { visibility = "cavingGroup" }));
        await RefuseOpenAsync("11-errors/cave-type-unknown", Named(), Open(archive, newCave: new { caveTypeId = long.MaxValue }));

        // Sending files to an upload still receiving them.
        var pending = await OpenAsync(Open(archive));
        var cave = await CaveOfAsync(pending);
        await Named((pending, "upload"), (cave, "cave")).AssertRefusalAsync(
            "11-errors/files-missing",
            $"POST {Uploads}/{pending}/complete",
            await device.PostAsync($"{Uploads}/{pending}/complete", null));
        await Named((pending, "upload"), (cave, "cave")).AssertRefusalAsync(
            "11-errors/file-unknown",
            $"PUT {Uploads}/{pending}/files/other.zip",
            await PutAsync(device, pending, "other.zip", archive));
        await Named((pending, "upload"), (cave, "cave")).AssertRefusalAsync(
            "11-errors/size-mismatch",
            $"PUT {Uploads}/{pending}/files/pestera_inventata.zip",
            await PutAsync(device, pending, "pestera_inventata.zip", archive[..^1]));
        await Named((pending, "upload"), (cave, "cave")).AssertRefusalAsync(
            "11-errors/content-mismatch",
            $"PUT {Uploads}/{pending}/files/pestera_inventata.zip",
            await PutAsync(device, pending, "pestera_inventata.zip", Encoding.ASCII.GetBytes(new string('x', archive.Length))));

        var stale = await OpenAsync(Open(archive, cave: new { id = cave }));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == stale)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await Named((stale, "upload"), (cave, "cave")).AssertRefusalAsync(
            "11-errors/expired",
            $"PUT {Uploads}/{stale}/files/pestera_inventata.zip",
            await PutAsync(device, stale, "pestera_inventata.zip", archive));

        await Named().AssertRefusalAsync(
            "11-errors/not-found", $"GET {Uploads}/{unknown}", await device.GetAsync($"{Uploads}/{unknown}"));
    }

    // ---- helpers ----

    private static ContractFixture Fixture() => new(ContractFixture.TopoDroidSurface);

    private string Open(
        byte[] archive,
        string source = "topodroid",
        object? cave = null,
        object? caveSurvey = null,
        object? newCave = null,
        object[]? files = null,
        string[]? outputs = null) => Json(new
    {
        source,
        client = new { name = "TopoDroid", version = "6.5.34" },
        cave,
        caveSurvey,
        newCave,
        surveyTitle = $"Refusals {suffix}",
        surveyName = "pestera_inventata",
        files = files ?? [new { name = "pestera_inventata.zip", size = archive.LongLength, role = "archive" }],
        pipeline = outputs is null ? null : new { outputs },
    });

    private async Task RefuseOpenAsync(string caseName, ContractFixture fixture, string body) =>
        await fixture.AssertRefusalAsync(caseName, $"POST {Uploads}", body, await device.PostAsync(Uploads, JsonContent(body)));

    private async Task<Guid> OpenAsync(string body) =>
        (await BodyAsync(await device.PostAsync(Uploads, JsonContent(body)), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private async Task<Guid> CaveOfAsync(Guid uploadId)
    {
        var status = JsonDocument.Parse(await device.GetStringAsync($"{Uploads}/{uploadId}")).RootElement;
        return status.GetProperty("cave").GetProperty("id").GetGuid();
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    private static StringContent JsonContent(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"{Uploads}/{id}/files/{Uri.EscapeDataString(name)}", content);
    }

    private static byte[] ArchiveFile(string name) =>
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));

    /// <summary>Runs the processing job the upload's completion queued, exactly once, as the worker would.</summary>
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
