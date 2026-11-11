// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>
/// How a profile's values become the parameters a step runs with: laid over the step's defaults,
/// so a profile that names only the keys it changes still gives the run every value.
/// </summary>
/// <remarks>
/// Objects are merged key by key at every depth — HDMapping's parameters are sections of keys, and
/// a profile that changes one key of a section must not wipe the rest of it. Anything else, a list
/// included, is replaced whole: a list of values is one setting, not a set of settings.
/// </remarks>
public static class ProcessingProfileValues
{
    /// <summary><paramref name="values"/> laid over <paramref name="defaults"/>; the defaults alone when the values are not an object.</summary>
    public static JsonElement LayOver(JsonElement defaults, JsonElement values)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, defaults, values);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement under, JsonElement over)
    {
        if (over.ValueKind is JsonValueKind.Undefined)
        {
            under.WriteTo(writer);
            return;
        }

        if (under.ValueKind != JsonValueKind.Object || over.ValueKind != JsonValueKind.Object)
        {
            over.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in under.EnumerateObject())
        {
            named.Add(property.Name);
            writer.WritePropertyName(property.Name);
            Write(writer, property.Value, over.TryGetProperty(property.Name, out var replaced) ? replaced : default);
        }

        foreach (var property in over.EnumerateObject().Where(p => !named.Contains(p.Name)))
        {
            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
