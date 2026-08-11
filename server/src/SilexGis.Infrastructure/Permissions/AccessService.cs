// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// <see cref="IAccessService"/> backed by the database: loads a target's facts —
/// ancestor visibility chain, feature-set membership — and delegates every decision to
/// the pure <see cref="AccessEvaluator"/>. No rule lives here; this class only fetches
/// what the rule needs.
/// </summary>
public sealed class AccessService(SilexGisDbContext db) : IAccessService
{
    public async Task<AccessDecision> DecideAsync(
        AccessContext? ctx, AccessAction action, IProtectedEntity entity, CancellationToken ct = default)
    {
        if (ctx is null)
        {
            return AccessDecision.AnonymousDeny;
        }

        if (ctx.IsFullAdmin)
        {
            return AccessDecision.FullAdministratorAllow;
        }

        var facts = await FactsOfAsync(entity, ct);
        return AccessEvaluator.Decide(ctx, AccessDomains.Of(entity), action, facts);
    }

    public async Task<AccessAction> EffectiveAsync(
        AccessContext? ctx, IProtectedEntity entity, CancellationToken ct = default)
    {
        if (ctx is null)
        {
            return AccessAction.None;
        }

        if (ctx.IsFullAdmin)
        {
            return AccessActions.Everything;
        }

        var domain = AccessDomains.Of(entity);
        var facts = await FactsOfAsync(entity, ct);
        var effective = AccessAction.None;
        foreach (var action in AccessActions.All)
        {
            if (AccessEvaluator.Decide(ctx, domain, action, facts).Allowed)
            {
                effective |= action;
            }
        }

        return effective;
    }

    public async Task<AccessTargetFacts> FactsOfAsync(IProtectedEntity entity, CancellationToken ct = default) =>
        (await FactsOfManyAsync([entity], ct))[entity.Id];

    public async Task<IReadOnlyDictionary<Guid, AccessTargetFacts>> FactsOfManyAsync(
        IReadOnlyCollection<IProtectedEntity> entities, CancellationToken ct = default)
    {
        var rows = entities.DistinctBy(e => e.Id).ToList();
        if (rows.Count == 0)
        {
            return new Dictionary<Guid, AccessTargetFacts>();
        }

        var documents = rows.OfType<Document>().ToList();
        var features = rows.OfType<Feature>().ToList();

        // Filing lives in a join table, so the reach a cabinet entry matches on has to be
        // read: every cabinet a document sits in, plus their ancestors, because a cabinet
        // entry covers everything below it. One query for the whole set.
        var cabinetsByDocument = new Dictionary<Guid, Guid[]>();
        if (documents.Count > 0)
        {
            var documentIds = documents.Select(d => d.Id).ToList();
            var filings = await db.CabinetDocuments.AsNoTracking()
                .Where(m => documentIds.Contains(m.DocumentId))
                .Join(
                    db.Cabinets.AsNoTracking(),
                    m => m.CabinetId,
                    c => c.Id,
                    (m, c) => new { m.DocumentId, c.AncestorIds })
                .ToListAsync(ct);
            cabinetsByDocument = filings
                .GroupBy(f => f.DocumentId)
                .ToDictionary(g => g.Key, g => g.SelectMany(f => f.AncestorIds).Distinct().ToArray());
        }

        var ancestorAudience = new Dictionary<Guid, VisibilityFact>();
        var setsByFeature = new Dictionary<Guid, Guid[]>();
        if (features.Count > 0)
        {
            // The audience of every ancestor of every feature in the set, in one query;
            // an ancestor that is missing or soft-deleted simply contributes no link to
            // the chain, exactly as the per-row filter left it out.
            var ancestorsAbove = features
                .SelectMany(f => f.AncestorIds.Where(id => id != f.Id))
                .Distinct()
                .ToArray();
            if (ancestorsAbove.Length > 0)
            {
                ancestorAudience = (await db.Features.AsNoTracking()
                        .Where(a => ancestorsAbove.Contains(a.Id))
                        .Select(a => new { a.Id, a.Visibility, a.CavingGroupId })
                        .ToListAsync(ct))
                    .ToDictionary(a => a.Id, a => new VisibilityFact(a.Visibility, a.CavingGroupId));
            }

            var featureIds = features.Select(f => f.Id).ToList();
            setsByFeature = (await db.FeatureSetMembers.AsNoTracking()
                    .Where(m => featureIds.Contains(m.FeatureId))
                    .Select(m => new { m.FeatureId, m.FeatureSetId })
                    .ToListAsync(ct))
                .GroupBy(m => m.FeatureId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.FeatureSetId).ToArray());
        }

        var facts = new Dictionary<Guid, AccessTargetFacts>(rows.Count);
        foreach (var entity in rows)
        {
            facts[entity.Id] = entity switch
            {
                Document => AccessTargetFacts.Of(entity) with
                {
                    CabinetIds = cabinetsByDocument.GetValueOrDefault(entity.Id, []),
                },
                Feature feature => new AccessTargetFacts
                {
                    ObjectId = feature.Id,
                    OwnerUserId = feature.OwnerUserId,
                    CavingGroupId = feature.CavingGroupId,
                    AncestorIds = feature.AncestorIds,
                    FeatureKind = feature.Kind,
                    FeatureTypeId = feature.FeatureTypeId,
                    FeatureSetIds = setsByFeature.GetValueOrDefault(feature.Id, []),

                    // The chain starts with the row's own (possibly not-yet-saved) values
                    // so a decision mid-edit sees what the caller is writing, then the
                    // stored ancestors.
                    VisibilityChain =
                    [
                        new VisibilityFact(feature.Visibility, feature.CavingGroupId),
                        .. feature.AncestorIds
                            .Where(id => id != feature.Id && ancestorAudience.ContainsKey(id))
                            .Select(id => ancestorAudience[id]),
                    ],
                },
                _ => AccessTargetFacts.Of(entity),
            };
        }

        return facts;
    }

    public async Task<HashSet<Guid>> ViewExactLocationRootIdsAsync(
        AccessContext? ctx, IReadOnlyCollection<Guid> protectionRootIds, CancellationToken ct = default)
    {
        if (ctx is null || protectionRootIds.Count == 0)
        {
            return [];
        }

        if (ctx.IsFullAdmin)
        {
            return [.. protectionRootIds];
        }

        var ids = protectionRootIds.Distinct().ToArray();

        // Soft-deleted roots still veto, so the root facts ignore the delete filter.
        var roots = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.OwnerUserId, f.CavingGroupId, f.AncestorIds, f.Kind, f.FeatureTypeId })
            .ToListAsync(ct);

        // Set membership matters only when a set-scoped VEL entry reaches the caller.
        var velSet = ctx.For(AccessDomain.Features, AccessAction.ViewExactLocation);
        var setsByRoot = new Dictionary<Guid, Guid[]>();
        if (velSet.DenySetIds.Length > 0 || velSet.AllowSetIds.Length > 0)
        {
            var memberships = await db.FeatureSetMembers.AsNoTracking()
                .Where(m => ids.Contains(m.FeatureId))
                .ToListAsync(ct);
            setsByRoot = memberships
                .GroupBy(m => m.FeatureId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.FeatureSetId).ToArray());
        }

        var granted = new HashSet<Guid>();
        foreach (var root in roots)
        {
            var facts = new AccessTargetFacts
            {
                ObjectId = root.Id,
                OwnerUserId = root.OwnerUserId,
                CavingGroupId = root.CavingGroupId,
                AncestorIds = root.AncestorIds,
                FeatureKind = root.Kind,
                FeatureTypeId = root.FeatureTypeId,
                FeatureSetIds = setsByRoot.GetValueOrDefault(root.Id, []),
            };
            if (AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.ViewExactLocation, facts).Allowed)
            {
                granted.Add(root.Id);
            }
        }

        return granted;
    }
}
