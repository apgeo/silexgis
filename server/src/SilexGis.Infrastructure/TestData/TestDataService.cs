// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>How much of what is in the database no test-data run created.</summary>
public sealed record TestDataExistingData(int Caves, int Trips, int Documents)
{
    public bool Any => Caves + Trips + Documents > 0;
}

public enum TestDataStartOutcome
{
    Queued,
    ExistingDataNeedsConfirmation,
    RunInProgress,
}

public sealed record TestDataStartResult(TestDataStartOutcome Outcome, TestDataRun? Run, TestDataExistingData Existing);

/// <summary>
/// Starting, stopping and removing runs: the decisions the page and the command line share. The
/// work itself is done by <see cref="TestDataRunner"/> and <see cref="TestDataRemoval"/> in jobs.
/// </summary>
public sealed class TestDataService(SilexGisDbContext db, TimeProvider clock)
{
    private static readonly TestDataRunStatus[] Active =
        [TestDataRunStatus.Queued, TestDataRunStatus.Running, TestDataRunStatus.Removing];

    public async Task<TestDataExistingData> ExistingDataAsync(CancellationToken ct)
    {
        // "Not listed by any run" — the registry, never a name or a tag, decides what is generated.
        var caves = await db.Features.IgnoreQueryFilters()
            .CountAsync(f => f.Kind == FeatureKind.Cave
                && !db.TestDataRows.Any(r => r.TableName == "features" && r.RowId == f.Id.ToString()), ct);
        var trips = await db.TripLogs.IgnoreQueryFilters()
            .CountAsync(t => !db.TestDataRows.Any(r => r.TableName == "trip_logs" && r.RowId == t.Id.ToString()), ct);
        var documents = await db.Documents.IgnoreQueryFilters()
            .CountAsync(d => !db.TestDataRows.Any(r => r.TableName == "documents" && r.RowId == d.Id.ToString()), ct);
        return new TestDataExistingData(caves, trips, documents);
    }

    /// <summary>
    /// Queues a run of <paramref name="profile"/> for <paramref name="requestedBy"/>, unless one is
    /// already going or the database holds data no run created and the caller has not confirmed
    /// generating beside it.
    /// </summary>
    public async Task<TestDataStartResult> StartAsync(
        TestDataProfile profile, Guid requestedBy, bool confirmAlongsideExistingData, bool queueJob, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var existing = await ExistingDataAsync(ct);
        if (await db.TestDataRuns.AnyAsync(r => Active.Contains(r.Status), ct))
        {
            return new TestDataStartResult(TestDataStartOutcome.RunInProgress, null, existing);
        }

        if (existing.Any && !confirmAlongsideExistingData)
        {
            return new TestDataStartResult(TestDataStartOutcome.ExistingDataNeedsConfirmation, null, existing);
        }

        var run = new TestDataRun
        {
            Profile = profile.ToJson(),
            Seed = profile.Seed,
            RequestedBy = requestedBy,
            AlongsideExistingData = existing.Any,
            CreatedAt = clock.GetUtcNow(),
        };
        db.TestDataRuns.Add(run);
        if (queueJob)
        {
            var job = Job(ProcessingJobKinds.TestDataGenerate, run.Id, requestedBy);
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync(ct);
            run.ProcessingJobId = job.Id;
        }

        await db.SaveChangesAsync(ct);
        return new TestDataStartResult(TestDataStartOutcome.Queued, run, existing);
    }

    /// <summary>Asks a waiting or running run to stop; false when it is not one that can stop.</summary>
    public async Task<bool> CancelAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.TestDataRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status is not (TestDataRunStatus.Queued or TestDataRunStatus.Running))
        {
            return false;
        }

        run.CancelRequestedAt ??= clock.GetUtcNow();
        if (run.Status == TestDataRunStatus.Queued)
        {
            // Nothing written yet: closed here, and the job finds nothing to do.
            run.Status = TestDataRunStatus.Cancelled;
            run.FinishedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Queues the removal of a finished run; false when the run is still going or already gone.</summary>
    public async Task<bool> RequestRemovalAsync(Guid runId, Guid requestedBy, bool queueJob, CancellationToken ct)
    {
        var run = await db.TestDataRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status is not (TestDataRunStatus.Succeeded or TestDataRunStatus.Failed or TestDataRunStatus.Cancelled))
        {
            return false;
        }

        run.Status = TestDataRunStatus.Removing;
        if (queueJob)
        {
            db.ProcessingJobs.Add(Job(ProcessingJobKinds.TestDataRemove, run.Id, requestedBy));
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public static Guid RunIdOf(ProcessingJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        using var payload = JsonDocument.Parse(job.Payload);
        return payload.RootElement.GetProperty("runId").GetGuid();
    }

    private static ProcessingJob Job(string kind, Guid runId, Guid requestedBy) => new()
    {
        Kind = kind,
        Payload = JsonSerializer.Serialize(new { runId }),
        RequestedBy = requestedBy,
    };
}
