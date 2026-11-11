// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>
/// The parameter schema of HDMapping's odometry, derived from the defaults the installed program
/// dumped, so that what a profile may hold follows the version that will run it and nothing is
/// copied by hand from one release to the next.
/// </summary>
/// <remarks>
/// <para>
/// Each key's type is the type of its default: a boolean stays a boolean, a whole number an
/// integer, a number with a fraction any number, a list a list of what its first element is.
/// HDMapping's own loader quietly turns a value of the wrong type into zero, false or an empty
/// string, so the type is the one check that has to happen here — the program will not refuse.
/// A text default is fixed: the worker refuses to change one, and the schema says the same.
/// TOML can say "nan" and "inf", JSON cannot, so the dump carries such a default as the word. The
/// key is a number like any other, but its default is left out of the editable set: the worker
/// refuses a value that is not finite, so the only way to keep the default is not to name the key.
/// </para>
/// <para>
/// Three sections are never part of a profile. <c>version_info</c> names the build that wrote a
/// file, <c>paths</c> names directories on the machine that wrote it, and <c>misc</c> holds the
/// desktop program's display settings; none of them is a processing parameter, and the worker
/// ignores or refuses them. A key outside every section is not a parameter either.
/// </para>
/// <para>
/// No key is required: HDMapping starts from its defaults and lays over them only the keys a file
/// names, so a partial set is a valid one.
/// </para>
/// </remarks>
public static class HdmappingParameterSchema
{
    /// <summary>The sections that are machine- or display-specific and never part of a profile.</summary>
    public static IReadOnlyList<string> DroppedSections { get; } = ["version_info", "paths", "misc"];

    private static readonly string[] NonFiniteWords = ["nan", "inf", "+inf", "-inf"];

    public static bool IsDropped(string section) => DroppedSections.Contains(section, StringComparer.Ordinal);

    /// <summary>The defaults with everything that is not a processing parameter taken out.</summary>
    public static JsonElement EditableDefaults(JsonElement defaults) =>
        Write(writer =>
        {
            writer.WriteStartObject();
            foreach (var section in Sections(defaults))
            {
                writer.WritePropertyName(section.Name);
                WriteFinite(writer, section.Value);
            }

            writer.WriteEndObject();
        });

    /// <summary>
    /// The schema of the odometry step's whole parameter object, <c>{"values": {section: {key: value}}}</c>,
    /// as the step stores it in a recipe and a profile holds it.
    /// </summary>
    public static string StepSchemaJson(JsonElement defaults)
    {
        var schema = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            writer.WritePropertyName("values");
            WriteSectionsSchema(writer, defaults);
            writer.WriteEndObject();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        });
        return schema.GetRawText();
    }

    private static IEnumerable<JsonProperty> Sections(JsonElement defaults) =>
        defaults.ValueKind != JsonValueKind.Object
            ? []
            : defaults.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && !IsDropped(p.Name));

    private static void WriteSectionsSchema(Utf8JsonWriter writer, JsonElement defaults)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteString("description", "HDMapping's odometry parameters by TOML section.");
        writer.WriteStartObject("properties");
        foreach (var section in Sections(defaults))
        {
            writer.WritePropertyName(section.Name);
            WriteValueSchema(writer, section.Value);
        }

        writer.WriteEndObject();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("default");
        foreach (var section in Sections(defaults))
        {
            writer.WritePropertyName(section.Name);
            WriteFinite(writer, section.Value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteValueSchema(Utf8JsonWriter writer, JsonElement value)
    {
        writer.WriteStartObject();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteString("type", "object");
                writer.WriteStartObject("properties");
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteValueSchema(writer, property.Value);
                }

                writer.WriteEndObject();
                writer.WriteBoolean("additionalProperties", false);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                writer.WriteString("type", "boolean");
                break;
            case JsonValueKind.Number when IsWhole(value):
                // The program reads a whole-number setting into a 32-bit integer.
                writer.WriteString("type", "integer");
                writer.WriteNumber("minimum", int.MinValue);
                writer.WriteNumber("maximum", int.MaxValue);
                break;
            case JsonValueKind.Number:
                writer.WriteString("type", "number");
                break;
            case JsonValueKind.String when IsNonFiniteWord(value.GetString()):
                writer.WriteString("type", "number");
                break;
            case JsonValueKind.String:
                writer.WriteString("type", "string");
                writer.WritePropertyName("const");
                value.WriteTo(writer);
                break;
            case JsonValueKind.Array:
                writer.WriteString("type", "array");
                var first = value.EnumerateArray().FirstOrDefault();
                if (first.ValueKind is not JsonValueKind.Undefined)
                {
                    writer.WritePropertyName("items");
                    WriteItemSchema(writer, first);
                }

                break;
        }

        if (value.ValueKind is not JsonValueKind.Object && !IsNonFinite(value))
        {
            writer.WritePropertyName("default");
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteItemSchema(Utf8JsonWriter writer, JsonElement first)
    {
        writer.WriteStartObject();
        switch (first.ValueKind)
        {
            case JsonValueKind.True or JsonValueKind.False:
                writer.WriteString("type", "boolean");
                break;
            case JsonValueKind.Number:
                // A list whose first element is whole may still hold fractions further on, and the
                // worker reads every element of a number list alike; so any number is accepted.
                writer.WriteString("type", "number");
                break;
            case JsonValueKind.String:
                writer.WriteString("type", "string");
                break;
        }

        writer.WriteEndObject();
    }

    private static bool IsNonFinite(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && IsNonFiniteWord(value.GetString());

    /// <summary>A section or value written out without the keys whose default is not finite.</summary>
    private static void WriteFinite(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            value.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        foreach (var property in value.EnumerateObject().Where(p => !IsNonFinite(p.Value)))
        {
            writer.WritePropertyName(property.Name);
            WriteFinite(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    private static bool IsWhole(JsonElement number)
    {
        var raw = number.GetRawText();
        return raw.IndexOfAny(['.', 'e', 'E']) < 0;
    }

    private static bool IsNonFiniteWord(string? text) =>
        text is not null && NonFiniteWords.Contains(text.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
