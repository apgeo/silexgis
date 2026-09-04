// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Import.TripCsv;

/// <summary>What a date cell turned out to be, before any day/month order is applied.</summary>
public enum TripCsvDateKind
{
    /// <summary>Nothing was written.</summary>
    Empty,

    /// <summary>The form itself says which component is the month.</summary>
    Settled,

    /// <summary>Two numbers whose meaning depends on the order the file is read in.</summary>
    OrderDependent,

    /// <summary>A year written with two digits.</summary>
    TwoDigitYear,

    /// <summary>No accepted form.</summary>
    Unreadable,
}

/// <summary>
/// A date cell read into components, with nothing decided that the text itself does not decide.
/// <see cref="First"/> and <see cref="Second"/> are the two leading numbers of a numeric date in
/// the order they were written; which is the day is a property of the file, not of the cell.
/// </summary>
public readonly record struct TripCsvDateReading(
    TripCsvDateKind Kind,
    int Year,
    int First,
    int Second)
{
    public static TripCsvDateReading Empty { get; } = new(TripCsvDateKind.Empty, 0, 0, 0);

    public static TripCsvDateReading Unreadable { get; } = new(TripCsvDateKind.Unreadable, 0, 0, 0);

    /// <summary>True when the day and month could be swapped and the text would look the same.</summary>
    public bool IsAmbiguous =>
        Kind == TripCsvDateKind.OrderDependent && First <= 12 && Second <= 12 && First != Second;

    /// <summary>The order this cell proves, where it proves one.</summary>
    public TripCsvDateOrder? ProvenOrder => Kind != TripCsvDateKind.OrderDependent
        ? null
        : First > 12 && Second <= 12 ? TripCsvDateOrder.DayFirst
        : Second > 12 && First <= 12 ? TripCsvDateOrder.MonthFirst
        : null;
}

/// <summary>
/// The one date grammar this importer has. Everything that reads a date from a sheet reads it
/// here — a pipeline carrying two grammars disagrees with itself about the same cell, and the
/// disagreement shows up as trips filed under a different year than the one the list sorts by.
///
/// <para>
/// Accepted: <c>yyyy-MM-dd</c>, <c>yyyy-MM</c>, <c>yyyy</c>, <c>yyyyMMdd</c>, and three numeric
/// components separated by <c>/</c>, <c>.</c> or <c>-</c> with a four-digit year either first or
/// last. Nothing else. A component outside its real range is refused rather than carried into the
/// next month, because a rolled-over typo becomes a plausible wrong date that nobody ever spots,
/// and a two-digit year is refused because it names no century.
/// </para>
/// </summary>
public static class TripCsvDates
{
    private static readonly char[] Separators = ['/', '.', '-'];

    /// <summary>Reads the components out of a cell without deciding the day/month order.</summary>
    public static TripCsvDateReading Read(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return TripCsvDateReading.Empty;
        }

        // A bare run of digits is either a year or a basic ISO date; nothing else in between is
        // a form anybody writes on purpose.
        if (value.All(char.IsAsciiDigit))
        {
            return value.Length switch
            {
                4 => Settled(value, "1", "1"),
                8 => Settled(value[..4], value[4..6], value[6..]),
                2 => new TripCsvDateReading(TripCsvDateKind.TwoDigitYear, 0, 0, 0),
                _ => TripCsvDateReading.Unreadable,
            };
        }

        var parts = value.Split(Separators, StringSplitOptions.TrimEntries);
        if (parts.Length is not (2 or 3) || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return TripCsvDateReading.Unreadable;
        }

        if (parts.Length == 2)
        {
            // yyyy-MM, the only two-component form. Two components the other way round name no
            // year at all, so there is nothing to read.
            return parts[0].Length == 4
                ? Settled(parts[0], parts[1], "1")
                : TripCsvDateReading.Unreadable;
        }

        if (parts[0].Length == 4)
        {
            // Year first fixes the rest: nobody writes yyyy/dd/MM.
            return Settled(parts[0], parts[1], parts[2]);
        }

        if (parts[2].Length != 4)
        {
            return new TripCsvDateReading(TripCsvDateKind.TwoDigitYear, 0, 0, 0);
        }

        return TryNumber(parts[2], out var year)
            && TryNumber(parts[0], out var first)
            && TryNumber(parts[1], out var second)
            ? new TripCsvDateReading(TripCsvDateKind.OrderDependent, year, first, second)
            : TripCsvDateReading.Unreadable;
    }

    /// <summary>
    /// A reading whose written form already says which component is the month.
    /// </summary>
    private static TripCsvDateReading Settled(string year, string month, string day) =>
        TryNumber(year, out var y) && TryNumber(month, out var m) && TryNumber(day, out var d)
            ? new TripCsvDateReading(TripCsvDateKind.Settled, y, m, d)
            : TripCsvDateReading.Unreadable;

    /// <summary>
    /// Turns a reading into a date under a decided order. Returns false with the code that says
    /// why for anything the grammar refuses; an empty cell is not a failure and yields no code.
    /// </summary>
    public static bool TryResolve(
        TripCsvDateReading reading,
        TripCsvDateOrder order,
        out DateOnly date,
        out TripCsvDiagnosticCode? refusal)
    {
        date = default;
        refusal = null;
        switch (reading.Kind)
        {
            case TripCsvDateKind.Empty:
                return false;
            case TripCsvDateKind.TwoDigitYear:
                refusal = TripCsvDiagnosticCode.DateTwoDigitYear;
                return false;
            case TripCsvDateKind.Unreadable:
                refusal = TripCsvDiagnosticCode.DateUnreadable;
                return false;
        }

        var (month, day) = reading.Kind == TripCsvDateKind.Settled
            ? (reading.First, reading.Second)
            : order == TripCsvDateOrder.DayFirst
                ? (reading.Second, reading.First)
                : (reading.First, reading.Second);

        if (reading.Year is < 1 or > 9999 || month is < 1 or > 12
            || day < 1 || day > DateTime.DaysInMonth(reading.Year, month))
        {
            refusal = TripCsvDiagnosticCode.DateOutOfRange;
            return false;
        }

        date = new DateOnly(reading.Year, month, day);
        return true;
    }

    /// <summary>
    /// Decides the day/month order for a whole file from every date it carries. One row cannot
    /// settle it for itself — <c>5/11</c> is a real date either way — so the question is asked
    /// once of the column and the answer applied to every row, which is also what stops one
    /// unusual row silently reading in a different order from its neighbours.
    /// </summary>
    public static (TripCsvDateOrder Order, TripCsvDateOrderSource Source) DecideOrder(
        IEnumerable<TripCsvDateReading> readings,
        TripCsvDateOrder stated)
    {
        var dayFirst = 0;
        var monthFirst = 0;
        foreach (var reading in readings)
        {
            switch (reading.ProvenOrder)
            {
                case TripCsvDateOrder.DayFirst:
                    dayFirst++;
                    break;
                case TripCsvDateOrder.MonthFirst:
                    monthFirst++;
                    break;
            }
        }

        return (dayFirst, monthFirst) switch
        {
            (> 0, 0) => (TripCsvDateOrder.DayFirst, TripCsvDateOrderSource.File),
            (0, > 0) => (TripCsvDateOrder.MonthFirst, TripCsvDateOrderSource.File),
            (> 0, > 0) => (stated, TripCsvDateOrderSource.Conflict),
            _ => (stated, TripCsvDateOrderSource.Stated),
        };
    }

    /// <summary>
    /// A component read as a number, refusing rather than throwing on a run of digits too long to
    /// be one. A cell like <c>99999999999/12/2024</c> reaches this from an ordinary mis-mapped
    /// column of reference numbers, and an exception here would cost every other row in the file
    /// its parse — the one thing reading somebody else's spreadsheet must never do.
    /// </summary>
    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
