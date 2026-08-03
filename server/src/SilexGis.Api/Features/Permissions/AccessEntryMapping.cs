// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// Wire ↔ storage for access entries, and the write-time gate every rule passes: the
/// scope-validity table, the existence of whatever the rule is anchored on, and the
/// no-amplification bound. Both rule surfaces — the ruleset editor and the per-object
/// tab — go through here, so neither can be the lenient one.
/// </summary>
public static class AccessEntryMapping
{
    public static AccessEntry ToEntity(AccessEntryWrite write, Guid grantedBy)
    {
        // The wire carries one anchor; storage keeps features in a real FK column and
        // everything else in the id column. Routing it here is what lets an editor stay
        // ignorant of that split.
        var anchorsOnFeature = write.Domain == AccessDomain.Features
            && write.ScopeKind is AccessScopeKind.Subtree or AccessScopeKind.Object;
        return new AccessEntry
        {
            Effect = write.Effect,
            Domain = write.Domain,
            Actions = write.Actions,
            ScopeKind = write.ScopeKind,
            ScopeFeatureId = anchorsOnFeature ? write.ScopeId : null,
            ScopeId = anchorsOnFeature ? null : write.ScopeId,
            FeatureKind = write.FeatureKind,
            FeatureTypeId = write.FeatureTypeId,
            GrantedBy = grantedBy,
        };
    }

    /// <summary>
    /// Returns the problem this rule must be refused with, or null when it may be saved.
    /// </summary>
    public static async Task<ProblemHttpResult?> RejectAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        AccessEntry entry,
        CancellationToken ct)
    {
        var snapshot = entry.ToSnapshot();

        if (AccessEntryRules.Validate(snapshot) is { } invalid)
        {
            return ApiProblems.BadRequest(invalid,
                "That combination of domain, actions and scope cannot be evaluated.");
        }

        if (!await AnchorExistsAsync(db, entry, ct))
        {
            return ApiProblems.BadRequest("access_entry.anchor_not_found",
                "What this rule is scoped to does not exist.");
        }

        // A deny naming Full Administrators would read as though it could reach them; it
        // cannot, because membership decides before any rule is consulted. Refusing it
        // keeps the model honest rather than quietly inert.
        if (entry.Effect == AccessEffect.Deny
            && entry.Domain == AccessDomain.PermissionGroups
            && entry.ScopeKind == AccessScopeKind.Object
            && entry.ScopeId is { } targetGroupId
            && await db.PermissionGroups.AnyAsync(
                g => g.Id == targetGroupId
                    && g.Slug == SeededPermissionGroups.FullAdministratorsSlug, ct))
        {
            return ApiProblems.Conflict(PermissionGroupEndpoints.DenyFullAdministratorsCode,
                "Full Administrators cannot be denied — its membership decides before any rule.");
        }

        var facts = await AnchorFactsAsync(db, entry, ct);
        if (AccessEntryRules.ExceededActions(ctx, snapshot, facts) != AccessAction.None)
        {
            return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
        }

        return null;
    }

    /// <summary>
    /// The context the no-amplification check evaluates the author against: the anchored
    /// object's own facts where there is one, otherwise nothing — which is exactly how a
    /// domain-wide rule is judged. Wherever the anchor sits in a hierarchy, the facts carry
    /// the <em>whole</em> ancestry, so a rule of the author's naming anything above the
    /// anchor still binds them — both the allow that lets them delegate below it and the
    /// deny that must keep them out.
    /// </summary>
    public static async Task<AccessTargetFacts?> AnchorFactsAsync(
        SilexGisDbContext db, AccessEntry entry, CancellationToken ct)
    {
        if (entry.ScopeFeatureId is { } featureId)
        {
            var feature = await db.Features.AsNoTracking().IgnoreQueryFilters()
                .Where(f => f.Id == featureId)
                .Select(f => new { f.Id, f.OwnerUserId, f.CavingGroupId, f.AncestorIds, f.Kind, f.FeatureTypeId, f.Visibility })
                .FirstOrDefaultAsync(ct);
            if (feature is null)
            {
                return null;
            }

            var setIds = await db.FeatureSetMembers.AsNoTracking()
                .Where(m => m.FeatureId == featureId).Select(m => m.FeatureSetId).ToArrayAsync(ct);
            return new AccessTargetFacts
            {
                ObjectId = feature.Id,
                OwnerUserId = feature.OwnerUserId,
                CavingGroupId = feature.CavingGroupId,
                AncestorIds = feature.AncestorIds,
                FeatureKind = feature.Kind,
                FeatureTypeId = feature.FeatureTypeId,
                FeatureSetIds = setIds,
                VisibilityChain = [new VisibilityFact(feature.Visibility, feature.CavingGroupId)],
            };
        }

        if (entry.ScopeKind == AccessScopeKind.Cabinet && entry.ScopeId is { } cabinetId)
        {
            // Judged with the cabinet's whole ancestry — the same value a document filed
            // there is judged with, and the same one the filing tree itself is administered
            // with. Naming only the cabinet would break the rule in both directions: a deny
            // the author carries on an archive would go unseen when they author on a shelf
            // inside it (amplification), and an author whose only right is on that archive
            // would be refused on its shelves (delegation they hold everywhere else). It
            // cannot amplify: the author still has to hold the action there, and facts
            // naming only cabinets let no object-level or ownership rule of theirs match.
            var ancestry = await db.Cabinets.AsNoTracking()
                .Where(c => c.Id == cabinetId)
                .Select(c => c.AncestorIds)
                .FirstOrDefaultAsync(ct);
            return ancestry is null ? null : new AccessTargetFacts { CabinetIds = ancestry };
        }

        if (entry.ScopeKind == AccessScopeKind.Object
            && entry.Domain == AccessDomain.Documents
            && entry.ScopeId is { } documentId)
        {
            // Where a document is filed is part of what the author holds over it, so it
            // rides along here too: without it a rule denying an archive could be walked
            // around one document at a time, by anchoring on the document instead of the
            // shelf it sits on.
            var filedUnder = await db.CabinetDocuments.AsNoTracking()
                .Where(m => m.DocumentId == documentId)
                .Join(db.Cabinets.AsNoTracking(), m => m.CabinetId, c => c.Id, (_, c) => c.AncestorIds)
                .ToListAsync(ct);
            return new AccessTargetFacts
            {
                ObjectId = documentId,
                CabinetIds = [.. filedUnder.SelectMany(ids => ids).Distinct()],
            };
        }

        return entry.ScopeKind switch
        {
            AccessScopeKind.CavingGroup => new AccessTargetFacts { CavingGroupId = entry.ScopeId },
            AccessScopeKind.Object => new AccessTargetFacts { ObjectId = entry.ScopeId },
            _ => null,
        };
    }

    private static async Task<bool> AnchorExistsAsync(
        SilexGisDbContext db, AccessEntry entry, CancellationToken ct)
    {
        if (entry.ScopeFeatureId is { } featureId)
        {
            return await db.Features.AnyAsync(f => f.Id == featureId, ct);
        }

        if (entry.FeatureTypeId is { } typeId
            && !await db.FeatureTypes.AnyAsync(t => t.Id == typeId, ct))
        {
            return false;
        }

        if (entry.ScopeId is not { } scopeId)
        {
            return true;
        }

        return entry.ScopeKind switch
        {
            AccessScopeKind.CavingGroup => await db.CavingGroups.AnyAsync(g => g.Id == scopeId, ct),
            AccessScopeKind.FeatureSet => await db.FeatureSets.AnyAsync(s => s.Id == scopeId, ct),
            AccessScopeKind.Cabinet => await db.Cabinets.AnyAsync(c => c.Id == scopeId, ct),
            AccessScopeKind.Object => entry.Domain switch
            {
                AccessDomain.TripLogs => await db.TripLogs.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.Geofiles => await db.Geofiles.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.GeoreferencedMaps => await db.GeoreferencedMaps.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.MapViews => await db.MapViews.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.Files => await db.StoredFiles.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.Documents => await db.Documents.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.CavingGroups => await db.CavingGroups.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.PermissionGroups => await db.PermissionGroups.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.FeatureSets => await db.FeatureSets.AnyAsync(x => x.Id == scopeId, ct),
                _ => true,
            },
            _ => true,
        };
    }

    /// <summary>
    /// Projects rules for display, naming each anchor where the caller may read it. The
    /// id is always served — it is the caller's own rule they are looking at — but a
    /// name is only ever resolved through the same filters that govern the thing named.
    /// </summary>
    public static async Task<List<AccessEntryDto>> ProjectAsync(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyList<AccessEntry> entries, CancellationToken ct)
    {
        var featureIds = entries.Where(e => e.ScopeFeatureId is not null)
            .Select(e => e.ScopeFeatureId!.Value).Distinct().ToArray();
        var featureNames = featureIds.Length == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => featureIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name ?? f.Kind.ToString(), ct);

        var groupIds = entries.Where(e => e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId is not null)
            .Select(e => e.ScopeId!.Value).Distinct().ToArray();
        var groupNames = groupIds.Length == 0
            ? []
            : await db.CavingGroups.AsNoTracking()
                .Where(g => groupIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var setIds = entries.Where(e => e.ScopeKind == AccessScopeKind.FeatureSet && e.ScopeId is not null)
            .Select(e => e.ScopeId!.Value).Distinct().ToArray();
        var setNames = setIds.Length == 0
            ? []
            : await db.FeatureSets.AsNoTracking()
                .Where(s => setIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var cabinetIds = entries.Where(e => e.ScopeKind == AccessScopeKind.Cabinet && e.ScopeId is not null)
            .Select(e => e.ScopeId!.Value).Distinct().ToArray();
        var cabinetNames = cabinetIds.Length == 0
            ? []
            : await db.Cabinets.AsNoTracking()
                .Where(c => cabinetIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return
        [
            .. entries.Select(e =>
            {
                var anchor = e.ScopeFeatureId ?? e.ScopeId;
                var label = e.ScopeFeatureId is { } fid
                    ? featureNames.GetValueOrDefault(fid)
                    : e.ScopeKind switch
                    {
                        AccessScopeKind.CavingGroup when e.ScopeId is { } gid => groupNames.GetValueOrDefault(gid),
                        AccessScopeKind.FeatureSet when e.ScopeId is { } sid => setNames.GetValueOrDefault(sid),
                        AccessScopeKind.Cabinet when e.ScopeId is { } cid => cabinetNames.GetValueOrDefault(cid),
                        _ => null,
                    };
                return new AccessEntryDto(
                    e.Id, e.Effect, e.Domain, e.Actions, e.ScopeKind, anchor, label,
                    e.FeatureKind, e.FeatureTypeId);
            }),
        ];
    }
}
