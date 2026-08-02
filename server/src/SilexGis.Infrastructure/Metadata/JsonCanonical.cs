// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Infrastructure.Metadata;

/// <summary>
/// Renders a JSON document in a canonical form — object keys sorted, no insignificant
/// whitespace — so two texts can be compared for meaning rather than for spelling.
/// </summary>
/// <remarks>
/// Needed because a schema stored in a jsonb column comes back with its own normalisation
/// applied (whitespace gone, keys reordered), which would make a plain string comparison
/// against the text a caller submitted report a change on every save and bump the schema
/// version for nothing. Comparing canonical forms of both sides removes that.
/// </remarks>
public static class JsonCanonical
{
    /// <summary>
    /// The canonical form of <paramref name="json"/>, or the trimmed input unchanged when it
    /// is not valid JSON — an unparseable text is compared verbatim rather than crashing a
    /// comparison that is only ever asking "did this change?".
    /// </summary>
    public static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(json);
            var builder = new StringBuilder();
            Write(node, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;

            case JsonObject obj:
                builder.Append('{');
                var first = true;
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    builder.Append(JsonSerializer.Serialize(property.Key)).Append(':');
                    Write(property.Value, builder);
                }

                builder.Append('}');
                break;

            case JsonArray array:
                builder.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    Write(array[i], builder);
                }

                builder.Append(']');
                break;

            default:
                builder.Append(node.ToJsonString());
                break;
        }
    }
}
