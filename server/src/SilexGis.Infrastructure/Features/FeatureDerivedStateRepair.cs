// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// What a pass over the stored hierarchy state found: how many features carry a stored ancestor
/// list, closure rows or effective-protection flag that disagrees with what the containment edges
/// say. Numbers only — the operator runs this against an installation that holds real data, and
/// the answer has to be one that can be pasted anywhere.
/// </summary>
/// <param name="FeaturesExamined">Every feature row, deleted ones included.</param>
/// <param name="FeaturesWrong">Features with at least one of the three facts wrong.</param>
/// <param name="AncestorIdsWrong">Stored ancestor list differs from the edges.</param>
/// <param name="ClosureWrong">Closure rows differ from the edges.</param>
/// <param name="ProtectedEffectiveWrong">Stored effective-protection flag differs.</param>
/// <param name="ShownAsUnprotected">
/// The dangerous subset of the previous figure: stored as unprotected while a protected feature
/// contains it, so every read that trusts the flag shows its exact position.
/// </param>
public sealed record DerivedStateCount(
    int FeaturesExamined,
    int FeaturesWrong,
    int AncestorIdsWrong,
    int ClosureWrong,
    int ProtectedEffectiveWrong,
    int ShownAsUnprotected);

/// <summary>What a repair did, or with <c>Applied = false</c> what it would have done.</summary>
/// <param name="Before">The count taken inside the repair's transaction.</param>
/// <param name="Applied">Whether the changes were committed.</param>
/// <param name="FeatureRowsUpdated">Feature rows whose stored list or flag was rewritten.</param>
/// <param name="ClosureRowsAdded">Closure rows inserted.</param>
/// <param name="ClosureRowsRemoved">Closure rows deleted.</param>
/// <param name="After">The count taken after the changes, inside the same transaction.</param>
public sealed record DerivedStateRepairResult(
    DerivedStateCount Before,
    bool Applied,
    int FeatureRowsUpdated,
    int ClosureRowsAdded,
    int ClosureRowsRemoved,
    DerivedStateCount After);

/// <summary>
/// Counts, and on request repairs, the hierarchy state the write service derives and stores for
/// every feature: its ancestor list, its closure rows and its effective-protection flag.
///
/// <para>
/// The scheduled verifier finds the same divergence and deliberately repairs nothing, because a
/// divergence it finds means a write path bypassed the write service and the path is what has to
/// be fixed. This exists for the other case: the faulty path has been fixed and shipped, and the
/// rows it wrote before the fix are still wrong. An installation that ran the faulty version keeps
/// those rows until somebody rewrites them, and a stale "not protected" flag is the one defect in
/// that state that shows an exact position to people who may not see it.
/// </para>
/// <para>
/// The expected state is worked out by the same domain rules the write service uses, so the
/// repair cannot disagree with what the next ordinary write would store. It touches only the
/// features the count names, and of those only the closure rows that are missing or extra.
/// </para>
/// </summary>
public sealed class FeatureDerivedStateRepair(SilexGisDbContext db)
{
    /// <summary>Counts the features whose stored hierarchy state is wrong. Reads only.</summary>
    public async Task<DerivedStateCount> CountAsync(CancellationToken ct = default) =>
        (await ScanAsync(ct)).Count;

    /// <summary>
    /// Counts, then — only when <paramref name="apply"/> is set — rewrites exactly the rows the
    /// count names, all in one serialisable transaction, and counts again inside it. A dry run
    /// rolls back, so it reports what would change without changing anything. If the second count
    /// is not zero the transaction is rolled back rather than committed half right.
    /// </summary>
    public async Task<DerivedStateRepairResult> RepairAsync(bool apply, CancellationToken ct = default)
    {
        // Serialisable so that a feature moved or protected by somebody else while this runs makes
        // the commit fail instead of being overwritten with a state computed before their change.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var scan = await ScanAsync(ct);
        var updated = 0;
        var added = 0;
        var removed = 0;
        foreach (var wrong in scan.Wrong)
        {
            var ancestors = wrong.ExpectedAncestors.OrderBy(a => a).ToArray();
            if (wrong.AncestorIdsWrong || wrong.ProtectedEffectiveWrong)
            {
                var expectedProtected = wrong.ExpectedProtected;
                updated += await db.Features.IgnoreQueryFilters()
                    .Where(f => f.Id == wrong.FeatureId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(f => f.AncestorIds, ancestors)
                            .SetProperty(f => f.IsProtectedEffective, expectedProtected),
                        ct);
            }

            if (wrong.ClosureWrong)
            {
                var extra = wrong.StoredClosure.Except(wrong.ExpectedAncestors).ToList();
                if (extra.Count > 0)
                {
                    removed += await db.FeatureAncestors
                        .Where(a => a.FeatureId == wrong.FeatureId && extra.Contains(a.AncestorId))
                        .ExecuteDeleteAsync(ct);
                }

                var missing = wrong.ExpectedAncestors.Except(wrong.StoredClosure).ToList();
                db.FeatureAncestors.AddRange(missing.Select(
                    a => new FeatureAncestor { FeatureId = wrong.FeatureId, AncestorId = a }));
                added += missing.Count;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var after = (await ScanAsync(ct)).Count;
        var result = new DerivedStateRepairResult(scan.Count, apply && after.FeaturesWrong == 0, updated, added, removed, after);
        if (result.Applied)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }

    private sealed record WrongFeature(
        Guid FeatureId,
        HashSet<Guid> ExpectedAncestors,
        HashSet<Guid> StoredClosure,
        bool ExpectedProtected,
        bool AncestorIdsWrong,
        bool ClosureWrong,
        bool ProtectedEffectiveWrong);

    private sealed record Scan(DerivedStateCount Count, IReadOnlyList<WrongFeature> Wrong);

    private async Task<Scan> ScanAsync(CancellationToken ct)
    {
        // The same reads the write service and the verifier make: the edge table as stored, every
        // feature including deleted ones (a deleted protected area still protects what it held,
        // and a restore must find its descendants' state right), and the whole closure table.
        var edges = await db.FeatureHierarchyEdges
            .Select(e => new FeatureHierarchyRules.Edge(e.ParentId, e.ChildId))
            .ToListAsync(ct);
        var parentsByChild = FeatureHierarchyRules.ParentsByChild(edges);

        var features = await db.Features.IgnoreQueryFilters()
            .Select(f => new { f.Id, f.AncestorIds, f.IsProtectedEffective, f.LocationProtected })
            .ToListAsync(ct);
        var protectedIds = features.Where(f => f.LocationProtected).Select(f => f.Id).ToHashSet();

        var closure = (await db.FeatureAncestors.AsNoTracking().ToListAsync(ct))
            .ToLookup(a => a.FeatureId, a => a.AncestorId);

        var wrong = new List<WrongFeature>();
        foreach (var feature in features)
        {
            var expected = FeatureHierarchyRules.AncestorsOf(feature.Id, parentsByChild);
            var stored = closure[feature.Id].ToHashSet();
            var expectedProtected = FeatureHierarchyRules.IsProtectedEffective(expected, protectedIds.Contains);

            var ancestorIdsWrong = !expected.SetEquals(feature.AncestorIds);
            var closureWrong = !expected.SetEquals(stored);
            var protectedWrong = feature.IsProtectedEffective != expectedProtected;
            if (ancestorIdsWrong || closureWrong || protectedWrong)
            {
                wrong.Add(new WrongFeature(
                    feature.Id, expected, stored, expectedProtected, ancestorIdsWrong, closureWrong, protectedWrong));
            }
        }

        var count = new DerivedStateCount(
            FeaturesExamined: features.Count,
            FeaturesWrong: wrong.Count,
            AncestorIdsWrong: wrong.Count(w => w.AncestorIdsWrong),
            ClosureWrong: wrong.Count(w => w.ClosureWrong),
            ProtectedEffectiveWrong: wrong.Count(w => w.ProtectedEffectiveWrong),
            ShownAsUnprotected: wrong.Count(w => w.ProtectedEffectiveWrong && w.ExpectedProtected));
        return new Scan(count, wrong);
    }
}
