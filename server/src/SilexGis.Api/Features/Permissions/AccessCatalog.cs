// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// The vocabulary a rule editor offers: which scopes each resource domain accepts, and
/// which actions are meaningful inside each of those scopes.
/// </summary>
/// <remarks>
/// Derived by asking <see cref="AccessEntryRules"/> itself — one probe entry per
/// (domain, scope, action) — rather than restating the table. Hand-maintaining a second
/// copy is how an editor ends up offering a combination the server rejects, or worse,
/// hiding one it would accept; generating it means the catalogue cannot drift from the
/// rule that actually decides. The whole sweep is a few hundred pure calls, computed
/// once per request.
/// </remarks>
public static class AccessCatalog
{
    private static readonly Guid ProbeAnchor = Guid.CreateVersion7();

    public static IReadOnlyList<AccessCatalogDomainDto> Domains() =>
    [
        .. Enum.GetValues<AccessDomain>().Select(domain => new AccessCatalogDomainDto(
            domain,
            Name(domain),
            SupportsKindNarrowing: domain == AccessDomain.Features,
            Scopes: [.. ScopesOf(domain)])),
    ];

    private static IEnumerable<AccessCatalogScopeDto> ScopesOf(AccessDomain domain)
    {
        foreach (var scope in Enum.GetValues<AccessScopeKind>())
        {
            var actions = AccessActions.All.Where(action => Accepts(domain, scope, action)).ToList();
            if (actions.Count > 0)
            {
                yield return new AccessCatalogScopeDto(scope, RequiresAnchor(scope), actions);
            }
        }
    }

    /// <summary>Would a well-formed entry with exactly this shape be accepted?</summary>
    private static bool Accepts(AccessDomain domain, AccessScopeKind scope, AccessAction action)
    {
        var anchorsOnFeature = domain == AccessDomain.Features
            && scope is AccessScopeKind.Subtree or AccessScopeKind.Object;
        var probe = new AccessEntrySnapshot(
            EntryId: 0,
            PermissionGroupId: Guid.CreateVersion7(),
            SubjectKind: null,
            SubjectId: null,
            Effect: AccessEffect.Allow,
            Domain: domain,
            Actions: action,
            ScopeKind: scope,
            ScopeFeatureId: anchorsOnFeature ? ProbeAnchor : null,
            ScopeId: RequiresAnchor(scope) && !anchorsOnFeature ? ProbeAnchor : null,
            FeatureKind: null,
            FeatureTypeId: null);
        return AccessEntryRules.Validate(probe) is null;
    }

    private static bool RequiresAnchor(AccessScopeKind scope) =>
        scope is AccessScopeKind.CavingGroup or AccessScopeKind.Subtree
            or AccessScopeKind.FeatureSet or AccessScopeKind.Object;

    /// <summary>Stable camel-case domain names for payloads and UI keys.</summary>
    public static string Name(AccessDomain domain) =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(domain.ToString());
}
