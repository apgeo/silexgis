// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>One class of the lithology vocabulary a mapped geological unit is described by.</summary>
/// <param name="Code">The stable code stored in a unit's properties and sent on the wire.</param>
/// <param name="Karstifiable">
/// Whether the rock dissolves readily enough to host karst: the carbonates, the evaporites and the
/// calcareous conglomerates. It is what turns a geology map into a karst denominator — the ground a
/// cave could be in rather than the ground an area happens to cover.
/// </param>
public sealed record LithologyClass(string Code, bool Karstifiable);

/// <summary>
/// The closed vocabulary of rock classes a geological unit carries, and the one place that says
/// which of them are karstifiable.
/// </summary>
/// <remarks>
/// <para>
/// The list lives in code rather than only in the stored properties schema because the schema of a
/// shipped kind is editable by an administrator and is never re-seeded once set. A vocabulary held
/// only there would not be closed, and a statistic that read "karstifiable" from it would change its
/// meaning when somebody edited a form. The schema is built from this list at seed time; statistics
/// read this list and treat any other stored value as unrecognised rather than guessing.
/// </para>
/// <para>
/// The order is the order the classes are offered in, carbonates and evaporites first. A code once
/// shipped is never renamed: stored rows and imported maps carry it.
/// </para>
/// </remarks>
public static class Lithology
{
    /// <summary>The typed property a unit's lithology class is stored under.</summary>
    public const string PropertyKey = "lithology";

    public static readonly IReadOnlyList<LithologyClass> Classes =
    [
        new("limestone", true),
        new("dolomite", true),
        new("marble", true),
        new("gypsum_anhydrite", true),
        new("salt", true),
        new("conglomerate_calcareous", true),
        new("sandstone", false),
        new("shale_marl", false),
        new("granite", false),
        new("crystalline_schist", false),
        new("volcanic", false),
        new("unconsolidated", false),
        // "Other" is not counted as karstifiable: a statistic that cannot say what the rock is
        // must not add its ground to the area caves are expected in.
        new("other", false),
    ];

    private static readonly Dictionary<string, LithologyClass> ByCode =
        Classes.ToDictionary(c => c.Code, StringComparer.Ordinal);

    /// <summary>The class a stored code names, or null for a code outside the vocabulary.</summary>
    public static LithologyClass? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var found) ? found : null;

    /// <summary>
    /// The lithology a unit's stored properties carry, as stored — a vocabulary code, or another
    /// text somebody put there — or null when the unit has none or the bag is not readable.
    /// </summary>
    public static string? ReadStoredCode(string json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject bag
                && bag.TryGetPropertyValue(PropertyKey, out var node)
                && node is System.Text.Json.Nodes.JsonValue value
                && value.TryGetValue<string>(out var code)
                && !string.IsNullOrWhiteSpace(code)
                    ? code
                    : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The typed-properties schema the geological-unit kind is seeded with: the lithology class as
    /// a closed enum, and the formation, age and source as free text. Nothing here locates the unit
    /// — a feature's properties reach every reader who may see the row, including one whose
    /// geometry was withheld — so the bag must never carry a position.
    /// </summary>
    public static string PropertiesSchemaJson()
    {
        var codes = string.Join(",", Classes.Select(c => "\"" + c.Code + "\""));
        return "{\"type\":\"object\",\"properties\":{"
            + "\"" + PropertyKey + "\":{\"type\":\"string\",\"title\":\"Lithology\",\"enum\":[" + codes + "]},"
            + "\"formation\":{\"type\":\"string\",\"title\":\"Formation\"},"
            + "\"age\":{\"type\":\"string\",\"title\":\"Age\"},"
            + "\"source\":{\"type\":\"string\",\"title\":\"Source\"}"
            + "}}";
    }
}

/// <summary>Ground of one lithology inside an area, and the entrances found on it.</summary>
/// <param name="Code">The stored class code; null for units nobody classified.</param>
/// <param name="AreaM2">The class's outcrop inside the area outline, overlaps within the class merged.</param>
/// <param name="EntranceCount">Entrances inside that outcrop.</param>
public sealed record LithologyOutcrop(string? Code, double AreaM2, int EntranceCount);

/// <summary>One class's row of the per-lithology split, with its density.</summary>
/// <param name="Code">The stored code, or null for unclassified units.</param>
/// <param name="Recognised">False for a stored code outside the vocabulary.</param>
/// <param name="Karstifiable">Null when the class is unknown — unclassified or unrecognised.</param>
/// <param name="AreaShare">The class's share of the area outline, or null when the outline has no area.</param>
/// <param name="EntrancesPerKm2">Entrances over the class's own outcrop, or null when it has none.</param>
public sealed record LithologyShare(
    string? Code,
    bool Recognised,
    bool? Karstifiable,
    double AreaKm2,
    double? AreaShare,
    int EntranceCount,
    double? EntrancesPerKm2);

/// <summary>The per-lithology split of an area, with the karstifiable outcrop totals.</summary>
/// <param name="UnmappedAreaKm2">Outline ground no unit covers.</param>
/// <param name="UnmappedEntranceCount">Entrances on that ground.</param>
/// <param name="KarstifiableEntrancesPerKm2">
/// Entrances on karstifiable outcrop divided by that outcrop — the outcrop-normalised density, which
/// compares two areas fairly where a density over the whole outline would reward the one with less
/// sandstone in it. Null when no karstifiable ground is mapped.
/// </param>
public sealed record LithologyBreakdown(
    IReadOnlyList<LithologyShare> Classes,
    double MappedAreaKm2,
    double UnmappedAreaKm2,
    int UnmappedEntranceCount,
    double KarstifiableAreaKm2,
    int KarstifiableEntranceCount,
    double? KarstifiableEntrancesPerKm2);

public static class LithologyStatistics
{
    private const double SquareMetresPerKm2 = 1_000_000d;

    /// <summary>
    /// Turns the measured outcrops into the per-class split. Classes come out largest first; the
    /// karstifiable totals add only classes the vocabulary marks so, never an unknown one.
    /// </summary>
    /// <param name="outlineAreaM2">The area outline's own ground area, the share denominator.</param>
    /// <param name="unmapped">Outline ground no unit covers, and the entrances on it.</param>
    public static LithologyBreakdown Summarize(
        IEnumerable<LithologyOutcrop> outcrops,
        double? outlineAreaM2,
        LithologyOutcrop unmapped)
    {
        ArgumentNullException.ThrowIfNull(outcrops);
        ArgumentNullException.ThrowIfNull(unmapped);

        var shares = new List<LithologyShare>();
        double mappedM2 = 0, karstM2 = 0;
        var karstEntrances = 0;

        foreach (var o in outcrops.OrderByDescending(o => o.AreaM2).ThenBy(o => o.Code, StringComparer.Ordinal))
        {
            if (!double.IsFinite(o.AreaM2) || o.AreaM2 < 0 || o.EntranceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outcrops), "an outcrop's area and count must be non-negative.");
            }

            var cls = Lithology.Find(o.Code);
            mappedM2 += o.AreaM2;
            if (cls is { Karstifiable: true })
            {
                karstM2 += o.AreaM2;
                karstEntrances += o.EntranceCount;
            }

            shares.Add(new LithologyShare(
                o.Code,
                cls is not null,
                cls?.Karstifiable,
                o.AreaM2 / SquareMetresPerKm2,
                outlineAreaM2 is > 0 and var total ? o.AreaM2 / total : null,
                o.EntranceCount,
                o.AreaM2 > 0 ? o.EntranceCount / (o.AreaM2 / SquareMetresPerKm2) : null));
        }

        return new LithologyBreakdown(
            shares,
            mappedM2 / SquareMetresPerKm2,
            unmapped.AreaM2 / SquareMetresPerKm2,
            unmapped.EntranceCount,
            karstM2 / SquareMetresPerKm2,
            karstEntrances,
            karstM2 > 0 ? karstEntrances / (karstM2 / SquareMetresPerKm2) : null);
    }
}
