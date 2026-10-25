// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// Who may put a deleted feature back. One home, because a deletion is undone by more than one
/// act — somebody restoring it from the deleted list, and the undo of the upload that deleted it —
/// and the right that deletes a row has to be the right that restores it on every one of them.
/// </summary>
public static class FeatureRestoreRights
{
    /// <summary>
    /// Of the given features, the ones this caller may put back: the ones they could delete.
    /// Reading is the caller's to have established already — it is asked in the database, of the
    /// feature's audience as it stood before the deletion.
    /// </summary>
    /// <remarks>
    /// Decided by the same evaluator every guard uses, over each row's own facts. An entrance is
    /// also deleted from its cave's page by whoever may edit the cave, so that right on the cave
    /// restores it too. The cave is read past the filter because an entrance deleted before its
    /// cave was is asked about here as well — and is then refused for the cave being in the way,
    /// which is the answer that says what to do, rather than for a right its caller does hold.
    /// </remarks>
    public static async Task<HashSet<Guid>> MayRestoreAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<Feature> features,
        CancellationToken ct)
    {
        if (ctx.IsFullAdmin || features.Count == 0)
        {
            return [.. features.Select(f => f.Id)];
        }

        var facts = await access.FactsOfManyAsync([.. features], ct);
        var allowed = features
            .Where(f => AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Delete, facts[f.Id]).Allowed)
            .Select(f => f.Id)
            .ToHashSet();

        var entranceIds = features
            .Where(f => f.Kind == FeatureKind.CaveEntrance && !allowed.Contains(f.Id))
            .Select(f => f.Id)
            .ToArray();
        if (entranceIds.Length == 0)
        {
            return allowed;
        }

        var caveOf = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => entranceIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.CaveFeatureId, ct);
        var caveIds = caveOf.Values.Distinct().ToArray();
        var caves = await AccessShapesAsync(db, caveIds, ct);
        var caveFacts = await access.FactsOfManyAsync([.. caves], ct);
        foreach (var (entranceId, caveId) in caveOf)
        {
            if (caveFacts.TryGetValue(caveId, out var cave)
                && AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Read, cave).Allowed
                && AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Write, cave).Allowed)
            {
                allowed.Add(entranceId);
            }
        }

        return allowed;
    }

    /// <summary>
    /// Whether this caller may put one deleted feature back: whether they could read it as it
    /// stood the moment before it was deleted, and could delete it.
    /// </summary>
    public static async Task<bool> MayRestoreOneAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid featureId, CancellationToken ct)
    {
        var past = db.Features.AsNoTracking().IgnoreQueryFilters();
        if (!await past.Where(f => f.Id == featureId).VisibleTo(ctx, past, db.FeatureSetMembers).AnyAsync(ct))
        {
            return false;
        }

        var shape = await AccessShapesAsync(db, [featureId], ct);
        return shape.Count == 1 && (await MayRestoreAsync(db, access, ctx, shape, ct)).Contains(featureId);
    }

    // The columns access is decided on, read past the filter and without the position, which a
    // question about rights has no use for.
    private static async Task<List<Feature>> AccessShapesAsync(SilexGisDbContext db, Guid[] ids, CancellationToken ct)
    {
        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                f.Kind,
                f.FeatureTypeId,
                f.OwnerUserId,
                f.CavingGroupId,
                f.Visibility,
                f.AncestorIds,
            })
            .ToListAsync(ct);
        return
        [
            .. rows.Select(r => new Feature
            {
                Id = r.Id,
                Kind = r.Kind,
                FeatureTypeId = r.FeatureTypeId,
                OwnerUserId = r.OwnerUserId,
                CavingGroupId = r.CavingGroupId,
                Visibility = r.Visibility,
                AncestorIds = r.AncestorIds,
            }),
        ];
    }
}
