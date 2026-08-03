// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>One association to decide about, under whatever key the caller wants back.</summary>
/// <param name="Id">The caller's own handle — an attachment id, a link id, a row index.</param>
public readonly record struct AssociationCandidate(Guid Id, FeatureAssociation Association);

/// <summary>
/// Batch evaluation of the association-disclosure rule. The decision itself lives in Domain
/// (<see cref="AssociationProtection.IsWithheld"/>); this class resolves the two facts the
/// rule cannot fetch for itself — which of the named features the caller may place exactly,
/// and what the installation has decided about revealing associations — and applies the rule
/// once per candidate in memory.
/// </summary>
/// <remarks>
/// The exact-view answer comes from <see cref="FeatureProtection"/> rather than a second walk
/// of its own: an association is withheld under exactly the condition the coordinates are, and
/// two implementations of that condition would eventually disagree about one feature. One read
/// of the setting and one batched protection query serve a whole list, so a page of attachments
/// costs the same two round trips as a single one.
/// </remarks>
public sealed class AssociationDisclosure(FeatureProtection protection, IAppSettingsService settings)
{
    /// <summary>
    /// Of the given associations, the ids whose association must be kept from the caller. The
    /// document behind each one is unaffected: this answers only whether the caller is told
    /// what it points at.
    /// </summary>
    public async Task<HashSet<Guid>> WithheldIdsAsync(
        AccessContext? ctx, IReadOnlyCollection<AssociationCandidate> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var targetIds = candidates
            .Select(c => c.Association.TargetFeatureId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        if (targetIds.Count == 0)
        {
            // Nothing positioned is named anywhere in the batch, so neither the protection
            // walk nor the setting can change an answer.
            return [];
        }

        var exact = await protection.ExactViewIdsAsync(ctx, targetIds, ct);
        var reveal = (await settings.GetProtectionAsync(ct)).RevealProtectedAssociations;

        return
        [
            .. candidates
                .Where(c => AssociationProtection.IsWithheld(
                    c.Association,
                    c.Association.TargetFeatureId is { } target && exact.Contains(target),
                    reveal))
                .Select(c => c.Id),
        ];
    }

    /// <summary>Single-association variant for detail paths.</summary>
    public async Task<bool> IsWithheldAsync(
        AccessContext? ctx, FeatureAssociation association, CancellationToken ct = default) =>
        (await WithheldIdsAsync(ctx, [new AssociationCandidate(Guid.Empty, association)], ct)).Count > 0;
}
