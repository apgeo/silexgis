// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// Which header carries which field, for a sheet of tracking reports.
/// </summary>
/// <remarks>
/// The same contract the trip sheet's mapping carries, and deliberately the same wording: every
/// entry is optional, an unnamed field is looked for among the built-in candidates, and a field
/// named here is looked for under that name <b>only</b>. A named header the file does not have
/// yields nothing rather than falling back to a candidate — a mapping somebody chose is an
/// instruction, and quietly substituting a different column for it is how a whole import ends up
/// filed under the wrong thing.
/// </remarks>
public sealed record TrackingCsvColumnMapping
{
    /// <summary>Header names chosen by hand, by field. A field absent from this map is auto-detected.</summary>
    public IReadOnlyDictionary<TrackingCsvField, string> Columns { get; init; } =
        new Dictionary<TrackingCsvField, string>();

    /// <summary>An empty mapping: every field is auto-detected from the built-in candidates.</summary>
    public static TrackingCsvColumnMapping Auto { get; } = new();

    public TrackingCsvColumnMapping With(TrackingCsvField field, string header)
    {
        var columns = new Dictionary<TrackingCsvField, string>(Columns) { [field] = header };
        return this with { Columns = columns };
    }

    /// <summary>The header chosen for a field, or null when it is left to detection.</summary>
    public string? NamedHeader(TrackingCsvField field) =>
        Columns.TryGetValue(field, out var header) && !string.IsNullOrWhiteSpace(header) ? header : null;

    /// <summary>
    /// Header spellings under which one column carries both the date and the time of day — the
    /// layout the moment is read from.
    /// </summary>
    public static IReadOnlyList<string> DateAndTimeHeaders { get; } =
        ["data si ora", "data/ora", "data ora", "moment", "datetime", "date and time", "timestamp", "when"];

    /// <summary>
    /// Header spellings that promise a date and say nothing about the time of day.
    /// </summary>
    /// <remarks>
    /// Tried after the combined spellings and before the time-only ones, so that a sheet keeping
    /// its date and its time in two columns has the moment read from the date column: every row
    /// then says its time is missing, which is true, rather than that "08:15" is not a time, which
    /// is not. The parser recognises that layout as a whole and refuses the file once, naming both
    /// columns, since nothing on a mapping screen can join two columns into one.
    /// </remarks>
    public static IReadOnlyList<string> DateOnlyHeaders { get; } = ["data", "date"];

    /// <summary>Header spellings that promise a time of day and say nothing about the date.</summary>
    public static IReadOnlyList<string> TimeOnlyHeaders { get; } = ["ora", "timp", "time"];

    /// <summary>
    /// Header spellings recognised without being told, best first.
    /// </summary>
    /// <remarks>
    /// Written in the folded form headers are compared in — lower case, no diacritics — because a
    /// Romanian sheet saved by one tool writes <c>adâncime</c> and by another <c>adancime</c>, and
    /// a candidate list carrying both spellings of every word is a list nobody keeps correct.
    /// </remarks>
    public static IReadOnlyList<string> CandidatesFor(TrackingCsvField field) => field switch
    {
        TrackingCsvField.RecordedAt => [.. DateAndTimeHeaders, .. DateOnlyHeaders, .. TimeOnlyHeaders],
        TrackingCsvField.Depth => ["adancime", "adancimea", "cota", "depth", "elevation", "m"],
        TrackingCsvField.Station => ["statie", "statia", "punct", "punctul", "station", "point"],
        TrackingCsvField.Place =>
            ["loc", "locul", "locatie", "locatia", "nume loc", "denumire", "denumirea locului",
             "reper", "toponim", "place", "place name", "location", "landmark"],
        TrackingCsvField.Cavers =>
            ["speologi", "speolog", "participanti", "participant", "echipa membri", "membri",
             "nume", "cavers", "caver", "people", "names"],
        TrackingCsvField.Team => ["echipa", "echipe", "grup", "grupa", "team", "party", "group"],
        TrackingCsvField.Note => ["nota", "note", "notite", "observatii", "notes", "remark"],
        TrackingCsvField.Details => ["detalii", "detalii suplimentare", "descriere", "details", "more details"],
        TrackingCsvField.State =>
            ["stare", "starea", "tip", "actiune", "state", "status", "action", "kind"],
        _ => [],
    };

    /// <summary>
    /// Every field. The parser walks this twice — once claiming the headers somebody named by
    /// hand, then once detecting the rest — so a field's place in this list decides nothing about
    /// whether a mapping is honoured, only which of two mappings naming one header wins.
    /// </summary>
    public static IReadOnlyList<TrackingCsvField> AllFields { get; } = Enum.GetValues<TrackingCsvField>();
}
