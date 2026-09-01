// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

/// <summary>
/// Which header carries which field. Every entry is optional: an unnamed field is looked for
/// among the built-in candidates, and a field named here is looked for under that name only.
///
/// <para>
/// A named header the file does not have yields nothing rather than falling back to a
/// candidate. A mapping somebody chose is an instruction, and quietly substituting a
/// different column for it is how a whole import ends up filed under the wrong thing.
/// </para>
/// </summary>
public sealed record TripCsvColumnMapping
{
    /// <summary>Header names chosen by hand, by field. A field absent from this map is auto-detected.</summary>
    public IReadOnlyDictionary<TripCsvField, string> Columns { get; init; } =
        new Dictionary<TripCsvField, string>();

    /// <summary>An empty mapping: every field is auto-detected from the built-in candidates.</summary>
    public static TripCsvColumnMapping Auto { get; } = new();

    public TripCsvColumnMapping With(TripCsvField field, string header)
    {
        var columns = new Dictionary<TripCsvField, string>(Columns) { [field] = header };
        return this with { Columns = columns };
    }

    /// <summary>The header chosen for a field, or null when it is left to detection.</summary>
    public string? NamedHeader(TripCsvField field) =>
        Columns.TryGetValue(field, out var header) && !string.IsNullOrWhiteSpace(header) ? header : null;

    /// <summary>
    /// Header spellings recognised without being told, best first. Every entry is written in the
    /// folded form headers are compared in — lower case, no diacritics — because a Romanian sheet
    /// saved by one tool writes <c>peșteri</c> and by another <c>pesteri</c>, and a candidate list
    /// carrying both spellings of every word would be a list nobody keeps correct.
    /// </summary>
    public static IReadOnlyList<string> CandidatesFor(TripCsvField field) => field switch
    {
        TripCsvField.SourceId => ["nr crt.", "nr crt", "nr. crt.", "nr.crt.", "nr", "crt", "id"],
        TripCsvField.StartDate => ["data inceput", "data de inceput", "data start", "data", "start"],
        TripCsvField.EndDate => ["data sfarsit", "data de sfarsit", "data final", "sfarsit", "end"],
        TripCsvField.Title => ["titlu", "denumire", "title", "nume tura"],
        TripCsvField.Country => ["tara", "country"],
        TripCsvField.Massif => ["masiv/zona", "masiv / zona", "masiv", "zona", "masiv-zona", "area"],
        TripCsvField.SubArea => ["subzona", "sub zona", "sub-zona", "subarea"],
        TripCsvField.Caves => ["pesteri", "pestera", "caves", "cave"],
        TripCsvField.Proposers => ["propus de", "propunator", "initiator", "proposed by"],
        TripCsvField.Participants => ["participanti", "participants", "echipa", "membri"],
        TripCsvField.Details => ["detalii", "observatii", "descriere", "details"],
        TripCsvField.Details2 => ["detalii2", "detalii 2", "detalii ii", "details2"],
        TripCsvField.TripType => ["tip", "tip tura", "tipul turei", "type"],
        TripCsvField.Errors => ["erori", "erori/observatii", "note", "errors"],
        _ => [],
    };

    /// <summary>
    /// Every field. The parser walks this twice — once claiming the headers somebody named by
    /// hand, then once detecting the rest — so a field's place in this list decides nothing about
    /// whether a mapping is honoured, only which of two mappings naming one header wins.
    /// </summary>
    public static IReadOnlyList<TripCsvField> AllFields { get; } = Enum.GetValues<TripCsvField>();
}
