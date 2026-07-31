// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One containment edge of the default feature hierarchy — a DAG: a feature may have
/// several parents (a cave under two overlapping karst areas), but exactly one edge per
/// feature is the primary one (partial unique index), driving breadcrumbs, tree display
/// and share-link subtree scope. Cycle prevention is enforced by the feature write
/// service and re-checked by the integrity verifier; the derived closure
/// (<see cref="FeatureAncestor"/>) and per-row ancestor arrays are recomputed on every
/// edge change.
/// </summary>
public class FeatureHierarchyEdge : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public Guid ParentId { get; set; }

    public Guid ChildId { get; set; }

    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// Transitive closure of the containment DAG, including the depth-0 row
/// (<c>FeatureId == AncestorId</c>) so subtree queries return the root itself.
/// Derived state: rebuilt by the feature write service from the edges; the per-row
/// <c>features.ancestor_ids</c> array is its flattened mirror for filter splicing.
/// </summary>
public class FeatureAncestor
{
    public Guid FeatureId { get; set; }

    public Guid AncestorId { get; set; }
}

/// <summary>
/// A named parallel hierarchy (organizational grouping — e.g. an administrative zone
/// tree). Deliberately carries no access-control or protection semantics: those ride
/// only on the default containment hierarchy.
/// </summary>
public class Hierarchy : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// A feature's placement inside a parallel hierarchy. The materialized path (ltree,
/// labels = feature ids) is a shadow column configured in Infrastructure so Domain stays
/// free of provider types; each hierarchy is a strict tree (one row per feature per
/// hierarchy).
/// </summary>
public class HierarchyMembership : ITimestamped
{
    public long Id { get; set; }

    public Guid HierarchyId { get; set; }

    public Guid FeatureId { get; set; }

    public Guid? ParentFeatureId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
