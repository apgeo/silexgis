// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The analysis service's contract, both sides of it, in this process: the service on a test server
/// with a stand-in for WhiteboxTools, and the application's client of it — against the service, and
/// against answers no honest service would give. The rasters are invented text.
/// </summary>
public sealed class WhiteboxServiceTests : IDisposable
{
    private static readonly WhiteboxArgument[] Geomorphons =
    [
        WhiteboxArgument.Input("dem", "dem.tif"),
        WhiteboxArgument.Output("output", "forms.tif"),
        WhiteboxArgument.Number("search", 50),
        WhiteboxArgument.Number("threshold", 1.5),
        WhiteboxArgument.Switch("forms"),
    ];

    private readonly string run = Path.Combine(TestScratch.Root, $"silexgis-test-whitebox-run-{Guid.NewGuid():N}");

    public WhiteboxServiceTests()
    {
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run, "dem.tif"), "elevation");
    }

    public void Dispose()
    {
        if (Directory.Exists(run))
        {
            Directory.Delete(run, recursive: true);
        }
    }

    [Fact]
    public async Task It_says_whether_it_can_run_and_which_version_and_refuses_runs_when_it_cannot()
    {
        await using (var service = await WhiteboxServiceHarness.StartAsync(new FakeWhiteboxTools()))
        {
            (await StatusAsync(service)).ShouldBe(new WhiteboxServiceStatus(true, FakeWhiteboxTools.FakeVersion));
        }

        await using var none = await WhiteboxServiceHarness.StartAsync(new NoWhiteboxTools());
        (await StatusAsync(none)).ShouldBe(new WhiteboxServiceStatus(false, null));
        using var refused = await PostAsync(none, Tar(Manifest("Geomorphons", Geomorphons), ("dem.tif", "elevation")));
        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(refused)).ShouldBe("whitebox_service.unavailable");
    }

    [Fact]
    public async Task A_run_goes_through_the_client_and_the_service_and_brings_back_only_its_outputs()
    {
        var tools = new FakeWhiteboxTools();
        await using var service = await WhiteboxServiceHarness.StartAsync(tools);
        var client = Client(service);

        var result = await client.RunAsync(new WhiteboxRunRequest(run, "Geomorphons", Geomorphons, TimeSpan.FromMinutes(1)), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.OutputFiles.ShouldBe(["forms.tif"]);
        File.ReadAllText(Path.Combine(run, "forms.tif")).ShouldBe("Geomorphons(elevation)");
        // The service held only the input it was sent, and says nothing about where it ran.
        tools.LastRunFiles.ShouldBe(["dem.tif"]);
        result.OutputTail.ShouldNotContain(service.WorkDir);
        result.OutputTail.ShouldBe("wrote into forms.tif");
        // Nothing is left behind on either side: no run directory in the service, no landing
        // directory beside the run.
        service.Leftovers().ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(Path.Combine(run, SandboxedWhiteboxRunner.ScratchDirectoryName)).ShouldBeEmpty();
        // The version the answer named is remembered, without a question of its own.
        client.Version.ShouldBe(FakeWhiteboxTools.FakeVersion);
    }

    [Theory]
    [InlineData("WhiteboxTools", "plain")]           // not a tool the application runs
    [InlineData("InstallWbExtension", "plain")]      // a plugin that downloads software
    [InlineData("Geomorphons", "path")]              // a file outside the run
    [InlineData("Geomorphons", "smuggled")]          // a second option inside a value
    public async Task The_service_refuses_what_the_rules_refuse_whatever_its_caller_checked(string tool, string variant)
    {
        var tools = new FakeWhiteboxTools();
        await using var service = await WhiteboxServiceHarness.StartAsync(tools);
        WhiteboxArgument[] arguments = variant switch
        {
            "path" => [WhiteboxArgument.Input("dem", "dem.tif"), WhiteboxArgument.Output("output", "../../etc/forms.tif")],
            "smuggled" => [WhiteboxArgument.Input("dem", "dem.tif"), WhiteboxArgument.Output("output", "forms.tif"),
                new WhiteboxArgument("search", WhiteboxArgumentKind.Value, "5 --wd=/")],
            _ => Geomorphons,
        };

        using var response = await PostAsync(service, Tar(Manifest(tool, arguments), ("dem.tif", "elevation")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe("whitebox_service.request_refused");
        tools.LastRunDirectory.ShouldBeNull();
        service.Leftovers().ShouldBeEmpty();

        // And the client refuses the same before anything is sent.
        await Should.ThrowAsync<ArgumentException>(() => Client(service).RunAsync(
            new WhiteboxRunRequest(run, tool, arguments, TimeSpan.FromMinutes(1)), CancellationToken.None));
    }

    [Fact]
    public async Task The_service_refuses_runs_that_are_not_exactly_a_manifest_and_its_inputs()
    {
        var tools = new FakeWhiteboxTools();
        await using var service = await WhiteboxServiceHarness.StartAsync(tools);

        // No manifest; a file the request does not name; a named input that is missing; a link.
        using (var noManifest = await PostAsync(service, Tar(("dem.tif", "elevation"))))
        {
            (await CodeAsync(noManifest)).ShouldBe("whitebox_service.request_invalid");
        }

        using (var extra = await PostAsync(service, Tar(Manifest("Geomorphons", Geomorphons), ("dem.tif", "e"), ("other.tif", "x"))))
        {
            (await CodeAsync(extra)).ShouldBe("whitebox_service.input_mismatch");
        }

        using (var missing = await PostAsync(service, Tar(Manifest("Geomorphons", Geomorphons))))
        {
            (await CodeAsync(missing)).ShouldBe("whitebox_service.input_mismatch");
        }

        using (var link = await PostAsync(service, Tar(Manifest("Geomorphons", Geomorphons), ("dem.tif", "->/etc/passwd"))))
        {
            link.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(link)).ShouldBe("whitebox_service.input_invalid");
        }

        using (var notTar = new HttpRequestMessage(HttpMethod.Post, WhiteboxServiceProtocol.RunPath)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        })
        using (var answer = await service.Client().SendAsync(notTar))
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        }

        tools.LastRunDirectory.ShouldBeNull();
        service.Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_over_its_time_answers_a_timeout_which_the_client_reads_as_one()
    {
        await using var service = await WhiteboxServiceHarness.StartAsync(
            new FakeWhiteboxTools { Delay = TimeSpan.FromMinutes(5) },
            new Dictionary<string, string?> { ["Analysis:Whitebox:TimeoutSeconds"] = "10" });
        var client = Client(service);

        // The service holds the run to its own limit, whatever the caller asked for.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Should.ThrowAsync<TimeoutException>(() => client.RunAsync(
            new WhiteboxRunRequest(run, "Geomorphons", Geomorphons, TimeSpan.FromHours(1)), CancellationToken.None));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(2));
        service.Leftovers().ShouldBeEmpty();
        File.Exists(Path.Combine(run, "forms.tif")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_run_is_answered_as_failed_with_no_outputs()
    {
        await using var service = await WhiteboxServiceHarness.StartAsync(new FakeWhiteboxTools { FailWith = 101 });

        var result = await Client(service).RunAsync(
            new WhiteboxRunRequest(run, "Geomorphons", Geomorphons, TimeSpan.FromMinutes(1)), CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.ExitCode.ShouldBe(101);
        result.OutputFiles.ShouldBeEmpty();
        // The service's run directory is taken out of the program's words before they leave it.
        result.OutputTail.ShouldBe("fake failure in x.tif");
    }

    [Fact]
    public async Task An_unreachable_or_unwilling_service_is_unavailable_and_a_slow_one_a_timeout()
    {
        var unreachable = new ServiceWhiteboxTools(
            new Answering(_ => throw new HttpRequestException("connection refused")), Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
        await Should.ThrowAsync<WhiteboxUnavailableException>(() => unreachable.RunAsync(Request(), CancellationToken.None));

        var unwilling = new ServiceWhiteboxTools(
            new Answering(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
        await Should.ThrowAsync<WhiteboxUnavailableException>(() => unwilling.RunAsync(Request(), CancellationToken.None));

        var slow = new ServiceWhiteboxTools(
            new Answering(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)), Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
        await Should.ThrowAsync<TimeoutException>(() => slow.RunAsync(Request(), CancellationToken.None));

        // Unreachable and unwilling are passing failures the job queue retries; a status the
        // contract does not have is not.
        var strange = new ServiceWhiteboxTools(
            new Answering(_ => new HttpResponseMessage(HttpStatusCode.Conflict)), Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
        await Should.ThrowAsync<InvalidOperationException>(() => strange.RunAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task An_answer_is_read_as_untrusted_only_asked_for_outputs_land_and_nothing_escapes()
    {
        // A dishonest answer: an output the run did not ask for, one under a folder, and the asked
        // one. Only the asked-for output lands in the run; the rest never reach it.
        var dishonest = new ServiceWhiteboxTools(
            new Answering(_ => Ok(Tar(
                Result(new WhiteboxServiceResult(true, 0, ["forms.tif", "dem.tif", "sub/x.tif"], "tail", "WhiteboxTools 9")),
                ("forms.tif", "forms"), ("extra.tif", "x"), ("sub/x.tif", "y")))),
            Settings(), NullLogger<ServiceWhiteboxTools>.Instance);

        var result = await dishonest.RunAsync(Request(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.OutputFiles.ShouldBe(["forms.tif"]);
        Directory.EnumerateFiles(run).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["dem.tif", "forms.tif"]);
        File.ReadAllText(Path.Combine(run, "dem.tif")).ShouldBe("elevation");
        Directory.EnumerateFileSystemEntries(Path.Combine(run, SandboxedWhiteboxRunner.ScratchDirectoryName)).ShouldBeEmpty();

        // An answer that steps outside, or carries a link, is refused whole.
        File.Delete(Path.Combine(run, "forms.tif"));
        foreach (var hostile in new[] { ("../escape.tif", "x"), ("forms.tif", "->/etc/passwd") })
        {
            var service = new ServiceWhiteboxTools(
                new Answering(_ => Ok(Tar(Result(new WhiteboxServiceResult(true, 0, ["forms.tif"], "", null)), hostile))),
                Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
            await Should.ThrowAsync<InvalidOperationException>(() => service.RunAsync(Request(), CancellationToken.None));
            File.Exists(Path.Combine(run, "forms.tif")).ShouldBeFalse();
            File.Exists(Path.Combine(run, "..", "escape.tif")).ShouldBeFalse();
        }

        // An answer without a manifest says nothing of how the run ended.
        var silent = new ServiceWhiteboxTools(
            new Answering(_ => Ok(Tar(("forms.tif", "x")))), Settings(), NullLogger<ServiceWhiteboxTools>.Instance);
        await Should.ThrowAsync<InvalidOperationException>(() => silent.RunAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task The_client_sends_only_the_named_inputs_and_never_overwrites_a_file_in_the_run()
    {
        File.WriteAllText(Path.Combine(run, "unrelated.txt"), "not for the service");
        var tools = new FakeWhiteboxTools();
        await using var service = await WhiteboxServiceHarness.StartAsync(tools);

        await Client(service).RunAsync(Request(), CancellationToken.None);
        tools.LastRunFiles.ShouldBe(["dem.tif"]);

        // The output is there now: a second run asking to write it again is refused before sending.
        await Should.ThrowAsync<ArgumentException>(() => Client(service).RunAsync(Request(), CancellationToken.None));
    }

    private WhiteboxRunRequest Request() => new(run, "Geomorphons", Geomorphons, TimeSpan.FromMinutes(1));

    private static ServiceWhiteboxTools Client(WhiteboxServiceHarness service) =>
        new(service, Settings(), NullLogger<ServiceWhiteboxTools>.Instance);

    private static IOptions<WhiteboxOptions> Settings() =>
        Options.Create(new WhiteboxOptions { Mode = WhiteboxOptions.ModeService, Url = "http://whitebox:3200" });

    private static (string, string) Manifest(string tool, IReadOnlyList<WhiteboxArgument> arguments) =>
        (WhiteboxServiceProtocol.RequestName, JsonSerializer.Serialize(new WhiteboxServiceRequest(tool, arguments), JsonSerializerOptions.Web));

    private static (string, string) Result(WhiteboxServiceResult result) =>
        (WhiteboxServiceProtocol.ResultName, JsonSerializer.Serialize(result, JsonSerializerOptions.Web));

    private static byte[] Tar(params (string Name, string Text)[] entries) => TherionServiceHarness.Tar(entries);

    private static HttpResponseMessage Ok(byte[] tar)
    {
        var content = new ByteArrayContent(tar);
        content.Headers.ContentType = new MediaTypeHeaderValue(SurveyProjectTar.MediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task<WhiteboxServiceStatus?> StatusAsync(WhiteboxServiceHarness service)
    {
        using var client = service.Client();
        return JsonSerializer.Deserialize<WhiteboxServiceStatus>(
            await client.GetStringAsync(WhiteboxServiceProtocol.VersionPath), JsonSerializerOptions.Web);
    }

    private static async Task<HttpResponseMessage> PostAsync(WhiteboxServiceHarness service, byte[] tar)
    {
        using var client = service.Client();
        var content = new ByteArrayContent(tar);
        content.Headers.ContentType = new MediaTypeHeaderValue(SurveyProjectTar.MediaType);
        return await client.PostAsync(WhiteboxServiceProtocol.RunPath + "?" + WhiteboxServiceProtocol.TimeoutParameter + "=60", content);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>A client factory whose every request is answered by <paramref name="answer"/>.</summary>
    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
