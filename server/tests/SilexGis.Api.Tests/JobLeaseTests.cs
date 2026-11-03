// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A job the worker took is never left Running for good: not when its outcome cannot be saved, not
/// when the process running it died, and not when it is the job that keeps killing the process.
/// Driven through a worker whose lane holds only this class's own kinds, a turn at a time.
/// </summary>
/// <remarks>
/// The lease here is a few seconds long and renewed every second, so a test can watch a job outlive
/// its first lease without waiting minutes for it.
/// </remarks>
public sealed class JobLeaseTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string LongError = "test-lease-long-error";
    private const string UnsavableOutcome = "test-lease-unsavable-outcome";
    private const string Interrupted = "test-lease-interrupted";
    private const string Lingers = "test-lease-lingers";
    private const string Overtaken = "test-lease-overtaken";
    private const string OvertakenThenPassing = "test-lease-overtaken-passing";
    private const string Abandons = "test-lease-abandons";
    private const string AbandonThrows = "test-lease-abandon-throws";
    private const string Completes = "test-lease-completes";
    private const string BreaksOnce = "test-lease-breaks-once";
    private const string Blocks = "test-lease-blocks";
    private const string DropsConnection = "test-lease-drops-connection";

    private static readonly string[] Kinds =
    [
        LongError, UnsavableOutcome, Interrupted, Lingers, Overtaken, OvertakenThenPassing,
        Abandons, AbandonThrows, Completes, BreaksOnce, Blocks, DropsConnection,
    ];

    private readonly SilexGisApiFactory factory;
    private Guid ownerId;

    public JobLeaseTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Jobs:LeaseSeconds"] = "3",
                ["Jobs:HeartbeatSeconds"] = "1",
            },
            configureServices: services =>
            {
                JobWorkers.RemoveFrom(services);
                services.AddSingleton<Seen>();
                services.AddScoped<IProcessingJobHandler, LongErrorHandler>();
                services.AddScoped<IProcessingJobHandler, UnsavableOutcomeHandler>();
                services.AddScoped<IProcessingJobHandler, LingersHandler>();
                services.AddScoped<IProcessingJobHandler, OvertakenHandler>();
                services.AddScoped<IProcessingJobHandler, OvertakenThenPassingHandler>();
                services.AddScoped<IProcessingJobHandler, AbandonsHandler>();
                services.AddScoped<IProcessingJobHandler, AbandonThrowsHandler>();
                services.AddScoped<IProcessingJobHandler, CompletesHandler>();
                services.AddScoped<IProcessingJobHandler, BreaksOnceHandler>();
                services.AddScoped<IProcessingJobHandler, BlocksHandler>();
                services.AddScoped<IProcessingJobHandler, DropsConnectionHandler>();
            });

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"lease-{Guid.NewGuid():N}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => Kinds.Contains(j.Kind)).ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_failure_longer_than_the_error_column_is_recorded_cut_to_it_and_told_to_the_requester()
    {
        var id = await QueueAsync(LongError);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Error.ShouldNotBeNull();
        job.Error.Length.ShouldBe(4000);
        job.CompletedAt.ShouldNotBeNull();
        (await FailureNoticesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_job_whose_outcome_cannot_be_saved_is_recorded_failed_rather_than_left_running()
    {
        var id = await QueueAsync(UnsavableOutcome);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Error.ShouldNotBeNullOrEmpty();
        job.CompletedAt.ShouldNotBeNull();
        job.LeaseExpiresAt.ShouldBeNull();
        // What the handler left unsaved stays unsaved: the payload is still the one it was queued with.
        job.Payload.ShouldContain("queued");
        (await FailureNoticesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task An_interrupted_job_at_the_attempt_ceiling_is_failed_and_told_rather_than_queued_again()
    {
        var atCeiling = await RunningAsync(Interrupted, JobRetry.MaxAttempts, leaseAgo: TimeSpan.FromMinutes(1));
        var belowCeiling = await RunningAsync(Interrupted, JobRetry.MaxAttempts - 1, leaseAgo: TimeSpan.FromMinutes(1));

        await Worker().SweepAsync(CancellationToken.None);

        var failed = await ReadAsync(atCeiling);
        failed.Status.ShouldBe(ProcessingJobStatus.Failed);
        failed.Error.ShouldNotBeNull();
        failed.Error.ShouldContain("interrupted");
        failed.CompletedAt.ShouldNotBeNull();
        failed.LeaseExpiresAt.ShouldBeNull();
        // The job that has a turn left goes back to the queue, and nobody is told anything about it.
        var queued = await ReadAsync(belowCeiling);
        queued.Status.ShouldBe(ProcessingJobStatus.Queued);
        queued.StartedAt.ShouldBeNull();
        queued.LeaseExpiresAt.ShouldBeNull();
        (await FailureNoticesAsync()).ShouldBe(1);

        // Taken again, it counts the interrupted turn as one of its attempts.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await JobsSql.ClaimNextAsync(db, Lane, LeaseLength, CancellationToken.None)).ShouldBe(belowCeiling);
        (await ReadAsync(belowCeiling)).Attempts.ShouldBe(JobRetry.MaxAttempts);
    }

    [Fact]
    public async Task A_running_job_with_no_lease_at_all_is_treated_as_interrupted()
    {
        // A row left Running before leases existed has none, and nothing will ever renew it.
        var id = await RunningAsync(Interrupted, 1, leaseAgo: null);

        await Worker().SweepAsync(CancellationToken.None);

        (await ReadAsync(id)).Status.ShouldBe(ProcessingJobStatus.Queued);
    }

    [Fact]
    public async Task A_job_another_worker_holds_a_live_lease_on_is_left_running_by_a_sweep()
    {
        var id = await QueueAsync(Interrupted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await JobsSql.ClaimNextAsync(db, Lane, LeaseLength, CancellationToken.None)).ShouldBe(id);
        }

        // Beside it, one of the same lane whose runner is gone: the same sweep has to take that one,
        // or "left running" would say nothing more than that the sweep did nothing at all.
        var lost = await RunningAsync(Interrupted, 1, leaseAgo: TimeSpan.FromMinutes(1));

        // A second process starting up, or this one's own sweep, while the first is still at work.
        await Worker().SweepAsync(CancellationToken.None);

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Running);
        job.LeaseExpiresAt.ShouldNotBeNull();
        (await ReadAsync(lost)).Status.ShouldBe(ProcessingJobStatus.Queued);
    }

    [Fact]
    public async Task A_terrain_build_whose_job_is_given_up_at_the_ceiling_is_stopped_rather_than_left_running()
    {
        // A build that took the process down on every attempt: its job is Running with the last
        // allowed attempt counted and nobody renewing it. The sweep fails the job without handing
        // it over again, so nothing but the sweep is left to close the build.
        Guid buildId;
        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var build = new TerrainBuild
            {
                Extent = new NetTopologySuite.Geometries.GeometryFactory(
                        new NetTopologySuite.Geometries.PrecisionModel(), 4326)
                    .ToGeometry(new NetTopologySuite.Geometries.Envelope(23.0, 23.01, 46.0, 46.01))
                    as NetTopologySuite.Geometries.Polygon
                    ?? throw new InvalidOperationException("Not a polygon."),
                Status = TerrainBuildStatus.Running,
                Phase = TerrainBuildPhase.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            };
            db.TerrainBuilds.Add(build);
            var job = new ProcessingJob
            {
                Kind = ProcessingJobKinds.TerrainBuild,
                RequestedBy = ownerId,
                Payload = JsonSerializer.Serialize(new TerrainBuildPayload(build.Id), JsonSerializerOptions.Web),
                Status = ProcessingJobStatus.Running,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                Attempts = JobRetry.MaxAttempts,
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            };
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync();
            buildId = build.Id;
            jobId = job.Id;
        }

        await new LaneWorker(
                JobLane.Terrain,
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                factory.Services.GetRequiredService<IConfiguration>())
            .SweepAsync(CancellationToken.None);

        (await ReadAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Failed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stopped = await db.TerrainBuilds.AsNoTracking().SingleAsync(b => b.Id == buildId);
            stopped.Status.ShouldBe(TerrainBuildStatus.Failed);
            stopped.ErrorCode.ShouldBe(TerrainBuildFailures.RepeatedFailure);
            stopped.FinishedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Each_job_given_up_is_handed_to_its_handler_and_one_handler_failing_keeps_no_job_running()
    {
        var abandons = await RunningAsync(Abandons, JobRetry.MaxAttempts, leaseAgo: TimeSpan.FromMinutes(1));
        var throws = await RunningAsync(AbandonThrows, JobRetry.MaxAttempts, leaseAgo: TimeSpan.FromMinutes(1));

        await Worker().SweepAsync(CancellationToken.None);

        factory.Services.GetRequiredService<Seen>().Abandoned.ShouldBe([abandons]);
        (await ReadAsync(abandons)).Status.ShouldBe(ProcessingJobStatus.Failed);
        var failed = await ReadAsync(throws);
        failed.Status.ShouldBe(ProcessingJobStatus.Failed);
        // What the failing handler wrote before it threw went with its savepoint.
        failed.Payload.ShouldNotContain("half-written");
        (await FailureNoticesAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_runner_that_lost_its_job_to_another_does_not_write_its_outcome_over_the_live_attempt()
    {
        // The handler finds, as it finishes, that its lease was taken back and the job claimed
        // again by somebody else as the next attempt. Its success is about an attempt that is
        // over, and must not end the job the other runner is still running.
        var id = await QueueAsync(Overtaken);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Running);
        job.Attempts.ShouldBe(2);
        job.CompletedAt.ShouldBeNull();
        (await NoticesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_runner_that_lost_its_job_to_another_does_not_put_the_live_attempt_back_in_the_queue()
    {
        var id = await QueueAsync(OvertakenThenPassing);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Running, "a put-back would hand the job to a third runner");
        job.Attempts.ShouldBe(2);
        job.NotBefore.ShouldBeNull();
    }

    [Fact]
    public async Task A_job_whose_connection_is_dropped_under_it_is_put_back_to_be_taken_again()
    {
        // The database ends the handler's own session — what a restart of the server looks like
        // from inside a query — and the next query through the model fails on it. The put-back
        // goes through a connection of its own, so the job waits for its next turn rather than
        // staying Running until its lease runs out.
        var id = await QueueAsync(DropsConnection);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Queued, job.Error);
        job.NotBefore.ShouldNotBeNull();
        job.Attempts.ShouldBe(1);
        job.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_running_loop_takes_back_a_lost_job_and_goes_on_after_an_iteration_that_failed()
    {
        // Started as a host starts it, polling every second and sweeping every second: nothing here
        // calls a turn or a sweep by hand.
        var lost = await RunningAsync(Completes, 1, leaseAgo: TimeSpan.FromMinutes(1));
        var breaks = await QueueAsync(BreaksOnce);
        var after = await QueueAsync(Completes);

        var worker = LoopingWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // The lost job comes back through the sweep at start; the job queued behind the
            // iteration that broke is still run; and the broken job, left Running by that
            // iteration, is taken back by a later sweep once its lease is over and run again.
            await UntilAsync(async () =>
                (await ReadAsync(lost)).Status == ProcessingJobStatus.Succeeded
                && (await ReadAsync(after)).Status == ProcessingJobStatus.Succeeded
                && (await ReadAsync(breaks)).Status == ProcessingJobStatus.Succeeded);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        (await ReadAsync(lost)).Attempts.ShouldBe(2);
        (await ReadAsync(breaks)).Attempts.ShouldBe(2);
        (await FailureNoticesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_host_that_stops_in_the_middle_of_a_job_leaves_it_running_and_tells_nobody_it_failed()
    {
        var id = await QueueAsync(Blocks);
        var seen = factory.Services.GetRequiredService<Seen>();

        var worker = LoopingWorker();
        await worker.StartAsync(CancellationToken.None);
        await seen.BlockEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await worker.StopAsync(CancellationToken.None);

        // Interrupted is not failed: it is taken back by the next start's sweep.
        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Running);
        job.Error.ShouldBeNull();
        job.CompletedAt.ShouldBeNull();
        (await FailureNoticesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_job_that_runs_longer_than_its_lease_keeps_it_renewed_and_is_never_swept()
    {
        var id = await QueueAsync(Lingers);

        (await Worker().TurnAsync(CancellationToken.None)).ShouldBeTrue();

        // The handler outlived its first lease, swept the queue itself and read its own row back.
        var seen = factory.Services.GetRequiredService<Seen>();
        seen.StatusAfterSweep.ShouldBe(ProcessingJobStatus.Running);
        seen.LeaseRenewed.ShouldBeTrue();
        var job = await ReadAsync(id);
        job.Status.ShouldBe(ProcessingJobStatus.Succeeded);
        job.Attempts.ShouldBe(1);
        job.LeaseExpiresAt.ShouldBeNull();
    }

    private static readonly JobLane Lane = new(Kinds, Excluded: false);

    private static readonly TimeSpan LeaseLength = TimeSpan.FromSeconds(3);

    private TestLaneWorker Worker() => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        factory.Services.GetRequiredService<IConfiguration>());

    /// <summary>The worker as a host runs it, on a poll and a sweep of one second each.</summary>
    private TestLaneWorker LoopingWorker() => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:PollSeconds"] = "1",
                ["Jobs:SweepSeconds"] = "1",
                ["Jobs:LeaseSeconds"] = "3",
                ["Jobs:HeartbeatSeconds"] = "1",
            })
            .Build());

    private static async Task UntilAsync(Func<Task<bool>> done)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (!await done())
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the running loop did not get there in time");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task<long> QueueAsync(string kind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var job = new ProcessingJob
        {
            Kind = kind,
            RequestedBy = ownerId,
            Payload = JsonSerializer.Serialize(new { state = "queued" }),
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>A row a dead process left Running, its lease run out <paramref name="leaseAgo"/> ago.</summary>
    private async Task<long> RunningAsync(string kind, int attempts, TimeSpan? leaseAgo)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var job = new ProcessingJob
        {
            Kind = kind,
            RequestedBy = ownerId,
            Status = ProcessingJobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Attempts = attempts,
            LeaseExpiresAt = leaseAgo is { } ago ? DateTimeOffset.UtcNow - ago : null,
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<ProcessingJob> ReadAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private async Task<int> NoticesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.CountAsync(n => n.RecipientUserId == ownerId);
    }

    private async Task<int> FailureNoticesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.CountAsync(
            n => n.RecipientUserId == ownerId && n.TemplateKey == MessageTemplateCatalog.NotifyJobFailed);
    }

    private sealed class TestLaneWorker(IServiceScopeFactory scopes, IConfiguration configuration)
        : LaneWorker(Lane, scopes, configuration);

    private class LaneWorker(JobLane lane, IServiceScopeFactory scopes, IConfiguration configuration)
        : ProcessingJobWorkerBase(lane, scopes, configuration, NullLogger.Instance)
    {
        public Task<bool> TurnAsync(CancellationToken ct) => RunNextAsync(ct);

        public Task SweepAsync(CancellationToken ct) => ReturnStaleAsync(ct);
    }

    private sealed class Seen
    {
        public ProcessingJobStatus? StatusAfterSweep { get; set; }

        public bool LeaseRenewed { get; set; }

        public System.Collections.Concurrent.ConcurrentBag<long> Abandoned { get; } = [];

        public TaskCompletionSource BlockEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Stands for the sweep taking the job back and another runner claiming it as the next attempt,
    /// all while this one was still at work.
    /// </summary>
    private static async Task TakeOverAsync(IServiceScopeFactory scopes, ProcessingJob job)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Id == job.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Attempts, job.Attempts + 1)
                .SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    private sealed class OvertakenHandler(IServiceScopeFactory scopes) : IProcessingJobHandler
    {
        public string Kind => Overtaken;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) => TakeOverAsync(scopes, job);
    }

    private sealed class OvertakenThenPassingHandler(IServiceScopeFactory scopes) : IProcessingJobHandler
    {
        public string Kind => OvertakenThenPassing;

        public bool RetriesPassingFailures => true;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            await TakeOverAsync(scopes, job);
            throw new TimeoutException("The service did not answer.");
        }
    }

    private sealed class AbandonsHandler(Seen seen) : IProcessingJobHandler
    {
        public string Kind => Abandons;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) => Task.CompletedTask;

        public Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
        {
            seen.Abandoned.Add(job.Id);
            return Task.CompletedTask;
        }
    }

    /// <summary>Writes half of what it means to and then throws, as a handler's stop can.</summary>
    private sealed class AbandonThrowsHandler(SilexGisDbContext db) : IProcessingJobHandler
    {
        public string Kind => AbandonThrows;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) => Task.CompletedTask;

        public async Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
        {
            await db.ProcessingJobs.Where(j => j.Id == job.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Payload, """{"state":"half-written"}"""), ct);
            throw new InvalidOperationException("The record could not be closed.");
        }
    }

    private sealed class CompletesHandler : IProcessingJobHandler
    {
        public string Kind => Completes;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// On its first attempt, breaks the worker's own context and throws — so the failure escapes
    /// the turn and ends the loop's iteration instead of being recorded on the job.
    /// </summary>
    private sealed class BreaksOnceHandler(SilexGisDbContext db) : IProcessingJobHandler
    {
        public string Kind => BreaksOnce;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            if (job.Attempts == 1)
            {
                await db.DisposeAsync();
                throw new InvalidOperationException("Broken on its first attempt.");
            }
        }
    }

    private sealed class BlocksHandler(Seen seen) : IProcessingJobHandler
    {
        public string Kind => Blocks;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            seen.BlockEntered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    /// <summary>Has the database end its own session, then queries through the model.</summary>
    private sealed class DropsConnectionHandler(SilexGisDbContext db, IServiceScopeFactory scopes) : IProcessingJobHandler
    {
        public string Kind => DropsConnection;

        public bool RetriesPassingFailures => true;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            var victim = await PidOfAsync(ct);
            await using (var other = scopes.CreateAsyncScope())
            {
                await other.ServiceProvider.GetRequiredService<SilexGisDbContext>().Database
                    .ExecuteSqlRawAsync("SELECT pg_terminate_backend({0})", [victim], ct);
            }

            _ = await db.ProcessingJobs.AsNoTracking().CountAsync(ct);
        }

        private async Task<int> PidOfAsync(CancellationToken ct)
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_backend_pid()";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed class LongErrorHandler : IProcessingJobHandler
    {
        public string Kind => LongError;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
            throw new InvalidOperationException(new string('x', 5000));
    }

    /// <summary>
    /// Succeeds, having left a change on its own job row that the database refuses — the outcome save
    /// carries it, and fails.
    /// </summary>
    private sealed class UnsavableOutcomeHandler : IProcessingJobHandler
    {
        public string Kind => UnsavableOutcome;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            job.Payload = "this is not json";
            return Task.CompletedTask;
        }
    }

    /// <summary>Runs past its first lease, then sweeps the queue as another worker would.</summary>
    private sealed class LingersHandler(IServiceScopeFactory scopes, IConfiguration configuration, Seen seen)
        : IProcessingJobHandler
    {
        public string Kind => Lingers;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            DateTimeOffset? firstLease;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                firstLease = (await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, ct)).LeaseExpiresAt;
            }

            await Task.Delay(LeaseLength + TimeSpan.FromSeconds(1.5), ct);
            await new TestLaneWorker(scopes, configuration).SweepAsync(ct);

            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                var row = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, ct);
                seen.StatusAfterSweep = row.Status;
                seen.LeaseRenewed = firstLease is { } first && row.LeaseExpiresAt > first;
            }
        }
    }
}
