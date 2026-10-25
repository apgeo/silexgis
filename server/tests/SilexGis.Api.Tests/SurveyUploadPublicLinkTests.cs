// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The public link to a survey upload's results: made at completion for an unprotected cave and
/// never for a protected one, shown again only to the cave's writers, withdrawn and made again from
/// the web, and the anonymous page it opens — which carries the results and nothing that places
/// the cave or names its party, and which answers as an invented address the moment the cave is
/// protected or the link withdrawn. Also the notice the uploader gets when the run ends.
/// </summary>
/// <remarks>
/// The survey is genuine TopoDroid output of an invented survey, compiled by a stand-in compiler
/// that writes invented outputs. Nothing here is a real cave.
/// </remarks>
public sealed class SurveyUploadPublicLinkTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Uploads = "/api/v1/survey-uploads";
    private const string Public = "/api/v1/public/survey-uploads";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private Guid ownerId;

    public SurveyUploadPublicLinkTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supub-{Guid.NewGuid():N}");
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
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler>(new FakeSurveyCompiler()));
            });
    }

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supub-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supub-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_unprotected_caves_upload_gets_a_link_whose_page_carries_the_results_and_nothing_that_places_the_cave()
    {
        var (id, caveId) = await SendCompiledAsync($"Publicata {suffix}");
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLinkRefusal").ValueKind.ShouldBe(JsonValueKind.Null);
        var url = status.GetProperty("publicLink").GetProperty("url").GetString()!;
        url.ShouldStartWith("/p/survey/");
        var token = url["/p/survey/".Length..];

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"{Public}/{token}");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var page = JsonDocument.Parse(text).RootElement;

        page.GetProperty("survey").GetProperty("title").GetString().ShouldBe($"Publicata {suffix}");
        page.GetProperty("cave").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        page.GetProperty("cave").EnumerateObject().Select(p => p.Name).ShouldBe(["name"]);
        page.GetProperty("status").GetString().ShouldBe("succeeded");
        page.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("state").GetString()).ShouldAllBe(s => s == "done");
        var kinds = page.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        kinds.ShouldContain("lox");
        kinds.ShouldContain("plan-pdf");
        kinds.ShouldNotContain(SurveyUploadArtefacts.LogKind);

        // Nothing that names the cave or the upload, the party, the sent files or the compiler's
        // words: the members are the envelope's own, and no identifier of either appears anywhere.
        page.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["survey", "cave", "version", "status", "stage", "stages", "progress", "message", "error", "artefacts", "createdAt", "finishedAt"]);
        text.ShouldNotContain(caveId.ToString(), Case.Insensitive);
        text.ShouldNotContain(id.ToString(), Case.Insensitive);
        text.ShouldNotContain("pestera_inventata.zip");
        text.ShouldNotContain("pestera_inventata.th\"");
        foreach (var member in new[] { "logTail", "team", "files", "latitude", "longitude", "coordinates", "geom", "fix" })
        {
            text.ShouldNotContain($"\"{member}\"", Case.Sensitive, member);
        }

        // Each artefact downloads, without an account, from the address the page handed out.
        var lox = page.GetProperty("artefacts").EnumerateArray().First(a => a.GetProperty("kind").GetString() == "lox");
        (await anonymous.GetAsync(lox.GetProperty("contentUrl").GetString())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_model_deleted_on_the_web_drops_off_the_public_page_and_the_status_while_the_drawings_stay()
    {
        var (id, _) = await SendCompiledAsync($"Stearsa {suffix}");
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        var token = await TokenAsync(id);
        var loxModel = status.GetProperty("artefacts").EnumerateArray()
            .Single(a => a.GetProperty("kind").GetString() == "lox").GetProperty("surveyModelId").GetGuid();

        (await owner.DeleteAsync($"/api/v1/survey-models/{loxModel}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = factory.CreateClient();
        var page = await anonymous.GetFromJsonAsync<JsonElement>($"{Public}/{token}");
        var kinds = page.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        kinds.ShouldNotContain("lox");
        kinds.ShouldContain("plan-pdf");
        kinds.ShouldContain("3d");

        var after = (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}"))
            .GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        after.ShouldNotContain("lox");
        after.ShouldContain("plan-pdf");
    }

    [Fact]
    public async Task A_protected_cave_gets_no_link_and_says_why_while_an_unprotected_one_beside_it_gets_one()
    {
        var (open, _) = await SendCompiledAsync($"Deschisa {suffix}");
        var (shut, _) = await SendCompiledAsync($"Protejata {suffix}", newCave: new { locationProtected = true });

        var opened = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{open}");
        opened.GetProperty("publicLink").GetProperty("url").GetString().ShouldStartWith("/p/survey/");

        var refused = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{shut}");
        refused.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        refused.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
        (await LinkCountAsync(shut)).ShouldBe(0);

        // Nor can one be made by hand.
        var minted = await owner.PostAsync($"{Uploads}/{shut}/public-link", null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(minted)).ShouldBe("survey_upload.public_link_refused_protected");
    }

    [Fact]
    public async Task A_cave_protected_after_its_link_was_made_answers_as_an_invented_token_at_once_and_opens_again_when_unprotected()
    {
        var (id, caveId) = await SendCompiledAsync($"Protejata dupa {suffix}");
        var token = await TokenAsync(id);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetProtectedAsync(caveId, true);
        var refused = await anonymous.GetAsync($"{Public}/{token}");
        var invented = await anonymous.GetAsync($"{Public}/{SurveyUploadPublicLinks.NewToken()}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe(await CodeAsync(invented));

        // The status page shows no address that would not open, and says why.
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.ProtectedCave);

        // The page remembers no verdict of its own: lifting the protection opens the same link.
        await SetProtectedAsync(caveId, false);
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_withdrawn_link_answers_as_an_invented_one_and_a_new_one_opens_in_its_place()
    {
        var (id, _) = await SendCompiledAsync($"Retrasa {suffix}");
        var first = await TokenAsync(id);
        using var anonymous = factory.CreateClient();

        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await anonymous.GetAsync($"{Public}/{first}");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(withdrawn)).ShouldBe(SurveyUploadEndpointsCodes.NotFound);
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.Revoked);

        var made = await owner.PostAsync($"{Uploads}/{id}/public-link", null);
        made.StatusCode.ShouldBe(HttpStatusCode.Created, await made.Content.ReadAsStringAsync());
        var second = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!["/p/survey/".Length..];
        second.ShouldNotBe(first);
        (await anonymous.GetAsync($"{Public}/{second}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"{Public}/{first}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TokenAsync(id)).ShouldBe(second);

        // Malformed and oversized tokens get the same answer.
        (await anonymous.GetAsync($"{Public}/{new string('A', 200)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_a_signed_in_writer_makes_or_withdraws_a_link_and_only_writers_see_its_address()
    {
        // A cave every signed-in account may read, so a viewer reaches the upload and is refused
        // the act rather than the upload.
        var (id, _) = await SendCompiledAsync($"Vazuta {suffix}", newCave: new { visibility = "public" });
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supub-v-{suffix}@t.local");
        using var viewer = await AuthHelper.BearerClientAsync(factory, $"supub-v-{suffix}@t.local");

        var seen = await viewer.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        seen.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}"))
            .GetProperty("publicLink").GetProperty("url").GetString().ShouldStartWith("/p/survey/");

        (await viewer.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A device's key completes uploads but does not hand caves to the world by hand.
        var key = await AuthHelper.CreateAccessKeyAsync(owner, "TopoDroid test phone");
        using var device = AuthHelper.AccessKeyClient(factory, key);
        var keyed = await device.PostAsync($"{Uploads}/{id}/public-link", null);
        keyed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(keyed)).ShouldBe("auth.access_key_not_accepted");

        (await owner.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_account_that_turned_links_off_gets_none_and_is_told_so()
    {
        var email = $"supub-off-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(factory, email);
        (await client.PutAsJsonAsync("/api/v1/me/survey-upload-preferences", new { alwaysOutputs = Array.Empty<string>(), publicLinks = false }))
            .IsSuccessStatusCode.ShouldBeTrue();

        var (id, _) = await SendCompiledAsync($"Fara link {suffix}", client: client);
        var status = await client.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.AccountOff);
        (await LinkCountAsync(id)).ShouldBe(0);
    }

    [Fact]
    public async Task The_uploader_is_told_the_run_ended_with_the_survey_and_cave_names_and_a_link_to_its_page_and_nothing_else()
    {
        var (id, caveId) = await SendCompiledAsync($"Anuntata {suffix}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var surveyId = await db.SurveyUploads.Where(u => u.Id == id).Select(u => u.CaveSurveyId).SingleAsync();
        var notice = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == ownerId && n.TemplateKey == MessageTemplateCatalog.NotifySurveyUploadSucceeded && n.TargetId == caveId)
            .SingleAsync();
        notice.TargetKind.ShouldBe(NotificationTargetKind.Feature);
        notice.Category.ShouldBe(NotificationCategory.JobCompleted);

        var values = JsonDocument.Parse(notice.Placeholders).RootElement;
        values.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["caveName", "surveyTitle", "url"]);
        values.GetProperty("surveyTitle").GetString().ShouldBe($"Anuntata {suffix}");
        values.GetProperty("url").GetString().ShouldBe(SurveyUploadLinks.StatusPage(caveId, surveyId, id));

        // One notice per run, and the generic job notice is not sent beside it.
        (await db.Notifications.CountAsync(n => n.RecipientUserId == ownerId && n.TargetId == caveId)).ShouldBe(1);
        (await db.Notifications.CountAsync(n => n.RecipientUserId == ownerId && n.TemplateKey == MessageTemplateCatalog.NotifyJobCompleted)).ShouldBe(0);
    }

    // ---- helpers ----

    /// <summary>Error codes this suite expects, named once.</summary>
    private static class SurveyUploadEndpointsCodes
    {
        public const string NotFound = "survey_upload.public_link_not_found";
    }

    /// <summary>Sends a compiled survey of a cave the upload makes, runs it, and answers the upload and its cave.</summary>
    private async Task<(Guid Id, Guid CaveId)> SendCompiledAsync(string title, object? newCave = null, HttpClient? client = null)
    {
        client ??= owner;
        var files = new (string Name, byte[] Bytes)[]
        {
            ("pestera_inventata.zip", Fixture("pestera_inventata.zip")),
            ("pestera_inventata.th", Fixture("pestera_inventata.th")),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")),
            ("pestera_inventata-1s.th2", Fixture("pestera_inventata-1s.th2")),
        };
        var opened = await client.PostAsJsonAsync(Uploads, new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            newCave,
            surveyTitle = title,
            surveyName = "pestera_inventata",
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
            (await client.PutAsync($"{Uploads}/{id}/files/{Uri.EscapeDataString(name)}", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var complete = await client.PostAsync($"{Uploads}/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        return (id, caveId);
    }

    private async Task<string> TokenAsync(Guid id)
    {
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        return status.GetProperty("publicLink").GetProperty("url").GetString()!["/p/survey/".Length..];
    }

    private async Task<int> LinkCountAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyUploadPublicLinks.CountAsync(l => l.SurveyUploadId == id);
    }

    /// <summary>Protects or unprotects the cave itself, both the flag and the derived column a write would set.</summary>
    private async Task SetProtectedAsync(Guid caveId, bool on)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => f.Id == caveId)
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
