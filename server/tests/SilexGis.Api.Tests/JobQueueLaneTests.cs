// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The queue's two lanes against a real database: which worker is allowed to take which row.
///
/// <para>
/// The claim statement is the only thing that divides the table. Locking does not: skipping locked
/// rows keeps two workers from taking the same row, and says nothing at all about the wrong worker
/// taking a row meant for the other one. So each test here drives the claim itself rather than a
/// handler — every other suite in this project runs handlers by hand and never touches the claim,
/// which means a predicate written backwards would pass all of them while a build measured in
/// hours quietly stood in front of every conversion and reading queued behind it.
/// </para>
/// <para>
/// Each refusal is paired, in the same test, with the claim that succeeds: a lane that claimed
/// nothing at all would otherwise look exactly like a lane that correctly passed a row over.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JobQueueLaneTests : IDisposable
{
    private readonly SilexGisApiFactory factory;

    // This class claims rows itself, so nothing else may be claiming beside it: the queue is one
    // table shared by every suite in the collection, and a drain running in this host would take
    // the rows these tests are reasoning about.
    public JobQueueLaneTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: JobWorkers.RemoveFrom);

    [Fact]
    public async Task The_general_worker_steps_over_a_terrain_job()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await AloneInTheQueueAsync(db);

        // The terrain row is written first and therefore has the lower id, which is the order the
        // claim reads in: the general worker meets it before anything it may have, and has to
        // pass over it rather than stop at it.
        var terrain = Queued(ProcessingJobKinds.TerrainBuild);
        var ordinary = Queued(ProcessingJobKinds.TextExtraction);
        db.ProcessingJobs.Add(terrain);
        await db.SaveChangesAsync();
        db.ProcessingJobs.Add(ordinary);
        await db.SaveChangesAsync();
        terrain.Id.ShouldBeLessThan(ordinary.Id);

        var claimed = await JobsSql.ClaimNextAsync(db, JobLane.General, CancellationToken.None);

        claimed.ShouldBe(ordinary.Id);
        await db.Entry(terrain).ReloadAsync();
        terrain.Status.ShouldBe(ProcessingJobStatus.Queued);

        // Nothing else is left for that lane, so the terrain row is not merely later in the queue.
        (await JobsSql.ClaimNextAsync(db, JobLane.General, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_terrain_worker_takes_terrain_and_nothing_else()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await AloneInTheQueueAsync(db);

        // The ordinary row is the older one this time, so the terrain worker has to reach past it.
        var ordinary = Queued(ProcessingJobKinds.DocumentConversion);
        var terrain = Queued(ProcessingJobKinds.TerrainBuild);
        db.ProcessingJobs.Add(ordinary);
        await db.SaveChangesAsync();
        db.ProcessingJobs.Add(terrain);
        await db.SaveChangesAsync();
        ordinary.Id.ShouldBeLessThan(terrain.Id);

        (await JobsSql.ClaimNextAsync(db, JobLane.Terrain, CancellationToken.None))
            .ShouldBe(terrain.Id);

        // With its own kind taken, the terrain worker sees an empty queue although a job is
        // sitting in it — and that job is claimable, which is what proves the refusal was about
        // the lane rather than about the row.
        (await JobsSql.ClaimNextAsync(db, JobLane.Terrain, CancellationToken.None)).ShouldBeNull();
        (await JobsSql.ClaimNextAsync(db, JobLane.General, CancellationToken.None))
            .ShouldBe(ordinary.Id);
    }

    [Fact]
    public async Task Restarting_one_worker_leaves_the_other_lanes_running_job_alone()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await AloneInTheQueueAsync(db);

        var terrain = Running(ProcessingJobKinds.TerrainBuild);
        var ordinary = Running(ProcessingJobKinds.ArchiveExpansion);
        db.ProcessingJobs.AddRange(terrain, ordinary);
        await db.SaveChangesAsync();

        // A worker puts back whatever its own lane was in the middle of when the process died.
        // Unfiltered it would also put back what the other lane's worker is doing right now, and
        // that job would then be run a second time alongside the first.
        await JobsSql.RequeueInterruptedAsync(db, JobLane.General, CancellationToken.None);

        await db.Entry(ordinary).ReloadAsync();
        ordinary.Status.ShouldBe(ProcessingJobStatus.Queued);
        ordinary.StartedAt.ShouldBeNull();
        await db.Entry(terrain).ReloadAsync();
        terrain.Status.ShouldBe(ProcessingJobStatus.Running);

        await JobsSql.RequeueInterruptedAsync(db, JobLane.Terrain, CancellationToken.None);
        await db.Entry(terrain).ReloadAsync();
        terrain.Status.ShouldBe(ProcessingJobStatus.Queued);
    }

    [Fact]
    public async Task Two_workers_polling_together_do_not_take_the_same_row()
    {
        // Committed rather than held in a transaction: the second worker polls on a connection of
        // its own, and one connection cannot see another's uncommitted rows.
        await using var seedScope = factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // A terrain row left queued by an interrupted run would be the oldest in the lane and
        // would be claimed instead of this one. Only the lane tests write terrain rows, so this
        // clears exactly what it names.
        await seedDb.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.TerrainBuild
                && j.Status == ProcessingJobStatus.Queued)
            .ExecuteDeleteAsync();

        var job = Queued(ProcessingJobKinds.TerrainBuild);
        seedDb.ProcessingJobs.Add(job);
        await seedDb.SaveChangesAsync();

        try
        {
            await using var firstScope = factory.Services.CreateAsyncScope();
            var first = firstScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await using var held = await first.Database.BeginTransactionAsync();
            (await JobsSql.ClaimNextAsync(first, JobLane.Terrain, CancellationToken.None))
                .ShouldBe(job.Id);

            await using var secondScope = factory.Services.CreateAsyncScope();
            var second = secondScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            // The row is claimed but not yet committed. A second worker must step over it and
            // answer "nothing to do"; without that it would sit waiting on the first worker's
            // lock for as long as the job takes — hours, for a terrain build — so a claim that
            // does not answer promptly is a failure rather than a slow pass.
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            long? secondClaim = null;
            var waited = false;
            try
            {
                secondClaim = await JobsSql.ClaimNextAsync(second, JobLane.Terrain, patience.Token);
            }
            catch (OperationCanceledException)
            {
                waited = true;
            }

            waited.ShouldBeFalse("the second worker waited on the row the first worker holds");
            secondClaim.ShouldBeNull();

            await held.RollbackAsync();
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            var cleanup = cleanupScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await cleanup.ProcessingJobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Empties the queue inside the caller's transaction, which is rolled back at the end of the
    /// test. The claim takes the oldest row of its lane whoever wrote it, and this table is shared
    /// with every other suite in the collection, so the only way to say exactly which row a worker
    /// should have taken is to be the only writer for the length of the test — and the only way to
    /// do that without destroying another suite's rows is to undo it afterwards.
    /// </summary>
    private static Task AloneInTheQueueAsync(SilexGisDbContext db) =>
        db.ProcessingJobs.ExecuteDeleteAsync();

    private static ProcessingJob Queued(string kind) => new()
    {
        Kind = kind,
        Status = ProcessingJobStatus.Queued,
    };

    private static ProcessingJob Running(string kind) => new()
    {
        Kind = kind,
        Status = ProcessingJobStatus.Running,
        StartedAt = DateTimeOffset.UtcNow,
        Attempts = 1,
    };

    public void Dispose() => factory.Dispose();
}
