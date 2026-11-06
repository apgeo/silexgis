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
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    public SurveyUploadTherionTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
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

    [Fact]
    public void A_compiler_that_answers_its_version_question_with_silence_is_given_up_on_in_time()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Stands in for the compiler with a shell script.");
        Directory.CreateDirectory(filesRoot);

        // A program that prints nothing and does not exit, and one that also fills its error pipe
        // first: either held the probe for as long as it ran.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        SandboxedTherionRunner.ProbeVersion(Script("silent", "exec sleep 60"), TimeSpan.FromSeconds(1)).ShouldBeNull();
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        clock.Restart();
        SandboxedTherionRunner.ProbeVersion(
                Script("noisy", "head -c 200000 /dev/zero >&2\nexec sleep 60"), TimeSpan.FromSeconds(1))
            .ShouldBeNull();
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));

        // And one that answers is still heard, with its error output read past.
        SandboxedTherionRunner.ProbeVersion(
                Script("answers", "head -c 200000 /dev/zero >&2\necho 'therion 6.3.4 (2025-04-08)'"), TimeSpan.FromSeconds(10))
            .ShouldBe("therion 6.3.4");
    }

    [Fact]
    public void The_sandbox_command_shares_nothing_binds_the_system_read_only_and_carries_none_of_the_servers_settings()
    {
        // Stand-ins for the server's own settings, which hold the database password.
        var setting = $"SILEXGIS_SANDBOX_PROBE_{suffix}";
        var connection = $"ConnectionStrings__SandboxProbe{suffix}";
        Environment.SetEnvironmentVariable(setting, "invented");
        Environment.SetEnvironmentVariable(connection, "Host=invented");
        try
        {
            var options = new SurveyCompilerOptions();
            var project = Path.Combine(filesRoot, "project");
            var scratch = Path.Combine(project, SandboxedTherionRunner.ScratchDirectoryName);
            var timeout = TimeSpan.FromMinutes(10);
            var therion = new System.Diagnostics.ProcessStartInfo("/usr/bin/therion") { RedirectStandardOutput = true };
            therion.ArgumentList.Add("upload.thconfig");

            var sandboxed = new SandboxedTherionRunner("/usr/bin/therion", "/usr/bin/bwrap", options)
                .Wrap(therion, project, scratch, timeout);
            List<string> command = [sandboxed.FileName, .. sandboxed.ArgumentList];
            var bwrap = command.IndexOf("/usr/bin/bwrap");
            bwrap.ShouldBeGreaterThanOrEqualTo(0, string.Join(' ', command));
            var inside = command[(bwrap + 1)..^2];

            // No network, no other process, no inherited environment, and gone with the server.
            foreach (var flag in (string[])["--unshare-all", "--die-with-parent", "--new-session", "--clearenv"])
            {
                inside.ShouldContain(flag);
            }

            inside.ShouldNotContain("--share-net");

            // The system read-only; the project and its scratch directory the only places it can
            // write, and the project the directory it starts in.
            ShouldContainRun(inside, "--ro-bind", "/usr", "/usr");
            ShouldContainRun(inside, "--chdir", project);
            var writable = inside.Select((argument, at) => (argument, at))
                .Where(a => a.argument is "--bind" or "--bind-try" or "--dev-bind" or "--dev-bind-try")
                .Select(a => (inside[a.at + 1], inside[a.at + 2]))
                .ToList();
            writable.ShouldBe([(project, project), (scratch, "/tmp")], ignoreOrder: true);
            // What the sandbox's environment is given is a fixed list, none of it the server's.
            inside.Select((argument, at) => (argument, at))
                .Where(a => a.argument == "--setenv")
                .Select(a => inside[a.at + 1])
                .ShouldBe(["PATH", "HOME", "TMPDIR", "TEXMFVAR", "LANG"], ignoreOrder: true);

            // The compiler is what the sandbox runs, with its own arguments, and its output is
            // read as it was asked to be.
            command[^2..].ShouldBe(["/usr/bin/therion", "upload.thconfig"]);
            sandboxed.RedirectStandardOutput.ShouldBeTrue();
            sandboxed.UseShellExecute.ShouldBeFalse();

            // The limits wrap the sandbox, where this machine has the programs that set them.
            if (ExecutableSearch.OnPath("prlimit") is { } prlimit)
            {
                command[0].ShouldBe(prlimit);
                command.IndexOf("--as=" + options.MemoryCap.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ShouldBeInRange(1, bwrap - 1);
                command.IndexOf("--fsize=" + options.OutputFileCap.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ShouldBeInRange(1, bwrap - 1);
                command.ShouldContain(a => a.StartsWith("--cpu=", StringComparison.Ordinal));
            }

            if (ExecutableSearch.OnPath("timeout") is { } stopper)
            {
                command.IndexOf(stopper).ShouldBeInRange(0, bwrap - 1);
                command.IndexOf("--kill-after=10").ShouldBeInRange(1, bwrap - 1);
            }

            // Sandboxed or not, nothing of the server's own settings reaches the child.
            var direct = new SandboxedTherionRunner("/usr/bin/therion", null, options).Wrap(therion, project, scratch, timeout);
            direct.ArgumentList.ShouldNotContain("--unshare-all");
            direct.Environment["HOME"].ShouldBe(scratch);
            direct.Environment["TMPDIR"].ShouldBe(scratch);
            foreach (var start in (System.Diagnostics.ProcessStartInfo[])[sandboxed, direct])
            {
                start.Environment.Keys.ShouldNotContain(k =>
                    k.StartsWith("SILEXGIS", StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
                if (!OperatingSystem.IsWindows())
                {
                    start.Environment["PATH"].ShouldBe("/usr/bin:/bin");
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                sandboxed.Environment.Keys.ShouldBe(["PATH", "LANG"], ignoreOrder: true);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(setting, null);
            Environment.SetEnvironmentVariable(connection, null);
        }
    }

    /// <summary>Asserts that <paramref name="run"/> appears in <paramref name="arguments"/> as consecutive items.</summary>
    private static void ShouldContainRun(List<string> arguments, params string[] run)
    {
        var found = Enumerable.Range(0, Math.Max(0, arguments.Count - run.Length + 1))
            .Any(at => arguments.Skip(at).Take(run.Length).SequenceEqual(run));
        found.ShouldBeTrue($"{string.Join(' ', run)} in {string.Join(' ', arguments)}");
    }

    private string Script(string name, string body)
    {
        var path = Path.Combine(filesRoot, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    [TherionFact]
    public async Task The_invented_TopoDroid_survey_compiles_end_to_end_in_the_sandbox()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sutherion-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(factory, $"sutherion-{suffix}@t.local");

        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/survey-uploads/capabilities");
        capabilities.GetProperty("compiler").GetProperty("available").GetBoolean().ShouldBeTrue();
        capabilities.GetProperty("compiler").GetProperty("version").GetString()!.ShouldStartWith("therion ");

        var id = await UploadAndRunAsync(factory, client);

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
    public async Task The_invented_TopoDroid_survey_compiles_end_to_end_through_the_compiler_service()
    {
        // The installation's service mode: the project travels to the compiler service as a tar and
        // its outputs come back. The scaffold makes the folder the outputs go to and leaves it empty.
        var local = new LocalSurveyCompiler(
            Options.Create(new SurveyCompilerOptions { Mode = SurveyCompilerOptions.ModeLocal, TimeoutSeconds = 120 }),
            NullLogger<LocalSurveyCompiler>.Instance);
        await using var service = await TherionServiceHarness.StartAsync(local);
        var remote = new ServiceSurveyCompiler(
            service,
            Options.Create(new SurveyCompilerOptions { Mode = SurveyCompilerOptions.ModeService, Url = "http://therion:3100", TimeoutSeconds = 120 }),
            NullLogger<ServiceSurveyCompiler>.Instance);
        var root = Path.Combine(filesRoot, "service-mode");
        using var host = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = root,
                ["Keys:Path"] = Path.Combine(root, "keys"),
                ["SurveyCompiler:Mode"] = SurveyCompilerOptions.ModeService,
                ["SurveyCompiler:Url"] = "http://therion:3100",
                ["SurveyCompiler:TimeoutSeconds"] = "120",
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler>(remote));
            });
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, $"sutherion-svc-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(host, $"sutherion-svc-{suffix}@t.local");

        var id = await UploadAndRunAsync(host, client);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/survey-uploads/{id}");
        status.GetProperty("status").GetString().ShouldBe("succeeded", status.ToString());
        status.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString())
            .ShouldBe(["3d", "lox", "plan-pdf", "extended-pdf", SurveyUploadArtefacts.LogKind], ignoreOrder: true);
        service.Leftovers().ShouldBeEmpty();
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

    /// <summary>
    /// Sends TopoDroid's default export of the invented survey — sketches and fix written as
    /// comments, which the compile copy makes live because the sketches came too — and runs it.
    /// </summary>
    private static async Task<Guid> UploadAndRunAsync(SilexGisApiFactory host, HttpClient client)
    {
        string[] names = ["pestera_inventata.zip", "pestera_inventata.th", "pestera_inventata-1p.th2", "pestera_inventata-1s.th2"];
        var opened = await client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            surveyTitle = $"Pestera Inventata {Guid.NewGuid():N}",
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
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            jobId = (await db.ProcessingJobs.AsNoTracking()
                    .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline && j.Status == ProcessingJobStatus.Queued)
                    .ToListAsync())
                .Single(j => JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == id)
                .Id;
        }

        await QueuedJob.RunAsync(host.Services, jobId);
        return id;
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));
}
