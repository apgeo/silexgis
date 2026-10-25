// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Features;

/// <summary>Why a field named in a restore from the history was left as it is.</summary>
public static class HistoryRestoreReasons
{
    /// <summary>
    /// The field cannot be put back from the history: the change did not touch it, or it is not
    /// one a restore writes (access, containment, deletion state, bookkeeping, a value derived
    /// from other rows), or the old value no longer fits (a type that has since been removed).
    /// </summary>
    public const string NotRestorable = "not_restorable";

    /// <summary>The caller's history hides the field, so they cannot ask for its old value back.</summary>
    public const string Redacted = "redacted";

    /// <summary>
    /// The old value is a large shape the history recorded only as a summary, and no full copy was
    /// kept (history written before full shapes were kept beside it).
    /// </summary>
    public const string NoPreImage = "no_pre_image";

    /// <summary>
    /// The caller may change the feature but not this field: a position, its altitude or its
    /// quality, or promoting an entrance to the cave's main one, all need the exact location.
    /// </summary>
    public const string Forbidden = "forbidden";

    /// <summary>
    /// Undoing an upload only: the field has been changed since the upload wrote it, so putting
    /// back what it held before the upload would throw away the newer edit. Left as it is unless
    /// the undo is forced.
    /// </summary>
    public const string Superseded = "superseded";
}

/// <summary>
/// Which fields of a feature's history a restore may put back, and which of them write a position.
/// </summary>
/// <remarks>
/// <para>
/// A restore is an ordinary edit whose values come out of the history, so it writes what the
/// kind's own edit form writes and nothing else. Left out on purpose: who may read the row
/// (visibility, caving group, the protection flag — each has its own guarded path, and an old
/// value of one could widen an audience), where it sits (containment), whether it is deleted (the
/// feature's own restore), its owner and every value the write path keeps on its own (timestamps,
/// the ancestor closure, the schema version stamp, a cave's entrance count and point, which mirror
/// its entrances). A centerline restores nothing: it is rebuilt from its survey file.
/// </para>
/// <para>
/// The category of a generic feature follows its feature type. A change that moved both is not
/// put back here — the type is not one of the fields a restore writes — so the category of such a
/// change is reported as not restorable rather than written apart from its type.
/// </para>
/// </remarks>
public static class FeatureHistoryFields
{
    private static readonly HashSet<string> Common =
        [nameof(Feature.Name), nameof(Feature.Description), nameof(Feature.Properties)];

    private static readonly HashSet<string> Generic =
        [.. Common, nameof(Feature.Category), nameof(Feature.Geom)];

    private static readonly HashSet<string> Entrance =
        [.. Common, nameof(Feature.Geom), nameof(CaveEntrance.Altitude), nameof(CaveEntrance.PositionQuality),
         nameof(CaveEntrance.EntranceTypeId), nameof(CaveEntrance.SurveyedAt), nameof(CaveEntrance.IsMain)];

    private static readonly HashSet<string> CaveFields =
        [.. Common,
         nameof(Cave.OtherToponyms), nameof(Cave.IdentificationCode), nameof(Cave.CaveTypeId), nameof(Cave.Website),
         nameof(Cave.Region), nameof(Cave.HydrographicBasin), nameof(Cave.Valley), nameof(Cave.TributaryRiver),
         nameof(Cave.ClosestAddress), nameof(Cave.LandRegistryNumber), nameof(Cave.LocationNotes),
         nameof(Cave.RockTypeId), nameof(Cave.RockAge), nameof(Cave.SurveyedLength), nameof(Cave.EstimatedLength),
         nameof(Cave.RealExtension), nameof(Cave.ProjectedExtension), nameof(Cave.Depth), nameof(Cave.PositiveDepth),
         nameof(Cave.NegativeDepth), nameof(Cave.PotentialDepth), nameof(Cave.Altitude), nameof(Cave.Volume),
         nameof(Cave.Area), nameof(Cave.RamificationIndex), nameof(Cave.CaveAge), nameof(Cave.ExplorationStatus),
         nameof(Cave.ProtectionClass), nameof(Cave.IsShowCave), nameof(Cave.ShowCaveLength),
         nameof(Cave.DiscoveryDate), nameof(Cave.Discoverer)];

    private static readonly HashSet<string> Positional =
        [nameof(Feature.Geom), nameof(CaveEntrance.Altitude), nameof(CaveEntrance.PositionQuality)];

    /// <summary>Whether a restore may write this field of a feature of this kind.</summary>
    public static bool IsRestorable(FeatureKind kind, string field) => kind switch
    {
        FeatureKind.Generic => Generic.Contains(field),
        FeatureKind.CaveEntrance => Entrance.Contains(field),
        FeatureKind.Cave => CaveFields.Contains(field),
        _ => false,
    };

    /// <summary>
    /// Whether putting this value back writes a position — refused to a caller without exact view,
    /// exactly as an ordinary edit refuses it. Promoting an entrance to the main one counts: it
    /// moves the cave's own point onto that entrance. Demoting one does not, as in the edit form.
    /// A cave's own columns never do — its point is its main entrance's.
    /// </summary>
    public static bool IsPositionWrite(FeatureKind kind, string field, JsonNode? value) => kind switch
    {
        FeatureKind.Generic => field == nameof(Feature.Geom),
        FeatureKind.CaveEntrance => Positional.Contains(field)
            || (field == nameof(CaveEntrance.IsMain) && value is JsonValue v && v.TryGetValue<bool>(out var main) && main),
        _ => false,
    };

    /// <summary>
    /// Whether a recorded value is a geometry summary rather than the geometry itself — the shape
    /// of <see cref="Geo.AuditGeometry.Summary"/>.
    /// </summary>
    public static bool IsGeometrySummary(JsonNode? value) =>
        value is JsonObject summary && summary["omitted"] is JsonValue omitted
        && omitted.TryGetValue<bool>(out var yes) && yes;

    /// <summary>The fingerprint a geometry summary carries, or null.</summary>
    public static string? SummaryFingerprint(JsonNode? value) =>
        value is JsonObject summary && summary["sha256"] is JsonValue hash && hash.TryGetValue<string>(out var text)
            ? text
            : null;
}

/// <summary>
/// Reads a value back out of a history diff into the type of the column it was recorded from —
/// the inverse of how the history writes one: enums by their camelCase name, geometry as
/// well-known text, id and text lists comma-joined, everything else as itself.
/// </summary>
public static class HistoryValues
{
    /// <summary>
    /// The recorded value as a value of <paramref name="type"/>, or false when it cannot be one —
    /// a null for a column that takes none, a name no member of the enum has any more, a summary
    /// where a geometry is wanted (the caller reads the kept copy for that).
    /// </summary>
    public static bool TryDecode(JsonNode? recorded, Type type, out object? value)
    {
        value = null;
        var underlying = Nullable.GetUnderlyingType(type);
        var target = underlying ?? type;
        if (recorded is null)
        {
            return underlying is not null || !type.IsValueType;
        }

        if (recorded is not JsonValue)
        {
            return false;
        }

        // Read through the JSON form, so a value parsed from a stored diff and one built in code
        // decode alike.
        var element = JsonSerializer.SerializeToElement(recorded);
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        try
        {
            if (typeof(Geometry).IsAssignableFrom(target))
            {
                if (text is null)
                {
                    return false;
                }

                var geometry = new WKTReader().Read(text);
                geometry.SRID = 4326;
                value = geometry;
                return target.IsInstanceOfType(geometry);
            }

            if (target.IsEnum)
            {
                if (text is not null
                    && Enum.TryParse(target, text, ignoreCase: true, out var member)
                    && Enum.IsDefined(target, member))
                {
                    value = member;
                    return true;
                }

                return false;
            }

            if (target == typeof(string))
            {
                return text is not null && Assign(text, out value);
            }

            if (target == typeof(Guid))
            {
                return Guid.TryParse(text, out var id) && Assign(id, out value);
            }

            if (target == typeof(DateOnly))
            {
                return DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date) && Assign(date, out value);
            }

            if (target == typeof(DateTimeOffset))
            {
                return element.ValueKind == JsonValueKind.String
                    && element.TryGetDateTimeOffset(out var at) && Assign(at, out value);
            }

            if (target == typeof(bool))
            {
                return element.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && Assign(element.GetBoolean(), out value);
            }

            if (target == typeof(decimal) || target == typeof(long) || target == typeof(int)
                || target == typeof(short) || target == typeof(double))
            {
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var number))
                {
                    return false;
                }

                value = Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is ParseException or FormatException or OverflowException or ArgumentException)
        {
            value = null;
            return false;
        }

        return false;
    }

    /// <summary>
    /// Whether a column still holds the value the history recorded for it — the question an undo
    /// asks before it puts an older value back, so that an edit made after the upload is not
    /// thrown away. Compared as the history wrote the value, not as text: a property document by
    /// its content (the database rewrites a document's spacing and key order on the way in), a
    /// shape by the text or the fingerprint it was recorded as, a moment at the microsecond the
    /// database keeps, and everything else by value (so 12.5 and 12.50 are one altitude).
    /// </summary>
    /// <param name="recorded">The value as the history recorded it.</param>
    /// <param name="current">The column's value now.</param>
    /// <param name="type">The column's type.</param>
    /// <param name="document">Whether the column holds a JSON document stored as text.</param>
    public static bool StillRecorded(JsonNode? recorded, object? current, Type type, bool document)
    {
        if (recorded is null || current is null)
        {
            return recorded is null && current is null;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (typeof(Geometry).IsAssignableFrom(target))
        {
            if (current is not Geometry shape)
            {
                return false;
            }

            if (FeatureHistoryFields.IsGeometrySummary(recorded))
            {
                return Geo.AuditGeometry.Matches(shape, FeatureHistoryFields.SummaryFingerprint(recorded));
            }

            return recorded is JsonValue text && text.TryGetValue<string>(out var wkt)
                && Geo.AuditGeometry.Record(shape) is string now && string.Equals(now, wkt, StringComparison.Ordinal);
        }

        if (document)
        {
            if (recorded is not JsonValue value || !value.TryGetValue<string>(out var then) || current is not string text)
            {
                return false;
            }

            try
            {
                return JsonNode.DeepEquals(JsonNode.Parse(then), JsonNode.Parse(text));
            }
            catch (JsonException)
            {
                return string.Equals(then, text, StringComparison.Ordinal);
            }
        }

        if (!TryDecode(recorded, type, out var decoded) || decoded is null)
        {
            return false;
        }

        if (decoded is DateTimeOffset at && current is DateTimeOffset stored)
        {
            return at.UtcTicks / 10 == stored.UtcTicks / 10;
        }

        return decoded.Equals(current);
    }

    private static bool Assign(object item, out object? value)
    {
        value = item;
        return true;
    }
}
