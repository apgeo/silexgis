// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Catalogue;

/// <summary>
/// What one catalogue record becomes once it is read as a cave of this application.
/// Field-for-field, and nothing else: creating the rows is the writer's job, and keeping the two
/// apart is what lets the whole translation be tested without a database.
/// </summary>
/// <param name="Name">The cave's name, trimmed to what the column holds.</param>
/// <param name="Description">Description text plus the provenance footer, or just the footer when the source had no description.</param>
/// <param name="Region">Mountain range.</param>
/// <param name="ClosestAddress">Nearest locality. A redacted field under location protection, which is where a locality belongs.</param>
/// <param name="Website">The cave's page in the source catalogue.</param>
/// <param name="Altitude">Entrance altitude in metres.</param>
/// <param name="SurveyedLength">Surveyed length in metres.</param>
/// <param name="Depth">Total vertical range in metres.</param>
/// <param name="NegativeDepth">Downward vertical range in metres, as a magnitude.</param>
/// <param name="ProtectionClass">Statutory protection class as a bare letter.</param>
/// <param name="Properties">The source's own codes, as the flat prefixed keys the properties bag takes.</param>
public sealed record SpeologieCaveValues(
    string Name,
    string? Description,
    string? Region,
    string? ClosestAddress,
    string? Website,
    decimal? Altitude,
    decimal? SurveyedLength,
    decimal? Depth,
    decimal? NegativeDepth,
    string? ProtectionClass,
    JsonObject Properties);

/// <summary>
/// Reads a <see cref="SpeologieRecord"/> as the fields of a cave in this application.
///
/// <para>
/// Three rules decide where every value lands, and they are worth stating because two of them
/// are not obvious and one of them is a hard prohibition.
/// </para>
/// <para>
/// <b>Altitude goes to its column and never to the properties bag.</b> The bag is serialised
/// straight onto the wire, including on the path that answers a share link to someone who has
/// not signed in, and there is no protection filter along that path to extend. An altitude is a
/// coordinate component; parking one in the bag would route around the whole location-protection
/// mechanism for every cave that carried it.
/// </para>
/// <para>
/// <b>A locality goes to the redacted column, not to the bag and not to the description.</b>
/// <c>closest_address</c> is withheld from a caller who may not see a protected cave's exact
/// position; the description is not. Naming the village a protected cave sits above, in a field
/// that is shown to everyone, would give away most of what the protection exists to withhold —
/// so the locality is the one piece of source text that is not summarised into the footer.
/// </para>
/// <para>
/// <b>A code with no published legend is carried, not guessed.</b> The catalogue's <c>roca</c>
/// is <c>00</c> on four rows in five and no legend for it is published anywhere. Deciding it
/// means limestone would fill in a rock type on thousands of caves on the strength of an
/// assumption, and a field that looks surveyed and is not is worse than an empty one. It is kept
/// verbatim under its own key and left for a person, or for a legend the catalogue may yet
/// publish.
/// </para>
/// </summary>
public static class SpeologieMapping
{
    /// <summary>
    /// The keys this integration writes into a feature's properties bag. Flat and prefixed, so
    /// that what a foreign system said is always distinguishable from what this one decided, and
    /// so a later integration cannot collide with them.
    /// </summary>
    public static class Keys
    {
        /// <summary>The catalogue's numeric id, as text. The key an import recognises a cave by.</summary>
        public const string Id = "speologieId";

        public const string Slug = "speologieSlug";
        public const string Url = "speologieUrl";

        /// <summary>Two-letter county code. Kept as the catalogue's code, not expanded to a name.</summary>
        public const string County = "speologieJudet";

        public const string HydroNumber = "speologieNrHidro";
        public const string HydroBasinId = "speologieBazinHidroId";
        public const string RockCode = "speologieRoca";
        public const string Sump = "speologieScufundabila";
        public const string Science = "speologieStiinta";
        public const string ProtectedAreaCode = "speologieCodAp";
        public const string Vanished = "speologieDisparuta";

        /// <summary>When this row was read from the catalogue, so a stale copy can be recognised as one.</summary>
        public const string RetrievedAt = "speologieRetrievedAt";
    }

    /// <summary>Largest value the morphometry columns hold — <c>decimal(10,2)</c>.</summary>
    private const decimal MetricCeiling = 99_999_999.99m;

    private const int NameMax = 255;
    private const int RegionMax = 100;
    private const int AddressMax = 200;
    private const int ProtectionClassMax = 50;
    private const int WebsiteMax = 500;

    /// <summary>
    /// Translates one record. <paramref name="maxDescriptionChars"/> bounds the converted
    /// description before the footer is added, so the footer is never the part that gets cut —
    /// losing the line that says where a record came from would be the worst possible truncation.
    /// </summary>
    public static SpeologieCaveValues ToCaveValues(
        SpeologieRecord record, DateTimeOffset retrievedAt, int maxDescriptionChars)
    {
        ArgumentNullException.ThrowIfNull(record);

        var body = HtmlToText.Convert(record.Descriere, maxDescriptionChars);
        var footer = Footer(record);

        var description = (body, footer) switch
        {
            (null, var f) => f,
            (var b, null) => b,
            var (b, f) => $"{b}\n\n{f}",
        };

        return new SpeologieCaveValues(
            Name: Clip(Blank(record.Title) ?? $"#{record.Id}", NameMax)!,
            Description: description,
            Region: Clip(Blank(record.Munte), RegionMax),
            ClosestAddress: Clip(Blank(record.Localitate), AddressMax),
            Website: Clip(record.PublicUrl, WebsiteMax),
            Altitude: Metric(record.Altitudine),
            SurveyedLength: Metric(record.Lungime),
            Depth: Metric(record.Denivelare),
            // The catalogue's sign convention for the downward range is not consistent — most
            // rows are positive and a few are negative. Both mean the same distance downwards,
            // so the magnitude is stored and the disagreement is not propagated.
            NegativeDepth: Metric(record.DenNegativa is { } d ? Math.Abs(d) : null),
            ProtectionClass: ProtectionClassOf(record.Clasificare),
            Properties: PropertiesOf(record, retrievedAt));
    }

    /// <summary>
    /// The catalogue's own codes, as the properties bag takes them. Absent values are absent
    /// keys rather than nulls: a key present with no value reads as "the catalogue says this is
    /// empty", which is a stronger claim than the catalogue makes.
    /// </summary>
    private static JsonObject PropertiesOf(SpeologieRecord record, DateTimeOffset retrievedAt)
    {
        var o = new JsonObject
        {
            // Text rather than a number, so the expression index that finds an already-imported
            // cave and the query that reads it compare the same thing without a cast.
            [Keys.Id] = record.Id.ToString(CultureInfo.InvariantCulture),
            [Keys.RetrievedAt] = retrievedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };

        Put(o, Keys.Slug, Blank(record.Slug));
        Put(o, Keys.Url, record.PublicUrl);
        Put(o, Keys.County, Blank(record.Judet)?.ToUpperInvariant());
        Put(o, Keys.HydroNumber, Blank(record.NrHidro));
        Put(o, Keys.RockCode, Blank(record.Roca));
        Put(o, Keys.Science, ScienceOf(record.Stiinta));
        Put(o, Keys.ProtectedAreaCode, Blank(record.CodAp));

        if (record.BazinHidroId is { } basin)
        {
            o[Keys.HydroBasinId] = basin;
        }

        if (SumpOf(record.Scufundabila) is { } sump)
        {
            o[Keys.Sump] = sump;
        }

        if (record.Disparuta is true)
        {
            o[Keys.Vanished] = true;
        }

        return o;
    }

    /// <summary>
    /// The line that says where the cave came from, and carries the codes that have no column of
    /// their own in a form a person reads rather than a form a program parses. The labels are the
    /// catalogue's own Romanian ones: these are Romanian codes from a Romanian register, and
    /// translating the label while keeping the value would make the pair harder to check against
    /// the source, not easier.
    /// </summary>
    private static string? Footer(SpeologieRecord record)
    {
        var parts = new List<string>();

        Add(parts, "Județ", Blank(record.Judet)?.ToUpperInvariant());
        Add(parts, "Nr. hidro", Blank(record.NrHidro));
        Add(parts, "Bazin hidro", record.BazinHidroId?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "Rocă (cod)", Blank(record.Roca));
        Add(parts, "Scufundabilă", SumpOf(record.Scufundabila) switch { true => "da", false => "nu", _ => null });
        Add(parts, "Cod arie protejată", Blank(record.CodAp));
        Add(parts, "Științific", ScienceOf(record.Stiinta));

        if (record.Disparuta is true)
        {
            parts.Add("Dispărută");
        }

        var origin = record.PublicUrl is { } url
            ? $"speologie.org #{record.Id} · {url}"
            : $"speologie.org #{record.Id}";

        var builder = new StringBuilder("— ").Append(origin);

        if (parts.Count > 0)
        {
            builder.Append('\n').Append(string.Join(" · ", parts));
        }

        return builder.ToString();

        static void Add(List<string> into, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                into.Add($"{label}: {value}");
            }
        }
    }

    /// <summary>
    /// <c>clasaB</c> becomes <c>B</c>. The classes are the statutory A/B/C/D, and the column is
    /// free text everywhere else in the application, so anything that does not look like one of
    /// them is carried through unchanged rather than dropped — a value nobody recognises is still
    /// better than a value nobody has.
    /// </summary>
    private static string? ProtectionClassOf(string? raw)
    {
        var value = Blank(raw);
        if (value is null)
        {
            return null;
        }

        const string prefix = "clasa";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var letter = value[prefix.Length..].Trim();
            if (letter.Length == 1 && char.IsAsciiLetter(letter[0]))
            {
                return letter.ToUpperInvariant();
            }
        }

        return Clip(value, ProtectionClassMax);
    }

    /// <summary>
    /// The scientific-interest list arrives comma-separated with inconsistent spacing
    /// (<c>"mineralogica,ursus"</c> and <c>"mineralogica, ursus"</c> both occur). Normalised to
    /// one spacing so the same set of interests is the same string.
    /// </summary>
    private static string? ScienceOf(string? raw)
    {
        var value = Blank(raw);
        if (value is null)
        {
            return null;
        }

        var terms = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return terms.Length == 0 ? null : string.Join(", ", terms);
    }

    /// <summary>The sump flag is carried as the text <c>"0"</c> or <c>"1"</c>; anything else is not an answer.</summary>
    private static bool? SumpOf(string? raw) => Blank(raw) switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };

    /// <summary>
    /// A metre value as the morphometry columns hold it. Out-of-range values become null rather
    /// than an exception at save time: the source is a community register and a typo in it should
    /// leave one field empty, not refuse the whole cave.
    /// </summary>
    private static decimal? Metric(double? value)
    {
        // The range is checked before the cast, not after it. A double larger than decimal can
        // represent throws on conversion, and the throw would not stay in this field: the commit
        // catches the two exception types an import raises deliberately, so an arithmetic one
        // escapes the per-row guard and takes the whole confirmation down — the exact opposite of
        // what a typo in one number should cost. A community register is entirely capable of
        // holding a length of 1e30 where somebody leant on a key.
        if (value is not { } v || double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > (double)MetricCeiling)
        {
            return null;
        }

        var rounded = Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) > MetricCeiling ? null : rounded;
    }

    private static void Put(JsonObject into, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            into[key] = value;
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max].TrimEnd();

    /// <summary>
    /// Merges this integration's keys into a feature's existing bag, leaving every other key
    /// alone. Used when a cave is refreshed from the catalogue: whatever else has been written
    /// onto that cave — by another integration, by a person — is not this import's to discard.
    /// </summary>
    public static string MergeProperties(string? existingJson, JsonObject incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        JsonObject target;
        try
        {
            target = string.IsNullOrWhiteSpace(existingJson)
                ? []
                : JsonNode.Parse(existingJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // A bag that will not parse is a defect somewhere else; refusing the refresh over it
            // would strand the cave. Start from empty and keep going.
            target = [];
        }

        // Every key this integration owns is rewritten, including the ones the new record has no
        // value for — a code removed at the source has to disappear here too, or the cave keeps
        // asserting something the catalogue has retracted.
        foreach (var key in OwnedKeys)
        {
            target.Remove(key);
        }

        foreach (var (key, value) in incoming)
        {
            target[key] = value?.DeepClone();
        }

        return target.ToJsonString();
    }

    private static readonly string[] OwnedKeys =
    [
        Keys.Id, Keys.Slug, Keys.Url, Keys.County, Keys.HydroNumber, Keys.HydroBasinId,
        Keys.RockCode, Keys.Sump, Keys.Science, Keys.ProtectedAreaCode, Keys.Vanished,
        Keys.RetrievedAt,
    ];
}
