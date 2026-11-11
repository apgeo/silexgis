// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SilexGis.Domain.Scans;
using Tomlyn;
using Tomlyn.Model;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// HDMapping's parameter files, read into and written from the JSON shape the application keeps
/// parameters in: one object per TOML section.
/// </summary>
/// <remarks>
/// <para>
/// The conversion keeps the one thing JSON would otherwise lose and HDMapping cares about: whether
/// a number was written as a whole number or with a fraction. HDMapping's loader reads a
/// fractional value into a whole-number setting (and the other way round) as zero, so a number
/// read from TOML keeps its fraction in the JSON text (<c>5.0</c> stays <c>5.0</c>), and a number
/// written out takes the kind of the installed default for its key, whatever its JSON text says.
/// TOML's <c>nan</c> and <c>inf</c>, which JSON cannot carry, travel as those words, as the worker
/// writes them in its dump.
/// </para>
/// <para>
/// What is written matches the worker's own parameter file: scalar keys first, then one table per
/// section, bare keys where TOML allows them.
/// </para>
/// </remarks>
public static partial class HdmappingToml
{
    /// <summary>The media type a TOML file is served as.</summary>
    public const string ContentType = "application/toml";

    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A TOML text as JSON, by section.</summary>
    /// <exception cref="FormatException">The text is not TOML; the message says where.</exception>
    public static JsonElement Read(string text)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text ?? string.Empty)
                ?? throw new FormatException("The file holds no TOML.");
        }
        catch (TomlException e)
        {
            throw new FormatException(e.Message, e);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, JsonOptions))
        {
            WriteJson(writer, table);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>HDMapping's parameter file for these values.</summary>
    /// <param name="sections">The values by section, as a profile holds them inside <c>values</c>.</param>
    /// <param name="dumpedDefaults">The installed program's defaults by section, which decide each number's kind; may be undefined.</param>
    /// <param name="versionInfo">The <c>[version_info]</c> table to write first, when there is one.</param>
    /// <param name="heading">Comment lines for the top of the file.</param>
    public static string Write(JsonElement sections, JsonElement dumpedDefaults, IReadOnlyDictionary<string, string>? versionInfo, IEnumerable<string> heading)
    {
        ArgumentNullException.ThrowIfNull(heading);
        var text = new StringBuilder();
        foreach (var line in heading)
        {
            text.Append("# ").Append(line).Append('\n');
        }

        if (sections.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in sections.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Object))
            {
                text.Append(Key(key.Name)).Append(" = ").Append(Value(key.Value, default)).Append('\n');
            }
        }

        if (versionInfo is { Count: > 0 })
        {
            text.Append("\n[version_info]\n");
            foreach (var (key, value) in versionInfo)
            {
                text.Append(Key(key)).Append(" = ").Append(Quoted(value)).Append('\n');
            }
        }

        if (sections.ValueKind == JsonValueKind.Object)
        {
            foreach (var section in sections.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
            {
                if (section.Name == "version_info")
                {
                    continue;
                }

                var defaults = dumpedDefaults.ValueKind == JsonValueKind.Object
                    && dumpedDefaults.TryGetProperty(section.Name, out var found)
                    && found.ValueKind == JsonValueKind.Object
                    ? found
                    : default;
                text.Append('\n').Append('[').Append(Key(section.Name)).Append("]\n");
                foreach (var key in section.Value.EnumerateObject())
                {
                    var standard = defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(key.Name, out var d) ? d : default;
                    text.Append(Key(key.Name)).Append(" = ").Append(Value(key.Value, standard)).Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>A double as TOML and JSON both read it, always with a fraction or an exponent.</summary>
    public static string Fractional(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.IndexOfAny(['.', 'E', 'e']) < 0 ? text + ".0" : text;
    }

    private static string Value(JsonElement value, JsonElement standard)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Number:
                return IsFractionalKind(standard, value) ? Fractional(value.GetDouble()) : value.GetRawText();
            case JsonValueKind.String when ProcessingProfileImport.IsNonFinite(value) && standard.ValueKind != JsonValueKind.String:
            case JsonValueKind.String when ProcessingProfileImport.IsNonFinite(value) && ProcessingProfileImport.IsNonFinite(standard):
                return value.GetString()!.Trim().ToLowerInvariant();
            case JsonValueKind.String:
                return Quoted(value.GetString()!);
            case JsonValueKind.Array:
                var first = standard.ValueKind == JsonValueKind.Array ? standard.EnumerateArray().FirstOrDefault() : default;
                return "[" + string.Join(", ", value.EnumerateArray().Select(item => Value(item, first))) + "]";
            default:
                throw new InvalidOperationException($"A {value.ValueKind} has no place in HDMapping's parameters.");
        }
    }

    /// <summary>
    /// Whether a number is written with a fraction: when its default is fractional (or not finite,
    /// which only a fractional setting can be), or, with no default to go by, when its own text is.
    /// </summary>
    private static bool IsFractionalKind(JsonElement standard, JsonElement value) => standard.ValueKind switch
    {
        JsonValueKind.Number => !ProcessingProfileImport.IsWhole(standard),
        JsonValueKind.String => ProcessingProfileImport.IsNonFinite(standard),
        _ => !ProcessingProfileImport.IsWhole(value),
    };

    private static string Key(string key) => BareKey().IsMatch(key) ? key : Quoted(key);

    /// <summary>A TOML basic string: JSON's quoting and escapes are TOML's too.</summary>
    private static string Quoted(string text) =>
        JsonSerializer.Serialize(text, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static void WriteJson(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case TomlTable table:
                writer.WriteStartObject();
                foreach (var (key, item) in table)
                {
                    writer.WritePropertyName(key);
                    WriteJson(writer, item);
                }

                writer.WriteEndObject();
                break;
            case TomlTableArray tables:
                writer.WriteStartArray();
                foreach (var item in tables)
                {
                    WriteJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case TomlArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case double number when double.IsFinite(number):
                writer.WriteRawValue(Fractional(number));
                break;
            case double number:
                writer.WriteStringValue(Fractional(number));
                break;
            case float number:
                WriteJson(writer, (double)number);
                break;
            case long or int or short or sbyte or ulong or uint or ushort or byte:
                writer.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                break;
            case TomlDateTime moment:
                writer.WriteStringValue(moment.ToString());
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex BareKey();
}
