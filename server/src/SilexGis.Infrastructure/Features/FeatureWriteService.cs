// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>One requested containment edge for a feature.</summary>
public readonly record struct ParentSpec(Guid ParentId, bool IsPrimary);

/// <summary>A rejected feature write, carrying user-presentable reasons.</summary>
public sealed class FeatureWriteException(string code, IReadOnlyList<string> errors)
    : InvalidOperationException($"{code}: {string.Join("; ", errors)}")
{
    public string Code { get; } = code;

    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// The single mutator of the feature aggregate's derived state: containment edges, the
/// ancestor closure and per-row ancestor arrays, effective protection, subtree
/// soft-delete stamping, the cave mirror (entrance count + main-entrance point) and the
/// default-centerline flag. Endpoint code edits plain attribute columns on tracked
/// entities but MUST route every operation below through this service — the integrity
/// verifier re-checks everything it maintains, and an architecture test pins the
/// convention.
///
/// Hierarchy semantics live in <see cref="FeatureHierarchyRules"/> (pure, unit-tested);
/// this class loads state, delegates, and persists. Recomputation loads the full edge
/// set — a two-column scan that stays trivial at this installation scale and keeps
/// every read path free of recursion.
///
/// Every state read here merges the database with the change tracker: callers (seeders,
/// endpoints, imports) legitimately batch several operations into one SaveChanges, and
/// a cycle check or ancestor recompute that missed a pending edge would corrupt the
/// security-bearing derived state.
/// </summary>
public sealed class FeatureWriteService(
    SilexGisDbContext db, ITypedPropertiesValidator propertiesValidator, ICurrentUser currentUser)
{
    // ---------- creation ----------

    public async Task<Feature> CreateGenericAsync(
        Feature feature, IReadOnlyList<ParentSpec> parents, CancellationToken ct = default)
    {
        feature.Kind = FeatureKind.Generic;
        var featureType = await RequireFeatureTypeAsync(feature, ct);
        feature.Category = featureType.Category;
        ValidateGeometry(feature.Geom, featureType);
        if (featureType.RequiresParent && parents.Count == 0)
        {
            throw new FeatureWriteException("feature.parent_required",
                [$"kind '{featureType.Code}' only exists inside a containing feature"]);
        }

        await ValidatePropertiesAsync(feature, featureType, ct);
        db.Features.Add(feature);
        await SetParentsCoreAsync(feature.Id, parents, ct);
        return feature;
    }

    public async Task<Feature> CreateCaveAsync(
        Feature feature, Cave cave, IReadOnlyList<ParentSpec> parents, CancellationToken ct = default)
    {
        feature.Kind = FeatureKind.Cave;
        feature.Category = FeatureCategory.Underground;
        cave.Id = feature.Id;
        db.Features.Add(feature);
        db.Caves.Add(cave);
        db.Entry(cave).Property("Kind").CurrentValue = FeatureKind.Cave;
        await SetParentsCoreAsync(feature.Id, parents, ct);
        return feature;
    }

    /// <summary>
    /// Creates an entrance under its cave. The structural cave FK and the primary
    /// containment edge are the same fact — this is the only place both are written.
    /// Owner and caving-group binding default from the cave (a creation-time default,
    /// editable afterwards — never re-synced); visibility starts Private because
    /// read-time inheritance over the ancestor chain is what shows the child.
    /// </summary>
    public async Task<Feature> CreateEntranceAsync(
        Feature feature, CaveEntrance entrance, CancellationToken ct = default)
    {
        feature.Kind = FeatureKind.CaveEntrance;
        feature.Category = FeatureCategory.Surface;
        if (feature.Geom is not Point)
        {
            throw new FeatureWriteException("feature.geometry_invalid", ["an entrance carries a point geometry"]);
        }

        entrance.Id = feature.Id;
        await InheritCaveBindingAsync(feature, entrance.CaveFeatureId, ct);
        db.Features.Add(feature);
        db.CaveEntrances.Add(entrance);
        db.Entry(entrance).Property("Kind").CurrentValue = FeatureKind.CaveEntrance;
        await SetParentsCoreAsync(
            feature.Id, [new ParentSpec(entrance.CaveFeatureId, IsPrimary: true)], ct);
        await SyncCaveMirrorAsync(entrance.CaveFeatureId, ct);
        return feature;
    }

    public async Task<Feature> CreateCenterlineAsync(
        Feature feature, Centerline centerline, CancellationToken ct = default)
    {
        feature.Kind = FeatureKind.Centerline;
        feature.Category = FeatureCategory.Underground;
        if (feature.Geom is not MultiLineString)
        {
            throw new FeatureWriteException("feature.geometry_invalid", ["a centerline carries a MultiLineString geometry"]);
        }

        centerline.Id = feature.Id;
        // The first centerline of a cave becomes the cave's shape.
        centerline.IsDefault = centerline.IsDefault
            || (!await db.Centerlines.AnyAsync(c => c.CaveFeatureId == centerline.CaveFeatureId, ct)
                && !db.Centerlines.Local.Any(c => c.CaveFeatureId == centerline.CaveFeatureId));
        if (centerline.IsDefault)
        {
            await ClearDefaultCenterlineAsync(centerline.CaveFeatureId, exceptId: feature.Id, ct);
        }

        await InheritCaveBindingAsync(feature, centerline.CaveFeatureId, ct);
        db.Features.Add(feature);
        db.Centerlines.Add(centerline);
        db.Entry(centerline).Property("Kind").CurrentValue = FeatureKind.Centerline;
        await SetParentsCoreAsync(
            feature.Id, [new ParentSpec(centerline.CaveFeatureId, IsPrimary: true)], ct);
        return feature;
    }

    // ---------- aggregate invariants ----------

    /// <summary>
    /// Replaces a feature's containment edges. DAG rules: no cycles, no self-edges, at
    /// most one primary edge (required when any edge exists), and no empty list for a kind
    /// that only exists inside a containing feature. Recomputes the closure for the
    /// feature's whole subtree.
    /// </summary>
    public async Task SetParentsAsync(Guid featureId, IReadOnlyList<ParentSpec> parents, CancellationToken ct = default)
    {
        // Replacing the edges with none is the third door to a shape that creation and update
        // already refuse: a kind meaningless outside a container, left with no container.
        //
        // It is not a cosmetic breach of the vocabulary. Protection is inherited along
        // containment and along nothing else, so a row with no ancestors inherits from nothing:
        // a place whose exact position was governed by the protected cave above it becomes
        // readable at full precision by every caller who can see the row at all. The account
        // able to do this is the row's own owner — which, for anything an app uploaded, is the
        // uploading account by construction — so it is a door the subject of the protection
        // does not hold the key to. Guarded here rather than in the one endpoint that calls
        // this today, so a later caller inherits the refusal instead of having to remember it.
        //
        // Read this for exactly what it is: it closes the empty list, and only that. Moving the
        // same row to a different container it is allowed to name has the same effect on what it
        // inherits, and is still permitted — the exact-view check the endpoint makes is
        // satisfied by ownership, so a row's owner can re-root it out from under somebody else's
        // protected cave. Closing that means deciding that owning a row is not enough to move it
        // out of a protection root one does not otherwise hold, which is a change to the
        // protection model and not to this guard. Anyone reading this line as coverage of it
        // would be reading it wrong.
        if (parents.Count == 0)
        {
            var featureTypeId = await db.Features
                .Where(f => f.Id == featureId)
                .Select(f => f.FeatureTypeId)
                .FirstOrDefaultAsync(ct);

            // Caves, entrances and centerlines name no feature type and may be re-rooted
            // freely; a missing type is therefore not a refusal.
            if (featureTypeId is not null)
            {
                var requiredKind = await db.FeatureTypes
                    .Where(t => t.Id == featureTypeId.Value && t.RequiresParent)
                    .Select(t => t.Code)
                    .FirstOrDefaultAsync(ct);
                if (requiredKind is not null)
                {
                    throw new FeatureWriteException("feature.parent_required",
                        [$"kind '{requiredKind}' only exists inside a containing feature"]);
                }
            }
        }

        var old = await db.FeatureHierarchyEdges.Where(e => e.ChildId == featureId).ToListAsync(ct);
        db.FeatureHierarchyEdges.RemoveRange(old);
        await SetParentsCoreAsync(featureId, parents, ct);
    }

    /// <summary>Flips a protection root and restamps effective protection over its subtree.</summary>
    public async Task SetLocationProtectedAsync(Guid featureId, bool value, CancellationToken ct = default)
    {
        var feature = await FeatureByIdAsync(featureId, ct)
            ?? throw new FeatureWriteException("feature.not_found", [$"feature {featureId} does not exist"]);
        feature.LocationProtected = value;
        await RecomputeDerivedStateAsync([featureId], ct);
    }

    /// <summary>
    /// Marks a valid entrance as the cave's main one and refreshes the cave mirror
    /// (entrance count + the cave feature's representative point).
    /// </summary>
    public async Task SetMainEntranceAsync(Guid caveFeatureId, Guid entranceFeatureId, CancellationToken ct = default)
    {
        var entrances = await EntrancesOfCaveAsync(caveFeatureId, ct);
        if (entrances.All(e => e.Id != entranceFeatureId))
        {
            throw new FeatureWriteException("entrance.not_of_cave", ["the entrance does not belong to the cave"]);
        }

        foreach (var e in entrances)
        {
            e.IsMain = e.Id == entranceFeatureId;
        }

        await SyncCaveMirrorAsync(caveFeatureId, ct);
    }

    /// <summary>Marks a centerline as the cave's current shape (exactly one per cave).</summary>
    public async Task SetDefaultCenterlineAsync(Guid centerlineFeatureId, CancellationToken ct = default)
    {
        var centerline = await db.Centerlines.FirstAsync(c => c.Id == centerlineFeatureId, ct);
        await ClearDefaultCenterlineAsync(centerline.CaveFeatureId, exceptId: centerlineFeatureId, ct);
        centerline.IsDefault = true;
    }

    /// <summary>
    /// Refreshes the cave's derived mirror: entrance count and the representative point
    /// (main entrance's geometry, Z included). Call after any entrance change.
    /// </summary>
    public async Task SyncCaveMirrorAsync(Guid caveFeatureId, CancellationToken ct = default)
    {
        var caveFeature = await FeatureByIdAsync(caveFeatureId, ct)
            ?? throw new FeatureWriteException("cave.not_found", [$"cave feature {caveFeatureId} does not exist"]);
        var cave = db.Caves.Local.FirstOrDefault(c => c.Id == caveFeatureId)
            ?? await db.Caves.IgnoreQueryFilters().FirstAsync(c => c.Id == caveFeatureId, ct);

        var entrances = await EntrancesOfCaveAsync(caveFeatureId, ct);
        cave.EntranceCount = entrances.Count;

        // A cave that has entrances always has exactly one main: it is the cave's anchor
        // on the map and what the detail pages label. Deleting the main entrance, or
        // demoting it, would otherwise leave the cave with none — silently, because the
        // representative point below falls back to any entrance and the cave keeps
        // plotting. The oldest survivor takes over, the same rule that makes a cave's
        // first entrance its main one; ordering by creation keeps the choice stable.
        var main = entrances.FirstOrDefault(e => e.IsMain);
        if (main is null && entrances.Count > 0)
        {
            var dated = new List<(CaveEntrance Entrance, DateTimeOffset CreatedAt)>();
            foreach (var entrance in entrances)
            {
                var feature = await FeatureByIdAsync(entrance.Id, ct);
                dated.Add((entrance, feature?.CreatedAt ?? DateTimeOffset.MaxValue));
            }

            main = dated.OrderBy(d => d.CreatedAt).ThenBy(d => d.Entrance.Id).First().Entrance;
            main.IsMain = true;
        }

        caveFeature.Geom = main is null ? null : (await FeatureByIdAsync(main.Id, ct))?.Geom;
    }

    /// <summary>
    /// Validates properties against the feature's kind schema and stamps the schema
    /// version. Kinds without a schema accept any JSON object.
    /// </summary>
    public async Task ValidatePropertiesAsync(Feature feature, CancellationToken ct = default)
    {
        var featureType = feature.FeatureTypeId is null
            ? null
            : await db.FeatureTypes.FirstOrDefaultAsync(t => t.Id == feature.FeatureTypeId, ct);
        await ValidatePropertiesAsync(feature, featureType, ct);
    }

    // ---------- lifecycle ----------

    /// <summary>Soft-deletes a feature and its whole containment subtree (one shared stamp, so restore can undo exactly this deletion).</summary>
    public async Task<DateTimeOffset> SoftDeleteAsync(Guid featureId, CancellationToken ct = default)
    {
        var stamp = DateTimeOffset.UtcNow;
        var subtree = await SubtreeIdsAsync(featureId, ct);
        var stamped = await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id) && f.DeletedAt == null)
            .Select(f => new { f.Id, f.Kind })
            .ToListAsync(ct);
        await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id) && f.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, stamp), ct);

        // ExecuteUpdate bypasses the audit interceptor; deletion events are forensics
        // the timeline must keep, so the rows are written here (caller's SaveChanges).
        db.Set<AuditEntry>().AddRange(stamped.Select(f => new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Deleted,
            EntityType = FeatureAudit.TypeName(f.Kind),
            EntityId = f.Id.ToString(),
        }));
        return stamp;
    }

    /// <summary>Restores the rows stamped by one soft deletion (children deleted earlier stay deleted).</summary>
    public async Task RestoreAsync(Guid featureId, CancellationToken ct = default)
    {
        var stamp = await db.Features.IgnoreQueryFilters()
            .Where(f => f.Id == featureId)
            .Select(f => f.DeletedAt)
            .FirstAsync(ct);
        if (stamp is null)
        {
            return;
        }

        var subtree = await SubtreeIdsAsync(featureId, ct);
        var restored = await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id) && f.DeletedAt == stamp)
            .Select(f => new { f.Id, f.Kind })
            .ToListAsync(ct);
        await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id) && f.DeletedAt == stamp)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, (DateTimeOffset?)null), ct);

        db.Set<AuditEntry>().AddRange(restored.Select(f => new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Updated,
            EntityType = FeatureAudit.TypeName(f.Kind),
            EntityId = f.Id.ToString(),
            Changes = /*lang=json,strict*/ """{"DeletedAt":{"old":"deleted","new":null}}""",
        }));
    }

    /// <summary>
    /// Hard-deletes a subtree. An explicit, audited operation (the admin purge job path)
    /// — never a side effect: FK cascades clean subtype rows, edges, closure, links,
    /// shares, feature attachments/taggings/grants and cave satellites.
    /// </summary>
    public async Task PurgeAsync(Guid featureId, CancellationToken ct = default)
    {
        var subtree = await SubtreeIdsAsync(featureId, ct);
        var purged = await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id))
            .Select(f => new { f.Id, f.Kind, f.Name })
            .ToListAsync(ct);
        await db.Features.IgnoreQueryFilters()
            .Where(f => subtree.Contains(f.Id))
            .ExecuteDeleteAsync(ct);

        db.Set<AuditEntry>().AddRange(purged.Select(f => new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Deleted,
            EntityType = FeatureAudit.TypeName(f.Kind),
            EntityId = f.Id.ToString(),
            Changes = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["Name"] = new() { ["old"] = f.Name, ["new"] = null },
                    ["Purged"] = new() { ["old"] = null, ["new"] = true },
                }),
        }));
    }

    // ---------- derived-state recomputation (the verifier re-checks all of this) ----------

    /// <summary>
    /// Recomputes closure rows, ancestor arrays and effective protection for every
    /// feature whose ancestry can be affected by the touched features — i.e. their
    /// containment subtrees.
    /// </summary>
    public async Task RecomputeDerivedStateAsync(IReadOnlyCollection<Guid> touched, CancellationToken ct = default)
    {
        var allEdges = await AllEdgesAsync(excludeChildId: null, ct);
        var parentsByChild = FeatureHierarchyRules.ParentsByChild(allEdges);
        var childrenByParent = FeatureHierarchyRules.ChildrenByParent(allEdges);

        var affected = new HashSet<Guid>();
        foreach (var id in touched)
        {
            affected.UnionWith(FeatureHierarchyRules.DescendantsOf(id, childrenByParent));
        }

        var ancestorSets = affected.ToDictionary(
            id => id,
            id => FeatureHierarchyRules.AncestorsOf(id, parentsByChild));

        // Protection flags of every id appearing in any ancestor set. Pending (tracked)
        // features override the database — an unsaved protection flip must count.
        var involved = ancestorSets.Values.SelectMany(s => s).ToHashSet();
        var protectedIds = (await db.Features.IgnoreQueryFilters()
                .Where(f => involved.Contains(f.Id) && f.LocationProtected)
                .Select(f => f.Id)
                .ToListAsync(ct))
            .ToHashSet();
        foreach (var local in db.Features.Local.Where(f => involved.Contains(f.Id)))
        {
            if (local.LocationProtected)
            {
                protectedIds.Add(local.Id);
            }
            else
            {
                protectedIds.Remove(local.Id);
            }
        }

        // Replace the affected features' closure rows — both the stored ones and any
        // pending rows an earlier recompute in this same unit of work queued.
        var oldClosure = await db.FeatureAncestors
            .Where(a => affected.Contains(a.FeatureId))
            .ToListAsync(ct);
        db.FeatureAncestors.RemoveRange(oldClosure);
        var pendingClosure = db.ChangeTracker.Entries<FeatureAncestor>()
            .Where(e => e.State == EntityState.Added && affected.Contains(e.Entity.FeatureId))
            .ToList();
        foreach (var pending in pendingClosure)
        {
            pending.State = EntityState.Detached;
        }

        db.FeatureAncestors.AddRange(ancestorSets.SelectMany(
            kv => kv.Value.Select(a => new FeatureAncestor { FeatureId = kv.Key, AncestorId = a })));

        // Stamp the affected features — database rows plus pending (unsaved) ones.
        var features = await db.Features.IgnoreQueryFilters()
            .Where(f => affected.Contains(f.Id))
            .ToListAsync(ct);
        var byId = features.ToDictionary(f => f.Id);
        foreach (var local in db.Features.Local.Where(f => affected.Contains(f.Id)))
        {
            byId[local.Id] = local;
        }

        foreach (var feature in byId.Values)
        {
            var ancestors = ancestorSets[feature.Id];
            feature.AncestorIds = [.. ancestors.OrderBy(a => a)];
            feature.IsProtectedEffective =
                FeatureHierarchyRules.IsProtectedEffective(ancestors, protectedIds.Contains);
        }
    }

    // ---------- internals ----------

    private async Task SetParentsCoreAsync(
        Guid featureId, IReadOnlyList<ParentSpec> parents, CancellationToken ct)
    {
        if (parents.Count > 0 && parents.Count(p => p.IsPrimary) != 1)
        {
            throw new FeatureWriteException("feature.primary_parent", ["exactly one parent edge must be primary"]);
        }

        if (parents.Select(p => p.ParentId).Distinct().Count() != parents.Count)
        {
            throw new FeatureWriteException("feature.parent_duplicate", ["duplicate parent"]);
        }

        // Cycle check against the edge set minus this child's edges (they are being replaced).
        var otherEdges = await AllEdgesAsync(excludeChildId: featureId, ct);
        var lookup = FeatureHierarchyRules.ParentsByChild(otherEdges);
        foreach (var parent in parents)
        {
            if (FeatureHierarchyRules.WouldCreateCycle(parent.ParentId, featureId, lookup))
            {
                throw new FeatureWriteException("feature.hierarchy_cycle",
                    [$"parent {parent.ParentId} is (or descends from) the feature itself"]);
            }
        }

        db.FeatureHierarchyEdges.AddRange(parents.Select(p => new FeatureHierarchyEdge
        {
            ParentId = p.ParentId,
            ChildId = featureId,
            IsPrimary = p.IsPrimary,
        }));

        await RecomputeDerivedStateAsync([featureId], ct);
    }

    private async Task ClearDefaultCenterlineAsync(Guid caveFeatureId, Guid exceptId, CancellationToken ct)
    {
        // One default per cave is a plain partial unique index, which PostgreSQL checks
        // per statement. Demoting and promoting as two tracked edits in the same flush
        // leaves the order to the change tracker — which sorts by primary key, not by
        // intent — so promoting a centerline whose id sorts below the current default
        // wrote the second flag while the first was still set and hit the index. Since
        // ids are time-ordered, that was exactly "go back to the earlier survey".
        // Clearing on its own statement means the flag is always free before it is taken.
        await db.Centerlines
            .Where(c => c.CaveFeatureId == caveFeatureId && c.IsDefault && c.Id != exceptId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false), ct);
    }

    // A new entrance/centerline defaults its owner and caving-group binding from the
    // cave so nobody's reach shrinks (group-scoped rules match the binding, and the
    // cave's owner keeps the ownership arm on delegated children). This is a
    // creation-time default only — the columns are the child's own afterwards, and
    // visibility starts Private because the read-time cascade over the ancestor chain
    // is what makes a child visible exactly when its cave is.
    private async Task InheritCaveBindingAsync(Feature child, Guid caveFeatureId, CancellationToken ct)
    {
        var cave = await FeatureByIdAsync(caveFeatureId, ct)
            ?? throw new FeatureWriteException("cave.not_found", [$"cave feature {caveFeatureId} does not exist"]);
        child.OwnerUserId = cave.OwnerUserId;
        child.CavingGroupId = cave.CavingGroupId;
        child.Visibility = Visibility.Private;
    }

    private async Task<FeatureType> RequireFeatureTypeAsync(Feature feature, CancellationToken ct)
    {
        var featureType = feature.FeatureTypeId is null
            ? null
            : await db.FeatureTypes.FirstOrDefaultAsync(t => t.Id == feature.FeatureTypeId, ct);
        return featureType
            ?? throw new FeatureWriteException("feature.type_required", ["a generic feature names its kind"]);
    }

    private void ValidateGeometry(Geometry? geometry, FeatureType featureType)
    {
        if (geometry is null)
        {
            return; // grouping kinds may carry no geometry (yet)
        }

        var geometryClass = GeometryClasses.Of(geometry);
        if (geometryClass is null || !featureType.AcceptedGeometryClasses.Contains(geometryClass.Value))
        {
            throw new FeatureWriteException("feature.geometry_invalid",
                [$"kind '{featureType.Code}' does not accept {geometry.GeometryType}"]);
        }
    }

    private async Task ValidatePropertiesAsync(Feature feature, FeatureType? featureType, CancellationToken ct)
    {
        if (featureType?.PropertiesSchema is null)
        {
            feature.PropertiesSchemaVersion = null;
            return;
        }

        var errors = propertiesValidator.Validate(featureType.PropertiesSchema, feature.Properties);
        if (errors.Count > 0)
        {
            throw new FeatureWriteException("feature.properties_invalid", errors);
        }

        feature.PropertiesSchemaVersion = featureType.PropertiesSchemaVersion;
        await Task.CompletedTask;
    }

    /// <summary>The stored containment subtree of a feature (closure-table read, root included).</summary>
    private async Task<HashSet<Guid>> SubtreeIdsAsync(Guid featureId, CancellationToken ct) =>
        (await db.FeatureAncestors
            .Where(a => a.AncestorId == featureId)
            .Select(a => a.FeatureId)
            .ToListAsync(ct))
        .Append(featureId)
        .ToHashSet();

    // ---------- tracker-aware state reads ----------

    /// <summary>A feature by id: pending (tracked) instance first, stored row second.</summary>
    private async Task<Feature?> FeatureByIdAsync(Guid id, CancellationToken ct) =>
        db.Features.Local.FirstOrDefault(f => f.Id == id)
        ?? await db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == id, ct);

    /// <summary>All containment edges — stored plus pending, minus pending deletions.</summary>
    private async Task<List<FeatureHierarchyRules.Edge>> AllEdgesAsync(Guid? excludeChildId, CancellationToken ct)
    {
        var stored = await db.FeatureHierarchyEdges
            .Select(e => new FeatureHierarchyRules.Edge(e.ParentId, e.ChildId))
            .ToListAsync(ct);
        var removed = db.ChangeTracker.Entries<FeatureHierarchyEdge>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => new FeatureHierarchyRules.Edge(e.Entity.ParentId, e.Entity.ChildId))
            .ToHashSet();
        var pending = db.ChangeTracker.Entries<FeatureHierarchyEdge>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .Select(e => new FeatureHierarchyRules.Edge(e.Entity.ParentId, e.Entity.ChildId));

        return stored.Concat(pending)
            .Where(e => !removed.Contains(e))
            .Where(e => excludeChildId is null || e.ChildId != excludeChildId)
            .Distinct()
            .ToList();
    }

    /// <summary>A cave's entrance rows — stored plus pending, minus pending deletions.</summary>
    private async Task<List<CaveEntrance>> EntrancesOfCaveAsync(Guid caveFeatureId, CancellationToken ct)
    {
        var stored = await db.CaveEntrances.IgnoreQueryFilters()
            .Where(e => e.CaveFeatureId == caveFeatureId)
            .ToListAsync(ct);
        var byId = stored.ToDictionary(e => e.Id);
        foreach (var entry in db.ChangeTracker.Entries<CaveEntrance>()
                     .Where(e => e.Entity.CaveFeatureId == caveFeatureId))
        {
            if (entry.State == EntityState.Deleted)
            {
                byId.Remove(entry.Entity.Id);
            }
            else
            {
                byId[entry.Entity.Id] = entry.Entity;
            }
        }

        // Soft-deleted entrances are not part of the mirror.
        var deletedFeatureIds = new HashSet<Guid>();
        foreach (var id in byId.Keys)
        {
            var feature = await FeatureByIdAsync(id, ct);
            if (feature?.DeletedAt is not null)
            {
                deletedFeatureIds.Add(id);
            }
        }

        return byId.Values.Where(e => !deletedFeatureIds.Contains(e.Id)).ToList();
    }
}
