// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Which features may be published — shown, positions and survey drawings and all, to somebody
/// carrying nothing. The one rule every public page asks: a followed trip's published page, a
/// photograph hung on its drawing, and the public page of a survey upload's results.
/// </summary>
/// <remarks>
/// <para>
/// Asked of the feature, not of a caller: there is no caller. A public page hands over a cave's
/// stations, its compiled drawing over a grid or its model with absolute coordinates, which is the
/// position by another name, so the only feature that may carry one is a feature under no location
/// protection at all — its own or an ancestor's. There is no obfuscated or partial form: the answer
/// is refuse, not degrade. Every surface asks again on every read, because protection is switched
/// on by somebody who has just decided a cave needs it, which is exactly when a link handed out last
/// week has to stop working.
/// </para>
/// <para>
/// Two facts have to agree, and requiring both is the point. <c>IsProtectedEffective</c> is the
/// quick one — a derived column, maintained by the write service rather than computed here. Beside
/// it the protection service is asked the same question the long way, from the roots' own
/// <c>location_protected</c> flags resolved through the ancestor arrays, with the derived column
/// deliberately not consulted. The two cannot normally disagree; where they did, a derived column
/// that had gone stale-false would otherwise publish a protected cave, and requiring both means
/// such a disagreement refuses. Asking the caller-facing exact-view predicate instead would not be a
/// second fact at all: it short-circuits on the same column.
/// </para>
/// <para>
/// A feature that is missing or soft-deleted (the ordinary filters leave it out), or a cave asked
/// for that is not a cave, is in none of these sets and is therefore not publishable.
/// </para>
/// <para>
/// One home on purpose: a second copy of this rule is how one public page comes to publish a cave
/// another refuses, the day one of them is tightened.
/// </para>
/// </remarks>
public static class FeaturePublication
{
    /// <summary>Which of the given caves may be published.</summary>
    public static Task<HashSet<Guid>> PublishableCaveIdsAsync(
        SilexGisDbContext db, FeatureProtection protection, IReadOnlyCollection<Guid> caveIds, CancellationToken ct) =>
        UnguardedIdsAsync(db, protection, caveIds, FeatureKind.Cave, ct);

    /// <summary>Whether one cave may be published now; false for a protected, missing or deleted cave.</summary>
    public static async Task<bool> PublishableCaveAsync(
        SilexGisDbContext db, FeatureProtection protection, Guid caveId, CancellationToken ct) =>
        (await PublishableCaveIdsAsync(db, protection, [caveId], ct)).Contains(caveId);

    /// <summary>
    /// Which of the given features — of any kind — carry no location protection at all: the same
    /// question asked of anything a feature can be, for a published page that hangs things linked
    /// to entrances, springs or other caves on a drawing.
    /// </summary>
    public static Task<HashSet<Guid>> UnguardedFeatureIdsAsync(
        SilexGisDbContext db, FeatureProtection protection, IReadOnlyCollection<Guid> featureIds, CancellationToken ct) =>
        UnguardedIdsAsync(db, protection, featureIds, kind: null, ct);

    /// <summary>The two facts, in one place; <paramref name="kind"/> narrows what may qualify.</summary>
    private static async Task<HashSet<Guid>> UnguardedIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> featureIds, FeatureKind? kind, CancellationToken ct)
    {
        var open = new HashSet<Guid>();
        if (featureIds.Count == 0) return open;

        var ids = featureIds.Distinct().ToList();

        // Fact one, read under the ordinary filters: a live feature the derived column calls
        // unprotected. Fact two, over the same candidates: no protection root above it,
        // resolved without that column. A feature needs both.
        var candidates = db.Features.AsNoTracking()
            .Where(f => ids.Contains(f.Id) && !f.IsProtectedEffective);
        // Applied as a second Where rather than folded into the predicate, so the query the cave
        // question asks is the one it always asked — a nullable compared inside the expression
        // would put a parameter test in the SQL for every caller.
        if (kind is { } wanted)
        {
            candidates = candidates.Where(f => f.Kind == wanted);
        }

        var byColumn = await candidates.Select(f => f.Id).ToListAsync(ct);
        if (byColumn.Count == 0) return open;

        var byAncestry = await protection.UnprotectedByAncestryIdsAsync(ids, ct);
        foreach (var id in byColumn)
        {
            if (byAncestry.Contains(id)) open.Add(id);
        }
        return open;
    }
}
