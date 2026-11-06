// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Basemap;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation's own basemap, as an administrator downloads it: who may, what is checked
/// before anything is fetched, one download at a time, a finished one served under an address of
/// its own and the extract before it removed, one called off or refused leaving what was served
/// alone — and the catalogue's entry offered only while there is an extract to draw.
/// </summary>
/// <remarks>
/// Nothing here reaches the internet: the program that cuts a region and the list of builds are
/// replaced by fakes, and the job worker is switched off so that each download is run by the test
/// at the moment it chooses.
/// </remarks>
public sealed class BasemapDownloadTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Template = "/basemap/tiles/{basemap}/{z}/{x}/{y}";

    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly string connectionString;
    private readonly string directory;
    private readonly FakeExtractor extractor = new();
    private readonly FakeBuilds builds = new();
    private SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;

    public BasemapDownloadTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        directory = Path.Combine(AppContext.BaseDirectory, "test-data", $"basemap-{suffix}");
        factory = Build(selfHosted: true);
    }

    private string OwnEntry => $"Own basemap {suffix}";

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"bmd-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"bmd-rdr-{suffix}@t.local");
        await SignInAsync();

        using var scope = factory.Services.CreateScope();
        // The class's tests share one database, and a download one of them left queued would be
        // the "one at a time" every later test is refused by.
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.BasemapExtract)
            .ExecuteDeleteAsync();
        var report = await MapLayerSeeder.SeedAsync(
            $$"""
            <silexgis-map-layers version="1">
              <layer name="{{OwnEntry}}" group="Vector" kind="vector" format="mvt" url="{{Template}}"
                     style="/map-styles/protomaps/light.json" styleSource="protomaps" maxZoom="15"
                     attribution="© OpenStreetMap contributors" />
            </silexgis-map-layers>
            """,
            new Dictionary<string, string>(),
            scope.ServiceProvider.GetRequiredService<ICrsRegistry>(),
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
        report.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_an_administrator_sees_or_downloads_the_installations_basemap()
    {
        (await anonymous.GetAsync("/api/v1/admin/basemap")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni())).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        await ProblemAsync(await reader.GetAsync("/api/v1/admin/basemap"), HttpStatusCode.Forbidden, "access.forbidden");
        await ProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni()), HttpStatusCode.Forbidden, "access.forbidden");
        await ProblemAsync(
            await reader.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni()), HttpStatusCode.Forbidden, "access.forbidden");
        (await admin.GetAsync("/api/v1/admin/basemap")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Downloads()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_an_extract_the_entry_is_not_offered_and_the_page_says_which_entries_wait_for_one()
    {
        var status = await StatusAsync();

        status["selfHosted"]!.GetValue<bool>().ShouldBeTrue();
        status["current"].ShouldBeNull();
        status["download"].ShouldBeNull();
        status["entries"]!.AsArray().Select(e => e!.GetValue<string>()).ShouldContain(OwnEntry);
        status["presets"]!.AsArray().Select(p => p!["key"]!.GetValue<string>()).ShouldBe(["romania", "apuseni"], ignoreOrder: true);
        status["maxZoom"]!.GetValue<int>().ShouldBe(15);
        (await PublishedAsync()).Keys.ShouldNotContain(OwnEntry);
    }

    [Theory]
    [InlineData("""{"region":{"west":23.4,"south":46.2,"east":22.2,"north":47.0},"maxZoom":15}""")]
    [InlineData("""{"region":{"west":22.2,"south":46.2,"east":23.4,"north":89.0},"maxZoom":15}""")]
    [InlineData("""{"region":{"west":22.2,"south":46.2,"east":23.4,"north":47.0},"maxZoom":16}""")]
    [InlineData("""{"region":{"west":22.2,"south":46.2,"east":23.4,"north":47.0},"maxZoom":15,"build":"../../planet"}""")]
    [InlineData("""{"maxZoom":15}""")]
    [InlineData("""{"region":{"west":22.2,"south":46.2,"east":23.4,"north":47.0}}""")]
    public async Task A_download_of_anything_but_a_box_a_level_and_a_dated_build_is_refused_before_anything_is_fetched(string body)
    {
        var response = await admin.PostAsync(
            "/api/v1/admin/basemap/downloads", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        await ProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        (await Downloads()).ShouldBeEmpty();
        extractor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_an_administrator_lists_builds_or_calls_off_a_download()
    {
        var job = await StartAsync(Apuseni());
        var id = job["id"]!.GetValue<long>();

        (await anonymous.GetAsync("/api/v1/admin/basemap/builds")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        await ProblemAsync(await reader.GetAsync("/api/v1/admin/basemap/builds"), HttpStatusCode.Forbidden, "access.forbidden");
        await ProblemAsync(
            await reader.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null), HttpStatusCode.Forbidden, "access.forbidden");

        // The refused calls left the download as it was; the administrator's own calls are answered.
        (await Downloads()).ShouldHaveSingleItem().Status.ShouldBe(ProcessingJobStatus.Queued);
        (await admin.GetAsync("/api/v1/admin/basemap/builds")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_source_that_cannot_answer_an_estimate_is_a_coded_503()
    {
        extractor.EstimateFailure = new BasemapException(SelfHostedBasemap.Codes.SourceUnreachable, "stand-in: no answer");

        await ProblemAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni()),
            HttpStatusCode.ServiceUnavailable,
            SelfHostedBasemap.Codes.SourceUnreachable);
    }

    [Fact]
    public async Task A_source_that_cannot_list_its_builds_is_a_coded_503()
    {
        (await admin.GetAsync("/api/v1/admin/basemap/builds")).StatusCode.ShouldBe(HttpStatusCode.OK);

        builds.Failure = new BasemapException(SelfHostedBasemap.Codes.SourceUnreachable, "stand-in: no answer");

        await ProblemAsync(
            await admin.GetAsync("/api/v1/admin/basemap/builds"),
            HttpStatusCode.ServiceUnavailable,
            SelfHostedBasemap.Codes.SourceUnreachable);
    }

    [Fact]
    public async Task An_extract_that_fails_part_way_fails_with_its_code_and_leaves_no_partial_file()
    {
        extractor.Failure = new BasemapException(SelfHostedBasemap.Codes.ExtractFailed, "stand-in: the extractor stopped");
        var job = await StartAsync(Apuseni());

        await RunAsync(job["id"]!.GetValue<long>());

        var download = (await StatusAsync())["download"]!;
        download["state"]!.GetValue<string>().ShouldBe("failed");
        download["errorCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.ExtractFailed);
        // The extractor did write part of a file before it failed, and none of it is left.
        extractor.Calls.ShouldContain(c => !c.DryRun);
        Directory.GetFiles(directory).ShouldBeEmpty();
        (await StatusAsync())["current"].ShouldBeNull();
    }

    [Fact]
    public async Task Downloads_asked_for_at_the_same_moment_still_run_one_at_a_time()
    {
        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni());
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));

        answers.Count(a => a.StatusCode == HttpStatusCode.Accepted).ShouldBe(1, string.Join("\n", answers.Select(a => $"{a.StatusCode} {a.Body}")));
        answers.Where(a => a.StatusCode != HttpStatusCode.Accepted)
            .ShouldAllBe(a => a.StatusCode == HttpStatusCode.Conflict && a.Body.Contains(SelfHostedBasemap.Codes.DownloadRunning));
        (await Downloads()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_download_at_a_time()
    {
        (await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni())).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await ProblemAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni()),
            HttpStatusCode.Conflict,
            SelfHostedBasemap.Codes.DownloadRunning);
        (await Downloads()).Count.ShouldBe(1);
    }

    /// <summary>
    /// The latest build is looked up, the region weighed and cut, and the extract served under a
    /// name of its own — the catalogue's entry then published at that address. A second download
    /// is served under another, and the first extract is removed.
    /// </summary>
    [Fact]
    public async Task A_finished_download_is_served_under_an_address_of_its_own_and_replaces_the_one_before()
    {
        var first = await StartAsync(Apuseni());
        first["state"]!.GetValue<string>().ShouldBe("queued");
        first["build"].ShouldBeNull();

        await RunAsync(first["id"]!.GetValue<long>());

        var status = await StatusAsync();
        status["download"]!["state"]!.GetValue<string>().ShouldBe("succeeded");
        status["download"]!["percent"]!.GetValue<int>().ShouldBe(100);
        // "The latest" was the newest build the source lists, and the page says which.
        status["download"]!["build"]!.GetValue<string>().ShouldBe(FakeBuilds.Newest);
        var current = status["current"]!;
        var sourceId = current["sourceId"]!.GetValue<string>();
        sourceId.ShouldStartWith($"basemap-{FakeBuilds.Newest}-");
        current["sizeBytes"]!.GetValue<long>().ShouldBe(FakeExtractor.Bytes);
        current["maxZoom"]!.GetValue<int>().ShouldBe(15);
        current["region"]!["west"]!.GetValue<double>().ShouldBe(SelfHostedBasemap.Apuseni.West);
        // Cut from the configured source at that build, nowhere else.
        extractor.Calls.ShouldAllBe(c => c.Source == $"https://build.protomaps.com/{FakeBuilds.Newest}.pmtiles");

        var published = await PublishedAsync();
        published[OwnEntry]["urlTemplate"]!.GetValue<string>().ShouldBe($"/basemap/tiles/{sourceId}/{{z}}/{{x}}/{{y}}");

        // A second download straight after, of an older build chosen by date: the build alone
        // gives it a name of its own.
        var second = await StartAsync(Apuseni() with { Build = "20261022" });
        await RunAsync(second["id"]!.GetValue<long>());

        var replaced = (await StatusAsync())["current"]!["sourceId"]!.GetValue<string>();
        replaced.ShouldStartWith("basemap-20261022-");
        replaced.ShouldNotBe(sourceId, "an address was reused");
        File.Exists(Path.Combine(directory, sourceId + SelfHostedBasemap.FileExtension)).ShouldBeFalse();
        Directory.GetFiles(directory, "*.pmtiles*").Select(Path.GetFileName).ShouldBe([replaced + SelfHostedBasemap.FileExtension]);
        (await PublishedAsync())[OwnEntry]["urlTemplate"]!.GetValue<string>().ShouldContain(replaced);

        // Each download asked for is in the audit trail, with who asked.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        string[] asked = [first["id"]!.GetValue<long>().ToString(), second["id"]!.GetValue<long>().ToString()];
        (await db.Set<AuditEntry>().CountAsync(a =>
                a.EntityType == "Basemap" && a.Action == AuditActions.Created && a.UserId != null && asked.Contains(a.EntityId)))
            .ShouldBe(2);
    }

    [Fact]
    public async Task A_download_larger_than_the_installation_accepts_fails_with_its_code_and_fetches_nothing()
    {
        const string code = SelfHostedBasemap.Codes.TooLarge;
        extractor.Estimate = 2_000_000_000;
        var job = await StartAsync(Apuseni());

        await RunAsync(job["id"]!.GetValue<long>());

        var download = (await StatusAsync())["download"]!;
        download["state"]!.GetValue<string>().ShouldBe("failed");
        download["errorCode"]!.GetValue<string>().ShouldBe(code);
        extractor.Calls.ShouldAllBe(c => c.DryRun);
        (await StatusAsync())["current"].ShouldBeNull();
    }

    /// <summary>
    /// A region the size cap would take but the disk has no room for is refused for the disk: the
    /// estimate names the disk, and a download fails with that code having fetched nothing. The
    /// region asked for is far larger than any disk, so the real free space decides it.
    /// </summary>
    [Fact]
    public async Task A_region_the_disk_has_no_room_for_is_refused_for_the_disk()
    {
        await RestartAsync(selfHosted: true, maxBytes: long.MaxValue);
        const long exabytes = 4_000_000_000_000_000_000;

        // The positive case first: a region that fits is weighed with no refusal.
        extractor.Estimate = 36_000_000;
        var fits = await (await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni())).Content.ReadFromJsonAsync<JsonNode>();
        fits!["refusalCode"].ShouldBeNull();
        fits["freeBytes"]!.GetValue<long>().ShouldBeGreaterThan(0);

        extractor.Estimate = exabytes;
        var weighed = await (await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni())).Content.ReadFromJsonAsync<JsonNode>();
        weighed!["bytes"]!.GetValue<long>().ShouldBe(exabytes);
        weighed["refusalCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.DiskFull);

        var job = await StartAsync(Apuseni());
        await RunAsync(job["id"]!.GetValue<long>());

        var download = (await StatusAsync())["download"]!;
        download["state"]!.GetValue<string>().ShouldBe("failed");
        download["errorCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.DiskFull);
        extractor.Calls.ShouldAllBe(c => c.DryRun);
        (await StatusAsync())["current"].ShouldBeNull();
    }

    [Fact]
    public async Task A_queued_download_called_off_never_runs()
    {
        var job = await StartAsync(Apuseni());
        var id = job["id"]!.GetValue<long>();

        var cancelled = await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelled.Content.ReadFromJsonAsync<JsonNode>())!["state"]!.GetValue<string>().ShouldBe("cancelled");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ProcessingJobs.SingleAsync(j => j.Id == id)).Status.ShouldBe(ProcessingJobStatus.Failed);
        (await db.Set<AuditEntry>().CountAsync(a =>
                a.EntityType == "Basemap" && a.Action == AuditActions.Updated && a.EntityId == id.ToString()))
            .ShouldBe(1);
        // And the next one may be asked for.
        (await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni())).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await ProblemAsync(
            await admin.PostAsync("/api/v1/admin/basemap/downloads/987654321/cancel", null),
            HttpStatusCode.NotFound,
            SelfHostedBasemap.Codes.NotFound);
    }

    /// <summary>
    /// A download being cut is stopped where it is, what it had written removed, and the extract
    /// that was being served goes on being served.
    /// </summary>
    [Fact]
    public async Task A_running_download_called_off_stops_and_leaves_what_was_served()
    {
        var first = await StartAsync(Apuseni());
        await RunAsync(first["id"]!.GetValue<long>());
        var served = (await StatusAsync())["current"]!["sourceId"]!.GetValue<string>();

        extractor.Hold = true;
        var second = await StartAsync(Apuseni());
        var id = second["id"]!.GetValue<long>();
        var running = RunAsync(id);
        await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        (await StatusAsync())["download"]!["state"]!.GetValue<string>().ShouldBe("running");

        (await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        var status = await StatusAsync();
        status["download"]!["state"]!.GetValue<string>().ShouldBe("cancelled");
        status["download"]!["errorCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.Cancelled);
        status["current"]!["sourceId"]!.GetValue<string>().ShouldBe(served);
        Directory.GetFiles(directory, "*.partial").ShouldBeEmpty();
    }

    /// <summary>
    /// The worker takes a download a moment before its run is ready to be called off. A cancel in
    /// that moment is held for the run rather than dropped: the run stops before it fetches
    /// anything, and the extract being served stays served.
    /// </summary>
    [Fact]
    public async Task A_download_called_off_between_its_claim_and_its_start_does_not_run()
    {
        var first = await StartAsync(Apuseni());
        await RunAsync(first["id"]!.GetValue<long>());
        var served = (await StatusAsync())["current"]!["sourceId"]!.GetValue<string>();

        var second = await StartAsync(Apuseni());
        var id = second["id"]!.GetValue<long>();
        using (var scope = factory.Services.CreateScope())
        {
            // Claimed, as the worker's claim leaves it, and not yet started.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == id);
            job.Status = ProcessingJobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;
            job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        (await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        int extractsBefore;
        lock (extractor.Calls)
        {
            extractsBefore = extractor.Calls.Count(c => !c.DryRun);
        }

        await RunAsync(id);

        var status = await StatusAsync();
        status["download"]!["state"]!.GetValue<string>().ShouldBe("cancelled");
        status["download"]!["errorCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.Cancelled);
        status["current"]!["sourceId"]!.GetValue<string>().ShouldBe(served);
        lock (extractor.Calls)
        {
            extractor.Calls.Count(c => !c.DryRun).ShouldBe(extractsBefore);
        }

        Directory.GetFiles(directory, "*.partial").ShouldBeEmpty();
    }

    /// <summary>
    /// A cancel that reaches a download only after it ended changes nothing and is not recorded as a
    /// cancellation: the audit says what happened, not what was asked for.
    /// </summary>
    [Fact]
    public async Task Calling_off_a_finished_download_changes_nothing_and_records_nothing()
    {
        var started = await StartAsync(Apuseni());
        var id = started["id"]!.GetValue<long>();
        await RunAsync(id);

        var response = await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonNode.Parse(payload)!["state"]!.GetValue<string>().ShouldBe("succeeded");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Set<AuditEntry>().CountAsync(a =>
                a.EntityType == "Basemap" && a.Action == AuditActions.Updated && a.EntityId == id.ToString()))
            .ShouldBe(0);
        scope.ServiceProvider.GetRequiredService<BasemapCancellation>().IsCancelled(id).ShouldBeFalse();
    }

    /// <summary>
    /// A download's run ends a moment before the worker records how it ended, so for that moment
    /// the job still reads as running. A cancel arriving then reached nothing: it is not recorded as
    /// a cancellation, and it leaves no call held for a run that will never come.
    /// </summary>
    [Fact]
    public async Task Calling_off_a_download_whose_run_has_ended_but_is_not_yet_recorded_records_nothing()
    {
        // The positive case first: a download the call does reach is recorded as called off.
        var reached = (await StartAsync(Apuseni()))["id"]!.GetValue<long>();
        (await admin.PostAsync($"/api/v1/admin/basemap/downloads/{reached}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CancelAuditsAsync(reached)).ShouldBe(1);

        var id = (await StartAsync(Apuseni()))["id"]!.GetValue<long>();
        using (var scope = factory.Services.CreateScope())
        {
            // Taken by the worker, as its claim leaves it; the run then ends, and the worker has not
            // yet written the outcome.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == id);
            job.Status = ProcessingJobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;
            job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
            var cancellation = scope.ServiceProvider.GetRequiredService<BasemapCancellation>();
            cancellation.Register(id);
            cancellation.Finish(id);
        }

        var response = await admin.PostAsync($"/api/v1/admin/basemap/downloads/{id}/cancel", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await CancelAuditsAsync(id)).ShouldBe(0);
        using var check = factory.Services.CreateScope();
        check.ServiceProvider.GetRequiredService<BasemapCancellation>().IsCancelled(id).ShouldBeFalse();
    }

    private async Task<int> CancelAuditsAsync(long jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Set<AuditEntry>().CountAsync(a =>
            a.EntityType == "Basemap" && a.Action == AuditActions.Updated && a.EntityId == jobId.ToString());
    }

    [Fact]
    public async Task Weighs_a_region_without_downloading_it_and_lists_the_builds_to_choose_from()
    {
        extractor.Estimate = 36_000_000;
        var response = await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni());
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var estimate = JsonNode.Parse(payload)!;

        estimate["bytes"]!.GetValue<long>().ShouldBe(36_000_000);
        estimate["build"]!.GetValue<string>().ShouldBe(FakeBuilds.Newest);
        estimate["refusalCode"].ShouldBeNull();
        extractor.Calls.ShouldHaveSingleItem().DryRun.ShouldBeTrue();
        (await Downloads()).ShouldBeEmpty();

        extractor.Estimate = 2_000_000_000;
        var refused = await (await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni())).Content.ReadFromJsonAsync<JsonNode>();
        refused!["refusalCode"]!.GetValue<string>().ShouldBe(SelfHostedBasemap.Codes.TooLarge);

        var builds = await (await admin.GetAsync("/api/v1/admin/basemap/builds")).Content.ReadFromJsonAsync<JsonNode>();
        builds!.AsArray().Select(b => b!["build"]!.GetValue<string>()).ShouldBe([FakeBuilds.Newest, "20261023", "20261022"]);
    }

    /// <summary>
    /// Switched off, the installation downloads nothing and offers nothing drawn from its own
    /// basemap — even with an extract still in the directory.
    /// </summary>
    [Fact]
    public async Task Switched_off_it_downloads_nothing_and_offers_no_entry_even_with_an_extract_in_place()
    {
        var job = await StartAsync(Apuseni());
        await RunAsync(job["id"]!.GetValue<long>());
        (await PublishedAsync()).Keys.ShouldContain(OwnEntry);

        await RestartAsync(selfHosted: false);

        (await StatusAsync())["selfHosted"]!.GetValue<bool>().ShouldBeFalse();
        (await PublishedAsync()).Keys.ShouldNotContain(OwnEntry);
        await ProblemAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", Apuseni()),
            HttpStatusCode.Conflict,
            SelfHostedBasemap.Codes.SelfHostingOff);
        await ProblemAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/basemap/estimate", Apuseni()),
            HttpStatusCode.Conflict,
            SelfHostedBasemap.Codes.SelfHostingOff);
    }

    /// <summary>
    /// A test or showcase installation names a first region, and its first start queues it — once:
    /// the queued download is the record that one was asked for, and an installation that already
    /// has an extract asks for nothing.
    /// </summary>
    [Fact]
    public async Task A_configured_first_region_is_queued_on_a_start_that_finds_nothing_and_never_again()
    {
        await RestartAsync(selfHosted: true, initialRegion: "apuseni");

        var queued = (await Downloads()).ShouldHaveSingleItem();
        queued.Status.ShouldBe(ProcessingJobStatus.Queued);
        queued.RequestedBy.ShouldBeNull();
        var payload = JsonNode.Parse(queued.Payload)!;
        payload["west"]!.GetValue<double>().ShouldBe(SelfHostedBasemap.Apuseni.West);
        payload["maxZoom"]!.GetValue<int>().ShouldBe(SelfHostedBasemap.MaxZoom);
        payload["build"].ShouldBeNull();

        await RestartAsync(selfHosted: true, initialRegion: "apuseni");
        (await Downloads()).Count.ShouldBe(1);

        // With an extract already in place, nothing is asked for even with no download on record.
        await RunAsync(queued.Id);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().ProcessingJobs
                .Where(j => j.Kind == ProcessingJobKinds.BasemapExtract)
                .ExecuteDeleteAsync();
        }

        await RestartAsync(selfHosted: true, initialRegion: "apuseni");
        (await Downloads()).ShouldBeEmpty();
    }

    // ---- fixtures

    private sealed record RegionBody(double West, double South, double East, double North);

    private sealed record DownloadBody(RegionBody Region, int MaxZoom, string? Build);

    private static DownloadBody Apuseni()
    {
        var r = SelfHostedBasemap.Apuseni;
        return new DownloadBody(new RegionBody(r.West, r.South, r.East, r.North), 15, null);
    }

    private SilexGisApiFactory Build(bool selfHosted, string? initialRegion = null, long maxBytes = 1_000_000_000) => new(
        connectionString,
        new Dictionary<string, string?>
        {
            ["Jobs:PollSeconds"] = "0",
            ["Basemap:SelfHosted"] = selfHosted ? "true" : "false",
            ["Basemap:Directory"] = directory,
            ["Basemap:MaxBytes"] = maxBytes.ToString(CultureInfo.InvariantCulture),
            ["Basemap:InitialRegion"] = initialRegion,
        },
        services =>
        {
            services.AddSingleton<IBasemapExtractor>(extractor);
            services.AddSingleton<IBasemapBuilds>(builds);
        });

    private async Task SignInAsync()
    {
        admin = await AuthHelper.BearerClientAsync(factory, $"bmd-adm-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"bmd-rdr-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    private async Task RestartAsync(bool selfHosted, string? initialRegion = null, long maxBytes = 1_000_000_000)
    {
        DisposeClients();
        await factory.DisposeAsync();
        factory = Build(selfHosted, initialRegion, maxBytes);
        await SignInAsync();
    }

    private async Task<JsonNode> StartAsync(DownloadBody body)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/basemap/downloads", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, payload);
        return JsonNode.Parse(payload)!;
    }

    /// <summary>Runs one download as the job worker would: taken, run, and its outcome recorded.</summary>
    private async Task RunAsync(long jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId);
        job.Status = ProcessingJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        // A running job always carries a lease, as the worker's own claim gives it.
        job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>().Single(h => h.Kind == ProcessingJobKinds.BasemapExtract);
        try
        {
            await handler.ExecuteAsync(job, CancellationToken.None);
            job.Status = ProcessingJobStatus.Succeeded;
        }
        catch (Exception e)
        {
            job.Status = ProcessingJobStatus.Failed;
            job.Error = e.Message;
        }

        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<JsonNode> StatusAsync()
    {
        var response = await admin.GetAsync("/api/v1/admin/basemap");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!;
    }

    private async Task<List<ProcessingJob>> Downloads()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().Where(j => j.Kind == ProcessingJobKinds.BasemapExtract).ToListAsync();
    }

    private async Task<Dictionary<string, JsonNode>> PublishedAsync()
    {
        var response = await reader.GetAsync("/api/v1/map-layers");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonNode.Parse(payload)!.AsArray().ToDictionary(l => l!["name"]!.GetValue<string>(), l => l!);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private void DisposeClients()
    {
        admin.Dispose();
        reader.Dispose();
        anonymous.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        DisposeClients();
        factory.Dispose();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>Stands in for the extracting program: weighs what it is told to, and writes a few bytes.</summary>
    private sealed class FakeExtractor : IBasemapExtractor
    {
        public const long Bytes = 4096;

        public long Estimate { get; set; } = 36_000_000;

        /// <summary>When set, a download waits to be called off instead of finishing.</summary>
        public bool Hold { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>When set, the size cannot be worked out and this is thrown instead.</summary>
        public BasemapException? EstimateFailure { get; set; }

        /// <summary>When set, a download writes part of its file and then fails with this.</summary>
        public BasemapException? Failure { get; set; }

        public List<(string Source, bool DryRun)> Calls { get; } = [];

        public Task<long> EstimateAsync(string source, BasemapRegion region, int maxZoom, CancellationToken ct)
        {
            lock (Calls)
            {
                Calls.Add((source, true));
            }

            return EstimateFailure is { } failure ? Task.FromException<long>(failure) : Task.FromResult(Estimate);
        }

        public async Task ExtractAsync(
            string source, BasemapRegion region, int maxZoom, string outputPath, Func<int, Task> progress, CancellationToken ct)
        {
            lock (Calls)
            {
                Calls.Add((source, false));
            }

            await File.WriteAllBytesAsync(outputPath, new byte[Bytes], ct);
            await progress(50);
            if (Failure is { } failure)
            {
                throw failure;
            }

            if (Hold)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
    }

    private sealed class FakeBuilds : IBasemapBuilds
    {
        public const string Newest = "20261024";

        /// <summary>When set, the source cannot be asked for its builds and this is thrown instead.</summary>
        public BasemapException? Failure { get; set; }

        public Task<IReadOnlyList<BasemapBuild>> ListAsync(CancellationToken ct) => Failure is { } failure
            ? Task.FromException<IReadOnlyList<BasemapBuild>>(failure)
            : Task.FromResult<IReadOnlyList<BasemapBuild>>(
            [
                new(Newest, 139_134_051_681, DateTimeOffset.Parse("2026-10-24T09:06:14Z"), "4.15.2"),
                new("20261023", 139_104_211_087, DateTimeOffset.Parse("2026-10-23T09:04:57Z"), "4.15.2"),
                new("20261022", 139_077_432_987, DateTimeOffset.Parse("2026-10-22T09:03:22Z"), "4.15.2"),
            ]);
    }
}
