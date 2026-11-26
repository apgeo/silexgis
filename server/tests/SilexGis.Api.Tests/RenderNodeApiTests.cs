// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.RenderNodes;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Rendering;
using Therion.Blender;

namespace SilexGis.Api.Tests;

/// <summary>
/// The render nodes' routes as a node calls them: who may call at all, which calls count for
/// which attempt, what a file must be to be taken, how a render ends, is stopped, and is taken back
/// when its node goes quiet — with every answer the status the survey library's protocol says.
/// </summary>
/// <remarks>
/// Each render here is of an invented bundle: bytes made up for the test, stored as a render's
/// bundle by the same code that stores a real one. No route here makes a bundle; making one is the
/// download's code, tested with it, and the route that queues a render with a real one is driven
/// by the browser test of the flyby panel.
/// </remarks>
public sealed class RenderNodeApiTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/render-nodes/";
    private const long MaxResultBytes = 4_096;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient requester = null!;
    private Guid requesterId;

    public RenderNodeApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
                // No worker and no sweep of their own: each test drives the queue itself.
                ["Jobs:PollSeconds"] = "0",
                ["RenderNodes:MaxResultBytes"] = MaxResultBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RenderNodes:ClaimHoldSeconds"] = "0",
                ["RenderNodes:MaxActivePerAccount"] = "10",
            });
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rn-adm-{suffix}@t.local");
        requesterId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rn-req-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rn-adm-{suffix}@t.local");
        requester = await AuthHelper.BearerClientAsync(factory, $"rn-req-{suffix}@t.local");

        // The queue is one table per test class; a render left by a test is not another's to claim.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.BlenderRender).ExecuteDeleteAsync();
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

    // ---- tokens --------------------------------------------------------------------------

    [Fact]
    public async Task An_administrator_issues_lists_and_withdraws_a_node_and_nobody_else_may()
    {
        var issued = await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = $"gpu-{suffix}" });
        issued.StatusCode.ShouldBe(HttpStatusCode.Created, await issued.Content.ReadAsStringAsync());
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        token.ShouldStartWith("sgn_");
        var id = body.GetProperty("node").GetProperty("id").GetGuid();

        // Stored as its hash only: the token is in no column.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.RenderNodes.SingleAsync(n => n.Id == id);
            row.TokenHash.ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            row.TokenPrefix.ShouldBe(token[..12]);
        }

        // The same name again, while the first is not withdrawn, is refused by name.
        var again = await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = $"gpu-{suffix}" });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(again)).ShouldBe(RenderNodeAdminEndpoints.NameTakenCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/render-nodes");
        var listed = list.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == id);
        listed.TryGetProperty("token", out _).ShouldBeFalse();
        listed.GetProperty("online").GetBoolean().ShouldBeFalse();

        // Not the settings' to write, not the nodes' to issue, list or withdraw.
        (await requester.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = "mine" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await requester.GetAsync("/api/v1/admin/render-nodes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await requester.DeleteAsync($"/api/v1/admin/render-nodes/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var node = Node(token);
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.DeleteAsync($"/api/v1/admin/render-nodes/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/admin/render-nodes/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Withdrawn: its next call is the one 401, and the name is free again.
        var refused = await node.PostAsync("claim", Json(Claim()));
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Code(refused)).ShouldBe(RenderNodeEndpoints.UnauthorizedCode);
        (await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = $"gpu-{suffix}" })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Every_node_route_refuses_a_call_without_a_node_token_alike()
    {
        var (_, jobId) = await QueueRenderAsync();
        var signedIn = requester.DefaultRequestHeaders.Authorization!.Parameter!;
        string?[] bearers = [null, "sgn_" + new string('A', 43), "not-a-token", signedIn];
        foreach (var bearer in bearers)
        {
            using var client = Node(bearer);
            var calls = new List<HttpResponseMessage>
            {
                await client.PostAsync("claim", Json(Claim())),
                await client.GetAsync($"jobs/{jobId}/bundle"),
                await client.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" })),
                await client.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File([1, 2, 3])),
                await client.PostAsync($"jobs/{jobId}/complete", Json(new { attempt = 1, files = new[] { new { name = "a.mp4", role = "movie", size = 1, sha256 = new string('0', 64) } }, seconds = 1, notices = Array.Empty<string>() })),
                await client.PostAsync($"jobs/{jobId}/fail", Json(new { attempt = 1, reason = "error", message = "x", retryable = false })),
            };
            foreach (var call in calls)
            {
                call.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{call.RequestMessage!.RequestUri} with {bearer ?? "no token"}");
                (await Code(call)).ShouldBe(RenderNodeEndpoints.UnauthorizedCode);
            }
        }

        // And nothing moved: the job is still waiting for a node.
        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Queued);
    }

    // ---- the whole way through -------------------------------------------------------------

    [Fact]
    public async Task A_node_claims_downloads_reports_uploads_and_completes_a_render()
    {
        var token = await IssueAsync();
        using var node = Node(token);
        var (renderId, jobId) = await QueueRenderAsync(plan: "draft", container: "webm");

        // No node has called lately (the other tests' nodes are made quiet): nothing is offered.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.RenderNodes.ExecuteUpdateAsync(s => s.SetProperty(n => n.LastSeenAt, (DateTimeOffset?)null));
        }

        (await requester.GetFromJsonAsync<JsonElement>("/api/v1/flybys/blender-renders/availability"))
            .GetProperty("available").GetBoolean().ShouldBeFalse();

        var claimed = await node.PostAsync("claim", Json(Claim(kept: ["999999"])));
        claimed.StatusCode.ShouldBe(HttpStatusCode.OK, await claimed.Content.ReadAsStringAsync());
        claimed.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var jobJson = await claimed.Content.ReadAsStringAsync();
        ConformsTo("job", jobJson);
        var job = JsonDocument.Parse(jobJson).RootElement;
        job.GetProperty("protocol").GetInt32().ShouldBe(1);
        job.GetProperty("jobId").GetString().ShouldBe(jobId);
        job.GetProperty("attempt").GetInt32().ShouldBe(1);
        job.GetProperty("leaseSeconds").GetInt32().ShouldBe(120);
        job.GetProperty("heartbeatSeconds").GetInt32().ShouldBe(40);
        job.GetProperty("plan").GetString().ShouldBe("draft");
        job.GetProperty("container").GetString().ShouldBe("webm");
        job.GetProperty("limits").GetProperty("maxResultBytes").GetInt64().ShouldBe(MaxResultBytes);
        var bundleUrl = job.GetProperty("bundle").GetProperty("url").GetString()!;
        bundleUrl.ShouldBe($"jobs/{jobId}/bundle");

        // The node called: rendering on the server is offered.
        (await requester.GetFromJsonAsync<JsonElement>("/api/v1/flybys/blender-renders/availability"))
            .GetProperty("available").GetBoolean().ShouldBeTrue();
        var running = await JobAsync(jobId);
        running.Status.ShouldBe(ProcessingJobStatus.Running);
        running.Attempts.ShouldBe(1);
        running.LeaseExpiresAt.ShouldNotBeNull();

        // The bundle as stored, the size and the SHA-256 the claim said.
        var bundle = await node.GetAsync(bundleUrl);
        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await bundle.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBe(job.GetProperty("bundle").GetProperty("size").GetInt32());
        Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(job.GetProperty("bundle").GetProperty("sha256").GetString());

        var beat = await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new
        {
            attempt = 1, phase = "rendering", message = "Rendering frame 3/10", frame = 3, frameCount = 10,
            secondsPerFrame = 0.5, secondsLeft = 3.5, somethingNewer = true,
        }));
        beat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reply = await beat.Content.ReadAsStringAsync();
        ConformsTo("heartbeatReply", reply);
        JsonDocument.Parse(reply).RootElement.GetProperty("action").GetString().ShouldBe("continue");

        var following = await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{renderId}");
        following.GetProperty("status").GetString().ShouldBe("running");
        following.GetProperty("done").GetInt32().ShouldBe(3);
        following.GetProperty("total").GetInt32().ShouldBe(10);
        following.GetProperty("message").GetString().ShouldBe("Rendering frame 3/10");

        byte[] film = [.. Enumerable.Range(0, 1000).Select(i => (byte)(i % 251))];
        byte[] log = Encoding.UTF8.GetBytes("Blender said things\n");
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.webm?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Sent again, it replaces the first.
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.webm?attempt=1", File(film))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.log?attempt=1", File(log))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.srt?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var complete = new
        {
            attempt = 1,
            files = new[]
            {
                new { name = $"{jobId}.webm", role = "movie", size = film.Length, sha256 = Sha(film) },
                new { name = $"{jobId}.log", role = "log", size = log.Length, sha256 = Sha(log) },
            },
            device = "CPU",
            secondsPerFrame = 0.5,
            seconds = 6.5,
            frameCount = 10,
            notices = new[] { "an invented notice" },
        };
        (await node.PostAsync($"jobs/{jobId}/complete", Json(complete))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var done = await JobAsync(jobId);
        done.Status.ShouldBe(ProcessingJobStatus.Succeeded);
        done.LeaseExpiresAt.ShouldBeNull();
        done.ProgressDone.ShouldBe(10);

        var finished = await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{renderId}");
        finished.GetProperty("status").GetString().ShouldBe("succeeded");
        finished.GetProperty("device").GetString().ShouldBe("CPU");
        finished.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .ShouldBe([$"{jobId}.log", $"{jobId}.webm"]);
        finished.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(13));

        // The film, to its requester only; the unlisted subtitles are gone, and so is the bundle.
        var download = await requester.GetAsync($"/api/v1/flybys/blender-renders/{renderId}/files/{jobId}.webm");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(film);
        (await requester.GetAsync($"/api/v1/flybys/blender-renders/{renderId}/files/{jobId}.srt")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/v1/flybys/blender-renders/{renderId}/files/{jobId}.webm")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/v1/flybys/blender-renders/{renderId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.FlybyRenders.SingleAsync(r => r.Id == renderId)).BundlePath.ShouldBeNull();
            (await db.FlybyRenderFiles.CountAsync(f => f.RenderId == renderId)).ShouldBe(2);
            (await db.Notifications.CountAsync(n => n.RecipientUserId == requesterId)).ShouldBeGreaterThan(0);
        }

        // Said again, the completion changes nothing; the bundle is not the node's to fetch now.
        (await node.PostAsync($"jobs/{jobId}/complete", Json(complete))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.GetAsync(bundleUrl)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "done" }))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---- what counts for which attempt -----------------------------------------------------

    [Fact]
    public async Task Another_nodes_calls_for_a_held_job_are_refused_as_not_its_own()
    {
        using var holder = Node(await IssueAsync());
        using var other = Node(await IssueAsync());
        var (_, jobId) = await QueueRenderAsync();
        (await holder.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await other.GetAsync($"jobs/{jobId}/bundle")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var beat = await other.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" }));
        beat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(beat)).ShouldBe(RenderNodeQueue.NotHeldCode);
        (await other.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await other.PostAsync($"jobs/{jobId}/fail", Json(new { attempt = 1, reason = "error", message = "x", retryable = false })))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A job that is no render job, and one that does not exist, are not found.
        (await other.PostAsync("jobs/123456789/heartbeat", Json(new { attempt = 1, phase = "x" }))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsync("jobs/not-a-number/heartbeat", Json(new { attempt = 1, phase = "x" }))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Running);
    }

    [Fact]
    public async Task A_lease_that_runs_out_puts_the_render_back_and_the_old_attempts_calls_are_refused()
    {
        using var node = Node(await IssueAsync());
        var (renderId, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ExpireLeaseAsync(jobId);
        await SweepAsync();
        var back = await JobAsync(jobId);
        back.Status.ShouldBe(ProcessingJobStatus.Queued);
        back.Attempts.ShouldBe(1);

        // The first attempt's node is told the job is not its own any more.
        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Claimed again — by the node that kept its frames — it is the second attempt, and the
        // first attempt's late calls are refused while the second's count.
        var again = await node.PostAsync("claim", Json(Claim(kept: [jobId])));
        JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetProperty("attempt").GetInt32().ShouldBe(2);
        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 1, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await node.PostAsync($"jobs/{jobId}/complete", Json(new { attempt = 1, files = new[] { new { name = $"{jobId}.mp4", role = "movie", size = 1, sha256 = Sha([1]) } }, seconds = 1, notices = Array.Empty<string>() })))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { attempt = 2, phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // On the last allowed attempt a lease that runs out fails the render, and says why.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.ProcessingJobs.Where(j => j.Id == long.Parse(jobId)).ExecuteUpdateAsync(s => s.SetProperty(j => j.Attempts, 5));
        }

        await ExpireLeaseAsync(jobId);
        await SweepAsync();
        var failed = await JobAsync(jobId);
        failed.Status.ShouldBe(ProcessingJobStatus.Failed);
        failed.Error.ShouldBe(RenderNodeQueue.InterruptedError);
        var render = await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{renderId}");
        render.GetProperty("status").GetString().ShouldBe("failed");
    }

    [Fact]
    public async Task A_node_holding_frames_of_a_job_is_given_that_job_first()
    {
        using var node = Node(await IssueAsync());
        var (_, older) = await QueueRenderAsync();
        var (_, kept) = await QueueRenderAsync();

        var claimed = await node.PostAsync("claim", Json(Claim(kept: [kept, "not-a-job"])));
        JsonDocument.Parse(await claimed.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetString().ShouldBe(kept);
        var next = await node.PostAsync("claim", Json(Claim()));
        JsonDocument.Parse(await next.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetString().ShouldBe(older);
    }

    // ---- what a message and a file must be ------------------------------------------------

    [Fact]
    public async Task Messages_the_protocol_does_not_allow_are_refused_with_400()
    {
        using var node = Node(await IssueAsync());

        var other = await node.PostAsync("claim", Json(Claim(protocol: 2)));
        other.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(other)).ShouldBe(RenderNodeQueue.ProtocolCode);

        foreach (var bad in new[] { "{}", "not json", "[1]", """{"protocol":1,"node":"","blenderVersions":[],"containers":[],"keptJobs":[],"waitSeconds":0}""" })
        {
            var refused = await node.PostAsync("claim", new StringContent(bad, Encoding.UTF8, "application/json"));
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
            (await Code(refused)).ShouldBe(RenderNodeQueue.InvalidMessageCode);
        }

        var (_, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await node.PostAsync($"jobs/{jobId}/heartbeat", Json(new { phase = "rendering" }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await node.PostAsync($"jobs/{jobId}/fail", Json(new { attempt = 1, reason = "bored", message = "x", retryable = false }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await node.PostAsync($"jobs/{jobId}/complete", Json(new { attempt = 1, files = Array.Empty<object>(), seconds = 1, notices = Array.Empty<string>() }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A file: a name the protocol does not allow, no attempt, no checksum.
        (await node.PutAsync($"jobs/{jobId}/files/..%2Fescape.mp4?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.exe?attempt=1", File([1]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4", File([1]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var noSum = new ByteArrayContent([1]);
        (await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", noSum)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Running);
    }

    [Fact]
    public async Task A_file_whose_bytes_are_not_its_checksum_or_that_is_too_large_is_not_taken()
    {
        using var node = Node(await IssueAsync());
        var (renderId, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var wrong = File([1, 2, 3]);
        wrong.Headers.Remove(RenderNodeProtocol.Sha256Header);
        wrong.Headers.Add(RenderNodeProtocol.Sha256Header, Sha([3, 2, 1]));
        var mismatch = await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", wrong);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(mismatch)).ShouldBe(RenderNodeQueue.ChecksumCode);

        var large = await node.PutAsync($"jobs/{jobId}/files/{jobId}.mp4?attempt=1", File(new byte[MaxResultBytes + 1]));
        large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await Code(large)).ShouldBe(RenderNodeQueue.TooLargeCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.FlybyRenderFiles.CountAsync(f => f.RenderId == renderId)).ShouldBe(0);

        // A completion naming a file the server does not hold as described is refused too.
        var named = await node.PostAsync($"jobs/{jobId}/complete", Json(new
        {
            attempt = 1, files = new[] { new { name = $"{jobId}.mp4", role = "movie", size = 3, sha256 = Sha([1, 2, 3]) } },
            seconds = 1, notices = Array.Empty<string>(),
        }));
        named.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(named)).ShouldBe(RenderNodeQueue.FilesMismatchCode);
        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Running);
    }

    // ---- failing and stopping -------------------------------------------------------------

    [Fact]
    public async Task A_failure_ends_the_render_or_puts_it_back_and_saying_it_twice_changes_nothing()
    {
        using var node = Node(await IssueAsync());
        var (_, passing) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var crash = new { attempt = 1, reason = "crashed", message = "Blender stopped", retryable = true };
        (await node.PostAsync($"jobs/{passing}/fail", Json(crash))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var back = await JobAsync(passing);
        back.Status.ShouldBe(ProcessingJobStatus.Queued);
        back.NotBefore.ShouldNotBeNull();
        back.Error.ShouldBe("crashed: Blender stopped");
        (await node.PostAsync($"jobs/{passing}/fail", Json(crash))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JobAsync(passing)).Status.ShouldBe(ProcessingJobStatus.Queued);

        var (renderId, broken) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var script = new { attempt = 1, reason = "scriptError", message = "the player failed", retryable = false };
        (await node.PostAsync($"jobs/{broken}/fail", Json(script))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var ended = await JobAsync(broken);
        ended.Status.ShouldBe(ProcessingJobStatus.Failed);
        ended.Error.ShouldBe("scriptError: the player failed");
        (await node.PostAsync($"jobs/{broken}/fail", Json(script))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JobAsync(broken)).Error.ShouldBe("scriptError: the player failed");

        var render = await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{renderId}");
        render.GetProperty("status").GetString().ShouldBe("failed");
        render.GetProperty("error").GetString().ShouldBe("scriptError: the player failed");
    }

    [Fact]
    public async Task A_stopped_render_ends_at_once_when_queued_and_at_the_next_heartbeat_when_running()
    {
        using var node = Node(await IssueAsync());

        var (queuedId, queuedJob) = await QueueRenderAsync();
        var cancelled = await requester.PostAsync($"/api/v1/flybys/blender-renders/{queuedId}/cancel", null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("cancelled");
        (await JobAsync(queuedJob)).Status.ShouldBe(ProcessingJobStatus.Failed);
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (runningId, runningJob) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var asked = await requester.PostAsync($"/api/v1/flybys/blender-renders/{runningId}/cancel", null);
        var askedBody = await asked.Content.ReadFromJsonAsync<JsonElement>();
        askedBody.GetProperty("status").GetString().ShouldBe("running");
        askedBody.GetProperty("cancelRequested").GetBoolean().ShouldBeTrue();

        // Another account's render is not found, as one that does not exist is.
        (await admin.PostAsync($"/api/v1/flybys/blender-renders/{runningId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var beat = await node.PostAsync($"jobs/{runningJob}/heartbeat", Json(new { attempt = 1, phase = "rendering" }));
        beat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reply = await beat.Content.ReadAsStringAsync();
        ConformsTo("heartbeatReply", reply);
        JsonDocument.Parse(reply).RootElement.GetProperty("action").GetString().ShouldBe("cancel");
        var stopped = await JobAsync(runningJob);
        stopped.Status.ShouldBe(ProcessingJobStatus.Failed);
        stopped.Error.ShouldBe(RenderNodeQueue.CancelledError);

        // The node's own word that it stopped is taken and changes nothing.
        (await node.PostAsync($"jobs/{runningJob}/fail", Json(new { attempt = 1, reason = "cancelled", message = "the server cancelled the job", retryable = false })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JobAsync(runningJob)).Error.ShouldBe(RenderNodeQueue.CancelledError);
        (await requester.GetFromJsonAsync<JsonElement>($"/api/v1/flybys/blender-renders/{runningId}"))
            .GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Fact]
    public async Task A_render_stopped_while_its_node_is_gone_ends_at_the_sweep_and_is_not_queued_again()
    {
        using var node = Node(await IssueAsync());
        var (renderId, jobId) = await QueueRenderAsync();
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await requester.PostAsync($"/api/v1/flybys/blender-renders/{renderId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ExpireLeaseAsync(jobId);
        await SweepAsync();
        var ended = await JobAsync(jobId);
        ended.Status.ShouldBe(ProcessingJobStatus.Failed);
        ended.Error.ShouldBe(RenderNodeQueue.CancelledError);
        (await node.PostAsync("claim", Json(Claim()))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private async Task<string> IssueAsync()
    {
        var issued = await admin.PostAsJsonAsync("/api/v1/admin/render-nodes", new { name = $"node-{Guid.NewGuid():N}"[..20] });
        issued.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    /// <summary>Queues a render of an invented bundle, as the route that makes a real one stores it.</summary>
    private async Task<(Guid RenderId, string JobId)> QueueRenderAsync(string plan = "full", string container = "mp4")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var renders = scope.ServiceProvider.GetRequiredService<FlybyRenders>();
        var bundle = Encoding.UTF8.GetBytes($"an invented bundle {Guid.NewGuid()}");
        var (view, refusal) = await renders.EnqueueAsync(requesterId, "An invented flyby", bundle, plan, container, CancellationToken.None);
        refusal.ShouldBe(FlybyRenderRefusal.None);
        return (view!.Render.Id, view.Job!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<ProcessingJob> JobAsync(string jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == long.Parse(jobId));
    }

    private async Task ExpireLeaseAsync(string jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = long.Parse(jobId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE processing_jobs SET lease_expires_at = now() - interval '1 minute' WHERE id = {id}");
    }

    private Task SweepAsync() => factory.Services.GetRequiredService<RenderNodeSweeper>().SweepAsync(CancellationToken.None);

    private HttpClient Node(string? token)
    {
        var client = factory.CreateClient();
        client.BaseAddress = new Uri(client.BaseAddress!, Base);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static object Claim(int protocol = 1, string[]? kept = null) => new
    {
        protocol,
        node = "invented-node",
        worker = "0.4.0.0",
        blenderVersions = new[] { "4.5.0" },
        device = "CPU",
        freeDiskBytes = 1_000_000_000L,
        containers = new[] { "mp4", "mkv", "webm" },
        keptJobs = kept ?? [],
        waitSeconds = 0,
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

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.Length == 0 ? null
            : JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// What this server answers is what the survey library's schema says, objects closed as it
    /// closes them: the worker reads it with the same records.
    /// </summary>
    private static void ConformsTo(string message, string json)
    {
        var root = JsonNode.Parse(RenderNodeProtocol.Schema)!.AsObject();
        root.Remove("$id");
        root["$ref"] = $"#/$defs/{message}";
        var result = JsonSchema.FromText(root.ToJsonString())
            .Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        result.IsValid.ShouldBeTrue(
            string.Join("; ", (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))) + " in " + json);
    }
}
