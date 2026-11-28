// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// Takes a run's rows out of the database for good, the reverse of the order they were written,
/// by the registry alone. A generated row that somebody has since hung data of their own on is
/// kept, with a note, rather than taken away with that data: a place with features of somebody's
/// own beneath it, and a cave or place people have used — a file attached to it, a survey
/// uploaded for it, a trip or a document linked to it, a map drawn of it. Purging a feature
/// deletes all of those with it by cascade, so "in use" has to be decided before the purge.
/// </summary>
public sealed class TestDataRemoval(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TestDataRemoval> logger)
{
    /// <summary>Registry tables this removal knows how to remove; a run that lists another one is refused.</summary>
    public static IReadOnlySet<string> RemovableTables { get; } =
        new HashSet<string>(TestDataSession.SelfListedTables.Concat(["features", "tags", "users", "trip_types"]), StringComparer.Ordinal);

    private const int Chunk = 50;

    public async Task RemoveAsync(Guid runId, CancellationToken ct)
    {
        List<TestDataRow> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var run = await db.TestDataRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null || run.Status != TestDataRunStatus.Removing)
            {
                return;
            }

            rows = await db.TestDataRows.AsNoTracking()
                .Where(r => r.RunId == runId)
                .OrderByDescending(r => r.Sequence)
                .ToListAsync(ct);
        }

        try
        {
            await RemoveRowsAsync(runId, rows, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Left as Removing, the run would count as the one run in progress for good — refusing
            // every new run — and could not be asked to remove again either. Closed as failed it
            // can be removed again, which picks up from what is still listed.
            logger.LogError(e, "Removing test-data run {RunId} failed", runId);
            await FailAsync(runId, e);
            throw;
        }
    }

    /// <summary>The prefix of the error a failed removal leaves on its run, cleared by the removal that then succeeds.</summary>
    public const string RemovalFailedPrefix = "Removal failed: ";

    private async Task FailAsync(Guid runId, Exception e)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var message = e.GetBaseException().Message;
        await db.TestDataRuns.Where(r => r.Id == runId && r.Status == TestDataRunStatus.Removing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, TestDataRunStatus.Failed)
                .SetProperty(r => r.Phase, (string?)null)
                .SetProperty(r => r.Error, RemovalFailedPrefix + (message.Length <= 480 ? message : message[..480])),
                CancellationToken.None);
    }

    private async Task RemoveRowsAsync(Guid runId, List<TestDataRow> rows, CancellationToken ct)
    {
        var unknown = rows.Select(r => r.TableName).Distinct().Where(t => !RemovableTables.Contains(t)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"The run lists rows of tables removal does not know: {string.Join(", ", unknown)}.");
        }

        var notes = new List<string>();
        var listed = rows.ToLookup(r => r.TableName, r => r.RowId, StringComparer.Ordinal);
        TestDataPeopleRemoval.Kept people;
        IReadOnlyList<StoredFile> storedGone;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
            changes.Begin(ChangeSource.Generated);
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await TestDataActivityRemoval.RemoveAsync(db, listed, ct);

            // The underground before the people are decided on: the centrelines the reading drew are
            // features no run listed, bound to the cave's club, and would otherwise keep the club.
            storedGone = await TestDataUndergroundRemoval.RemoveAsync(
                db,
                scope.ServiceProvider.GetRequiredService<FeatureWriteService>(),
                scope.ServiceProvider.GetRequiredService<Documents.DocumentWriteService>(),
                listed,
                ct);
            people = await TestDataPeopleRemoval.DecideAsync(db, runId, listed, ct);
            await TestDataPeopleRemoval.RemoveHangOnsAsync(db, listed, people, ct);
            await transaction.CommitAsync(ct);

            // The bytes go once nothing names them any more; a file that cannot be deleted now is
            // logged and left on disk rather than failing a removal whose rows have gone.
            var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
            foreach (var file in storedGone)
            {
                try
                {
                    await store.DeleteAsync(file.StoragePath, ct);
                }
                catch (IOException e)
                {
                    logger.LogWarning(e, "A removed test-data file could not be deleted from the store");
                }
            }
        }

        var kept = 0;
        var keptInUse = 0;
        var removed = 0;
        var keptIds = new HashSet<Guid>();
        var features = rows.Where(r => r.TableName == "features").Select(r => Guid.Parse(r.RowId)).ToList();
        var generatedAnywhere = await GeneratedFeatureIdsAsync(ct);
        var held = await HeldAsync(features, ct);

        foreach (var chunk in features.Chunk(Chunk))
        {
            await using var scope = scopes.CreateAsyncScope();
            var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
            changes.Begin(ChangeSource.Generated);
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Asked again inside the transaction, so a use made while the removal ran still counts.
            var inUse = await InUseAsync(db, chunk, ct);
            foreach (var id in chunk)
            {
                if (!await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == id, ct))
                {
                    continue;
                }

                if (inUse.Contains(id) || held.Contains(id))
                {
                    keptIds.Add(id);
                    keptInUse++;
                    continue;
                }

                // Leaves were written last and go first, so by the time an area's turn comes its
                // generated caves are gone. Anything still beneath it is somebody's own, or a
                // generated row kept because it is in use — which the purge would take with it.
                var foreignBelow = await db.FeatureAncestors
                    .Where(a => a.AncestorId == id && a.FeatureId != id)
                    .Select(a => a.FeatureId)
                    .ToListAsync(ct);
                if (foreignBelow.Any(f => !generatedAnywhere.Contains(f) || keptIds.Contains(f)))
                {
                    keptIds.Add(id);
                    kept++;
                    continue;
                }

                await writer.PurgeAsync(id, ct);
                await db.SaveChangesAsync(ct);
                removed++;
            }

            await transaction.CommitAsync(ct);
        }

        int keptVocabulary;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
            changes.Begin(ChangeSource.Generated);
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            keptVocabulary = await TestDataPeopleRemoval.RemoveVocabularyAsync(db, runId, listed, ct);
            await TestDataPeopleRemoval.RemovePeopleAsync(db, listed, people, ct);
            await transaction.CommitAsync(ct);
        }

        if (people.Count + keptVocabulary > 0)
        {
            notes.Add(TestDataNotes.WithCount(TestDataNotes.PeopleKeptInUse, people.Count + keptVocabulary));
        }

        if (keptInUse > 0)
        {
            notes.Add(TestDataNotes.WithCount(TestDataNotes.KeptInUse, keptInUse));
        }

        if (kept > 0)
        {
            notes.Add(TestDataNotes.WithCount(TestDataNotes.KeptBeneath, kept));
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var tagRow in rows.Where(r => r.TableName == "tags"))
            {
                var tagId = long.Parse(tagRow.RowId, System.Globalization.CultureInfo.InvariantCulture);
                if (!await db.Taggings.AnyAsync(t => t.TagId == tagId, ct))
                {
                    await db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync(ct);
                }
            }

            var run = await db.TestDataRuns.FirstAsync(r => r.Id == runId, ct);
            await db.TestDataRows.Where(r => r.RunId == runId).ExecuteDeleteAsync(ct);
            run.Status = TestDataRunStatus.Removed;
            run.RemovedAt = clock.GetUtcNow();
            run.AccountPasswordHash = null;
            run.Phase = null;
            if (run.Error?.StartsWith(RemovalFailedPrefix, StringComparison.Ordinal) == true)
            {
                run.Error = null;
            }

            var allNotes = JsonSerializer.Deserialize<List<string>>(run.Notes) ?? [];
            allNotes.AddRange(notes);
            run.Notes = JsonSerializer.Serialize(allNotes);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Test-data run {RunId} removed: {Removed} features removed, {KeptInUse} kept because in use, {Kept} kept for data beneath them",
            runId, removed, keptInUse, kept);
    }

    /// <summary>
    /// The run's features that are in use, and the generated features beneath them — a cave kept
    /// because a trip names it keeps its entrances too. Asked once before anything goes, because
    /// the entrances were written after their cave and so come up for removal before it does.
    /// </summary>
    private async Task<HashSet<Guid>> HeldAsync(List<Guid> features, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var inUse = await InUseAsync(db, [.. features], ct);
        if (inUse.Count == 0)
        {
            return inUse;
        }

        var used = inUse.ToList();
        var beneath = await db.FeatureAncestors
            .Where(a => used.Contains(a.AncestorId))
            .Select(a => a.FeatureId)
            .ToListAsync(ct);
        inUse.UnionWith(beneath);
        return inUse;
    }

    /// <summary>
    /// Which of <paramref name="ids"/> people have used. The rows a run hangs on its features —
    /// links, external ids, shares, QR publications, grants — are removed before this is asked,
    /// so every row found here was made by somebody (or belongs to a generated account kept
    /// because somebody used it). A later part of the generator that writes attachments, surveys
    /// or links of its own has to remove its own rows first in the same way, or nothing it
    /// touched would ever be removed.
    /// </summary>
    private static async Task<HashSet<Guid>> InUseAsync(SilexGisDbContext db, Guid[] ids, CancellationToken ct)
    {
        var used = new HashSet<Guid>();
        async Task Add(IQueryable<Guid?> references) =>
            used.UnionWith(await references
                .Where(r => r != null && ids.Contains(r.Value))
                .Select(r => r!.Value)
                .Distinct()
                .ToListAsync(ct));

        await Add(db.Attachments.IgnoreQueryFilters().Select(a => a.FeatureId));
        await Add(db.ResLinkMembers.IgnoreQueryFilters().Select(m => m.FeatureId));
        await Add(db.SurveyModels.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.CaveSurveys.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.SurveyUploads.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.SurveySources.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.SurveyCompilations.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.TripTrackings.IgnoreQueryFilters().Select(t => t.CaveFeatureId));
        await Add(db.TripRecordings.IgnoreQueryFilters().Select(t => (Guid?)t.CaveFeatureId));
        await Add(db.RasterMaps.IgnoreQueryFilters().Select(m => (Guid?)m.CaveFeatureId));
        await Add(db.RasterMaps.IgnoreQueryFilters().Select(m => m.CaveAreaFeatureId));
        await Add(db.GeoreferencedMaps.IgnoreQueryFilters().Select(m => m.CaveFeatureId));
        await Add(db.Albums.IgnoreQueryFilters().Select(a => a.SubjectFeatureId));
        await Add(db.FeatureLinks.IgnoreQueryFilters().Select(l => (Guid?)l.FromId));
        await Add(db.FeatureLinks.IgnoreQueryFilters().Select(l => (Guid?)l.ToId));
        await Add(db.FeatureSetMembers.IgnoreQueryFilters().Select(m => (Guid?)m.FeatureId));
        await Add(db.FeatureShares.IgnoreQueryFilters().Select(s => (Guid?)s.FeatureId));
        await Add(db.SyncSetMembers.IgnoreQueryFilters().Select(m => (Guid?)m.RootFeatureId));
        await Add(db.CaveQrPublications.IgnoreQueryFilters().Select(p => (Guid?)p.FeatureId));
        await Add(db.CaveDepthPlaces.IgnoreQueryFilters().Select(p => (Guid?)p.CaveFeatureId));
        await Add(db.AccessEntries.IgnoreQueryFilters().Select(e => e.ScopeFeatureId));
        await Add(db.FeatureExternalIds.IgnoreQueryFilters().Select(x => (Guid?)x.FeatureId));
        return used;
    }

    /// <summary>Every feature any run lists, so one run's area is not held back by another run's cave.</summary>
    private async Task<HashSet<Guid>> GeneratedFeatureIdsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ids = await db.TestDataRows.AsNoTracking()
            .Where(r => r.TableName == "features")
            .Select(r => r.RowId)
            .ToListAsync(ct);
        return [.. ids.Select(Guid.Parse)];
    }
}
