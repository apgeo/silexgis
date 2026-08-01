// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>What decided one action, in terms a person can read.</summary>
/// <param name="RuleName">
/// The deciding rule's anchor — a permission group, a subtree root, a feature set — or
/// null when the caller may not know it.
/// </param>
public readonly record struct AccessExplanation(
    AccessAction Action,
    bool Allowed,
    AccessDecisionSource Source,
    AccessLevel? Level,
    string? RuleName,
    bool Redacted);

/// <summary>
/// Turns decisions into explanations, naming the rule that won.
/// </summary>
/// <remarks>
/// Names are resolved only where the caller could have discovered them anyway: a
/// permission group they may read, a feature they may read, a feature set they may read.
/// Everything else is reported as level and effect with the anchor withheld — otherwise
/// "why can't I see this?" would answer with the name of an area somebody hid, or with a
/// map of the installation's policy. Redaction is a property of the answer, not of the
/// decision: the outcome is always stated truthfully, only its reason is abbreviated.
/// </remarks>
public sealed class AccessExplainer(SilexGisDbContext db)
{
    public async Task<IReadOnlyList<AccessExplanation>> ExplainAsync(
        AccessContext ctx, AccessDomain domain, AccessTargetFacts? facts, CancellationToken ct = default)
    {
        var decisions = AccessActions.All
            .Select(action => (Action: action, Decision: AccessEvaluator.Decide(ctx, domain, action, facts)))
            .ToList();

        var anchors = await ReadableAnchorsAsync(ctx, decisions.Select(d => d.Decision), ct);

        return
        [
            .. decisions.Select(d =>
            {
                var deciding = d.Decision.DecidingEntries.FirstOrDefault();
                if (deciding is null)
                {
                    // Ownership, visibility, full administration, or nothing at all —
                    // none of which names anything the caller cannot already see.
                    return new AccessExplanation(
                        d.Action, d.Decision.Allowed, d.Decision.Source, d.Decision.Level, null, false);
                }

                var key = AnchorKey(deciding);
                var name = anchors.GetValueOrDefault(key);
                return new AccessExplanation(
                    d.Action,
                    d.Decision.Allowed,
                    d.Decision.Source,
                    d.Decision.Level,
                    name,
                    // A rule written straight onto this object has no anchor to name, so
                    // there is nothing withheld; anywhere else, a missing name IS the
                    // withholding.
                    Redacted: name is null && key != DirectAnchor);
            }),
        ];
    }

    /// <summary>
    /// The display name of every deciding rule's anchor that this caller may read.
    /// Absent from the result means "withhold it".
    /// </summary>
    private async Task<Dictionary<string, string>> ReadableAnchorsAsync(
        AccessContext ctx, IEnumerable<AccessDecision> decisions, CancellationToken ct)
    {
        var deciding = decisions
            .SelectMany(d => d.DecidingEntries)
            .DistinctBy(e => e.EntryId)
            .ToList();
        var names = new Dictionary<string, string>();
        if (deciding.Count == 0)
        {
            return names;
        }

        // A ruleset entry is explained by its permission group, when the caller may read
        // the permission model at all.
        var groupIds = deciding.Where(e => e.PermissionGroupId is not null)
            .Select(e => e.PermissionGroupId!.Value).Distinct().ToArray();
        if (groupIds.Length > 0
            && AccessEvaluator.Decide(ctx, AccessDomain.PermissionGroups, AccessAction.Read, null).Allowed)
        {
            foreach (var group in await db.PermissionGroups.AsNoTracking()
                         .Where(g => groupIds.Contains(g.Id))
                         .Select(g => new { g.Id, g.Name })
                         .ToListAsync(ct))
            {
                names[$"group:{group.Id}"] = group.Name;
            }
        }

        // A collection-level entry is explained by the thing it hangs on, when that thing
        // is itself readable.
        var featureIds = deciding.Where(e => e.ScopeKind == AccessScopeKind.Subtree && e.ScopeFeatureId is not null)
            .Select(e => e.ScopeFeatureId!.Value).Distinct().ToArray();
        if (featureIds.Length > 0)
        {
            foreach (var feature in await db.Features.AsNoTracking()
                         .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                         .Where(f => featureIds.Contains(f.Id))
                         .Select(f => new { f.Id, f.Name, f.Kind })
                         .ToListAsync(ct))
            {
                names[$"subtree:{feature.Id}"] =
                    feature.Name is { Length: > 0 } n ? n : $"{feature.Kind} {feature.Id.ToString("N")[..8]}";
            }
        }

        var setIds = deciding.Where(e => e.ScopeKind == AccessScopeKind.FeatureSet && e.ScopeId is not null)
            .Select(e => e.ScopeId!.Value).Distinct().ToArray();
        if (setIds.Length > 0
            && AccessEvaluator.Decide(ctx, AccessDomain.FeatureSets, AccessAction.Read, null).Allowed)
        {
            foreach (var set in await db.FeatureSets.AsNoTracking()
                         .Where(s => setIds.Contains(s.Id))
                         .Select(s => new { s.Id, s.Name })
                         .ToListAsync(ct))
            {
                names[$"set:{set.Id}"] = set.Name;
            }
        }

        return names;
    }

    /// <summary>A direct rule on the evaluated object — nothing to name, nothing to hide.</summary>
    private const string DirectAnchor = "direct";

    private static string AnchorKey(AccessEntrySnapshot entry) => entry switch
    {
        { PermissionGroupId: { } id } => $"group:{id}",
        { ScopeKind: AccessScopeKind.Subtree, ScopeFeatureId: { } id } => $"subtree:{id}",
        { ScopeKind: AccessScopeKind.FeatureSet, ScopeId: { } id } => $"set:{id}",
        _ => DirectAnchor,
    };
}
