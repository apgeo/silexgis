// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Rendering;
using Therion.Blender;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the render nodes' side holds to beyond the protocol's own rules: the space renders may take
/// (and that a refused or broken upload takes none), how many files one keeps, how long a film is
/// kept and a render may run or wait, which node is given which job (its container, its disk),
/// the record of each download, an account's renders going with the account, and the calls that
/// race each other — a completion against a stop and against the sweep, two claims for one job.
/// </summary>
/// <remarks>
/// Each render here is of an invented bundle: bytes made up for the test, stored by the same code
/// that stores a real one. The class's renders are deleted before each test, so the space they
/// take is the test's own.
/// </remarks>
public sealed class RenderNodeLimitsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/render-nodes/";
    private const long MaxResultBytes = 4_096;
    private const long StorageCap = 6_000;
    private const int MaxFiles = 3;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient requester = null!;
    private Guid requesterId;

    public RenderNodeLimitsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
                ["Jobs:PollSeconds"] = "0",
                ["RenderNodes:MaxResultBytes"] = MaxResultBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RenderNodes:StorageCapBytes"] = StorageCap.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RenderNodes:MaxFilesPerRender"] = MaxFiles.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RenderNodes:ClaimHoldSeconds"] = "6",
                ["RenderNodes:MaxActivePerAccount"] = "50",
                ["RenderNodes:MaxHours"] = "2",
                ["RenderNodes:QueuedDays"] = "3",
            });
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rnl-adm-{suffix}@t.local");
        requesterId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rnl-req-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rnl-adm-{suffix}@t.local");
        requester = await AuthHelper.BearerClientAsync(factory, $"rnl-req-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.BlenderRender).ExecuteDeleteAsync();
        await db.FlybyRenders.ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin?.Dispose();
        requester?.Dispose();
        factory.Dispose();
        try
        {
            Directory.Delete(filesRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    // ---- storage ---------------------------------------------------------------------------

    [Fact]
    public async Task An_upload_refused_or_broken_part_way_leaves_no_file_behind()
    {
        using var node = Node(await IssueAsync());
        var (_, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // No length, and past the largest file: refused by the streamed bytes, not by a header.
        var large = await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", Chunked(new byte[MaxResultBytes + 1_000]));
        large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await large.Content.ReadAsStringAsync()).ShouldContain($"this one is {MaxResultBytes + 1}");

        // A body that breaks off part way, as a dropped connection does, and a copy that is
        // cancelled: the store's own save, which every upload goes through, keeps nothing of
        // either. (Asked of the store, because the test server does not end a request whose body
        // broke off the way a real connection's end does.)
        await using (var store = factory.Services.CreateAsyncScope())
        {
            var files = store.ServiceProvider.GetRequiredService<IFileStore>();
            await Should.ThrowAsync<IOException>(() => files.SaveAsync(new NoLength(new MemoryStream(new byte[3_000]), 1_500), ".webm"));
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => files.SaveAsync(new MemoryStream(new byte[3_000]), ".mkv", cancelled.Token));
        }

        // A checksum that does not match, and a body shorter than its length said.
        var wrong = Chunked(new byte[100]);
        wrong.Headers.Remove(RenderNodeProtocol.Sha256Header);
        wrong.Headers.Add(RenderNodeProtocol.Sha256Header, new string('a', 64));
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mkv?attempt=1", wrong)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        StoredFiles().ShouldAllBe(f => f.EndsWith(".zip", StringComparison.Ordinal), "only the bundle is stored");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.FlybyRenderFiles.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Uploads_with_or_without_a_length_are_held_to_the_storage_cap_and_one_render_to_its_files()
    {
        using var node = Node(await IssueAsync());
        var (_, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3,000 bytes fit under the cap with the bundle; another 3,000 do not, with a length or without.
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File(new byte[3_000]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var declared = await node.PutAsync($"jobs/{jobId}/files/{jobId}.png?attempt=1", File(new byte[3_000]));
        declared.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await Code(declared)).ShouldBe(RenderNodeQueue.StorageFullCode);
        var chunked = await node.PutAsync($"jobs/{jobId}/files/{jobId}.png?attempt=1", Chunked(new byte[3_000]));
        chunked.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await Code(chunked)).ShouldBe(RenderNodeQueue.StorageFullCode);

        // A file sent again is measured against the room its first copy gives back.
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", Chunked(new byte[3_500]))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // At most so many files for one render; one of a name already held may still be sent again.
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.log?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.srt?attempt=1", File([2]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var tooMany = await node.PutAsync($"jobs/{jobId}/files/{jobId}.vtt?attempt=1", File([3]));
        tooMany.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await Code(tooMany)).ShouldBe(RenderNodeQueue.TooLargeCode);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.log?attempt=1", File([4]))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.FlybyRenderFiles.CountAsync()).ShouldBe(MaxFiles);
        StoredFiles().Count.ShouldBe(MaxFiles + 1, "the files kept and the bundle, and nothing refused");

        // And a new render that would go past the cap is refused before anything is stored.
        var renders = scope.ServiceProvider.GetRequiredService<FlybyRenders>();
        var (refused, refusal) = await renders.EnqueueAsync(
            requesterId, "An invented flyby", new byte[2_500], "full", "mp4", CancellationToken.None);
        refused.ShouldBeNull();
        refusal.ShouldBe(FlybyRenderRefusal.StorageFull);
        StoredFiles().Count.ShouldBe(MaxFiles + 1);
    }

    [Fact]
    public async Task Two_uploads_streamed_side_by_side_do_not_both_take_the_last_room()
    {
        using var node = Node(await IssueAsync());
        var (_, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var answers = await Task.WhenAll(
            node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", Chunked(new byte[3_200])),
            node.PutAsync($"jobs/{jobId}/files/{jobId}.webm?attempt=1", Chunked(new byte[3_200])));
        answers.Count(a => a.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        answers.Count(a => a.StatusCode == HttpStatusCode.RequestEntityTooLarge).ShouldBe(1);
        StoredFiles().Count.ShouldBe(2, "the bundle and the one file taken");
    }

    // ---- keeping, time limits --------------------------------------------------------------

    [Fact]
    public async Task A_finished_film_is_kept_until_its_time_and_then_deleted_with_its_files()
    {
        using var node = Node(await IssueAsync());
        var (renderId, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        byte[] film = [.. Enumerable.Range(0, 500).Select(i => (byte)i)];
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PostAsync($"jobs/{jobId}/complete", Json(Complete(jobId, film)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        StoredFiles().Count.ShouldBe(1, "the film; the bundle went when the render ended");

        // Not yet: a sweep leaves it.
        await SweepAsync();
        (await requester.GetAsync($"/api/v1/flybys/blender-renders/{renderId}/files/{jobId}.mp4")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.FlybyRenders.Where(r => r.Id == renderId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await SweepAsync();
        (await requester.GetAsync($"/api/v1/flybys/blender-renders/{renderId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        StoredFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_render_that_runs_past_its_hours_or_waits_past_its_days_is_ended_and_its_requester_told()
    {
        using var node = Node(await IssueAsync());
        var (runningId, runningJob) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (waitingId, waitingJob) = await QueueRenderAsync();

        // An hour within the limit and the margin: nothing happens, heartbeats or none.
        await AgeAsync(runningJob, startedHoursAgo: 2.5, createdDaysAgo: 0);
        await AgeAsync(waitingJob, startedHoursAgo: null, createdDaysAgo: 2.5);
        await SweepAsync();
        (await StatusAsync(runningId)).GetProperty("status").GetString().ShouldBe("running");
        (await StatusAsync(waitingId)).GetProperty("status").GetString().ShouldBe("queued");

        // Past them, both end — the running one although its node still sends heartbeats.
        (await node.PostAsync($"jobs/{runningJob}/heartbeat", Json(new { attempt = 1, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await AgeAsync(runningJob, startedHoursAgo: 3.5, createdDaysAgo: 0);
        await AgeAsync(waitingJob, startedHoursAgo: null, createdDaysAgo: 3.5);
        await SweepAsync();

        var ran = await StatusAsync(runningId);
        ran.GetProperty("status").GetString().ShouldBe("failed");
        ran.GetProperty("reason").GetString().ShouldBe("timeLimit");
        ran.GetProperty("error").GetString()!.ShouldContain("2 hours");
        var waited = await StatusAsync(waitingId);
        waited.GetProperty("status").GetString().ShouldBe("failed");
        waited.GetProperty("reason").GetString().ShouldBe("notTaken");
        waited.GetProperty("error").GetString()!.ShouldContain("3 days");

        // Its node is told at its next call, and the bundles are gone.
        (await node.PostAsync($"jobs/{runningJob}/heartbeat", Json(new { attempt = 1, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        StoredFiles().ShouldBeEmpty();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Notifications.CountAsync(n => n.RecipientUserId == requesterId)).ShouldBeGreaterThanOrEqualTo(2);
    }

    // ---- which node is given which job -------------------------------------------------------

    [Fact]
    public async Task A_node_is_given_only_a_render_it_can_encode_and_has_the_disk_for_and_its_row_says_why_not()
    {
        using var node = Node(await IssueAsync());
        var (_, webm) = await QueueRenderAsync(container: "webm");
        var (_, large) = await QueueRenderAsync(scratchBytes: 10_000_000_000);

        // Writes mp4 only, with a gigabyte free: neither.
        (await node.PostAsync("claim", Json(Claim(containers: ["mp4"], freeDiskBytes: 1_000_000_000)))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var row = await NodeRowAsync();
        row.GetProperty("skipReason").GetString().ShouldBe("container");
        row.GetProperty("skippedRenders").GetInt32().ShouldBe(1);

        // Writes all three, with a gigabyte free: the webm one, and the large one is left.
        var claimed = await node.PostAsync("claim", Json(Claim(freeDiskBytes: 1_000_000_000)));
        claimed.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await claimed.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetString().ShouldBe(webm);
        row = await NodeRowAsync();
        row.GetProperty("skipReason").GetString().ShouldBe("disk");
        row.GetProperty("skippedRenders").GetInt32().ShouldBe(1);
        row.GetProperty("skipNeedsBytes").GetInt64().ShouldBe(10_000_000_000);

        // A node that says nothing of its disk is not held to the estimate.
        var unsaid = await node.PostAsync("claim", Json(Claim(freeDiskBytes: null)));
        JsonDocument.Parse(await unsaid.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetString().ShouldBe(large);
        var cleared = await NodeRowAsync();
        (!cleared.TryGetProperty("skipReason", out var reason) || reason.ValueKind == JsonValueKind.Null).ShouldBeTrue();
    }

    [Fact]
    public async Task A_waiting_claim_takes_a_render_asked_for_while_it_waits_and_names_it_by_its_base_name()
    {
        using var node = Node(await IssueAsync());
        var claim = node.PostAsync("claim", Json(Claim(waitSeconds: 6)));
        await Task.Delay(1_500);
        claim.IsCompleted.ShouldBeFalse("the claim is held open while nothing is queued");

        var (_, jobId) = await QueueRenderAsync(name: "NUL");
        var answer = await claim.WaitAsync(TimeSpan.FromSeconds(10));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        var job = JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement;
        job.GetProperty("jobId").GetString().ShouldBe(jobId);
        job.GetProperty("name").GetString().ShouldBe("nul-flyby");

        // With nothing to take, the wait ends at the hold and answers no job.
        var started = DateTime.UtcNow;
        (await node.PostAsync("claim", Json(Claim(waitSeconds: 2)))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (DateTime.UtcNow - started).ShouldBeGreaterThan(TimeSpan.FromSeconds(1.5));
    }

    // ---- the bundle ------------------------------------------------------------------------

    [Fact]
    public async Task Each_download_of_a_bundle_is_recorded_with_its_node_and_a_missing_bundle_ends_the_render()
    {
        using var node = Node(await IssueAsync("rnl-recorded"));
        var (renderId, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.GetAsync($"jobs/{jobId}/bundle")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var render = await db.FlybyRenders.AsNoTracking().SingleAsync(r => r.Id == renderId);
            var record = await db.Set<AuditEntry>().AsNoTracking()
                .SingleAsync(a => a.EntityType == RenderNodeQueue.BundleAuditEntityType && a.EntityId == renderId.ToString());
            record.Action.ShouldBe(AuditActions.Exported);
            record.UserId.ShouldBeNull();
            var said = JsonDocument.Parse(record.Changes!).RootElement;
            said.GetProperty("format").GetString().ShouldBe(RenderNodeQueue.NodeDownloadFormat);
            said.GetProperty("nodeName").GetString().ShouldStartWith("rnl-recorded");
            said.GetProperty("attempt").GetInt32().ShouldBe(1);
            said.GetProperty("sha256").GetString().ShouldBe(render.BundleSha256);
            said.GetProperty("requestedBy").GetString().ShouldBe(requesterId.ToString());
        }

        // Its stored file gone: not found, and the render ends as a job no node can render.
        foreach (var file in StoredFiles())
        {
            System.IO.File.Delete(file);
        }

        var missing = await node.GetAsync($"jobs/{jobId}/bundle");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var ended = await StatusAsync(renderId);
        ended.GetProperty("status").GetString().ShouldBe("failed");
        ended.GetProperty("reason").GetString().ShouldBe(RenderNodeFailure.BadJob);
    }

    [Fact]
    public async Task A_withdrawn_node_is_refused_mid_render_and_its_render_returns_to_the_queue()
    {
        var token = await IssueAsync();
        using var node = Node(token);
        var (_, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/render-nodes")).EnumerateArray()
            .Single(n => token.StartsWith(n.GetProperty("tokenPrefix").GetString()!, StringComparison.Ordinal)).GetProperty("id").GetGuid();
        (await admin.DeleteAsync($"/api/v1/admin/render-nodes/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ExpireLeaseAsync(jobId);
        await SweepAsync();
        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Queued);
    }

    // ---- an account goes with its renders ------------------------------------------------------

    [Fact]
    public async Task Deleting_an_account_deletes_its_stored_bundles_and_films()
    {
        var leaver = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rnl-gone-{Guid.NewGuid():N}@t.local");
        using var node = Node(await IssueAsync());
        var (_, finished) = await QueueRenderAsync(requestedBy: leaver);
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        byte[] film = [1, 2, 3, 4];
        (await node.PutAsync($"jobs/{finished}/files/{finished}.mp4?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PostAsync($"jobs/{finished}/complete", Json(Complete(finished, film)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await QueueRenderAsync(requestedBy: leaver);
        StoredFiles().Count.ShouldBe(2, "a film and a queued bundle");

        (await admin.DeleteAsync($"/api/v1/users/{leaver}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        StoredFiles().ShouldBeEmpty();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.FlybyRenders.CountAsync(r => r.RequestedBy == leaver)).ShouldBe(0);
    }

    // ---- races -----------------------------------------------------------------------------

    [Fact]
    public async Task Two_nodes_claiming_at_once_never_both_get_one_render()
    {
        using var first = Node(await IssueAsync());
        using var second = Node(await IssueAsync());
        for (var round = 0; round < 8; round++)
        {
            var (_, jobId) = await QueueRenderAsync();
            var answers = await Task.WhenAll(first.PostAsync("claim", Json(Claim())), second.PostAsync("claim", Json(Claim())));
            answers.Count(a => a.StatusCode == HttpStatusCode.OK).ShouldBe(1, $"round {round}");
            answers.Count(a => a.StatusCode == HttpStatusCode.NoContent).ShouldBe(1, $"round {round}");
            (await JobAsync(jobId)).Attempts.ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_completion_racing_a_stop_or_the_sweep_ends_the_render_one_way_only_and_keeps_no_stray_file()
    {
        using var node = Node(await IssueAsync());
        for (var round = 0; round < 6; round++)
        {
            // Against a stop that its heartbeat carries.
            var (renderId, jobId) = await QueueRenderAsync();
            (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
            byte[] film = [.. Enumerable.Range(0, 64).Select(i => (byte)(i + round))];
            (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await requester.PostAsync($"/api/v1/flybys/blender-renders/{renderId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            var answers = await Task.WhenAll(
                node.PostAsync($"jobs/{jobId}/complete", Json(Complete(jobId, film))),
                node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" })));
            var status = (await StatusAsync(renderId)).GetProperty("status").GetString();
            if (answers[0].StatusCode == HttpStatusCode.OK)
            {
                status.ShouldBe("succeeded", $"round {round}: completion answered 200");
            }
            else
            {
                answers[0].StatusCode.ShouldBe(HttpStatusCode.Conflict);
                status.ShouldBe("cancelled", $"round {round}");
            }

            await ShouldHoldOnlyItsRowsFilesAsync();

            // Against the sweep that takes a lapsed lease back.
            var (againId, again) = await QueueRenderAsync();
            (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await node.PutAsync($"jobs/{again}/files/{again}.mp4?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
            await ExpireLeaseAsync(again);
            var complete = node.PostAsync($"jobs/{again}/complete", Json(Complete(again, film)));
            await Task.WhenAll(complete, SweepAsync());
            var job = await JobAsync(again);
            if ((await complete).StatusCode == HttpStatusCode.OK)
            {
                job.Status.ShouldBe(ProcessingJobStatus.Succeeded, $"round {round}");
            }
            else
            {
                (await complete).StatusCode.ShouldBe(HttpStatusCode.Conflict);
                job.Status.ShouldBe(ProcessingJobStatus.Queued, $"round {round}");
                (await StatusAsync(againId)).GetProperty("files").GetArrayLength().ShouldBe(0);

                // Cleared for the next round: the render goes, and so does what it stored.
                await requester.PostAsync($"/api/v1/flybys/blender-renders/{againId}/cancel", null);
            }

            await ShouldHoldOnlyItsRowsFilesAsync();
        }
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Every file in the store is a bundle or a result some row names; nothing else is there.</summary>
    private async Task ShouldHoldOnlyItsRowsFilesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var named = (await db.FlybyRenders.Where(r => r.BundlePath != null).Select(r => r.BundlePath!).ToListAsync())
            .Concat(await db.FlybyRenderFiles.Select(f => f.StoragePath).ToListAsync())
            .Select(p => Path.GetFullPath(store.GetAbsolutePath(p)))
            .ToHashSet();
        StoredFiles().Select(Path.GetFullPath).ShouldAllBe(file => named.Contains(file));
    }

    private IReadOnlyList<string> StoredFiles() =>
        Directory.Exists(filesRoot)
            ? [.. Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(Path.Combine(filesRoot, "keys"), StringComparison.Ordinal))]
            : [];

    private async Task<string> IssueAsync(string name = "rnl")
    {
        var issued = await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = $"{name}-{Guid.NewGuid():N}"[..(name.Length + 12)] });
        issued.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<(Guid RenderId, string JobId)> QueueRenderAsync(
        string container = "mp4", long scratchBytes = 0, string name = "An invented flyby", Guid? requestedBy = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var renders = scope.ServiceProvider.GetRequiredService<FlybyRenders>();
        var bundle = Encoding.UTF8.GetBytes($"an invented bundle {Guid.NewGuid()}");
        var (view, refusal) = await renders.EnqueueAsync(
            requestedBy ?? requesterId, name, bundle, "full", container, CancellationToken.None, scratchBytes);
        refusal.ShouldBe(FlybyRenderRefusal.None);
        return (view!.Render.Id, view.Job!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<JsonElement> StatusAsync(Guid renderId) =>
        await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{renderId}");

    private async Task<JsonElement> NodeRowAsync() =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/render-nodes")).EnumerateArray()
            .OrderByDescending(n => n.GetProperty("lastSeenAt").ValueKind == JsonValueKind.Null ? DateTimeOffset.MinValue : n.GetProperty("lastSeenAt").GetDateTimeOffset())
            .First();

    private async Task<ProcessingJob> JobAsync(string jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == long.Parse(jobId));
    }

    /// <summary>Moves a render's claim and asking back on the database's own clock.</summary>
    private async Task AgeAsync(string jobId, double? startedHoursAgo, double createdDaysAgo)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = long.Parse(jobId);
        if (startedHoursAgo is { } hours)
        {
            var seconds = hours * 3600;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_jobs SET started_at = now() - make_interval(secs => {seconds}) WHERE id = {id}");
        }

        var created = createdDaysAgo * 86400;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE flyby_renders SET created_at = now() - make_interval(secs => {created}) WHERE processing_job_id = {id}");
    }

    private async Task ExpireLeaseAsync(string jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = long.Parse(jobId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE processing_jobs SET lease_expires_at = now() - interval '1 minute' WHERE id = {id}");
    }

    private Task SweepAsync() =>
        factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RenderNodeSweeper>().Single()
            .SweepAsync(CancellationToken.None);

    /// <summary>A node's client: no redirect handling, which would buffer a body and give it a length.</summary>
    private HttpClient Node(string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.BaseAddress = new Uri(client.BaseAddress!, Base);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A claim; a node that says nothing of its disk leaves <c>freeDiskBytes</c> out.</summary>
    private static Dictionary<string, object?> Claim(string[]? containers = null, long? freeDiskBytes = 1_000_000_000L, int waitSeconds = 0)
    {
        var claim = new Dictionary<string, object?>
        {
            ["protocol"] = 1,
            ["node"] = "invented-node",
            ["worker"] = "0.4.0.0",
            ["blenderVersions"] = new[] { "4.5.0" },
            ["device"] = "CPU",
            ["containers"] = containers ?? ["mp4", "mkv", "webm"],
            ["keptJobs"] = Array.Empty<string>(),
            ["waitSeconds"] = waitSeconds,
        };
        if (freeDiskBytes is { } free)
        {
            claim["freeDiskBytes"] = free;
        }

        return claim;
    }

    private static object Complete(string jobId, byte[] film) => new
    {
        attempt = 1,
        files = new[] { new { name = $"{jobId}.mp4", role = "movie", size = film.Length, sha256 = Sha(film) } },
        seconds = 1.5,
        frameCount = 10,
        notices = Array.Empty<string>(),
    };

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static ByteArrayContent File(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.Add(RenderNodeProtocol.Sha256Header, Sha(bytes));
        return content;
    }

    /// <summary>A body whose length the client cannot know, so it goes without one.</summary>
    private static StreamContent Chunked(byte[] bytes)
    {
        var content = new StreamContent(new NoLength(new MemoryStream(bytes), null));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.Add(RenderNodeProtocol.Sha256Header, Sha(bytes));
        return content;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.Length == 0 ? null
            : JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>A stream of no known length; one that breaks off after <paramref name="breakAfter"/> bytes when given.</summary>
    private sealed class NoLength(Stream inner, int? breakAfter) : Stream
    {
        private int given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (breakAfter is { } after && given >= after)
            {
                throw new IOException("An invented connection dropped part way.");
            }

            var n = inner.Read(buffer, offset, breakAfter is { } limit ? Math.Min(count, Math.Max(1, limit - given)) : count);
            given += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
