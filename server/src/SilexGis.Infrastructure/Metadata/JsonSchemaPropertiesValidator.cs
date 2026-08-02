// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Json.Schema;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Metadata;

/// <summary>
/// JSON Schema validation of typed property bags (JsonSchema.Net) — feature properties and
/// document metadata alike. Fails closed: a malformed stored schema or malformed property
/// document reports as an error rather than passing silently, so a write is refused with a
/// diagnosable message instead of storing data nobody validated.
/// </summary>
public sealed class JsonSchemaPropertiesValidator : ITypedPropertiesValidator
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

    public IReadOnlyList<string> ValidateSchema(string schemaJson)
    {
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return [$"schema: not valid JSON Schema ({e.Message})"];
        }

        // A schema that parses can still be unusable — a keyword whose value has the wrong
        // shape only fails when something is evaluated against it. Evaluating the empty
        // object is the cheapest way to reach that, and its own conformance is irrelevant:
        // only a schema that cannot be evaluated at all is rejected here.
        try
        {
            using var probe = JsonDocument.Parse("{}");
            schema.Evaluate(probe.RootElement, Options);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return [$"schema: cannot be evaluated ({e.Message})"];
        }

        return [];
    }

    private static string Location(EvaluationResults detail)
    {
        var location = detail.InstanceLocation.ToString();
        return location.Length == 0 ? "/" : location;
    }
}
