// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.Filters;

/// <summary>One feature type's declared property schema, as the vocabulary fold reads it.</summary>
public sealed record FeatureTypeSchema(long TypeId, string TypeCode, string? Schema);

/// <summary>
/// The typed properties a caller may filter features by, read from the feature types' own schemas.
/// </summary>
/// <remarks>
/// <para>
/// A field is offered only where a schema declares the key and gives it a type this model can
/// compare: a number (or integer), a string, a date or date-time string, or a boolean. Anything else a schema may hold — an
/// object, an array, a key with several types, a reference — is left out rather than guessed at,
/// because an offered field is a promise that the comparison means what it says.
/// </para>
/// <para>
/// A string declared with the <c>date</c> or <c>date-time</c> format is offered as an instant, so it
/// is asked before, after and between rather than searched as text. A stored value that is not a
/// well-formed date simply does not match; it never fails the question.
/// </para>
/// <para>
/// Every field carries the requirement that its type be fixed beside it, and its label is the
/// schema's own title — data an administrator typed, shown as written.
/// </para>
/// </remarks>
public static class FeaturePropertyFields
{
    /// <summary>The translation key every property field shares; its own name is in the label.</summary>
    public const string LabelKey = "filters.fields.property";

    /// <summary>The field key a condition on this type's property names.</summary>
    public static string FieldKey(string typeCode, string key) =>
        $"{FeatureFilterFields.PropertyPrefix}{typeCode}:{key}";

    /// <summary>
    /// The feature world's vocabulary with the typed properties of <paramref name="types"/> added.
    /// </summary>
    /// <param name="types">
    /// The types the caller may read. A type left out contributes nothing, so a property of a type
    /// the caller may not read is refused exactly like a key nobody declared.
    /// </param>
    public static WorldVocabulary Fold(WorldVocabulary vocabulary, IEnumerable<FeatureTypeSchema> types)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(types);

        var properties = types.SelectMany(Read).ToList();
        return properties.Count == 0
            ? vocabulary
            : vocabulary with { Fields = [.. vocabulary.Fields, .. properties] };
    }

    /// <summary>The fields one type's schema declares, in the schema's own order.</summary>
    public static IReadOnlyList<FieldDescriptor> Read(FeatureTypeSchema type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrWhiteSpace(type.Schema) || string.IsNullOrEmpty(type.TypeCode)
            || type.TypeCode.Contains(':', StringComparison.Ordinal))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(type.Schema);
        }
        catch (JsonException)
        {
            // An administrator's schema that does not parse declares nothing filterable; the
            // writes it governs are refused by the schema validator, which is where that is said.
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var requirement = new FieldRequirement(
                FeatureFilterFields.TypeId, type.TypeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var fields = new List<FieldDescriptor>();
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Name.Length == 0 || property.Value.ValueKind != JsonValueKind.Object
                    || KindOf(property.Value) is not { } kind)
                {
                    continue;
                }

                var title = property.Value.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(t.GetString())
                        ? t.GetString()!
                        : property.Name;

                fields.Add(new FieldDescriptor(
                    FieldKey(type.TypeCode, property.Name), LabelKey, kind, Label: title, Requires: requirement));
            }

            return fields;
        }
    }

    private static FieldKind? KindOf(JsonElement spec)
    {
        if (!spec.TryGetProperty("type", out var type))
        {
            return null;
        }

        string? name = type.ValueKind switch
        {
            JsonValueKind.String => type.GetString(),
            // ["number", "null"] is the usual way to say "optional number"; anything with two real
            // types is not one comparison and is left out.
            JsonValueKind.Array => type.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String && e.GetString() != "null")
                .Select(e => e.GetString())
                .ToList() is [var only] ? only : null,
            _ => null,
        };

        return name switch
        {
            "number" or "integer" => FieldKind.Number,
            "string" => IsDateFormat(spec) ? FieldKind.Instant : FieldKind.Text,
            "boolean" => FieldKind.Boolean,
            _ => null,
        };
    }

    private static bool IsDateFormat(JsonElement spec) =>
        spec.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String
        && format.GetString() is "date" or "date-time";
}
