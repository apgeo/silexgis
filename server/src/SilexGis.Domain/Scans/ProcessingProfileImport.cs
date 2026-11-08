// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>A value an import would not take, with the key named and why.</summary>
/// <param name="Key">The key, as <c>section.key</c> for HDMapping's parameters.</param>
public sealed record ProfileImportRefusal(string Key, string Reason);

/// <summary>What an import made of a parameter file, key by key.</summary>
/// <param name="Taken">Keys the file set and the profile keeps.</param>
/// <param name="Unknown">Keys the step does not have; dropped.</param>
/// <param name="Defaulted">Keys the file did not set; filled from the installed defaults.</param>
/// <param name="Fixed">
/// Text keys the file set to something else; text values are the program's own (names, versions)
/// and cannot be changed, so the default stands.
/// </param>
/// <param name="DroppedSections">Sections that are never part of a profile, present in the file and dropped.</param>
/// <param name="Refused">Values of the wrong type or out of range. Any one refuses the whole import.</param>
/// <param name="FileVersion">The version the file says it was written by, when it says.</param>
/// <param name="InstalledVersion">The version the values were checked against, when one is known.</param>
public sealed record ProfileImportReport(
    IReadOnlyList<string> Taken,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> Defaulted,
    IReadOnlyList<string> Fixed,
    IReadOnlyList<string> DroppedSections,
    IReadOnlyList<ProfileImportRefusal> Refused,
    string? FileVersion,
    string? InstalledVersion)
{
    /// <summary>Whether the file names a version and it is not the one installed.</summary>
    public bool VersionDiffers =>
        !string.IsNullOrEmpty(FileVersion) && !string.IsNullOrEmpty(InstalledVersion)
        && !string.Equals(FileVersion, InstalledVersion, StringComparison.Ordinal);

    public bool Accepted => Refused.Count == 0;

    /// <summary>The report with the schema's own complaints added as refusals.</summary>
    /// <param name="errors">"path: message" lines, as the parameter validator writes them.</param>
    public ProfileImportReport WithSchemaErrors(IEnumerable<string> errors)
    {
        var added = errors.Select(line =>
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0
                ? new ProfileImportRefusal(line[..colon], line[(colon + 2)..])
                : new ProfileImportRefusal("", line);
        }).ToList();
        return added.Count == 0 ? this : this with { Refused = [.. Refused, .. added] };
    }
}

/// <summary>The values an import resolved — the whole set the step would run with — and its report.</summary>
public sealed record ProfileImportResult(JsonElement Values, ProfileImportReport Report);

/// <summary>
/// Turns a parameter file read from outside — HDMapping's own TOML, or a profile exported from
/// SilexGIS — into the values a profile stores, saying exactly what was taken, dropped, filled in
/// or refused.
/// </summary>
/// <remarks>
/// <para>
/// The result is always the whole set: every key the installed defaults have and the file did not
/// name is filled from them, so a profile imported from a partial file means the same thing on the
/// day it is used as on the day it was imported, even after the defaults change.
/// </para>
/// <para>
/// HDMapping's loader reads a value of the wrong type as zero, false or nothing and says nothing
/// about it, so the type is checked here, from each default's own kind, and a mismatch refuses the
/// import naming the key — a file that would silently run with a zeroed setting is worse than one
/// that does not import. That includes a whole-number setting written with a fraction (<c>5.0</c>),
/// which the program would not read as five. A whole number must fit the program's 32-bit integer,
/// and a fractional one must be finite: the worker refuses anything else at run time, and an
/// import should not store what the run will refuse.
/// </para>
/// </remarks>
public static class ProcessingProfileImport
{
    /// <summary>The format word of a SilexGIS profile file.</summary>
    public const string FileFormat = "silexgis.processing-profile";

    /// <summary>The version of the SilexGIS profile file's own shape.</summary>
    public const int FileFormatVersion = 1;

    private static readonly string[] NonFiniteWords = ["nan", "+nan", "-nan", "inf", "+inf", "-inf"];

    /// <summary>
    /// HDMapping odometry parameters by section, against the defaults the installed program dumped
    /// (the dump as it is, with its version, path and display sections and its non-finite words).
    /// </summary>
    /// <param name="fileSections">The file's values, by TOML section.</param>
    /// <param name="dumpedDefaults">The installed program's defaults, by section.</param>
    /// <param name="fileVersion">The version the file names; when null, its <c>[version_info]</c> is read.</param>
    /// <param name="installedVersion">The version of the installed program, in the same words as the file's.</param>
    /// <returns>Values in the step's stored shape, <c>{"values": {section: {key: value}}}</c>.</returns>
    public static ProfileImportResult Hdmapping(
        JsonElement fileSections, JsonElement dumpedDefaults, string? fileVersion, string? installedVersion)
    {
        var taken = new List<string>();
        var unknown = new List<string>();
        var defaulted = new List<string>();
        var fixedKeys = new List<string>();
        var dropped = new List<string>();
        var refused = new List<ProfileImportRefusal>();
        var chosen = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);

        if (fileSections.ValueKind != JsonValueKind.Object)
        {
            refused.Add(new ProfileImportRefusal("", "the file holds no sections of parameters"));
        }
        else
        {
            foreach (var section in fileSections.EnumerateObject())
            {
                if (HdmappingParameterSchema.IsDropped(section.Name))
                {
                    dropped.Add(section.Name);
                    if (section.Name == "version_info" && fileVersion is null
                        && section.Value.ValueKind == JsonValueKind.Object
                        && section.Value.TryGetProperty("software_version", out var word)
                        && word.ValueKind == JsonValueKind.String)
                    {
                        fileVersion = word.GetString();
                    }

                    continue;
                }

                if (section.Value.ValueKind != JsonValueKind.Object)
                {
                    unknown.Add(section.Name);
                    continue;
                }

                if (!TryGetSection(dumpedDefaults, section.Name, out var defaultSection))
                {
                    var keys = section.Value.EnumerateObject().Select(k => $"{section.Name}.{k.Name}").ToList();
                    unknown.AddRange(keys.Count == 0 ? [section.Name] : keys);
                    continue;
                }

                foreach (var key in section.Value.EnumerateObject())
                {
                    var path = $"{section.Name}.{key.Name}";
                    if (!defaultSection.TryGetProperty(key.Name, out var standard))
                    {
                        unknown.Add(path);
                        continue;
                    }

                    switch (Check(standard, key.Value, out var reason))
                    {
                        case Verdict.Take:
                            taken.Add(path);
                            Section(chosen, section.Name)[key.Name] = key.Value.Clone();
                            break;
                        case Verdict.KeepDefault:
                            taken.Add(path);
                            break;
                        case Verdict.Fixed:
                            fixedKeys.Add(path);
                            break;
                        default:
                            refused.Add(new ProfileImportRefusal(path, reason!));
                            break;
                    }
                }
            }
        }

        var values = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("values");
            foreach (var section in EditableSections(dumpedDefaults))
            {
                writer.WriteStartObject(section.Name);
                chosen.TryGetValue(section.Name, out var mine);
                foreach (var key in section.Value.EnumerateObject())
                {
                    if (mine is not null && mine.TryGetValue(key.Name, out var value))
                    {
                        writer.WritePropertyName(key.Name);
                        value.WriteTo(writer);
                        continue;
                    }

                    // A default that is not finite stays out of the set: the worker keeps the
                    // program's own default for a key nobody named, and refuses one written out.
                    if (IsNonFinite(key.Value))
                    {
                        continue;
                    }

                    var path = $"{section.Name}.{key.Name}";
                    if (!taken.Contains(path, StringComparer.Ordinal) && !fixedKeys.Contains(path, StringComparer.Ordinal)
                        && !refused.Any(r => r.Key == path))
                    {
                        defaulted.Add(path);
                    }

                    writer.WritePropertyName(key.Name);
                    key.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        });

        return new ProfileImportResult(values, new ProfileImportReport(
            taken, unknown, defaulted, fixedKeys, dropped, refused, NullIfEmpty(fileVersion), NullIfEmpty(installedVersion)));
    }

    /// <summary>
    /// A step whose parameters are one flat object, defined in this application: keys the step's
    /// schema does not declare are dropped, keys it has a default for and the file left out are
    /// filled. Types and ranges are the schema's to check; the caller adds its complaints with
    /// <see cref="ProfileImportReport.WithSchemaErrors"/>.
    /// </summary>
    public static ProfileImportResult Flat(
        JsonElement fileValues, JsonElement defaults, IReadOnlyCollection<string> declaredKeys, string? fileVersion, string? installedVersion)
    {
        ArgumentNullException.ThrowIfNull(declaredKeys);
        var taken = new List<string>();
        var unknown = new List<string>();
        var defaulted = new List<string>();
        var refused = new List<ProfileImportRefusal>();
        if (fileValues.ValueKind != JsonValueKind.Object)
        {
            refused.Add(new ProfileImportRefusal("", "the file's values are not an object"));
        }

        var values = Write(writer =>
        {
            writer.WriteStartObject();
            if (fileValues.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in fileValues.EnumerateObject())
                {
                    if (!declaredKeys.Contains(property.Name))
                    {
                        unknown.Add(property.Name);
                        continue;
                    }

                    taken.Add(property.Name);
                    property.WriteTo(writer);
                }
            }

            if (defaults.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in defaults.EnumerateObject().Where(p => !taken.Contains(p.Name, StringComparer.Ordinal)))
                {
                    defaulted.Add(property.Name);
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        });

        return new ProfileImportResult(values, new ProfileImportReport(
            taken, unknown, defaulted, [], [], refused, NullIfEmpty(fileVersion), NullIfEmpty(installedVersion)));
    }

    /// <summary>The program's own version word out of a tool version written <c>version@commit</c>.</summary>
    public static string? SoftwareVersionOf(string? toolVersion)
    {
        if (string.IsNullOrEmpty(toolVersion))
        {
            return null;
        }

        var at = toolVersion.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? toolVersion : NullIfEmpty(toolVersion[..at]);
    }

    /// <summary>Whether a default (or a value) is TOML's nan or infinity, which JSON carries as a word.</summary>
    public static bool IsNonFinite(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && NonFiniteWords.Contains(value.GetString()!.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>Whether a number's text is written without a fraction or an exponent.</summary>
    public static bool IsWhole(JsonElement number) => number.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0;

    private enum Verdict
    {
        Take,
        KeepDefault,
        Fixed,
        Refuse,
    }

    private static Verdict Check(JsonElement standard, JsonElement value, out string? reason)
    {
        reason = null;
        if (IsNonFinite(standard))
        {
            // The key is a number whose default is nan or infinity: a finite number may replace
            // it, the same word keeps it, anything else the run would refuse.
            if (IsNonFinite(value))
            {
                if (string.Equals(value.GetString()!.Trim(), standard.GetString()!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Verdict.KeepDefault;
                }

                reason = "must be a finite number";
                return Verdict.Refuse;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return Verdict.Take;
            }

            reason = "must be a number";
            return Verdict.Refuse;
        }

        switch (standard.ValueKind)
        {
            case JsonValueKind.String:
                if (value.ValueKind != JsonValueKind.String)
                {
                    reason = "must be text";
                    return Verdict.Refuse;
                }

                return value.GetString() == standard.GetString() ? Verdict.Take : Verdict.Fixed;
            case JsonValueKind.Array:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    reason = "must be a list";
                    return Verdict.Refuse;
                }

                var first = standard.EnumerateArray().FirstOrDefault();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (first.ValueKind != JsonValueKind.Undefined && !Scalar(first, item, out var itemReason))
                    {
                        reason = $"item {index} {itemReason}";
                        return Verdict.Refuse;
                    }

                    if (first.ValueKind == JsonValueKind.Undefined
                        && item.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null)
                    {
                        reason = $"item {index} must be a number, true or false, or text";
                        return Verdict.Refuse;
                    }

                    index++;
                }

                return Verdict.Take;
            case JsonValueKind.Object:
                reason = "is a table, which a profile does not hold";
                return Verdict.Refuse;
            default:
                return Scalar(standard, value, out reason) ? Verdict.Take : Verdict.Refuse;
        }
    }

    private static bool Scalar(JsonElement standard, JsonElement value, out string? reason)
    {
        reason = null;
        switch (standard.ValueKind)
        {
            case JsonValueKind.True or JsonValueKind.False:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return true;
                }

                reason = "must be true or false";
                return false;
            case JsonValueKind.Number when IsWhole(standard):
                if (value.ValueKind != JsonValueKind.Number || !IsWhole(value))
                {
                    reason = "must be a whole number";
                    return false;
                }

                if (!long.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)
                    || whole is < int.MinValue or > int.MaxValue)
                {
                    reason = $"is out of range ({int.MinValue} to {int.MaxValue})";
                    return false;
                }

                return true;
            case JsonValueKind.Number:
                if (value.ValueKind == JsonValueKind.Number)
                {
                    if (!double.IsFinite(value.GetDouble()))
                    {
                        reason = "must be a finite number";
                        return false;
                    }

                    return true;
                }

                reason = IsNonFinite(value) ? "must be a finite number" : "must be a number";
                return false;
            case JsonValueKind.String:
                if (value.ValueKind == JsonValueKind.String)
                {
                    return true;
                }

                reason = "must be text";
                return false;
            default:
                reason = "has no place in the parameters";
                return false;
        }
    }

    private static IEnumerable<JsonProperty> EditableSections(JsonElement defaults) =>
        defaults.ValueKind != JsonValueKind.Object
            ? []
            : defaults.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && !HdmappingParameterSchema.IsDropped(p.Name));

    private static bool TryGetSection(JsonElement defaults, string name, out JsonElement section)
    {
        section = default;
        return defaults.ValueKind == JsonValueKind.Object
            && !HdmappingParameterSchema.IsDropped(name)
            && defaults.TryGetProperty(name, out section)
            && section.ValueKind == JsonValueKind.Object;
    }

    private static Dictionary<string, JsonElement> Section(Dictionary<string, Dictionary<string, JsonElement>> chosen, string name)
    {
        if (!chosen.TryGetValue(name, out var section))
        {
            section = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            chosen[name] = section;
        }

        return section;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
