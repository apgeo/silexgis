// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Writes the one thing the served description was missing: that this API is behind a bearer
/// token at all.
/// </summary>
/// <remarks>
/// Without it the document describes every route as open, because the authorization requirement
/// lives on the route group rather than on any per-endpoint metadata a describer can read. That is
/// wrong for a browser client and actively misleading for a generated client on another platform,
/// which reads the description as its whole specification and would emit calls with no credential
/// on them. The scheme is declared once and required document-wide rather than per operation,
/// because that is what is true of almost every route here; the short allow-list of deliberately
/// open routes takes the requirement back off itself, per operation, from its own metadata — see
/// <see cref="AnonymousRouteSecurityOperationTransformer"/>. Without that half this document would
/// state that the sign-in calls need a token, which is the one place a generated client cannot
/// recover from.
/// </remarks>
public sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "bearerAuth";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "An access token from the sign-in endpoints, sent as \"Authorization: Bearer <token>\". "
                + "A few routes are deliberately reachable without one; they are named in the "
                + "documentation rather than exempted here.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
