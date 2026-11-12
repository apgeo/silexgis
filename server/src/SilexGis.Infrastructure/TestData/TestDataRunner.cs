// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>A run stopped because somebody asked it to, between two items.</summary>
public sealed class TestDataRunCancelledException() : Exception("The test-data run was cancelled.");

/// <summary>
/// Carries out one test-data run: plans the world from the profile, then writes it phase by
/// phase, each chunk in a scope of its own so a run of thousands of rows never holds them all in
/// one change tracker. Progress, the tally and the notes are written to the run row as it goes.
/// </summary>
public sealed class TestDataRunner(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TestDataRunner> logger)
{
    /// <summary>Caves written per save and per scope.</summary>
    private const int CaveChunk = 25;

    public static class Phases
    {
        public const string Plan = "plan";
        public const string Vocabulary = "vocabulary";
        public const string Regions = "regions";
        public const string Caves = "caves";
        public const string Verify = "verify";
    }

    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await StartAsync(runId, ct);
        if (run is null)
        {
            return;
        }

        TestDataSession? session = null;
        try
        {
            var profile = TestDataProfile.FromJson(run.Profile);
            var owner = run.RequestedBy
                ?? throw new InvalidOperationException("A test-data run needs the account it is generated for.");
            var plan = TestDataPlanner.Plan(profile);
            session = new TestDataSession(run.Id, owner, plan);
            session.Note("Elevations come from an invented surface; real ground heights from a terrain build come in a later part of the generator.");
            var total = 2 + plan.Regions.Count + plan.Caves.Count;
            var done = 0;
            await ProgressAsync(session, Phases.Plan, done, total, ct);

            await InScopeAsync(session, async (db, _) =>
            {
                session.Vocabulary = await TestDataGeographyWriter.ResolveVocabularyAsync(db, ct);
                await TestDataGeographyWriter.EnsureMarkingTagAsync(db, session, ct);
            }, ct);
            await ProgressAsync(session, Phases.Vocabulary, ++done, total, ct);

            await InScopeAsync(session, (db, writer) => TestDataGeographyWriter.WriteRegionsAsync(db, writer, session, ct), ct);
            done += plan.Regions.Count;
            await ProgressAsync(session, Phases.Regions, done, total, ct);

            foreach (var chunk in plan.Caves.Chunk(CaveChunk))
            {
                await ThrowIfCancelRequestedAsync(run.Id, ct);
                await InScopeAsync(session, (db, writer) => TestDataGeographyWriter.WriteCavesAsync(db, writer, session, chunk, ct), ct);
                done += chunk.Length;
                await ProgressAsync(session, Phases.Caves, done, total, ct);
            }

            await FinishAsync(session, TestDataRunStatus.Succeeded, error: null, total, ct);
            logger.LogInformation("Test-data run {RunId} wrote {Counts}", run.Id, JsonSerializer.Serialize(session.Counts));
        }
        catch (TestDataRunCancelledException)
        {
            await FinishAsync(session, TestDataRunStatus.Cancelled, error: null, total: null, CancellationToken.None, run.Id);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Test-data run {RunId} failed", run.Id);
            await FinishAsync(session, TestDataRunStatus.Failed, Short(e), total: null, CancellationToken.None, run.Id);
            throw;
        }
    }

    /// <summary>Claims the run for this job; null when it is not waiting (already taken, removed, or interrupted earlier).</summary>
    private async Task<TestDataRun?> StartAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var run = await db.TestDataRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return null;
        }

        if (run.Status == TestDataRunStatus.Running)
        {
            // The job was taken back after its host stopped mid-run. What it wrote is listed, so
            // the run is closed as failed and can be removed; it is not resumed, because a run
            // resumed from the middle would not be the run its seed promises.
            run.Status = TestDataRunStatus.Failed;
            run.Error = "The run was interrupted (the server stopped while it was generating). Remove it and start again.";
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (run.Status != TestDataRunStatus.Queued)
        {
            return null;
        }

        run.Status = TestDataRunStatus.Running;
        run.StartedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>
    /// Runs one unit of writing in a fresh scope, marked as generated data done for the run's
    /// owner, so the history names the generator and the account behind it.
    /// </summary>
    private async Task InScopeAsync(
        TestDataSession session, Func<SilexGisDbContext, FeatureWriteService, Task> work, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
        changes.Begin(ChangeSource.Generated);
        changes.ActFor(session.OwnerUserId);
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await work(db, writer);
        await transaction.CommitAsync(ct);
    }

    private async Task ThrowIfCancelRequestedAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        if (await db.TestDataRuns.AnyAsync(r => r.Id == runId && r.CancelRequestedAt != null, ct))
        {
            throw new TestDataRunCancelledException();
        }
    }

    private async Task ProgressAsync(TestDataSession session, string phase, int done, int total, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var counts = JsonSerializer.Serialize(session.Counts);
        var notes = JsonSerializer.Serialize(session.Notes);
        await db.TestDataRuns.Where(r => r.Id == session.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Phase, phase)
            .SetProperty(r => r.ProgressDone, done)
            .SetProperty(r => r.ProgressTotal, total)
            .SetProperty(r => r.Counts, counts)
            .SetProperty(r => r.Notes, notes), ct);
    }

    private async Task FinishAsync(
        TestDataSession? session, TestDataRunStatus status, string? error, int? total, CancellationToken ct, Guid? runId = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = session?.RunId ?? runId!.Value;
        var run = await db.TestDataRuns.FirstAsync(r => r.Id == id, ct);
        run.Status = status;
        run.Error = error;
        run.Phase = null;
        run.FinishedAt = clock.GetUtcNow();
        if (session is not null)
        {
            run.Counts = JsonSerializer.Serialize(session.Counts);
            run.Notes = JsonSerializer.Serialize(session.Notes);
        }

        if (total is { } t)
        {
            run.ProgressDone = t;
            run.ProgressTotal = t;
        }

        await db.SaveChangesAsync(ct);
    }

    private static string Short(Exception e)
    {
        var message = e.GetBaseException().Message;
        return message.Length <= 500 ? message : message[..500];
    }
}
