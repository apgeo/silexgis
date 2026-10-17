// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the worker does with a job that fails, and how a running job says how far it has got —
/// driven through a worker whose lane holds only this class's own kinds, a turn at a time, so no
/// other suite's queued rows are ever taken and nothing waits on the poll timer.
/// </summary>
public sealed class JobRetryAndProgressTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Passing = "test-fails-in-passing";
    private const string NotRetried = "test-not-retried";
    private const string Broken = "test-fails-on-its-input";
    private const string LeavesRows = "test-adds-then-fails";
    private const string Reports = "test-reports-progress";

    private static readonly string[] Kinds = [Passing, NotRetried, Broken, LeavesRows, Reports];

    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private Guid ownerId;

    public JobRetryAndProgressTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                JobWorkers.RemoveFrom(services);
                services.AddSingleton<Calls>();
                services.AddScoped<IProcessingJobHandler, FailsInPassingHandler>();
                services.AddScoped<IProcessingJobHandler, NotRetriedHandler>();
                services.AddScoped<IProcessingJobHandler, BrokenInputHandler>();
                services.AddScoped<IProcessingJobHandler, AddsThenFailsHandler>();
                services.AddScoped<IProcessingJobHandler, ReportsProgressHandler>();
            });

    public async ValueTask InitializeAsync()
    {
        var email = $"jobs-{Guid.NewGuid():N}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        owner = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task A_job_that_fails_in_passing_is_put_back_and_succeeds_on_its_next_turn()
    {
        var id = await QueueAsync(Passing);

        (await TurnAsync()).ShouldBeTrue();

        var waiting = await ReadAsync(id);
        waiting.Status.ShouldBe(ProcessingJobStatus.Queued);
        waiting.Attempts.ShouldBe(1);
        waiting.Error.ShouldNotBeNull();
        waiting.Error.ShouldContain("did not answer");
        // Not before thirty seconds from the failure, and not taken before then.
        waiting.NotBefore.ShouldNotBeNull();
        (waiting.NotBefore!.Value - DateTimeOffset.UtcNow).ShouldBeInRange(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(31));
        (await TurnAsync()).ShouldBeFalse();

        await DueNowAsync(id);
        (await TurnAsync()).ShouldBeTrue();

        var done = await ReadAsync(id);
        done.Status.ShouldBe(ProcessingJobStatus.Succeeded);
        done.Attempts.ShouldBe(2);
        done.Error.ShouldBeNull();
        done.NotBefore.ShouldBeNull();
    }

    [Fact]
    public async Task A_passing_failure_is_tried_three_times_in_all_and_then_stays_failed()
    {
        var id = await QueueAsync(Passing, failuresBeforeSuccess: 5);

        for (var attempt = 1; attempt < JobRetry.MaxAttempts; attempt++)
        {
            await TurnAsync();
            (await ReadAsync(id)).Status.ShouldBe(ProcessingJobStatus.Queued);
            await DueNowAsync(id);
        }

        await TurnAsync();

        var failed = await ReadAsync(id);
        failed.Status.ShouldBe(ProcessingJobStatus.Failed);
        failed.Attempts.ShouldBe(JobRetry.MaxAttempts);
    }

    [Fact]
    public async Task A_kind_that_does_not_allow_it_and_a_failure_of_the_input_are_not_tried_again()
    {
        // The same passing failure, from a kind that has not said running it twice is safe.
        var notRetried = await QueueAsync(NotRetried);
        // A kind that allows it, failing on what it was given.
        var broken = await QueueAsync(Broken);

        await TurnAsync();
        await TurnAsync();

        (await ReadAsync(notRetried)).Status.ShouldBe(ProcessingJobStatus.Failed);
        (await ReadAsync(broken)).Status.ShouldBe(ProcessingJobStatus.Failed);
    }

    [Fact]
    public async Task Rows_a_failed_attempt_added_are_not_saved_with_its_failure()
    {
        var id = await QueueAsync(LeavesRows);

        await TurnAsync();

        (await ReadAsync(id)).Status.ShouldBe(ProcessingJobStatus.Failed);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Tags.AnyAsync(t => t.Slug == SlugFor(id))).ShouldBeFalse();
    }

    [Fact]
    public async Task A_running_job_says_how_far_it_has_got_and_its_requester_can_read_it()
    {
        var id = await QueueAsync(Reports);

        await TurnAsync();

        var job = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{id}");
        job.GetProperty("status").GetString().ShouldBe("succeeded");
        job.GetProperty("attempts").GetInt32().ShouldBe(1);
        job.GetProperty("progressDone").GetInt32().ShouldBe(10);
        job.GetProperty("progressTotal").GetInt32().ShouldBe(10);

        // Seen while it ran, not only at the end: the handler read its own row back after its fifth
        // unit. The writer writes at most once a second, so what it read is some earlier count.
        var midway = factory.Services.GetRequiredService<Calls>().SeenMidway[id];
        midway.Total.ShouldBe(10);
        midway.Done.ShouldBeInRange(0, 4);
    }

    [Fact]
    public void A_failure_counts_as_passing_only_when_it_says_nothing_about_the_input()
    {
        // Of the surroundings: tried again.
        JobRetry.IsPassing(new TimeoutException()).ShouldBeTrue();
        JobRetry.IsPassing(new HttpRequestException("no answer")).ShouldBeTrue();
        JobRetry.IsPassing(new HttpRequestException("busy", null, System.Net.HttpStatusCode.ServiceUnavailable)).ShouldBeTrue();
        JobRetry.IsPassing(new IOException("the device is not ready")).ShouldBeTrue();
        JobRetry.IsPassing(new DbUpdateException("save", new TimeoutException())).ShouldBeTrue();
        JobRetry.IsPassing(new TaskCanceledException("timed out", new TimeoutException())).ShouldBeTrue();

        // About the input, or said so by the handler: not tried again.
        JobRetry.IsPassing(new HttpRequestException("refused", null, System.Net.HttpStatusCode.BadRequest)).ShouldBeFalse();
        JobRetry.IsPassing(new EndOfStreamException()).ShouldBeFalse();
        JobRetry.IsPassing(new FileNotFoundException()).ShouldBeFalse();
        JobRetry.IsPassing(new InvalidOperationException("not a readable archive", new IOException())).ShouldBeFalse();
        JobRetry.IsPassing(new InvalidDataException()).ShouldBeFalse();

        // And never more than three attempts in all.
        JobRetry.MayRetry(2, new TimeoutException()).ShouldBeTrue();
        JobRetry.MayRetry(JobRetry.MaxAttempts, new TimeoutException()).ShouldBeFalse();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    private async Task<bool> TurnAsync()
    {
        var worker = new TestLaneWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IConfiguration>());
        return await worker.TurnAsync(CancellationToken.None);
    }

    private async Task<long> QueueAsync(string kind, int failuresBeforeSuccess = 1)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var job = new ProcessingJob
        {
            Kind = kind,
            RequestedBy = ownerId,
            Payload = JsonSerializer.Serialize(new { failuresBeforeSuccess }),
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

    /// <summary>The delay elapsing: it is read against the database's clock, so it is moved there.</summary>
    private async Task DueNowAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.NotBefore, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private static string SlugFor(long jobId) => $"job-{jobId}-leftover";

    private sealed class TestLaneWorker(IServiceScopeFactory scopes, IConfiguration configuration)
        : ProcessingJobWorkerBase(new JobLane(Kinds, Excluded: false), scopes, configuration, NullLogger.Instance)
    {
        public Task<bool> TurnAsync(CancellationToken ct) => RunNextAsync(ct);
    }

    private sealed class Calls
    {
        public System.Collections.Concurrent.ConcurrentDictionary<long, int> Failures { get; } = new();

        public System.Collections.Concurrent.ConcurrentDictionary<long, (int Done, int Total)> SeenMidway { get; } = new();
    }

    private static int FailuresBeforeSuccess(ProcessingJob job) =>
        JsonDocument.Parse(job.Payload).RootElement.GetProperty("failuresBeforeSuccess").GetInt32();

    private sealed class FailsInPassingHandler(Calls calls) : IProcessingJobHandler
    {
        public string Kind => Passing;

        public bool RetriesPassingFailures => true;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
            calls.Failures.AddOrUpdate(job.Id, 1, (_, n) => n + 1) <= FailuresBeforeSuccess(job)
                ? throw new TimeoutException("The conversion service did not answer in time.")
                : Task.CompletedTask;
    }

    private sealed class NotRetriedHandler : IProcessingJobHandler
    {
        public string Kind => NotRetried;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
            throw new TimeoutException("The conversion service did not answer in time.");
    }

    private sealed class BrokenInputHandler : IProcessingJobHandler
    {
        public string Kind => Broken;

        public bool RetriesPassingFailures => true;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
            throw new InvalidOperationException("The file is not a survey this reader knows.");
    }

    private sealed class AddsThenFailsHandler(SilexGisDbContext db) : IProcessingJobHandler
    {
        public string Kind => LeavesRows;

        public Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            db.Tags.Add(new Tag { Name = "Left over", Slug = SlugFor(job.Id) });
            throw new InvalidOperationException("Failed after adding a row it never saved.");
        }
    }

    private sealed class ReportsProgressHandler(IJobProgress progress, IServiceScopeFactory scopes, Calls calls)
        : IProcessingJobHandler
    {
        public string Kind => Reports;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            for (var done = 0; done <= 10; done++)
            {
                await progress.ReportAsync(job.Id, done, 10, ct);
                if (done == 4)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                    var row = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, ct);
                    calls.SeenMidway[job.Id] = (row.ProgressDone ?? -1, row.ProgressTotal ?? -1);
                }
            }
        }
    }
}
