// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Surveys;
using SilexGis.TherionService;

namespace SilexGis.Api.Tests;

/// <summary>
/// The survey compiler service's own surface, in this process on a test server: what it answers a
/// project with, and what it refuses before the compiler is started. The projects are invented.
/// </summary>
public sealed class TherionServiceTests
{
    private const string Config = "proba.thconfig";

    private static readonly (string, string)[] Project =
    [
        (Config, "source proba.th\nexport model -o \"out/proba.lox\"\nexport map -o proba-plan.pdf\n"),
        ("proba.th", "survey proba\nendsurvey\n"),
    ];

    [Fact]
    public async Task It_says_whether_it_can_compile_and_with_which_compiler()
    {
        await using (var service = await TherionServiceHarness.StartAsync(new FakeSurveyCompiler()))
        {
            var status = await StatusAsync(service);
            status.ShouldBe(new SurveyCompilerServiceStatus(true, FakeSurveyCompiler.FakeVersion));
        }

        await using (var none = await TherionServiceHarness.StartAsync(new NoSurveyCompiler()))
        {
            (await StatusAsync(none)).ShouldBe(new SurveyCompilerServiceStatus(false, null));

            using var refused = await PostAsync(none, TherionServiceHarness.Tar(Project));
            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await CodeAsync(refused)).ShouldBe("therion_service.unavailable");
        }
    }

    [Fact]
    public async Task A_project_is_answered_with_how_it_ended_its_log_and_its_outputs_and_the_run_is_removed()
    {
        var compiler = new FakeSurveyCompiler();
        await using var service = await TherionServiceHarness.StartAsync(compiler);

        using var response = await PostAsync(service, TherionServiceHarness.Tar(Project));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(SurveyProjectTar.MediaType);
        var answer = await TherionServiceHarness.ReadTarAsync(await response.Content.ReadAsStreamAsync());

        // The manifest first, then the log and the outputs, and nothing the caller sent.
        answer.Keys.First().ShouldBe(SurveyCompilerServiceProtocol.ManifestName);
        answer.Keys.ShouldBe(
            [SurveyCompilerServiceProtocol.ManifestName, "therion.log", "out/proba.lox", "proba-plan.pdf"], ignoreOrder: true);
        var manifest = JsonSerializer.Deserialize<SurveyCompilerServiceResult>(
            answer[SurveyCompilerServiceProtocol.ManifestName], JsonSerializerOptions.Web)!;
        manifest.Succeeded.ShouldBeTrue();
        manifest.ExitCode.ShouldBe(0);
        manifest.OutputFiles.ShouldBe(["out/proba.lox", "proba-plan.pdf"]);
        manifest.Version.ShouldBe(FakeSurveyCompiler.FakeVersion);
        answer["out/proba.lox"].ShouldBe("invented out/proba.lox");

        compiler.LastProject.Keys.ShouldBe([Config, "proba.th"], ignoreOrder: true);
        service.Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public void The_run_directory_is_taken_out_of_the_paths_the_log_names()
    {
        var run = Path.Combine(TestScratch.Root, "work", "3f2a");
        var log = $"writing {run}/out/proba.lox ... done\nworking in {run}\n";

        TherionServiceHost.RelativeToProject(log, run).ShouldBe("writing out/proba.lox ... done\nworking in .\n");
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("rooted")]
    [InlineData("link")]
    public async Task A_project_with_an_entry_that_is_not_an_ordinary_file_inside_it_is_refused_before_anything_runs(string kind)
    {
        var compiler = new FakeSurveyCompiler();
        await using var service = await TherionServiceHarness.StartAsync(compiler);
        var outside = $"silexgis-escaped-{Guid.NewGuid():N}.th";
        (string, string) hostile = kind switch
        {
            "escape" => ($"../{outside}", "survey escaped\nendsurvey\n"),
            "rooted" => ($"/{outside}", "survey escaped\nendsurvey\n"),
            _ => ("secrets.th", "->/etc/passwd"),
        };

        using var response = await PostAsync(service, TherionServiceHarness.Tar([.. Project, hostile]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe("therion_service.project_invalid");
        compiler.Calls.ShouldBe(0);
        File.Exists(Path.Combine(TestScratch.Root, outside)).ShouldBeFalse();
        service.Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_configuration_that_is_not_a_file_at_the_top_of_the_project_is_refused()
    {
        var compiler = new FakeSurveyCompiler();
        await using var service = await TherionServiceHarness.StartAsync(compiler);

        using (var nested = await PostAsync(service, TherionServiceHarness.Tar(Project), config: "out/proba.thconfig"))
        {
            nested.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(nested)).ShouldBe("therion_service.config_invalid");
        }

        using (var missing = await PostAsync(service, TherionServiceHarness.Tar(Project), config: "other.thconfig"))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(missing)).ShouldBe("therion_service.config_missing");
        }

        compiler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_project_that_is_not_a_tar_or_is_too_large_is_refused()
    {
        var compiler = new FakeSurveyCompiler();
        await using var service = await TherionServiceHarness.StartAsync(
            compiler, new Dictionary<string, string?> { ["TherionService:MaxProjectBytes"] = "1048576" });

        using (var octets = await PostAsync(service, TherionServiceHarness.Tar(Project), mediaType: "application/octet-stream"))
        {
            octets.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        }

        using (var large = await PostAsync(service, TherionServiceHarness.Tar([.. Project, ("big.th2", new string('x', 2 * 1024 * 1024))])))
        {
            large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            (await CodeAsync(large)).ShouldBe("therion_service.project_too_large");
        }

        compiler.Calls.ShouldBe(0);
        service.Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_compile_that_runs_out_of_time_is_answered_as_such()
    {
        await using var service = await TherionServiceHarness.StartAsync(new SlowCompiler());

        using var response = await PostAsync(service, TherionServiceHarness.Tar(Project));

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        (await CodeAsync(response)).ShouldBe("therion_service.timeout");
        service.Leftovers().ShouldBeEmpty();
    }

    private static async Task<SurveyCompilerServiceStatus?> StatusAsync(TherionServiceHarness service)
    {
        using var client = service.Client();
        var text = await client.GetStringAsync(SurveyCompilerServiceProtocol.VersionPath);
        return JsonSerializer.Deserialize<SurveyCompilerServiceStatus>(text, JsonSerializerOptions.Web);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        TherionServiceHarness service, byte[] tar, string config = Config, string mediaType = SurveyProjectTar.MediaType)
    {
        using var client = service.Client();
        var content = new ByteArrayContent(tar);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return await client.PostAsync(
            $"{SurveyCompilerServiceProtocol.CompilePath}?{SurveyCompilerServiceProtocol.ConfigParameter}={Uri.EscapeDataString(config)}"
            + $"&{SurveyCompilerServiceProtocol.TimeoutParameter}=30",
            content);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>A compiler whose every compile runs out of time.</summary>
    private sealed class SlowCompiler : ISurveyCompiler
    {
        public bool IsAvailable => true;

        public string? Version => "slow 1.0";

        public Task<SurveyCompileResult> CompileAsync(
            SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct) =>
            throw new TimeoutException("invented");
    }
}
