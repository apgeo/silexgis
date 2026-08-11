// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>One step of a feature's containment path.</summary>
public readonly record struct FeatureChainStep(Guid Id, string? Name);

/// <summary>
/// The primary-parent chain of a feature — outermost ancestor first, the feature itself
/// excluded — as one rule with one home, because two surfaces now say it: the breadcrumb
/// above a feature's own page, and the containment path a link chip carries so that two
/// passages of the same name stay apart.
///
/// The chain is walked over primary edges rather than read out of the denormalised
/// ancestor set: that set is every ancestor over every path of the containment DAG and
/// carries no order, so it can say which features are above this one but never in what
/// order to read them.
///
/// It is truncated at the first ancestor the caller may not read, and the names above
/// that point are never disclosed — a partial path is the honest answer, and continuing
/// past the gap would name features the visibility rules keep from this caller.
/// </summary>
public static class FeaturePrimaryChains
{
    /// <summary>A feature to chain: its id and the denormalised ancestor set it carries.</summary>
    public readonly record struct Subject(Guid Id, IReadOnlyList<Guid> AncestorIds);

    /// <summary>
    /// The chains of one feature. Kept as a convenience over the batched form so that a
    /// single-row surface reads as one call, not as a set of one it then unpacks.
    /// </summary>
    public static async Task<IReadOnlyList<FeatureChainStep>> OfAsync(
        SilexGisDbContext db, AccessContext ctx, Subject subject, CancellationToken ct) =>
        (await OfAsync(db, ctx, [subject], ct)).GetValueOrDefault(subject.Id) ?? [];

    /// <summary>
    /// The chains of a whole set of features, keyed by feature id, in two queries however
    /// many features are asked about — the primary edges of every feature and ancestor
    /// involved, then the ancestor names this caller may read. A page of chips must not
    /// cost a query each: that is the shape this exists in.
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<FeatureChainStep>>> OfAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Subject> subjects,
        CancellationToken ct)
    {
        var empty = subjects.ToDictionary(s => s.Id, _ => (IReadOnlyList<FeatureChainStep>)[]);
        var ancestorIds = subjects
            .SelectMany(s => s.AncestorIds.Where(a => a != s.Id))
            .Distinct()
            .ToList();
        if (ancestorIds.Count == 0)
        {
            return empty;
        }

        // Every id that can appear as a child while walking upwards: the subjects
        // themselves and everything above them.
        var walkedIds = subjects
            .SelectMany(s => s.AncestorIds.Append(s.Id))
            .Distinct()
            .ToList();
        var primaryParentOf = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.IsPrimary && walkedIds.Contains(e.ChildId))
            .Select(e => new { e.ChildId, e.ParentId })
            .ToDictionaryAsync(e => e.ChildId, e => e.ParentId, ct);
        var visibleNames = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ancestorIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var chains = new Dictionary<Guid, IReadOnlyList<FeatureChainStep>>();
        foreach (var subject in subjects)
        {
            var chain = new List<FeatureChainStep>();
            var current = subject.Id;
            // The DAG is cycle-free by construction; the cap keeps corrupt data from looping.
            while (chain.Count <= subject.AncestorIds.Count
                && primaryParentOf.TryGetValue(current, out var parentId))
            {
                if (!visibleNames.TryGetValue(parentId, out var name))
                {
                    break;
                }

                chain.Add(new FeatureChainStep(parentId, name));
                current = parentId;
            }

            chain.Reverse();
            chains[subject.Id] = chain;
        }

        return chains;
    }

    /// <summary>
    /// The installation's one root feature, when it has exactly one — everything then
    /// hangs under a single top, so naming it at the head of every path says nothing
    /// anyone needs; null when there are none or several, and then no step is special. A
    /// root is a feature whose denormalised ancestor set holds only itself, and two of
    /// them already settle the question, so this never counts the table.
    ///
    /// It answers with the id rather than a yes or no because a path truncated at an
    /// unreadable ancestor no longer begins at the root, and dropping its outermost step
    /// would throw away the only containment its reader was allowed to be told.
    /// </summary>
    public static async Task<Guid?> SingleRootIdAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var roots = await db.Features.AsNoTracking()
            .Where(f => f.AncestorIds.Length == 1)
            .Select(f => f.Id)
            .Take(2)
            .ToListAsync(ct);
        return roots.Count == 1 ? roots[0] : null;
    }
}
