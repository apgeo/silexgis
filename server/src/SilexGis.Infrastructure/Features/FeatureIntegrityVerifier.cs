// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>One integrity violation found by the verifier.</summary>
public sealed record IntegrityProblem(string Check, Guid FeatureId, string Detail);

/// <summary>
/// Re-derives every security-bearing piece of state the write service maintains and
/// reports divergence: the model's correctness depends on write-path discipline, and
/// this is the scheduled proof it held. Read-only — problems are reported (and logged by
/// the caller), never auto-repaired: a divergence means a write path bypassed the
/// service and must be fixed, not papered over.
/// </summary>
public sealed class FeatureIntegrityVerifier(SilexGisDbContext db)
{
    public async Task<IReadOnlyList<IntegrityProblem>> VerifyAsync(CancellationToken ct = default)
    {
        var problems = new List<IntegrityProblem>();

        var edges = await db.FeatureHierarchyEdges
            .Select(e => new FeatureHierarchyRules.Edge(e.ParentId, e.ChildId))
            .ToListAsync(ct);
        var parentsByChild = FeatureHierarchyRules.ParentsByChild(edges);

        var features = await db.Features.IgnoreQueryFilters()
            .Select(f => new { f.Id, f.Kind, f.AncestorIds, f.IsProtectedEffective, f.LocationProtected, f.DeletedAt })
            .ToListAsync(ct);
        var protectedIds = features.Where(f => f.LocationProtected).Select(f => f.Id).ToHashSet();

        var closure = (await db.FeatureAncestors.ToListAsync(ct))
            .ToLookup(a => a.FeatureId, a => a.AncestorId);

        foreach (var feature in features)
        {
            var expected = FeatureHierarchyRules.AncestorsOf(feature.Id, parentsByChild);

            if (!expected.SetEquals(feature.AncestorIds))
            {
                problems.Add(new IntegrityProblem("ancestor_ids", feature.Id,
                    $"stored [{feature.AncestorIds.Length}] != derived [{expected.Count}]"));
            }

            if (!expected.SetEquals(closure[feature.Id].ToHashSet()))
            {
                problems.Add(new IntegrityProblem("closure", feature.Id, "feature_ancestors rows diverge from edges"));
            }

            var expectedProtected = FeatureHierarchyRules.IsProtectedEffective(expected, protectedIds.Contains);
            if (feature.IsProtectedEffective != expectedProtected)
            {
                problems.Add(new IntegrityProblem("protected_effective", feature.Id,
                    $"stored {feature.IsProtectedEffective}, derived {expectedProtected}"));
            }
        }

        // A cave is two rows and only one direction of the pair is guarded: the caves row
        // carries a composite foreign key on (id, kind) into the feature row, so a caves row
        // without its feature is impossible, while a feature of kind Cave that lost — or was
        // never given — its caves row is a state nothing refuses. It is worth reporting because
        // the consequence is not local: everything cave-specific a reader is shown comes off
        // that second row, so a single broken one used to answer the whole listing with a
        // server error, for every reader of the archive rather than for whoever owns it.
        //
        // Only Cave is checked. cave_entrances and centerlines are paired with their features
        // the same way, but every read path that reaches one does so through a query the
        // database composes, where a feature missing its half simply fails to match instead of
        // failing the request — so nothing observed so far argues for reporting them, and a
        // check earns its place by the failure it catches.
        //
        // Ignoring the soft-delete filters on both sides: a soft delete stamps the feature and
        // leaves the subtype row alone, so comparing a filtered subtype set against the
        // unfiltered feature set read above would report every soft-deleted cave in the
        // installation.
        var caveSubtypeIds = (await db.Caves.IgnoreQueryFilters().Select(c => c.Id).ToListAsync(ct))
            .ToHashSet();
        foreach (var feature in features)
        {
            if (feature.Kind == FeatureKind.Cave && !caveSubtypeIds.Contains(feature.Id))
            {
                problems.Add(new IntegrityProblem("subtype_row_missing", feature.Id,
                    "a feature of kind Cave has no row in its subtype table"));
            }
        }

        // Cycles (the rules tolerate them; their presence is itself the violation).
        foreach (var feature in features)
        {
            if (parentsByChild[feature.Id].Any()
                && FeatureHierarchyRules.AncestorsOf(feature.Id, parentsByChild)
                    .Any(a => a != feature.Id && FeatureHierarchyRules.AncestorsOf(a, parentsByChild).Contains(feature.Id)
                        && parentsByChild[a].Contains(feature.Id)))
            {
                problems.Add(new IntegrityProblem("cycle", feature.Id, "feature participates in a containment cycle"));
            }
        }

        // Structural FK ↔ primary-edge mirror for entrances and centerlines.
        var primaryEdges = await db.FeatureHierarchyEdges.Where(e => e.IsPrimary)
            .ToDictionaryAsync(e => e.ChildId, e => e.ParentId, ct);
        foreach (var e in await db.CaveEntrances.IgnoreQueryFilters()
                     .Select(x => new { x.Id, x.CaveFeatureId }).ToListAsync(ct))
        {
            if (!primaryEdges.TryGetValue(e.Id, out var parent) || parent != e.CaveFeatureId)
            {
                problems.Add(new IntegrityProblem("entrance_edge_mirror", e.Id,
                    "cave FK and primary containment edge disagree"));
            }
        }

        foreach (var c in await db.Centerlines.IgnoreQueryFilters()
                     .Select(x => new { x.Id, x.CaveFeatureId }).ToListAsync(ct))
        {
            if (!primaryEdges.TryGetValue(c.Id, out var parent) || parent != c.CaveFeatureId)
            {
                problems.Add(new IntegrityProblem("centerline_edge_mirror", c.Id,
                    "cave FK and primary containment edge disagree"));
            }
        }

        // Cave mirror: entrance count and representative point. Live rows only on both
        // sides — the mirror describes what readers can see, and the write service
        // recomputes it from live entrances (a soft-deleted entrance is not counted).
        var caveMirrors = await db.Caves
            .Select(c => new
            {
                c.Id,
                c.EntranceCount,
                CaveGeom = c.Feature.Geom,
                Entrances = db.CaveEntrances
                    .Where(e => e.CaveFeatureId == c.Id)
                    .Select(e => new { e.IsMain, e.Feature.Geom })
                    .ToList(),
            })
            .ToListAsync(ct);
        foreach (var cave in caveMirrors)
        {
            if (cave.EntranceCount != cave.Entrances.Count)
            {
                problems.Add(new IntegrityProblem("entrance_count", cave.Id,
                    $"stored {cave.EntranceCount}, actual {cave.Entrances.Count}"));
            }

            var main = cave.Entrances.FirstOrDefault(e => e.IsMain) ?? cave.Entrances.FirstOrDefault();
            var expectedGeom = main?.Geom;
            if ((cave.CaveGeom is null) != (expectedGeom is null)
                || (cave.CaveGeom is not null && !cave.CaveGeom.EqualsExact(expectedGeom)))
            {
                problems.Add(new IntegrityProblem("cave_geom_mirror", cave.Id,
                    "cave feature geometry is not the main entrance's point"));
            }
        }

        // Default centerline: a cave that has any live centerline has exactly one default
        // — that one is the cave's current shape on the map. The partial unique index
        // already makes two impossible, so what this catches is the zero case: a delete
        // path that removed the default without promoting a successor would silently drop
        // the cave off the centerline overlay.
        var centerlinesByCave = (await db.Centerlines
                .Select(c => new { c.CaveFeatureId, c.IsDefault })
                .ToListAsync(ct))
            .GroupBy(c => c.CaveFeatureId);
        foreach (var cave in centerlinesByCave)
        {
            var defaults = cave.Count(c => c.IsDefault);
            if (defaults != 1)
            {
                problems.Add(new IntegrityProblem("default_centerline", cave.Key,
                    $"{cave.Count()} centerline(s), {defaults} marked default"));
            }
        }

        // Share tokens: the stored value must be a SHA-256 digest, base64url. Uniqueness
        // and the feature FK are enforced by the schema, so what is left to check is the
        // shape — a truncated, re-encoded or otherwise differently-derived value means a
        // mint path that did not go through the hash. Note this cannot prove a plaintext
        // token was never stored: the tokens are themselves 32 random bytes, so a digest
        // and a token are indistinguishable by width alone.
        foreach (var share in await db.FeatureShares.Select(s => new { s.Id, s.TokenHash }).ToListAsync(ct))
        {
            if (!Base64Url.IsValid(share.TokenHash)
                || Base64Url.DecodeFromChars(share.TokenHash).Length != SHA256.HashSizeInBytes)
            {
                problems.Add(new IntegrityProblem("share_token_hash", share.Id,
                    "token hash is not a base64url SHA-256 digest"));
            }
        }

        // FK-less polymorphic pairs: report orphans (the pair has no FK by design; each
        // owning slice cleans up transactionally — this is the promised safety net).
        problems.AddRange(await PairOrphansAsync(ct));

        // FK-less access-model anchors: a dangling scope or member id means a delete
        // flow skipped its "entries first" guard — security-bearing, because a deny
        // whose anchor silently vanished no longer denies anything.
        problems.AddRange(await AccessAnchorOrphansAsync(ct));

        // Document version sequences: the same derived state the document write service
        // maintains, re-derived here to prove no path bypassed it.
        problems.AddRange(await DocumentVersionProblemsAsync(ct));

        return problems;
    }

    /// <summary>
    /// Every document must have a well-formed version sequence — exactly one current
    /// version, unique numbers — and every version must carry at least one file. A
    /// document that lost its current version has nothing to serve; one with no files at
    /// all is a row nothing can reach.
    /// </summary>
    private async Task<List<IntegrityProblem>> DocumentVersionProblemsAsync(CancellationToken ct)
    {
        var problems = new List<IntegrityProblem>();

        var versions = await db.DocumentVersions
            .Select(v => new { v.DocumentId, State = new DocumentVersionRules.VersionState(v.Id, v.VersionNumber, v.IsCurrent) })
            .ToListAsync(ct);
        var documentIds = await db.Documents.Select(d => d.Id).ToListAsync(ct);
        var byDocument = versions.ToLookup(v => v.DocumentId, v => v.State);

        foreach (var documentId in documentIds)
        {
            foreach (var problem in DocumentVersionRules.Validate(byDocument[documentId].ToList()))
            {
                problems.Add(new IntegrityProblem("document_versions", default, $"document {documentId}: {problem}"));
            }
        }

        var fileless = await db.DocumentVersions
            .Where(v => !db.StoredFiles.Any(f => f.DocumentVersionId == v.Id))
            .Select(v => new { v.Id, v.DocumentId })
            .ToListAsync(ct);
        foreach (var version in fileless)
        {
            problems.Add(new IntegrityProblem("document_version_fileless", default,
                $"document {version.DocumentId}: version {version.Id} carries no file"));
        }

        return problems;
    }

    private async Task<List<IntegrityProblem>> AccessAnchorOrphansAsync(CancellationToken ct)
    {
        var problems = new List<IntegrityProblem>();

        var cavingGroupIds = await db.CavingGroups.Select(g => g.Id).ToHashSetAsync(ct);
        var featureSetIds = await db.FeatureSets.Select(s => s.Id).ToHashSetAsync(ct);
        var userIds = await db.Users.Select(u => u.Id).ToHashSetAsync(ct);

        bool ScopeIdExists(AccessScopeKind scope, AccessDomain domain, Guid id) =>
            scope switch
            {
                AccessScopeKind.CavingGroup => cavingGroupIds.Contains(id),
                AccessScopeKind.FeatureSet => featureSetIds.Contains(id),
                // Object scope outside the feature domain: the id lives in the domain's
                // own table; only the domains with object scopes need resolving here.
                AccessScopeKind.Object => domain switch
                {
                    AccessDomain.TripLogs => db.TripLogs.IgnoreQueryFilters().Any(x => x.Id == id),
                    AccessDomain.Geofiles => db.Geofiles.Any(x => x.Id == id),
                    AccessDomain.GeoreferencedMaps => db.GeoreferencedMaps.Any(x => x.Id == id),
                    AccessDomain.MapViews => db.MapViews.Any(x => x.Id == id),
                    AccessDomain.Files => db.StoredFiles.Any(x => x.Id == id),
                    AccessDomain.Documents => db.Documents.Any(x => x.Id == id),
                    AccessDomain.Expeditions => db.Expeditions.Any(x => x.Id == id),
                    AccessDomain.Checklists => db.Checklists.Any(x => x.Id == id),
                    AccessDomain.Events => db.Events.Any(x => x.Id == id),
                    AccessDomain.CavingGroups => cavingGroupIds.Contains(id),
                    AccessDomain.PermissionGroups => db.PermissionGroups.Any(x => x.Id == id),
                    AccessDomain.FeatureSets => featureSetIds.Contains(id),
                    _ => true,
                },
                _ => true,
            };

        foreach (var entry in await db.AccessEntries
                     .Where(e => e.ScopeId != null)
                     .Select(e => new { e.Id, e.ScopeKind, e.Domain, e.ScopeId })
                     .ToListAsync(ct))
        {
            if (!ScopeIdExists(entry.ScopeKind, entry.Domain, entry.ScopeId!.Value))
            {
                problems.Add(new IntegrityProblem("access_scope_orphan", default,
                    $"{entry.ScopeKind} anchor {entry.ScopeId} missing (entry {entry.Id})"));
            }
        }

        foreach (var subject in await db.AccessEntries
                     .Where(e => e.SubjectId != null)
                     .Select(e => new { e.Id, e.SubjectKind, e.SubjectId })
                     .ToListAsync(ct))
        {
            var exists = subject.SubjectKind == AccessSubjectKind.CavingGroup
                ? cavingGroupIds.Contains(subject.SubjectId!.Value)
                : userIds.Contains(subject.SubjectId!.Value);
            if (!exists)
            {
                problems.Add(new IntegrityProblem("access_subject_orphan", default,
                    $"{subject.SubjectKind} subject {subject.SubjectId} missing (entry {subject.Id})"));
            }
        }

        foreach (var member in await db.PermissionGroupMembers
                     .Select(m => new { m.Id, m.MemberKind, m.MemberId })
                     .ToListAsync(ct))
        {
            var exists = member.MemberKind == AccessSubjectKind.CavingGroup
                ? cavingGroupIds.Contains(member.MemberId)
                : userIds.Contains(member.MemberId);
            if (!exists)
            {
                problems.Add(new IntegrityProblem("permission_member_orphan", default,
                    $"{member.MemberKind} member {member.MemberId} missing (row {member.Id})"));
            }
        }

        return problems;
    }

    private async Task<List<IntegrityProblem>> PairOrphansAsync(CancellationToken ct)
    {
        var problems = new List<IntegrityProblem>();

        async Task CheckAsync(AttachedEntityType type, IQueryable<Guid> existingIds)
        {
            var ids = await existingIds.ToHashSetAsync(ct);
            foreach (var orphan in await db.Attachments
                         .Where(a => a.EntityType == type)
                         .Select(a => new { a.Id, a.EntityId })
                         .ToListAsync(ct))
            {
                if (!ids.Contains(orphan.EntityId!.Value))
                {
                    problems.Add(new IntegrityProblem("attachment_orphan", orphan.Id, $"{type} {orphan.EntityId} missing"));
                }
            }

            foreach (var orphan in await db.Taggings
                         .Where(t => t.EntityType == type)
                         .Select(t => new { t.Id, t.EntityId })
                         .ToListAsync(ct))
            {
                if (!ids.Contains(orphan.EntityId!.Value))
                {
                    problems.Add(new IntegrityProblem("tagging_orphan", default, $"{type} {orphan.EntityId} missing (tagging {orphan.Id})"));
                }
            }

            // Resource-link members share the pair convention (their feature targets have
            // a real cascade FK, so only the typed pair needs the net).
            foreach (var orphan in await db.ResLinkMembers
                         .Where(m => m.EntityType == type)
                         .Select(m => new { m.Id, m.EntityId })
                         .ToListAsync(ct))
            {
                if (!ids.Contains(orphan.EntityId!.Value))
                {
                    problems.Add(new IntegrityProblem("res_link_member_orphan", orphan.Id, $"{type} {orphan.EntityId} missing"));
                }
            }
        }

        await CheckAsync(AttachedEntityType.TripLog, db.TripLogs.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.CavingGroup, db.CavingGroups.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Geofile, db.Geofiles.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.GeoreferencedMap, db.GeoreferencedMaps.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.MapView, db.MapViews.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.StoredFile, db.StoredFiles.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Document, db.Documents.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.SurveyModel, db.SurveyModels.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Caver, db.Cavers.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Cabinet, db.Cabinets.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Album, db.Albums.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Expedition, db.Expeditions.Select(x => x.Id));

        // Every entity-type value with a table of its own is listed above; the one that is
        // not is the comment type, which is reserved for an entity that does not exist yet.
        // A value left off this list is silently unchecked rather than loudly wrong, so the
        // list is the thing to extend when the shared vocabulary gains a member.
        return problems;
    }
}
