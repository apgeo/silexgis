// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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

        // Delegated trio copies: an entrance/centerline must carry exactly its cave's
        // access columns (a drift here changes who can see an entrance — security).
        var featureAccess = await db.Features.IgnoreQueryFilters()
            .Select(f => new { f.Id, f.OwnerUserId, f.TeamId, f.Visibility })
            .ToDictionaryAsync(f => f.Id, ct);
        // Live rows only: a soft-deleted child's stale trio is invisible and gets
        // recopied by the sync when its cave next changes.
        var delegated = (await db.CaveEntrances
                .Select(e => new { e.Id, e.CaveFeatureId }).ToListAsync(ct))
            .Concat(await db.Centerlines
                .Select(c => new { c.Id, c.CaveFeatureId }).ToListAsync(ct));
        foreach (var child in delegated)
        {
            if (featureAccess.TryGetValue(child.Id, out var c)
                && featureAccess.TryGetValue(child.CaveFeatureId, out var cave)
                && (c.OwnerUserId != cave.OwnerUserId || c.TeamId != cave.TeamId || c.Visibility != cave.Visibility))
            {
                problems.Add(new IntegrityProblem("delegated_trio", child.Id,
                    "access columns diverge from the owning cave's"));
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

            foreach (var orphan in await db.ObjectAcls
                         .Where(a => a.EntityType == type)
                         .Select(a => new { a.Id, a.EntityId })
                         .ToListAsync(ct))
            {
                if (!ids.Contains(orphan.EntityId!.Value))
                {
                    problems.Add(new IntegrityProblem("acl_orphan", default, $"{type} {orphan.EntityId} missing (grant {orphan.Id})"));
                }
            }
        }

        await CheckAsync(AttachedEntityType.TripLog, db.TripLogs.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Team, db.Teams.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.Geofile, db.Geofiles.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.GeoreferencedMap, db.GeoreferencedMaps.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.MapView, db.MapViews.Select(x => x.Id));
        await CheckAsync(AttachedEntityType.StoredFile, db.StoredFiles.Select(x => x.Id));

        return problems;
    }
}
