// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Therion.Blender;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>
/// The render node protocol's messages read and written with the survey library's own records and
/// checked against its own schema, so this server and the worker cannot disagree about a shape.
/// </summary>
/// <remarks>
/// <para>
/// A message a node sends is checked against the schema's entry for it with one change: the
/// schema closes every object (<c>additionalProperties: false</c>), and the protocol also says a
/// receiver ignores properties it does not know. The closing is the schema's promise about what a
/// sender writes; a newer worker that sends one property more must still be served, so the check
/// here leaves it out. Everything else — what is required, every type, pattern, enum and bound —
/// is the schema's, unchanged.
/// </para>
/// <para>
/// What this server answers is written by the library's own serializer: camelCase, absent values
/// left out rather than written as null.
/// </para>
/// </remarks>
public static class RenderNodeMessages
{
    public const string Claim = "claim";
    public const string Heartbeat = "heartbeat";
    public const string Complete = "complete";
    public const string Fail = "fail";

    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    private static readonly Lazy<IReadOnlyDictionary<string, JsonSchema>> Schemas = new(Build);

    /// <summary>
    /// The message in <paramref name="json"/> as the record <typeparamref name="T"/>, or what is
    /// wrong with it: not JSON, not an object, or not what the schema's <paramref name="name"/>
    /// entry says.
    /// </summary>
    public static (T? Message, IReadOnlyList<string> Problems) Read<T>(string name, string json)
        where T : class
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            return (null, [$"the body is not JSON ({e.Message})"]);
        }

        using (document)
        {
            var result = Schemas.Value[name].Evaluate(document.RootElement, Options);
            if (!result.IsValid)
            {
                var problems = (result.Details ?? [])
                    .Where(d => d.Errors is { Count: > 0 })
                    .SelectMany(d => d.Errors!.Select(e => $"{Location(d)}: {e.Value}"))
                    .Distinct()
                    .Take(20)
                    .ToList();
                return (null, problems.Count > 0 ? problems : [$"the body is not a {name} message"]);
            }
        }

        return RenderNodeProtocol.Read<T>(json) is { } message
            ? (message, [])
            : (null, [$"the body is not a {name} message"]);
    }

    /// <summary>A message as the protocol writes it.</summary>
    public static string Write<T>(T message) => RenderNodeProtocol.Write(message);

    private static string Location(EvaluationResults details) =>
        details.InstanceLocation.ToString() is { Length: > 0 } pointer ? pointer : "/";

    private static IReadOnlyDictionary<string, JsonSchema> Build()
    {
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        foreach (var name in new[] { Claim, Heartbeat, Complete, Fail })
        {
            // One copy of the whole schema for each message, pointed at its entry. The copies carry
            // no $id: the library keeps one registry of schemas by id, and four with the same one
            // would overwrite each other.
            var root = JsonNode.Parse(RenderNodeProtocol.Schema)!.AsObject();
            root.Remove("$id");
            Open(root);
            root["$ref"] = $"#/$defs/{name}";
            schemas[name] = JsonSchema.FromText(root.ToJsonString());
        }

        return schemas;
    }

    /// <summary>Takes out every <c>additionalProperties: false</c>: a receiver ignores what it does not know.</summary>
    private static void Open(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["additionalProperties"] is JsonValue value && value.TryGetValue<bool>(out var closed) && !closed)
                {
                    obj.Remove("additionalProperties");
                }

                foreach (var (_, child) in obj.ToList())
                {
                    Open(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Open(child);
                }

                break;
        }
    }
}
