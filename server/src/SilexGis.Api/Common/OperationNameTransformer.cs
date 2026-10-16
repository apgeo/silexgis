// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Gives every operation in the served description a name. A route that names itself
/// (<c>WithName</c>) keeps its own; every other one is named after the handler it runs — the
/// endpoint class without <c>Endpoints</c> and the method without <c>Async</c>, joined in lower
/// camel case: <c>CaveEndpoints.GetSummaryAsync</c> is <c>caveGetSummary</c>. A handler mapped for
/// more than one HTTP method is one operation per method, so each takes the method as well:
/// <c>connectAuthorizeGet</c>, <c>connectAuthorizePost</c>.
/// </summary>
/// <remarks>
/// <para>
/// A generated client names its methods after these, so without them every generator invents a
/// name from the path and the method, and the name moves whenever a path is tidied. That is the
/// reason a mobile client could not be generated from this description.
/// </para>
/// <para>
/// Derived in one place rather than written on each of several hundred registrations, which
/// would be one more line for every new route to forget and a change spread through every slice
/// at once. The cost is that renaming a handler renames its operation; the generated client in
/// the repository is regenerated and diffed on every change, so such a rename is seen in review,
/// and a name that must not move is pinned with <c>WithName</c>. A handler with no name of its
/// own to go by (a lambda) gets none, and the contract tests refuse an unnamed operation.
/// </para>
/// </remarks>
public sealed class OperationNameTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(operation.OperationId)
            && context.Description.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault()
                is { } handler
            && NameOf(handler) is { } name)
        {
            var methods = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>().FirstOrDefault()?.HttpMethods;
            operation.OperationId = methods is { Count: > 1 } && context.Description.HttpMethod is { Length: > 0 } verb
                ? name + char.ToUpperInvariant(verb[0]) + verb[1..].ToLowerInvariant()
                : name;
        }

        return Task.CompletedTask;
    }

    /// <summary>The operation name a handler method stands for, or null when it has no usable name.</summary>
    public static string? NameOf(MethodInfo handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var owner = handler.DeclaringType?.Name;
        var method = handler.Name;
        if (owner is null || owner.Contains('<', StringComparison.Ordinal) || method.Contains('<', StringComparison.Ordinal))
        {
            return null;
        }

        owner = Trim(owner, "Endpoints");
        method = Trim(method, "Async");
        return owner.Length == 0 || method.Length == 0
            ? null
            : char.ToLowerInvariant(owner[0]) + owner[1..] + method;
    }

    private static string Trim(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
}
