// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The application's side of the compiler service: a project handed over and its outputs unpacked
/// back into it, against the service itself in this process — and, against answers made up here,
/// that a service which is down, slow or answering something hostile is never taken at its word.
/// The surveys are invented.
/// </summary>
public sealed class ServiceSurveyCompilerTests : IDisposable
{
    private const string Config = "proba.thconfig";

    private readonly string project = Path.Combine(TestScratch.Root, $"silexgis-test-servicecompiler-{Guid.NewGuid():N}", "project");

    public ServiceSurveyCompilerTests()
    {
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, Config), "source proba.th\nexport model -o \"out/proba.lox\"\nexport map -o proba-plan.pdf\n");
        File.WriteAllText(Path.Combine(project, "proba.th"), "survey proba\nendsurvey\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(project)!, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task A_project_goes_to_the_service_and_its_log_and_outputs_come_back_into_it()
    {
        var fake = new FakeSurveyCompiler();
        await using var service = await TherionServiceHarness.StartAsync(fake);
        // The runner's scratch directory is the run's own and never travels.
        Directory.CreateDirectory(Path.Combine(project, SandboxedTherionRunner.ScratchDirectoryName));
        File.WriteAllText(Path.Combine(project, SandboxedTherionRunner.ScratchDirectoryName, "left.tmp"), "invented");
        var compiler = Compiler(service);

        var result = await compiler.CompileAsync(Request(), new Progress<SurveyCompileProgress>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        result.OutputFiles.ShouldBe(["out/proba.lox", "proba-plan.pdf"]);
        result.OutputTail.ShouldBe("invented tail");
        result.LogText.ShouldContain("writing out/proba.lox ... done");
        File.ReadAllText(Path.Combine(project, "out", "proba.lox")).ShouldBe("invented out/proba.lox");
        File.ReadAllText(Path.Combine(project, "therion.log")).ShouldBe(result.LogText);
        fake.LastProject.Keys.ShouldBe([Config, "proba.th"], ignoreOrder: true);
        compiler.Version.ShouldBe(FakeSurveyCompiler.FakeVersion);
        service.Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_service_that_cannot_be_reached_or_cannot_compile_is_a_fact_about_the_installation()
    {
        var down = Compiler(new Answering(_ => throw new HttpRequestException("invented: connection refused")));
        down.IsAvailable.ShouldBeTrue();
        await Should.ThrowAsync<SurveyCompilerUnavailableException>(() => CompileAsync(down));

        var refusing = Compiler(new Answering(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Should.ThrowAsync<SurveyCompilerUnavailableException>(() => CompileAsync(refusing));

        var slow = Compiler(new Answering(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)));
        await Should.ThrowAsync<TimeoutException>(() => CompileAsync(slow));

        var broken = Compiler(new Answering(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        await Should.ThrowAsync<InvalidOperationException>(() => CompileAsync(broken));
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("link")]
    [InlineData("overwrite")]
    [InlineData("no-manifest")]
    public async Task An_answer_that_is_not_ordinary_new_files_inside_the_project_is_refused(string kind)
    {
        var outside = $"silexgis-escaped-{Guid.NewGuid():N}.lox";
        var entries = new List<(string, string)> { (SurveyCompilerServiceProtocol.ManifestName, Manifest("proba.lox")) };
        entries.AddRange(kind switch
        {
            "escape" => [($"../{outside}", "invented")],
            "link" => [("proba.lox", "->/etc/passwd")],
            "overwrite" => [("proba.th", "survey replaced\nendsurvey\n")],
            _ => [],
        });
        if (kind == "no-manifest")
        {
            entries = [("proba.lox", "invented")];
        }

        var compiler = Compiler(new Answering(_ => Tar(TherionServiceHarness.Tar([.. entries]))));

        await Should.ThrowAsync<InvalidOperationException>(() => CompileAsync(compiler));

        File.Exists(Path.Combine(Path.GetDirectoryName(project)!, outside)).ShouldBeFalse();
        File.ReadAllText(Path.Combine(project, "proba.th")).ShouldBe("survey proba\nendsurvey\n");
    }

    [Fact]
    public async Task An_answer_larger_than_the_installation_allows_is_refused()
    {
        var big = new string('x', 2 * 1024 * 1024);
        var compiler = Compiler(
            new Answering(_ => Tar(TherionServiceHarness.Tar((SurveyCompilerServiceProtocol.ManifestName, Manifest("proba.lox")), ("proba.lox", big)))),
            maxOutputBytes: 1024 * 1024);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => CompileAsync(compiler));

        refused.InnerException.ShouldBeOfType<SurveyProjectTarException>().TooLarge.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_outputs_that_arrived_are_reported()
    {
        // The manifest names two outputs; one of them never came.
        var compiler = Compiler(new Answering(_ => Tar(TherionServiceHarness.Tar(
            (SurveyCompilerServiceProtocol.ManifestName, Manifest("out/proba.lox", "proba-plan.pdf")),
            ("out/proba.lox", "invented")))));

        var result = await CompileAsync(compiler);

        result.OutputFiles.ShouldBe(["out/proba.lox"]);
        result.LogText.ShouldBeEmpty();
    }

    [TherionFact]
    public async Task The_real_compiler_in_the_service_compiles_an_invented_survey_to_a_model()
    {
        File.WriteAllText(Path.Combine(project, Config), "source proba.th\nexport model -fmt loch -o proba.lox\n");
        File.WriteAllText(
            Path.Combine(project, "proba.th"),
            """
            survey proba -title "Proba"
              centreline
                units length meters
                units compass clino degrees
                data normal from to length compass clino
                1 2 5.0 10 -5
                2 3 4.0 90 0
                3 4 6.0 180 10
              endcentreline
            endsurvey

            """);
        var local = new LocalSurveyCompiler(
            Options.Create(new SurveyCompilerOptions { Mode = SurveyCompilerOptions.ModeLocal, TimeoutSeconds = 120 }),
            NullLogger<LocalSurveyCompiler>.Instance);
        await using var service = await TherionServiceHarness.StartAsync(local);
        var compiler = Compiler(service);

        var result = await compiler.CompileAsync(Request(), new Progress<SurveyCompileProgress>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.OutputTail);
        result.OutputFiles.ShouldBe(["proba.lox"]);
        new FileInfo(Path.Combine(project, "proba.lox")).Length.ShouldBeGreaterThan(0);
        // The service's run directory means nothing here and is not in the log it sent back.
        result.LogText.ShouldContain("proba.lox");
        result.LogText.ShouldNotContain(service.WorkDir);
        compiler.Version.ShouldStartWith("therion ");
        service.Leftovers().ShouldBeEmpty();
    }

    private SurveyCompileRequest Request() => new(project, Config, TimeSpan.FromSeconds(60));

    private Task<SurveyCompileResult> CompileAsync(ServiceSurveyCompiler compiler) =>
        compiler.CompileAsync(Request(), new Progress<SurveyCompileProgress>(), CancellationToken.None);

    private static ServiceSurveyCompiler Compiler(IHttpClientFactory clients, long maxOutputBytes = 512L * 1024 * 1024) =>
        new(
            clients,
            Options.Create(new SurveyCompilerOptions
            {
                Mode = SurveyCompilerOptions.ModeService,
                Url = "http://therion:3100",
                MaxOutputBytes = maxOutputBytes,
            }),
            NullLogger<ServiceSurveyCompiler>.Instance);

    private static string Manifest(params string[] outputs) =>
        JsonSerializer.Serialize(new SurveyCompilerServiceResult(true, 0, outputs, "invented tail", "invented 1.0"), JsonSerializerOptions.Web);

    private static HttpResponseMessage Tar(byte[] tar)
    {
        var content = new ByteArrayContent(tar);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(SurveyProjectTar.MediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>A service made up here: every request is answered by the function given.</summary>
    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read what was sent, as a service would, so the request's tar is written out in full.
            if (request.Content is { } content)
            {
                await content.CopyToAsync(Stream.Null, cancellationToken);
            }

            return answer(request);
        }
    }
}
