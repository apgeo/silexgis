// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Keeps <c>null</c> out of the published list of an enumeration's values.
/// </summary>
/// <remarks>
/// <para>
/// The framework writes one shared schema per enumeration, and where any property somewhere is a
/// nullable value of it, that shared schema came out listing <c>null</c> among the values — so
/// every generated client was told that a trip's state, an event's kind or a notification's
/// category may be null wherever it appears, including the many places it never is. A property
/// that really may be null already says so itself (as "null, or this enumeration"), so taking
/// <c>null</c> off the shared list loses nothing and makes every other use exact.
/// </para>
/// <para>
/// Done over the finished document rather than per schema as each is written: the shared schema
/// is taken from whichever use of the enumeration the generator met first, so a per-schema step
/// sees it as a property's schema or as the type's depending on the order of the routes.
/// </para>
/// </remarks>
public sealed class EnumSchemaTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        foreach (var schema in document.Components?.Schemas?.Values ?? [])
        {
            if (schema is OpenApiSchema { Enum: { Count: > 0 } values }
                && values.Any(v => v is not null && v.GetValueKind() == JsonValueKind.String))
            {
                for (var i = values.Count - 1; i >= 0; i--)
                {
                    if (values[i] is null || values[i]!.GetValueKind() == JsonValueKind.Null)
                    {
                        values.RemoveAt(i);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
