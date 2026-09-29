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

    /// <summary>Something is written in the moment cell and it is not a moment.</summary>
    MomentUnreadable,

    /// <summary>The moment cell carries a date and no time, which cannot place a report.</summary>
    MomentWithoutTime,

    /// <summary>The row names nobody.</summary>
    NoCavers,

    /// <summary>The row says nothing about where anybody is, nor that they went in or came out.</summary>
    NoPlaceAndNoState,

    /// <summary>A depth cell carries something that is not a number.</summary>
    DepthUnreadable,

    /// <summary>The state column carries a word neither list knows.</summary>
    StateWordUnknown,

    // ---- found while matching a row against the trip it is being imported into ----

    /// <summary>A written name answers to nobody on the roster this import was given.</summary>
    CaverNotOnRoster,

    /// <summary>A written name answers to more than one person, which only a human can settle.</summary>
    CaverAmbiguous,

    /// <summary>The team named is not one of this trip's teams. Dropped; the report still stands.</summary>
    TeamNotOnTrip,

    /// <summary>The cave has declared no place by that name.</summary>
    PlaceLabelUnknown,

    /// <summary>Two declared places share that name, so which station is meant is undecidable.</summary>
    PlaceLabelAmbiguous,

    /// <summary>The station named is not one of the survey the watch is on.</summary>
    StationNotInModel,

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

    /// <summary>The log already holds this report and the reviewer did not allow overwriting.</summary>
    AlreadyRecorded,

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
