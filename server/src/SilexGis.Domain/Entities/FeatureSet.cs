// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A named, arbitrary grouping of features, independent of the containment DAG,
/// existing so access entries can scope to it. That makes set membership part of the
/// security surface: editing a set moves access without touching an entry, so set CRUD
/// and membership edits are audited exactly like entry edits, and the sets themselves
/// are governed by their own resource domain.
/// </summary>
public class FeatureSet : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>Pure junction: membership of one feature in one set. Composite PK.</summary>
public class FeatureSetMember
{
    public Guid FeatureSetId { get; set; }

    public Guid FeatureId { get; set; }
}
