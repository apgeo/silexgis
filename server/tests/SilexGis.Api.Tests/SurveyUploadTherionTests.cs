// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// Survey uploads compiled by the real Therion, in the sandbox, on a machine that has both: the
/// on-demand tier of the compile tests. Everything compiled is invented — TopoDroid's own export
/// of an invented survey, and files the tests write.
/// </summary>
[Trait("Category", "Therion")]
public sealed class SurveyUploadTherionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    public SurveyUploadTherionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-sutherion-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
                ["SurveyCompiler:Mode"] = SurveyCompilerOptions.ModeLocal,
                ["SurveyCompiler:TimeoutSeconds"] = "120",
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler, LocalSurveyCompiler>());
            });
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

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

    [TherionFact]
    public async Task The_invented_TopoDroid_survey_compiles_end_to_end_in_the_sandbox()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sutherion-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(factory, $"sutherion-{suffix}@t.local");

        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        capabilities.GetProperty("compiler").GetProperty("available").GetBoolean().ShouldBeTrue();
        capabilities.GetProperty("compiler").GetProperty("version").GetString()!.ShouldStartWith("therion ");

        // TopoDroid's default export: sketches and fix written as comments, which the compile
        // copy makes live because the sketches came too.
        string[] names = ["pestera_inventata.zip", "pestera_inventata.th", "pestera_inventata-1p.th2", "pestera_inventata-1s.th2"];
        var opened = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = $"Pestera Inventata {suffix}",
            surveyName = "pestera_inventata",
            files = names.Select(n => new
            {
                name = n,
                size = Fixture(n).LongLength,
                role = n.EndsWith(".zip", StringComparison.Ordinal) ? "archive"
                    : n.EndsWith(".th2", StringComparison.Ordinal) ? "therionSketch" : "therionSource",
            }),
        });
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var name in names)
        {
            var content = new ByteArrayContent(Fixture(name));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await client.PutAsync($"/api/v1/survey-uploads/{id}/files/{name}", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.PostAsync($"/api/v1/survey-uploads/{id}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            jobId = (await db.ProcessingJobs.AsNoTracking()
                    .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline && j.Status == ProcessingJobStatus.Queued)
                    .ToListAsync())
                .Single(j => JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == id)
                .Id;
        }

        await QueuedJob.RunAsync(factory.Services, jobId);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        status.GetProperty("stage").GetString().ShouldBe("read");
        status.GetProperty("message").GetString().ShouldBe(SurveyPipelineNotes.Compiled(4, 4, sketchesDrawn: true));
        status.GetProperty("compiler").GetProperty("version").GetString()!.ShouldStartWith("therion ");
        Directory.EnumerateDirectories(Path.Combine(filesRoot, "survey-work")).ShouldBeEmpty();

        // What Therion wrote is stored as what it is; the model is placed in the coordinate system
        // the log names, because the compile copy made the survey's fix live.
        var artefacts = status.GetProperty("artefacts").EnumerateArray().ToList();
        artefacts.Select(a => a.GetProperty("kind").GetString())
            .ShouldBe(["3d", "lox", "plan-pdf", "extended-pdf", SurveyUploadArtefacts.LogKind], ignoreOrder: true);
        var loxId = artefacts.Single(a => a.GetProperty("kind").GetString() == "lox").GetProperty("surveyModelId").GetGuid();
        long readingId;
        long compilationJobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == loxId);
            model.SourceEpsg.ShouldNotBeNull();
            // The upload made the cave and this is its first model, so it is the cave's current one.
            model.IsCurrent.ShouldBeTrue();
            var queued = await db.ProcessingJobs.AsNoTracking().Where(j => j.Status == ProcessingJobStatus.Queued).ToListAsync();
            readingId = queued.Where(j => j.Kind == ProcessingJobKinds.SurveyGraph)
                .Single(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyModelId == loxId).Id;
            var compilation = await db.SurveyCompilations.AsNoTracking().SingleAsync(c => c.SurveyModelId == loxId);
            compilationJobId = queued.Where(j => j.Kind == ProcessingJobKinds.SurveyCompilation)
                .Single(j => JsonSerializer.Deserialize<SurveyCompilationPayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyCompilationId == compilation.Id).Id;
        }

        // The model's reading finds the survey's stations, and the log's reading its figures.
        await QueuedJob.RunAsync(factory.Services, readingId);
        await QueuedJob.RunAsync(factory.Services, compilationJobId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var model = await db.SurveyModels.AsNoTracking().SingleAsync(m => m.Id == loxId);
            model.Status.ShouldBe(SurveyModelStatus.Ready, model.ProcessingError);
            var stations = await db.SurveyStations.AsNoTracking().Where(s => s.SurveyModelId == loxId).Select(s => s.Name).ToListAsync();
            // Named by their path through the generated wrapper survey and the uploaded one.
            stations.Select(n => n[(n.LastIndexOf('.') + 1)..])
                .ShouldBe(["0", "1", "2", "3", "4", "5", "6"], ignoreOrder: true);
            stations.ShouldAllBe(n => n.Contains(".pestera_inventata.", StringComparison.Ordinal));
            (await db.SurveyCompilations.AsNoTracking().SingleAsync(c => c.SurveyModelId == loxId))
                .Status.ShouldBe(SurveyCompilationStatus.Read);

            // Read, still the cave's current model and its only one, and its centreline the cave's
            // shape: the cave the upload made is drawn from the survey that made it.
            model.IsCurrent.ShouldBeTrue();
            var caveId = status.GetProperty("cave").GetProperty("id").GetGuid();
            (await db.SurveyModels.AsNoTracking().Where(m => m.CaveFeatureId == caveId && m.IsCurrent).Select(m => m.Id).ToListAsync())
                .ShouldBe([loxId]);
            var lines = await db.Centerlines.AsNoTracking().Where(c => c.CaveFeatureId == caveId).ToListAsync();
            lines.ShouldHaveSingleItem().SurveyModelId.ShouldBe(loxId);
            lines[0].IsDefault.ShouldBeTrue();
        }
    }

    [TherionFact]
    public async Task The_sandbox_lets_the_compiler_read_the_project_and_nothing_outside_it()
    {
        var root = Path.Combine(filesRoot, "sandbox");
        var outside = Path.Combine(root, "outside");
        var project = Path.Combine(root, "project");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(project, "th"));
        Directory.CreateDirectory(Path.Combine(project, "rez"));

        // A file Therion cannot parse as a survey: when it opens one it quotes the first line of
        // it into its log, which is how what it read would come back to an uploader.
        const string Inside = "invented-marker-inside-the-project";
        const string Outside = "invented-marker-outside-the-project";
        await File.WriteAllTextAsync(Path.Combine(project, "th", "inside.th"), Inside + "\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outside, "outside.th"), Outside + "\n", TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(outside, "outside.th")).ShouldBeTrue();

        var compiler = new LocalSurveyCompiler(
            Options.Create(new SurveyCompilerOptions { Mode = SurveyCompilerOptions.ModeLocal }),
            NullLogger<LocalSurveyCompiler>.Instance);
        compiler.IsAvailable.ShouldBeTrue();

        async Task<string> CompileReadingAsync(string path)
        {
            await File.WriteAllTextAsync(
                Path.Combine(project, "thconfig"),
                $"encoding utf-8\nsource \"{path}\"\nexport model -fmt loch -o rez/a.lox\n",
                TestContext.Current.CancellationToken);
            var result = await compiler.CompileAsync(
                new SurveyCompileRequest(project, "thconfig", TimeSpan.FromMinutes(1)),
                new Progress<SurveyCompileProgress>(),
                TestContext.Current.CancellationToken);
            return result.LogText + "\n" + result.OutputTail;
        }

        // What is in the project is read (the positive case); what is outside it is not there.
        (await CompileReadingAsync(Path.Combine(project, "th", "inside.th"))).ShouldContain(Inside);
        (await CompileReadingAsync(Path.Combine(outside, "outside.th"))).ShouldNotContain(Outside);
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));
}
