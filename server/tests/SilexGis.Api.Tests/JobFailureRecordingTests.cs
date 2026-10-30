// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A kind that the queue retries records its failure on what it works on only on the attempt that
/// is the last word. Driven with the real geofile import handler behind a vector reader that
/// fails as a test tells it to, a turn of the queue at a time.
/// </summary>
public sealed class JobFailureRecordingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly string[] Kinds = [ProcessingJobKinds.GeofileImport];

    private readonly SilexGisApiFactory factory;
    private readonly ScriptedVectorIO script = new();
    private readonly string filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
    private HttpClient editor = null!;

    public JobFailureRecordingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Files:Root"] = filesRoot },
            configureServices: services =>
            {
                JobWorkers.RemoveFrom(services);
                services.RemoveAll<IVectorIO>();
                services.AddSingleton<IVectorIO>(script);
            });

    public async ValueTask InitializeAsync()
    {
        var email = $"jfr-{Guid.NewGuid():N}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        editor = await AuthHelper.BearerClientAsync(factory, email);

        // The database is shared by the tests of this class: whatever an earlier test left queued or
        // uploaded would be picked up by this test's turns of the queue and found by its lookups.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.ExecuteDeleteAsync();
        await db.Geofiles.ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        editor.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task A_passing_failure_leaves_the_record_working_until_the_last_attempt_and_then_marks_it_failed()
    {
        var (jobId, geofileId) = await UploadAsync();
        script.Fail = () => new TimeoutException("The reader did not answer.");

        for (var attempt = 1; attempt < JobRetry.MaxAttempts; attempt++)
        {
            await TurnAsync();

            (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Queued);
            var waiting = await GeofileAsync(geofileId);
            waiting.ImportStatus.ShouldBe(GeofileImportStatus.Importing);
            waiting.ImportError.ShouldBeNull();
            await DueNowAsync(jobId);
        }

        await TurnAsync();

        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Failed);
        var failed = await GeofileAsync(geofileId);
        failed.ImportStatus.ShouldBe(GeofileImportStatus.Failed);
        failed.ImportError.ShouldBe("Import failed unexpectedly.");
    }

    [Fact]
    public async Task A_passing_failure_that_clears_is_never_shown_as_a_failure()
    {
        var (jobId, geofileId) = await UploadAsync();
        script.Fail = () => new TimeoutException("The reader did not answer.");
        await TurnAsync();
        (await GeofileAsync(geofileId)).ImportStatus.ShouldBe(GeofileImportStatus.Importing);

        script.Fail = null;
        await DueNowAsync(jobId);
        await TurnAsync();

        (await JobAsync(jobId)).Status.ShouldBe(ProcessingJobStatus.Succeeded);
        var done = await GeofileAsync(geofileId);
        done.ImportStatus.ShouldBe(GeofileImportStatus.Imported);
        done.ImportError.ShouldBeNull();
        done.FeatureCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_of_the_input_is_recorded_at_once_because_nothing_will_try_again()
    {
        var (jobId, geofileId) = await UploadAsync();
        script.Fail = () => new VectorIOException("The file is not a track log.");

        await TurnAsync();

        var job = await JobAsync(jobId);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Attempts.ShouldBe(1);
        var failed = await GeofileAsync(geofileId);
        failed.ImportStatus.ShouldBe(GeofileImportStatus.Failed);
        failed.ImportError.ShouldBe("The file is not a track log.");
    }

    // Every kind that retries must also hold its failure back, or the first attempt's failure is
    // shown as final under a job that is still going to run.
    [Theory]
    [InlineData(ProcessingJobKinds.GeofileImport)]
    [InlineData(ProcessingJobKinds.SurveyCompilation)]
    [InlineData(ProcessingJobKinds.SurveyGraph)]
    [InlineData(ProcessingJobKinds.SurveyMesh)]
    [InlineData(ProcessingJobKinds.TerrainDerivative)]
    [InlineData(ProcessingJobKinds.TextExtraction)]
    public void The_kinds_that_hold_their_failure_back_are_the_kinds_that_retry(string kind)
    {
        using var scope = factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>().Single(h => h.Kind == kind);

        handler.RetriesPassingFailures.ShouldBeTrue();
    }

    [Fact]
    public void A_failure_is_final_on_the_last_attempt_for_the_input_and_when_the_run_was_cancelled()
    {
        var timeout = new TimeoutException();
        var job = new ProcessingJob { Kind = ProcessingJobKinds.GeofileImport, Attempts = 1 };
        using var cancelled = new CancellationTokenSource();

        JobRetry.WillBeRetried(job, timeout, CancellationToken.None).ShouldBeTrue();
        JobRetry.WillBeRetried(job, new JobFailedException("shown", timeout), CancellationToken.None).ShouldBeTrue();
        JobRetry.WillBeRetried(job, new InvalidOperationException("input"), CancellationToken.None).ShouldBeFalse();
        JobRetry.WillBeRetried(job, new JobFailedException("shown", new InvalidOperationException()), CancellationToken.None)
            .ShouldBeFalse();
        job.Attempts = JobRetry.MaxAttempts;
        JobRetry.WillBeRetried(job, timeout, CancellationToken.None).ShouldBeFalse();
        job.Attempts = 1;
        cancelled.Cancel();
        JobRetry.WillBeRetried(job, timeout, cancelled.Token).ShouldBeFalse();
    }

    private async Task<(long JobId, Guid GeofileId)> UploadAsync()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("<gpx version=\"1.1\" creator=\"t\"/>"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "track.gpx" } };
        var response = await editor.PostAsync("/api/v1/geofiles", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var geofile = await db.Geofiles.AsNoTracking().SingleAsync();
        var job = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Kind == ProcessingJobKinds.GeofileImport);
        return (job.Id, geofile.Id);
    }

    private async Task TurnAsync()
    {
        using var scope = factory.Services.CreateScope();
        var worker = new TestLaneWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IConfiguration>());
        (await worker.TurnAsync(CancellationToken.None)).ShouldBeTrue();
    }

    private async Task<ProcessingJob> JobAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private async Task<Geofile> GeofileAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Geofiles.AsNoTracking().SingleAsync(g => g.Id == id);
    }

    private async Task DueNowAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.NotBefore, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private sealed class TestLaneWorker(IServiceScopeFactory scopes, IConfiguration configuration)
        : ProcessingJobWorkerBase(new JobLane(Kinds, Excluded: false), scopes, configuration, NullLogger.Instance)
    {
        public Task<bool> TurnAsync(CancellationToken ct) => RunNextAsync(ct);
    }

    /// <summary>Reads one point, or fails with whatever the test has set.</summary>
    private sealed class ScriptedVectorIO : IVectorIO
    {
        public Func<Exception>? Fail { get; set; }

        public VectorDataset Read(string absolutePath, GeofileFormat format, GeofileSourceOptions? sourceOptions = null)
        {
            if (Fail is { } fail)
            {
                throw fail();
            }

            var point = new Point(25.4, 45.5) { SRID = 4326 };
            return new VectorDataset([new VectorFeature(point, new Dictionary<string, object?>())], 4326);
        }

        public IReadOnlyList<TrackFix> ReadTrackFixes(string absolutePath, GeofileFormat format) => [];

        public byte[] Write(ExportFormat format, string layerName, IReadOnlyList<VectorFeature> features) => [];
    }
}
