// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A place a document lives: one node of a global, strictly hierarchical filing tree
/// ("Club archive / Bulletins / 1987"). A cabinet exists for two reasons a tag cannot
/// serve — it is hierarchical, and it is a collection an access entry can be scoped to,
/// so "the committee may read the club archive" is one rule rather than one per document.
/// <para>
/// A cabinet deliberately carries **no owning caving group**: who may see a cabinet has
/// exactly one answer, the access-entry walk, rather than one answer for group-owned
/// cabinets and another for the rest. Should group ownership be wanted later it arrives
/// additively — a nullable group column here plus the caving-group scope the access model
/// already defines — which is why nothing may resolve cabinet access by a shortcut.
/// </para>
/// <para>
/// The materialized path of ancestor ids is a shadow column configured in Infrastructure
/// so Domain stays free of provider types; the cabinet write service is its sole mutator.
/// </para>
/// </summary>
public class Cabinet : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The cabinet this one sits in; null for a root cabinet. Exactly one parent — unlike
    /// the feature containment DAG, a filing tree is strict, so a breadcrumb is unambiguous.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>Unique among its siblings, not globally: "1987" may sit under many archives.</summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Derived: this cabinet's id plus every ancestor's, root first. The materialized path
    /// says the same thing, but a path is a text prefix match — this array is what the flat
    /// access filters overlap against ("is this document filed at or below cabinet X"),
    /// with no per-row parse and no recursion. The cabinet write service stamps both.
    /// </summary>
    public Guid[] AncestorIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One document filed in one cabinet. Many-to-many on purpose: a document may sit in
/// several cabinets at once (a 1987 survey report is both club-archive material and a
/// survey of one cave), which dissolves the two problems a filesystem metaphor creates —
/// there is no move-versus-copy question and nothing is orphaned by belonging in two
/// places. Filing and unfiling are the only operations.
/// <para>
/// Membership grants nothing by itself. Filing a document into a cabinet nobody holds an
/// entry on changes no one's access; access comes from entries scoped to the cabinet.
/// </para>
/// <para>
/// The row carries an id of its own so that filing rides the ordinary audit trail and
/// surfaces on the filed document's timeline, instead of needing a hand-written event the
/// way an identity-less junction would.
/// </para>
/// </summary>
public class CabinetDocument : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CabinetId { get; set; }

    public Guid DocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Filing is a change to the document, so it belongs on the document's timeline.
    public string RootEntityType => nameof(Document);

    public string RootEntityId => DocumentId.ToString();
}
