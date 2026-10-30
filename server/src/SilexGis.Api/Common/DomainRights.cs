// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Api.Common;

/// <summary>
/// How the rights an account holds are named and summed up for the wire. Shared because the
/// permission screens and the caller's own capabilities answer in the same words, and the two
/// must not come to spell a domain differently.
/// </summary>
public static class DomainRights
{
    /// <summary>Stable camel-case domain names for payloads and UI keys.</summary>
    public static string Name(AccessDomain domain) =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(domain.ToString());

    /// <summary>Domain-level rights, with no row in view — what UI gating needs.</summary>
    public static IReadOnlyDictionary<string, AccessAction> Of(AccessContext ctx)
    {
        var domains = new Dictionary<string, AccessAction>();
        foreach (var domain in Enum.GetValues<AccessDomain>())
        {
            var held = AccessAction.None;
            foreach (var action in AccessActions.All)
            {
                if (AccessEvaluator.Decide(ctx, domain, action, null).Allowed)
                {
                    held |= action;
                }
            }

            domains[Name(domain)] = held;
        }

        return domains;
    }
}
