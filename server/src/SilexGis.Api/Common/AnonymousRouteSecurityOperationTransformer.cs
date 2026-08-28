// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Takes the document-wide bearer requirement back off the routes that are deliberately reachable
/// without a token.
/// </summary>
/// <remarks>
/// The requirement is declared once for the whole document because that is what is true of almost
/// every route here. It is not true of the short allow-list — the sign-in calls above all, which a
/// client has to issue with no credential in hand, because obtaining one is what they are for. A
/// description that demanded a token there would be wrong in the one place a generated client
/// cannot recover from: it would either refuse to call sign-in without a token it cannot yet have,
/// or make the caller invent one.
/// <para>
/// An empty requirement list on an operation is how OpenAPI says "no requirement here", and it
/// overrides the document-level entry rather than adding to it. The exemption is read from the
/// endpoint's own metadata rather than from a list kept by hand, so a route that is opened or
/// closed later says so in the description on the day it changes.
/// </para>
/// </remarks>
public sealed class AnonymousRouteSecurityOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var anonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();
        if (anonymous)
        {
            operation.Security = [];
        }

        return Task.CompletedTask;
    }
}
