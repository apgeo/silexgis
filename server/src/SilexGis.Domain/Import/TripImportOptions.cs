// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Import;

/// <summary>What the reviewer decided about one row of the sheet.</summary>
public enum TripImportRowAction
{
    /// <summary>Record the trip this row describes.</summary>
    Create = 0,

    /// <summary>Leave the row alone. Nothing is created from it and it is not counted as a failure.</summary>
    Skip = 1,
}

/// <summary>
/// One row's overrides. Every member is optional and absent means "as proposed", so a reviewer
/// who agrees with the whole sheet stores nothing at all.
/// </summary>
public sealed record TripImportDecision
{
    /// <summary>Null follows what the row proposes, which is to record it.</summary>
    public TripImportRowAction? Action { get; init; }
}

/// <summary>
/// The choices that apply to a whole trip spreadsheet: how it is read, and what its rows become.
///
/// <para>
/// The reading half is the parser's own settings expressed in a form a browser can send — a
/// delimiter and separator characters as strings rather than as <c>char</c>, and the column
/// mapping as header names rather than indexes, because an index stops meaning anything the
/// moment somebody inserts a column. The becoming half is answered once for every row: an import
/// binds everything it creates to the same group and the same visibility, so asking per row would
/// be asking a thousand times for one answer.
/// </para>
/// <para>
/// The three creation switches are off by default and each one covers only what did *not* match:
/// matching against what the installation already holds happens either way. A switch that is off
/// does not lose the text — the unmatched name is carried into the trip's own words instead.
/// </para>
/// </summary>
public sealed record TripImportOptions
{
    /// <summary>
    /// The field separator, as a one-character string. Pinned rather than sniffed: scoring
    /// candidate characters by frequency lets one free-text column full of semicolons flip the
    /// reading of every row while the result still looks like a successful parse.
    /// </summary>
    public string Delimiter { get; init; } = ",";

    /// <summary>The characters that separate several values inside one cell, written as one string.</summary>
    public string MultiValueSeparators { get; init; } = ";,";

    /// <summary>Fields where a forward slash also separates values. Opt-in per column.</summary>
    public IReadOnlyList<TripCsvField> SlashSeparatedFields { get; init; } = [];

    /// <summary>The day/month order to read numeric dates in where the file itself cannot settle it.</summary>
    public TripCsvDateOrder DateOrder { get; init; } = TripCsvDateOrder.DayFirst;

    /// <summary>
    /// Header names chosen by hand, by field. A field absent here is detected from the built-in
    /// spellings; a field named here is looked for under that name only.
    /// </summary>
    public IReadOnlyDictionary<TripCsvField, string> Columns { get; init; } =
        new Dictionary<TripCsvField, string>();

    /// <summary>
    /// Visibility every created trip starts with. The most restrictive value rather than the
    /// installation's usual default: an import is a bulk act, and a bulk act that publishes by
    /// default publishes a club's whole history at once.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Private;

    /// <summary>The group every created trip is organised by.</summary>
    public Guid? CavingGroupId { get; init; }

    /// <summary>An unmatched cave name becomes a cave feature rather than only text.</summary>
    public bool CreateMissingCaves { get; init; }

    /// <summary>An unmatched massif or sub-area becomes an area feature rather than only text.</summary>
    public bool CreateMissingAreas { get; init; }

    /// <summary>An unmatched participant becomes a roster entry rather than only text.</summary>
    public bool CreateMissingCavers { get; init; }

    /// <summary>An unmatched value of the type column may be offered as a new trip type.</summary>
    ///
    /// <remarks>
    /// There is no switch beside it for the job somebody did on a trip, and there is not meant to
    /// be: this shape of spreadsheet has no column saying so. Everyone it lists either went or put
    /// the trip forward, which are the two roles every installation ships, so a switch offering to
    /// invent more would be a control over nothing.
    /// </remarks>
    public bool CreateMissingTripTypes { get; init; }

    /// <summary>
    /// Type values the reviewer pointed at a term by hand, keyed by the text the sheet wrote.
    /// This is the answer to "that column says <c>topo</c> and it means the surveying type you
    /// already have" — a statement about a value of the column rather than about one row, because
    /// the same word on the ninth page means the same thing it meant on the first.
    /// </summary>
    public IReadOnlyDictionary<string, long> TripTypeChoices { get; init; } =
        new Dictionary<string, long>();

    /// <summary>
    /// The name a type value gets when it is added to the vocabulary, keyed by the text the sheet
    /// wrote. Absent means the sheet's own words. A vocabulary is append-only and every trip form
    /// shows it, so the one chance to spell a term properly is before it is written.
    /// </summary>
    public IReadOnlyDictionary<string, string> TripTypeNames { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Names the reviewer settled by hand onto one roster entry, keyed by the text the sheet
    /// wrote. This is how a name two people answer to stops being unresolved — the importer
    /// refuses to guess, and this is the person saying which.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> CaverChoices { get; init; } =
        new Dictionary<string, Guid>();

    /// <summary>
    /// Cave or area names the reviewer settled by hand onto one feature, keyed by the text the
    /// sheet wrote.
    /// </summary>
    /// <remarks>
    /// Honoured only where the chosen feature is one this caller was already offered under that
    /// name. A choice settles an ambiguity the importer raised; it is never a way to name an
    /// identifier and have a trip link to it, because that reading would turn the request body
    /// into a path around both disclosure gates.
    /// </remarks>
    public IReadOnlyDictionary<string, Guid> FeatureChoices { get; init; } =
        new Dictionary<string, Guid>();

    public static TripImportOptions Default { get; } = new();

    /// <summary>The parser's own settings, as these choices spell them.</summary>
    public TripCsvOptions ToParserOptions() => new()
    {
        Delimiter = OneChar(Delimiter, TripCsvOptions.Default.Delimiter),
        MultiValueSeparators = MultiValueSeparators.Length == 0
            ? TripCsvOptions.Default.MultiValueSeparators
            : [.. MultiValueSeparators.Distinct()],
        SlashSeparatedFields = new HashSet<TripCsvField>(SlashSeparatedFields),
        DateOrder = DateOrder,
    };

    /// <summary>The column mapping, with blank names dropped so an emptied box means "detect it".</summary>
    public TripCsvColumnMapping ToMapping() => new()
    {
        Columns = Columns
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value),
    };

    /// <summary>The term the reviewer pointed this type value at, if they pointed it anywhere.</summary>
    public long? ChosenTripTypeId(string? source) =>
        Chosen(TripTypeChoices, source, out var id) ? id : null;

    /// <summary>The name the reviewer gave this type value, if they renamed it.</summary>
    public string? ChosenTripTypeName(string? source)
    {
        if (!Chosen(TripTypeNames, source, out var name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>The roster entry the reviewer settled this name on, if they settled it.</summary>
    public Guid? ChosenCaverId(string? source) =>
        Chosen(CaverChoices, source, out var id) ? id : null;

    /// <summary>The feature the reviewer settled this place name on, if they settled it.</summary>
    public Guid? ChosenFeatureId(string? source) =>
        Chosen(FeatureChoices, source, out var id) ? id : null;

    /// <summary>
    /// A choice looked up by the folded form of the sheet's text on both sides. The browser sends
    /// back whatever the sheet wrote, and a value that differs from the stored key only by a
    /// diacritic or a doubled space is the same value — the same rule that made them one entry in
    /// the candidate list in the first place.
    /// </summary>
    private static bool Chosen<TValue>(
        IReadOnlyDictionary<string, TValue> choices, string? source, out TValue value)
    {
        value = default!;
        var key = TripImportNames.Key(source);
        if (key.Length == 0 || choices.Count == 0)
        {
            return false;
        }

        foreach (var (written, chosen) in choices)
        {
            if (TripImportNames.Key(written) == key)
            {
                value = chosen;
                return true;
            }
        }

        return false;
    }

    private static char OneChar(string value, char fallback) => value.Length == 1 ? value[0] : fallback;
}
