// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Json.Schema;
using SilexGis.Domain.Features;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// JSON Schema validation of feature properties (JsonSchema.Net). Fails closed: a
/// malformed stored schema or malformed properties document reports as an error rather
/// than passing silently — a write is refused with a diagnosable message instead of
/// storing data nobody validated.
/// </summary>
public sealed class JsonSchemaFeaturePropertiesValidator : IFeaturePropertiesValidator
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    public IReadOnlyList<string> Validate(string schemaJson, string propertiesJson)
    {
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return [$"schema: the kind's properties schema is not valid JSON Schema ({e.Message})"];
        }

        JsonDocument properties;
        try
        {
            properties = JsonDocument.Parse(propertiesJson);
        }
        catch (JsonException e)
        {
            return [$"properties: not valid JSON ({e.Message})"];
        }

        using (properties)
        {
            var result = schema.Evaluate(properties.RootElement, Options);
            if (result.IsValid)
            {
                return [];
            }

            var errors = (result.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{Location(d)}: {e.Value}"))
                .Distinct()
                .ToList();
            return errors.Count > 0 ? errors : ["properties do not conform to the kind's schema"];
        }
    }

    private static string Location(EvaluationResults detail)
    {
        var location = detail.InstanceLocation.ToString();
        return location.Length == 0 ? "/" : location;
    }
}
