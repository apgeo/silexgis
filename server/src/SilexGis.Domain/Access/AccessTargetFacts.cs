// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Access;

/// <summary>One row of the visibility chain: a row's own or an ancestor's audience facts.</summary>
public readonly record struct VisibilityFact(Visibility Visibility, Guid? CavingGroupId);

/// <summary>
/// The context an access check evaluates against — the row's facts for actions on an
/// existing object, or the target facts for Create (prospective parent + requested
/// caving-group binding). Null facts mean a genuinely target-less domain-wide check,
/// which only Global-level entries can match.
/// </summary>
public sealed record AccessTargetFacts
{
    /// <summary>The evaluated row's id — what Object-scope entries match on. Null for
    /// Create (Create is rejected at Object scope, so nothing may match Level 1) and
    /// for domain-wide checks.</summary>
    public Guid? ObjectId { get; init; }

    public Guid? OwnerUserId { get; init; }

    public Guid? CavingGroupId { get; init; }

    /// <summary>Features: self + every ancestor over all DAG paths (the flat context
    /// subtree entries match against). For Create: the prospective parent's array, so
    /// "create under this subtree" matches. Empty elsewhere.</summary>
    public Guid[] AncestorIds { get; init; } = [];

    /// <summary>The evaluated feature's kind; for Create, the kind being created.</summary>
    public FeatureKind? FeatureKind { get; init; }

    /// <summary>The evaluated feature's data-level type; for Create, the type being created.</summary>
    public long? FeatureTypeId { get; init; }

    /// <summary>Feature sets containing the evaluated row (resolved by the access
    /// service). Empty for Create and non-feature rows.</summary>
    public Guid[] FeatureSetIds { get; init; } = [];

    /// <summary>
    /// Cabinets that reach the evaluated document: every cabinet it is filed in plus each
    /// of their ancestors, so a cabinet-scoped entry matches the whole subtree below it
    /// with one flat containment test. Resolved by the access service — filing lives in a
    /// join table the pure rule cannot read. Empty for Create, for documents filed
    /// nowhere, and for every domain but documents.
    /// </summary>
    public Guid[] CabinetIds { get; init; } = [];

    /// <summary>
    /// Audience facts of the row itself plus — for features — every ancestor, feeding
    /// the read-time visibility cascade: a row is visibility-readable when any link of
    /// this chain admits the caller. Empty when visibility can never apply (Create,
    /// domain-wide checks, subjects without a visibility column).
    /// </summary>
    public IReadOnlyList<VisibilityFact> VisibilityChain { get; init; } = [];

    /// <summary>
    /// Whether the caller holds the action being decided on at least one object the row's
    /// content is attached to — the fact behind the attachment built-in. Resolving it
    /// needs storage, which the pure rule cannot reach, so whoever asks the question
    /// supplies the answer for the action they are asking about; a fact resolved for Read
    /// must never be handed to a Write check. False when nothing resolved it: the built-in
    /// only ever admits, so an unresolved fact costs an allow it can never disclose.
    /// </summary>
    public bool ReachedByAttachment { get; init; }

    /// <summary>Facts of a non-feature protected row (trip log, geofile, raster, view,
    /// document). Its visibility chain is just itself — nothing above it to inherit
    /// from.</summary>
    public static AccessTargetFacts Of(IProtectedEntity entity) => new()
    {
        ObjectId = entity.Id,
        OwnerUserId = entity.OwnerUserId,
        CavingGroupId = entity.CavingGroupId,
        VisibilityChain = [new VisibilityFact(entity.Visibility, entity.CavingGroupId)],
    };

    /// <summary>
    /// The Create target context: the prospective parent feature's facts (ownership
    /// built-in and subtree matching key on the parent) plus the requested caving-group
    /// binding. Everything nullable — a root-level, unbound create sees Global entries
    /// only. The created kind/type ride along so kind-narrowed Create entries can match.
    /// </summary>
    public static AccessTargetFacts ForCreate(
        Guid? parentOwnerUserId,
        Guid[] parentAncestorIds,
        Guid? requestedCavingGroupId,
        FeatureKind? createdKind = null,
        long? createdFeatureTypeId = null) => new()
    {
        OwnerUserId = parentOwnerUserId,
        AncestorIds = parentAncestorIds,
        CavingGroupId = requestedCavingGroupId,
        FeatureKind = createdKind,
        FeatureTypeId = createdFeatureTypeId,
    };
}
