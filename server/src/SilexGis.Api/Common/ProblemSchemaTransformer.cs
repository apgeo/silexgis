// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Puts the stable machine-readable <c>code</c> into the published shape of a problem response.
/// </summary>
/// <remarks>
/// Every refusal this API sends carries a <c>code</c> (<c>acl.forbidden</c>,
/// <c>validation.failed</c>, <c>auth.unauthenticated</c>, …) and it is the only member a client is
/// meant to branch on — the detail sentence is for a person. It travels as an extension member of
/// the problem document, which the framework's own schema for that document does not know, so a
/// generated client would have seen every refusal without the one thing it needs to act on.
/// </remarks>
public sealed class ProblemSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description =
                    "The stable, machine-readable reason, e.g. \"acl.forbidden\". The one member a "
                    + "client branches on; the detail is for a person and may change.",
            };
        }

        return Task.CompletedTask;
    }
}
