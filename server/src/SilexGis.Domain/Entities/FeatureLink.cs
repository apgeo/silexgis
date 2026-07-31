// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Link-kind taxonomy (associated_cave, hydrological_connection, …). A locating link is
/// one whose existence discloses the target's position when the source carries exact
/// coordinates — such links are redacted, in both directions, for callers without exact
/// view on the protected endpoint. Fail-closed: kinds are locating unless an
/// administrator explicitly says otherwise (security-bearing metadata: admin-only edits,
/// audited, seeded rows contract-tested).
/// </summary>
public class LinkKind : TaxonomyBase
{
    public bool Locating { get; set; } = true;
}

/// <summary>
/// A typed association between two features (NOT containment — that is the hierarchy).
/// Replaces the hardcoded per-relation FK columns; both endpoints are real FKs. Trip
/// logs deliberately do not participate (they keep their own cave relation).
/// </summary>
public class FeatureLink : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public Guid FromId { get; set; }

    public Guid ToId { get; set; }

    public long LinkKindId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
