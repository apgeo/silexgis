// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The storage-backed half of the access rule: builds a target's facts (ancestor
/// visibility chain, feature-set membership) and hands them to the pure
/// <see cref="AccessEvaluator"/>. Endpoint guards call this; list and map queries
/// compose the filter twins instead.
/// </summary>
public interface IAccessService
{
    /// <summary>Decides one action on one protected row. Null context → deny.</summary>
    Task<AccessDecision> DecideAsync(
        AccessContext? ctx, AccessAction action, IProtectedEntity entity, CancellationToken ct = default);

    /// <summary>All action flags the caller holds on the row (UI capability hints).</summary>
    Task<AccessAction> EffectiveAsync(
        AccessContext? ctx, IProtectedEntity entity, CancellationToken ct = default);

    /// <summary>The evaluated facts of one row — what the no-amplification check needs
    /// as the anchor context when authoring direct entries on it.</summary>
    Task<AccessTargetFacts> FactsOfAsync(IProtectedEntity entity, CancellationToken ct = default);

    /// <summary>
    /// The same facts for many rows at once, keyed by row id. Identical answers to asking
    /// one row at a time; the difference is the number of round trips, which stays fixed
    /// as the set grows instead of multiplying by it. A listing that decides a per-row
    /// capability needs that: the two kinds whose facts are storage-backed — features
    /// (ancestor audience chain, set membership) and documents (the cabinets that reach
    /// them) — otherwise cost two and one extra queries per row respectively. Rows of a
    /// kind whose facts are pure cost nothing here at all. Duplicate rows collapse; a row
    /// absent from the answer never happens, since the facts are built from the entity
    /// handed in rather than looked up.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AccessTargetFacts>> FactsOfManyAsync(
        IReadOnlyCollection<IProtectedEntity> entities, CancellationToken ct = default);

    /// <summary>
    /// Bulk half of the exact-location rule: of the given protection roots, the ones the
    /// caller holds ViewExactLocation on per the precedence walk. Location protection
    /// then vetoes over every root — a VEL deny can only further restrict exact view,
    /// never widen it past the protected-roots rule.
    /// </summary>
    Task<HashSet<Guid>> ViewExactLocationRootIdsAsync(
        AccessContext? ctx, IReadOnlyCollection<Guid> protectionRootIds, CancellationToken ct = default);
}
