// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Features;

/// <summary>
/// Pure semantics of the containment DAG: ancestor computation, cycle prevention and
/// subtree collection over an in-memory edge set. The feature write service is the only
/// caller mutating hierarchy state — it loads the edge set, delegates here, and persists
/// the recomputed closure/ancestor arrays; the integrity verifier re-runs the same
/// functions against stored state. Read paths never traverse: they use the materialized
/// ancestor arrays.
/// </summary>
public static class FeatureHierarchyRules
{
    /// <summary>One containment edge (parent contains child).</summary>
    public readonly record struct Edge(Guid ParentId, Guid ChildId);

    /// <summary>
    /// Ancestors of <paramref name="featureId"/> over all DAG paths, including itself
    /// (the ancestor set read filters splice as <c>= ANY(...)</c>). Tolerates cycles in
    /// the input (visited-set guarded) so the verifier can run on corrupted state.
    /// </summary>
    public static HashSet<Guid> AncestorsOf(Guid featureId, ILookup<Guid, Guid> parentsByChild)
    {
        var result = new HashSet<Guid> { featureId };
        var queue = new Queue<Guid>();
        queue.Enqueue(featureId);
        while (queue.Count > 0)
        {
            foreach (var parent in parentsByChild[queue.Dequeue()])
            {
                if (result.Add(parent))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether adding parent→child would create a cycle: the child (or the child itself
    /// being the parent) must not already be an ancestor of the parent.
    /// </summary>
    public static bool WouldCreateCycle(Guid parentId, Guid childId, ILookup<Guid, Guid> parentsByChild) =>
        parentId == childId || AncestorsOf(parentId, parentsByChild).Contains(childId);

    /// <summary>
    /// The containment subtree of <paramref name="rootId"/> — every feature reachable
    /// downward over any path, including the root. This is the set a protection flag,
    /// a soft delete or a subtree share covers.
    /// </summary>
    public static HashSet<Guid> DescendantsOf(Guid rootId, ILookup<Guid, Guid> childrenByParent)
    {
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            foreach (var child in childrenByParent[queue.Dequeue()])
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Effective protection of one feature: any ancestor (including itself) is a
    /// protection root.
    /// </summary>
    public static bool IsProtectedEffective(
        IReadOnlySet<Guid> ancestorIds, Func<Guid, bool> isProtectionRoot) =>
        ancestorIds.Any(isProtectionRoot);

    /// <summary>Builds a child→parents lookup from an edge set.</summary>
    public static ILookup<Guid, Guid> ParentsByChild(IEnumerable<Edge> edges) =>
        edges.ToLookup(e => e.ChildId, e => e.ParentId);

    /// <summary>Builds a parent→children lookup from an edge set.</summary>
    public static ILookup<Guid, Guid> ChildrenByParent(IEnumerable<Edge> edges) =>
        edges.ToLookup(e => e.ParentId, e => e.ChildId);
}
