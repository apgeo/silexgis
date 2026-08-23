// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
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
    /// <remarks>
    /// The facts an anchored row contributes are its access columns, never a shortened set:
    /// who owns it and who it is shown to are two of the reasons the author may hold an
    /// action there, and a fact left unbuilt is indistinguishable from a fact that is
    /// genuinely absent — an unread owner column reads as "owned by nobody" and refuses the
    /// owner.
    /// </remarks>
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

            // A feature is readable when its own audience or any ancestor's admits the
            // caller, so the chain has to carry the ancestors too: a private cave under an
            // area everybody signed in may see is readable, and an author whose read comes
            // only from that area would otherwise be refused permission to pass it on one
            // step after the guard deciding whether they may administer the cave at all had
            // admitted them on exactly that reading.
            var ancestorsAbove = feature.AncestorIds.Where(id => id != feature.Id).ToArray();
            List<VisibilityFact> ancestorAudience = ancestorsAbove.Length == 0
                ? []
                : await db.Features.AsNoTracking()
                    .Where(a => ancestorsAbove.Contains(a.Id))
                    .Select(a => new VisibilityFact(a.Visibility, a.CavingGroupId))
                    .ToListAsync(ct);

            return new AccessTargetFacts
            {
                ObjectId = feature.Id,
                OwnerUserId = feature.OwnerUserId,
                CavingGroupId = feature.CavingGroupId,
                AncestorIds = feature.AncestorIds,
                FeatureKind = feature.Kind,
                FeatureTypeId = feature.FeatureTypeId,
                FeatureSetIds = setIds,
                VisibilityChain =
                [
                    new VisibilityFact(feature.Visibility, feature.CavingGroupId),
                    .. ancestorAudience,
                ],
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
            // shelf it sits on. Its own access columns ride along for the same reason they
            // do everywhere else — a document's owner holds every action on it by owning
            // it, and its audience can admit a read.
            var filedUnder = await db.CabinetDocuments.AsNoTracking()
                .Where(m => m.DocumentId == documentId)
                .Join(db.Cabinets.AsNoTracking(), m => m.CabinetId, c => c.Id, (_, c) => c.AncestorIds)
                .ToListAsync(ct);
            Guid[] cabinetIds = [.. filedUnder.SelectMany(ids => ids).Distinct()];

            // Read past the soft-delete filter, as the feature anchor above is: a rule the
            // author carries over a document that has been sent to the bin still binds
            // them, and reading nothing there would quietly turn that rule off.
            var documentColumns = await AccessColumnsAsync(
                db.Documents.IgnoreQueryFilters(), documentId, ct);
            return documentColumns is null
                ? new AccessTargetFacts { ObjectId = documentId, CabinetIds = cabinetIds }
                : AccessTargetFacts.Of(documentColumns) with { CabinetIds = cabinetIds };
        }

        if (entry.ScopeKind == AccessScopeKind.Object && entry.ScopeId is { } objectId)
        {
            // Everything anchored on a row that is neither a feature nor a document. Some
            // of those kinds carry the owner/club/audience columns, and most of what an
            // author holds over such a row is made of them: its owner holds every action on
            // it by ownership alone, and its audience can admit a read. Naming only the id
            // hid both, and the author of a trip was refused permission to share their own
            // trip one step after the guard deciding whether they may manage it at all had
            // let them through on exactly that ownership. The remaining kinds a rule may be
            // anchored on here — a stored file, a club, a ruleset, a feature set — carry no
            // owner and no audience column, so an id really is all there is to say.
            var columns = entry.Domain switch
            {
                AccessDomain.TripLogs => await AccessColumnsAsync(db.TripLogs, objectId, ct),
                AccessDomain.Geofiles => await AccessColumnsAsync(db.Geofiles, objectId, ct),
                AccessDomain.GeoreferencedMaps => await AccessColumnsAsync(db.GeoreferencedMaps, objectId, ct),
                AccessDomain.MapViews => await AccessColumnsAsync(db.MapViews, objectId, ct),
                AccessDomain.Expeditions => await AccessColumnsAsync(db.Expeditions, objectId, ct),
                AccessDomain.Checklists => await AccessColumnsAsync(db.Checklists, objectId, ct),
                _ => null,
            };

            return columns is null
                ? new AccessTargetFacts { ObjectId = objectId }
                : AccessTargetFacts.Of(columns);
        }

        return entry.ScopeKind switch
        {
            AccessScopeKind.CavingGroup => new AccessTargetFacts { CavingGroupId = entry.ScopeId },
            _ => null,
        };
    }

    /// <summary>
    /// One row's access columns and nothing else — read rather than materialized whole
    /// because the rows behind them carry geometry and whole written reports this decision
    /// has no use for. Null when the row is genuinely gone — which a rule outlives, since
    /// deleting what a rule is anchored on does not delete the rule — and the id-only facts
    /// that result can only refuse, so nothing is admitted on the strength of a row nobody
    /// could read.
    /// </summary>
    private static Task<ProtectedColumns?> AccessColumnsAsync<TRow>(
        IQueryable<TRow> rows, Guid id, CancellationToken ct)
        where TRow : class, IProtectedEntity =>
        rows.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new ProtectedColumns
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                CavingGroupId = row.CavingGroupId,
                Visibility = row.Visibility,
            })
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The access columns of a protected row, carried in the shape the fact builder every
    /// other caller uses already accepts — so the facts of an anchored row are described in
    /// one place rather than two that can drift apart.
    /// </summary>
    private sealed class ProtectedColumns : IProtectedEntity
    {
        public required Guid Id { get; init; }

        public Guid OwnerUserId { get; set; }

        public Guid? CavingGroupId { get; set; }

        public Visibility Visibility { get; set; }
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
                AccessDomain.Expeditions => await db.Expeditions.AnyAsync(x => x.Id == scopeId, ct),
                AccessDomain.Checklists => await db.Checklists.AnyAsync(x => x.Id == scopeId, ct),
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
