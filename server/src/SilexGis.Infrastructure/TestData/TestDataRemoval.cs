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
/// kept, with a note, rather than taken away with that data.
/// </summary>
public sealed class TestDataRemoval(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TestDataRemoval> logger)
{
    /// <summary>Registry tables this removal knows how to remove; a run that lists another one is refused.</summary>
    public static IReadOnlySet<string> RemovableTables { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "features", "tags" };

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

        var unknown = rows.Select(r => r.TableName).Distinct().Where(t => !RemovableTables.Contains(t)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"The run lists rows of tables removal does not know: {string.Join(", ", unknown)}.");
        }

        var notes = new List<string>();
        var kept = 0;
        var removed = 0;
        var features = rows.Where(r => r.TableName == "features").Select(r => Guid.Parse(r.RowId)).ToList();
        var generatedAnywhere = await GeneratedFeatureIdsAsync(ct);

        foreach (var chunk in features.Chunk(Chunk))
        {
            await using var scope = scopes.CreateAsyncScope();
            var changes = scope.ServiceProvider.GetRequiredService<IChangeContext>();
            changes.Begin(ChangeSource.Generated);
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var id in chunk)
            {
                if (!await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == id, ct))
                {
                    continue;
                }

                // Leaves were written last and go first, so by the time an area's turn comes its
                // generated caves are gone. Anything still beneath it is somebody's own.
                var foreignBelow = await db.FeatureAncestors
                    .Where(a => a.AncestorId == id && a.FeatureId != id)
                    .Select(a => a.FeatureId)
                    .ToListAsync(ct);
                if (foreignBelow.Any(f => !generatedAnywhere.Contains(f)))
                {
                    kept++;
                    continue;
                }

                await writer.PurgeAsync(id, ct);
                await db.SaveChangesAsync(ct);
                removed++;
            }

            await transaction.CommitAsync(ct);
        }

        if (kept > 0)
        {
            notes.Add($"{kept} generated area(s) or cave(s) were kept because data not created by a test-data run is attached beneath them.");
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
            run.Phase = null;
            var allNotes = JsonSerializer.Deserialize<List<string>>(run.Notes) ?? [];
            allNotes.AddRange(notes);
            run.Notes = JsonSerializer.Serialize(allNotes);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Test-data run {RunId} removed: {Removed} features removed, {Kept} kept", runId, removed, kept);
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
