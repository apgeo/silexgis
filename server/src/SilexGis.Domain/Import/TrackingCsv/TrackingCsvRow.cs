// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>Which of a row's three place columns the importer must honour.</summary>
/// <remarks>
/// A sheet may carry all three, and they can disagree. The order is fixed here rather than at the
/// point of use so that every caller resolves the same way: a named station is what the survey
/// actually contains, a place label is a name the cave itself declared for one, and a depth is the
/// weakest of the three because it has to be turned into a station by looking for the nearest one.
/// </remarks>
public enum TrackingCsvPlaceKind
{
    /// <summary>The row names no place at all.</summary>
    None,

    /// <summary>A station, written in the survey viewer's own spelling.</summary>
    Station,

    /// <summary>A place label the cave declared, which resolves to a station and a depth.</summary>
    Place,

    /// <summary>A depth in metres, to be resolved to the nearest station.</summary>
    Depth,
}

/// <summary>
/// One row of a tracking sheet, read but not yet resolved against a cave.
/// </summary>
/// <remarks>
/// Deliberately still textual about people and places. A caver is the name somebody wrote, not an
/// id; a station is a string, not a station of a model. Resolving either needs the trip, its survey
/// and its roster, none of which a parser should have to be handed — and keeping the two apart is
/// what lets a preview show a reviewer exactly what the file said beside what it matched to.
/// </remarks>
public sealed record TrackingCsvRow
{
    /// <summary>The 1-based physical line this row was read from.</summary>
    public required int Line { get; init; }

    /// <summary>The instant the report is about, where one could be read.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>
    /// Whether the date of <see cref="At"/> is the day the importer named for the sheet rather
    /// than one the row wrote. Only such rows can be on the wrong side of a midnight the sheet
    /// never marked, so only they are checked for a clock that runs backwards.
    /// </summary>
    public bool OnNamedDay { get; init; }

    /// <summary>The people the row is about, as written. One row can be about several.</summary>
    public IReadOnlyList<string> Cavers { get; init; } = [];

    /// <summary>The team named beside them, as written. Indicative: the report belongs to the caver.</summary>
    public string? Team { get; init; }

    public string? StationName { get; init; }

    public string? PlaceLabel { get; init; }

    public decimal? DepthM { get; init; }

    /// <summary>Which place column decides, under the order <see cref="TrackingCsvPlaceKind"/> sets out.</summary>
    public TrackingCsvPlaceKind Decides { get; init; }

    /// <summary>Went in or came out, where the row says so; null for the ordinary case.</summary>
    public TripPositionEventKind? State { get; init; }

    /// <summary>The note, with anything a further details column carried folded in after it.</summary>
    public string? Note { get; init; }

    public IReadOnlyList<TrackingCsvDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// What kind of event this row is, once the standing column has had its say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A standing beats a place: entering and leaving claim no station, so a row that says
    /// "intrare" is an <see cref="TripPositionEventKind.Entered"/> even where a place column is
    /// filled in beside it — which is the usual shape, because a club writes depth 0 on those rows.
    /// The place is reported as overridden rather than silently ignored when it says more than that.
    /// </para>
    /// <para>
    /// A declared place is recorded as a depth, not as a station, although resolving it produces
    /// both. What the cave declared is a (depth, station, name) triple, so the depth is part of
    /// what the sheet said and a report that kept only the station would have thrown half of it
    /// away — and the depth is also what reading the same place back out of the declarations
    /// depends on. The kind that carries a depth beside a station is
    /// <see cref="TripPositionEventKind.AtDepth"/>; the one that does not is
    /// <see cref="TripPositionEventKind.AtStation"/>, which is what a row naming a station
    /// outright becomes.
    /// </para>
    /// <para>
    /// A row that says neither is a <see cref="TripPositionEventKind.Note"/> when it has a note,
    /// because that is what the row is: somebody rang at that moment and said something that was
    /// not a place. A note is the last thing asked, so a row with a place or a standing never
    /// becomes one — its note rides on the report it already is. A row with nothing in any of the
    /// three has no kind, and is refused; so is one whose place or standing was written and could
    /// not be read, whatever this answers for it, because a cell that was not understood is not a
    /// cell that was empty.
    /// </para>
    /// </remarks>
    public TripPositionEventKind? Kind => State ?? Decides switch
    {
        TrackingCsvPlaceKind.Station => TripPositionEventKind.AtStation,
        TrackingCsvPlaceKind.Place or TrackingCsvPlaceKind.Depth => TripPositionEventKind.AtDepth,
        _ => Note is null ? null : TripPositionEventKind.Note,
    };

    /// <summary>Whether this row can be imported as it stands.</summary>
    public bool Importable =>
        !Diagnostics.Any(d => d.Severity == TrackingCsvSeverity.Error);
}

/// <summary>What reading a whole sheet of tracking reports produced.</summary>
public sealed record TrackingCsvParseResult
{
    public IReadOnlyList<TrackingCsvRow> Rows { get; init; } = [];

    /// <summary>Things wrong with the file rather than with one of its rows.</summary>
    public IReadOnlyList<TrackingCsvDiagnostic> FileDiagnostics { get; init; } = [];

    /// <summary>The header as written, so a mapping screen can offer the real spellings.</summary>
    public IReadOnlyList<string> Header { get; init; } = [];

    /// <summary>Which header each field was read from.</summary>
    public IReadOnlyDictionary<TrackingCsvField, string> ResolvedColumns { get; init; } =
        new Dictionary<TrackingCsvField, string>();

    /// <summary>Headers nothing claimed, so a misspelled one can be shown rather than dropped.</summary>
    public IReadOnlyList<string> UnmappedColumns { get; init; } = [];

    public TripCsv.TripCsvDateOrder DateOrder { get; init; }

    public TripCsv.TripCsvDateOrderSource DateOrderSource { get; init; }

    /// <summary>
    /// The day the importer named, where at least one row was put on it; null where every row that
    /// was read wrote its own date, so that what is echoed to a reviewer is what was actually used.
    /// </summary>
    public DateOnly? NamedDay { get; init; }

    /// <summary>Whether the file itself could be read at all.</summary>
    public bool Readable =>
        !FileDiagnostics.Any(d => d.Severity == TrackingCsvSeverity.Error);
}
