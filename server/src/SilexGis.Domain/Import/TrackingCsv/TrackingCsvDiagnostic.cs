// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>Whether something found in a sheet stops a row or merely needs saying.</summary>
public enum TrackingCsvSeverity
{
    /// <summary>Worth telling somebody; the row still imports.</summary>
    Warning,

    /// <summary>The row cannot be imported as it stands.</summary>
    Error,
}

/// <summary>What can be wrong with a sheet of tracking reports, named so a screen can say it.</summary>
public enum TrackingCsvProblem
{
    /// <summary>A quote was opened and never closed, so the rest of the file is one cell.</summary>
    UnterminatedQuote,

    /// <summary>The file has no rows at all, so there is no header to read.</summary>
    NoHeader,

    /// <summary>More columns than a sheet plausibly has; the file is probably not what it seems.</summary>
    TooManyColumns,

    /// <summary>A header was named by hand and the file does not have it.</summary>
    NamedColumnMissing,

    /// <summary>A column nothing claimed. Said out loud, because a misspelled header is silent.</summary>
    UnmappedColumn,

    /// <summary>No column carries the moment, without which no row can be placed in time.</summary>
    MomentColumnMissing,

    /// <summary>No column carries the people, without which a row is about nobody.</summary>
    CaverColumnMissing,

    /// <summary>
    /// The sheet writes its times in a column of their own and no dates anywhere, and nobody named
    /// the day it was kept on. Refused once for the file, naming the time column, because the
    /// repair is one answer for the whole sheet and not something to be told on every row.
    /// </summary>
    TimeColumnNeedsADay,

    /// <summary>Something is written in the moment cell and it is not a moment.</summary>
    MomentUnreadable,

    /// <summary>The moment cell carries a date and no time, which cannot place a report.</summary>
    MomentWithoutTime,

    /// <summary>
    /// The row writes a time and no date, in a sheet that has a place for the date. Refused on the
    /// row: the day is not carried down from the row above, because a blank that meant "as above"
    /// and a blank that was simply forgotten look the same.
    /// </summary>
    MomentWithoutDate,

    /// <summary>
    /// The moment cell names a date and time the sheet's zone never showed, because the clocks
    /// were put forward over it. Refused on the row: there is no instant to file it under, and
    /// the cell is either mistyped or was not kept on that zone's clocks.
    /// </summary>
    MomentSkippedByClockChange,

    /// <summary>
    /// The moment cell names a date and time the sheet's zone showed twice, because the clocks
    /// were put back over it. Imported as the first of the two, and said out loud so the reviewer
    /// can settle it by writing the offset in the cell.
    /// </summary>
    MomentRepeatedByClockChange,

    /// <summary>The row names nobody.</summary>
    NoCavers,

    /// <summary>The row says nothing about where anybody is, nor that they went in or came out.</summary>
    NoPlaceAndNoState,

    /// <summary>A depth cell carries something that is not a number.</summary>
    DepthUnreadable,

    /// <summary>The state column carries a word neither list knows.</summary>
    StateWordUnknown,

    /// <summary>
    /// The note, once the note and details columns are folded together, is longer than a report
    /// may carry. Refused on the row rather than cut short, because a typed report of that length
    /// is refused too and an import is not allowed to say more than typing can.
    /// </summary>
    NoteTooLong,

    // ---- found while matching a row against the trip it is being imported into ----

    /// <summary>A written name answers to nobody on the roster this import was given.</summary>
    CaverNotOnRoster,

    /// <summary>A written name answers to more than one person, which only a human can settle.</summary>
    CaverAmbiguous,

    /// <summary>The team named is not one of this trip's teams. Dropped; the report still stands.</summary>
    TeamNotOnTrip,

    /// <summary>
    /// The team named is the name of more than one of this trip's teams. Which of them is meant is
    /// not the importer's to guess, so the row says nothing about the team: a report the log
    /// already holds keeps the team it has, a new one is made without a team, and the report
    /// still stands.
    /// </summary>
    TeamAmbiguous,

    /// <summary>The cave has declared no place by that name.</summary>
    PlaceLabelUnknown,

    /// <summary>Two declared places share that name, so which station is meant is undecidable.</summary>
    PlaceLabelAmbiguous,

    /// <summary>The station named is not one of the survey the watch is on.</summary>
    StationNotInModel,

    /// <summary>
    /// The station named as the far end of a stretch is not one of the survey the watch is on.
    /// Said apart from the first station so the reviewer knows which cell to look at.
    /// </summary>
    ToStationNotInModel,

    /// <summary>The row names the same station as both ends of a stretch.</summary>
    StretchSameStation,

    /// <summary>
    /// The row names the far end of a stretch and no station for it to be measured from. Refused
    /// rather than read as a report at that one station: the sheet said "between", and this
    /// reading did not follow where from.
    /// </summary>
    ToStationWithoutStation,

    /// <summary>The model has no datum to measure a depth from.</summary>
    DepthReferenceUnknown,

    /// <summary>No station the trip's filter allows is at that depth.</summary>
    NoStationAtDepth,

    /// <summary>The watch has no survey model, so no row claiming a place can be placed.</summary>
    ModelMissing,

    /// <summary>Two rows of this file are the same report — same person, same instant.</summary>
    DuplicateInFile,

    /// <summary>A report cannot be about the future, whatever the sheet says.</summary>
    MomentInFuture,

    /// <summary>
    /// In a sheet read onto one named day, this row's time is earlier than the latest time on the
    /// rows above it, whoever those rows are about — the sign of a sheet that ran past midnight.
    /// Imported on the named day and said out loud, because the next day is a guess the importer
    /// does not make: the reviewer leaves the row out, or gives the sheet a date column.
    /// </summary>
    ClockRunsBackwards,

    /// <summary>The log already holds this report and the reviewer did not allow overwriting.</summary>
    AlreadyRecorded,

    /// <summary>
    /// The log holds more than one report for this person at this instant, so which of them the
    /// sheet's row corrects cannot be decided. Settled in the log, by a person, before the row can
    /// be imported.
    /// </summary>
    AlreadyRecordedSeveralTimes,

    /// <summary>The moment cell is blank, so the row cannot be placed in time.</summary>
    MomentMissing,

    /// <summary>A row is not as wide as the header. Reconciled to it, and said out loud.</summary>
    RaggedRow,

    /// <summary>A depth is a number and not a depth any cave has.</summary>
    DepthOutOfRange,

    /// <summary>The file's dates argue both ways about which number is the day.</summary>
    DateOrderConflict,

    /// <summary>
    /// A row says somebody went in or came out <em>and</em> names a place inside the cave. The
    /// standing wins and the place is dropped, because entering and leaving claim no station.
    /// </summary>
    StateOverridesPlace,
}

/// <summary>One thing worth saying about a sheet, or about one of its rows.</summary>
/// <param name="Line">The 1-based physical line it is about, or 0 for the file as a whole.</param>
/// <param name="Column">The header it concerns, where it concerns one.</param>
/// <param name="Detail">The offending text, kept as written so it can be shown back.</param>
public sealed record TrackingCsvDiagnostic(
    TrackingCsvSeverity Severity,
    TrackingCsvProblem Problem,
    int Line,
    string? Column = null,
    string? Detail = null);
