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

/// <summary>One domain's explanations, in the shape the ruleset preview renders.</summary>
public readonly record struct DomainExplanations(
    AccessDomain Domain, IReadOnlyList<AccessExplanation> Explanations);

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
        return [.. decisions.Select(d => ToExplanation(d.Action, d.Decision, anchors))];
    }

    /// <summary>
    /// Domain-level explanations over the whole catalogue — the ruleset editor's preview.
    /// Decisions are made as <paramref name="subject"/>; anchor names are resolved for
    /// <paramref name="viewer"/>, because it is the person looking, not the person asked
    /// about, whose right to read a rule's anchor governs what the answer may name.
    /// </summary>
    public async Task<IReadOnlyList<DomainExplanations>> ExplainDomainsAsync(
        AccessContext subject, AccessContext viewer, CancellationToken ct = default)
    {
        var perDomain = Enum.GetValues<AccessDomain>()
            .Select(domain => (Domain: domain, Decisions: AccessActions.All
                .Select(action => (Action: action, Decision: AccessEvaluator.Decide(subject, domain, action, null)))
                .ToList()))
            .ToList();

        // One name-resolution pass over every deciding entry, not one per domain.
        var anchors = await ReadableAnchorsAsync(
            viewer, perDomain.SelectMany(d => d.Decisions.Select(x => x.Decision)), ct);

        return
        [
            .. perDomain.Select(d => new DomainExplanations(
                d.Domain,
                [.. d.Decisions.Select(x => ToExplanation(x.Action, x.Decision, anchors))])),
        ];
    }

    private static AccessExplanation ToExplanation(
        AccessAction action, AccessDecision decision, Dictionary<string, string> anchors)
    {
        var deciding = decision.DecidingEntries.FirstOrDefault();
        if (deciding is null)
        {
            // Ownership, visibility, full administration, or nothing at all —
            // none of which names anything the caller cannot already see.
            return new AccessExplanation(action, decision.Allowed, decision.Source, decision.Level, null, false);
        }

        var key = AnchorKey(deciding);
        var name = anchors.GetValueOrDefault(key);
        return new AccessExplanation(
            action,
            decision.Allowed,
            decision.Source,
            decision.Level,
            name,
            // A rule written straight onto this object has no anchor to name, so
            // there is nothing withheld; anywhere else, a missing name IS the
            // withholding.
            Redacted: name is null && key != DirectAnchor);
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

        // A cabinet name is served whenever a cabinet rule decided: cabinets are global
        // filing structure with no read gate of their own, so withholding the name would
        // hide why a refusal happened without hiding anything the caller could not
        // otherwise learn. Should cabinets gain a domain of their own, gate this the way
        // the feature sets above are gated.
        var cabinetIds = deciding.Where(e => e.ScopeKind == AccessScopeKind.Cabinet && e.ScopeId is not null)
            .Select(e => e.ScopeId!.Value).Distinct().ToArray();
        if (cabinetIds.Length > 0)
        {
            foreach (var cabinet in await db.Cabinets.AsNoTracking()
                         .Where(c => cabinetIds.Contains(c.Id))
                         .Select(c => new { c.Id, c.Name })
                         .ToListAsync(ct))
            {
                names[$"cabinet:{cabinet.Id}"] = cabinet.Name;
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
        { ScopeKind: AccessScopeKind.Cabinet, ScopeId: { } id } => $"cabinet:{id}",
        _ => DirectAnchor,
    };
}
