// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

public enum TripCsvSeverity
{
    /// <summary>Something was reconciled or dropped; the row can still be imported.</summary>
    Warning,

    /// <summary>The row cannot be imported as it stands.</summary>
    Error,
}

/// <summary>Why a diagnostic was raised. Stable, so a caller can group or translate them.</summary>
public enum TripCsvDiagnosticCode
{
    /// <summary>A header the mapping named is not in the file, so its field stays empty.</summary>
    MappedColumnMissing,

    /// <summary>A header the mapping named was already claimed by another field's mapping.</summary>
    MappedColumnTaken,

    /// <summary>A header nothing claimed. Its values are kept on the row rather than dropped.</summary>
    UnmappedColumn,

    /// <summary>A data row was not as wide as the header, and was reconciled to it.</summary>
    RaggedRow,

    /// <summary>A row whose every cell was blank.</summary>
    BlankRow,

    /// <summary>A required field was empty.</summary>
    RequiredFieldEmpty,

    /// <summary>A date that fits no accepted form.</summary>
    DateUnreadable,

    /// <summary>A date whose year, month or day is not a real one. Never rolled over into the next month.</summary>
    DateOutOfRange,

    /// <summary>A year written with two digits, which names no century.</summary>
    DateTwoDigitYear,

    /// <summary>A numeric date whose day and month could be either way round.</summary>
    DateAmbiguous,

    /// <summary>The file carries evidence for both day-first and month-first dates.</summary>
    DateOrderConflict,

    /// <summary>A value left over from splitting a cell that carried no letter or digit.</summary>
    ValueDropped,

    /// <summary>Two rows carry the same source id.</summary>
    DuplicateSourceId,

    /// <summary>The header is wider than a header can sensibly be; nothing was read.</summary>
    TooManyColumns,

    /// <summary>The text held no header row.</summary>
    NoHeader,

    /// <summary>A quoted field the file never closed, which swallowed everything after it.</summary>
    UnterminatedQuote,
}

/// <summary>
/// One thing worth telling the reviewer about, tied to the physical line of the file it came
/// from. The line is counted in the file, not in the rows that survived parsing: a blank line
/// or a value carrying a newline shifts the two apart, and a number that does not point at
/// what the reviewer sees in their editor is worse than no number.
/// </summary>
/// <param name="Severity">Whether the row can still be imported.</param>
/// <param name="Code">Why it was raised.</param>
/// <param name="Line">The 1-based physical line of the file, or 0 for something about the file as a whole.</param>
/// <param name="Field">The field it concerns, where it concerns one.</param>
/// <param name="Column">The header it concerns, where it concerns one.</param>
/// <param name="Detail">The offending text, or a short explanation. Never a whole row.</param>
public sealed record TripCsvDiagnostic(
    TripCsvSeverity Severity,
    TripCsvDiagnosticCode Code,
    int Line,
    TripCsvField? Field = null,
    string? Column = null,
    string? Detail = null);
