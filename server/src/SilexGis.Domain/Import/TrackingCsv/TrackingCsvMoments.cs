// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>Why a cell could not be read as a moment, or that it could.</summary>
public enum TrackingCsvMomentKind
{
    /// <summary>Nothing was written. Not a fault: a sheet has blank rows and trailing lines.</summary>
    Empty,

    /// <summary>Read, and the instant is known.</summary>
    Read,

    /// <summary>Something was written and it is not a moment.</summary>
    Unreadable,

    /// <summary>A date was read and no time with it, which for a tracking report is not enough.</summary>
    DateWithoutTime,
}

/// <summary>What one cell of a moment column turned out to be.</summary>
public readonly record struct TrackingCsvMoment(TrackingCsvMomentKind Kind, DateTimeOffset At)
{
    public static TrackingCsvMoment Empty { get; } = new(TrackingCsvMomentKind.Empty, default);

    public static TrackingCsvMoment Unreadable { get; } = new(TrackingCsvMomentKind.Unreadable, default);

    public static TrackingCsvMoment DateWithoutTime { get; } = new(TrackingCsvMomentKind.DateWithoutTime, default);
}

/// <summary>
/// Reading the moment a tracking report was made off a spreadsheet cell.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not the trip sheet's date reader.</b> That one reads a <em>day</em>, because a
/// trip happened on one, and it deliberately answers a date with no time. A tracking report is an
/// instant: the whole log is ordered by it, the upsert key is built from it, and two reports an
/// hour apart on one day are the normal case. So a cell carrying only a date is reported as
/// <see cref="TrackingCsvMomentKind.DateWithoutTime"/> rather than read as midnight — filing a
/// day's reports at midnight would collapse them onto one instant and, with the caver, onto one
/// upsert key, so re-importing the sheet would keep overwriting one row with the next.
/// </para>
/// <para>
/// <b>Day-first or month-first is decided for the file, not per cell.</b> Which of 03/04 is the
/// day cannot be told from one cell, and a reader that guessed per row would let one unusual row
/// read in a different order from its neighbours. So the order arrives already decided and is
/// applied strictly here — the caller settles it once over the whole column, through the shared
/// <see cref="TripCsvDates.DecideOrder"/> that the trip sheet uses for the same reason. A cell the
/// decided order cannot make a real date of is unreadable rather than quietly re-read the other
/// way round.
/// </para>
/// <para>
/// <b>Read as the instant it says, with no zone invented.</b> A club writes local wall-clock time
/// and says nothing about offset. Treating that as UTC is a lie of up to three hours; inventing
/// the server's zone is a lie that changes when the server moves. So the cell is read as an
/// unspecified instant and stamped as UTC, and the importer says plainly that a sheet's times are
/// taken at face value — which is the only reading that is stable, reversible, and the same on
/// every installation.
/// </para>
/// </remarks>
public static class TrackingCsvMoments
{
    /// <summary>
    /// Time-of-day spellings accepted, most specific first. Seconds are optional because a sheet
    /// typed from a phone call carries minutes, and a sheet exported from a device carries seconds.
    /// </summary>
    private static readonly string[] TimeFormats = ["HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "HHmm"];

    /// <summary>
    /// The moment a cell names.
    /// </summary>
    public static TrackingCsvMoment Read(string? text, TripCsvDateOrder order)
    {
        var tidy = TripCsvValues.Tidy(text);
        if (tidy.Length == 0 || !TripCsvValues.CarriesMeaning(tidy))
        {
            return TrackingCsvMoment.Empty;
        }

        // An instant that states its own offset, which is what an export or a device writes. Taken
        // as given, because it is the one case where the cell says what zone it means.
        //
        // <b>Only when the offset is actually written.</b> Asking the framework to parse a cell
        // with no offset in it hands back one in the *server's* zone, which is the lie this reader
        // exists to avoid: the same sheet would import three hours out on one installation and
        // right on another, and would change meaning if the server moved. A cell with no offset
        // falls through to the reading below, which stamps what the club wrote without shifting it.
        if (StatesAnOffset(tidy)
            && DateTimeOffset.TryParse(tidy, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var stated))
        {
            return new TrackingCsvMoment(TrackingCsvMomentKind.Read, stated.ToUniversalTime());
        }

        // Otherwise: a date and a time beside it, in whatever the club writes. Split at the first
        // run of whitespace that has a colon after it, so "12.09.2026 14:30" and "12 09 2026 14:30"
        // both divide in the right place.
        var (datePart, timePart) = Divide(tidy);

        var date = TripCsvDates.Read(datePart);
        if (date.Kind == TripCsvDateKind.Unreadable || date.Kind == TripCsvDateKind.Empty)
        {
            return TrackingCsvMoment.Unreadable;
        }

        if (!TripCsvDates.TryResolve(date, order, out var day, out _))
        {
            return TrackingCsvMoment.Unreadable;
        }

        if (timePart.Length == 0)
        {
            return TrackingCsvMoment.DateWithoutTime;
        }

        if (!TimeOnly.TryParseExact(timePart, TimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            return TrackingCsvMoment.Unreadable;
        }

        return new TrackingCsvMoment(
            TrackingCsvMomentKind.Read,
            new DateTimeOffset(day.ToDateTime(time), TimeSpan.Zero));
    }

    /// <summary>
    /// The date half of a cell, read but not yet resolved — the evidence this row contributes to
    /// the file's day-first-or-month-first question.
    /// </summary>
    /// <remarks>
    /// Exposed separately because the order has to be settled over the whole column before any row
    /// can be read, and <see cref="Read"/> already needs the answer. A cell that states its own
    /// offset abstains: it is ISO, so it proves nothing about how this club writes an ambiguous
    /// date, and counting it as evidence would let one exported row decide the reading of a hundred
    /// hand-typed ones.
    /// </remarks>
    public static TripCsvDateReading DatePartOf(string? text)
    {
        var tidy = TripCsvValues.Tidy(text);
        if (tidy.Length == 0 || !TripCsvValues.CarriesMeaning(tidy))
        {
            return TripCsvDateReading.Empty;
        }

        if (StatesAnOffset(tidy)
            && DateTimeOffset.TryParse(tidy, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return TripCsvDateReading.Empty;
        }

        var (datePart, _) = Divide(tidy);
        return TripCsvDates.Read(datePart);
    }

    /// <summary>
    /// Whether the cell states a zone of its own — a trailing Z, or a signed offset after the time.
    /// </summary>
    /// <remarks>
    /// Looked for rather than inferred from a successful parse, because the framework will happily
    /// supply the server's own offset for a cell that states none, and a reading that depends on
    /// where the server is, is not a reading.
    /// </remarks>
    private static bool StatesAnOffset(string tidy)
    {
        if (tidy.EndsWith('Z') || tidy.EndsWith('z'))
        {
            return true;
        }

        var colon = tidy.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return false;
        }

        // A sign after the time is an offset; a sign before it is part of the date.
        var afterTime = tidy[colon..];
        return afterTime.Contains('+', StringComparison.Ordinal)
            || afterTime.LastIndexOf('-') > 0;
    }

    /// <summary>
    /// The cell split into what looks like a date and what looks like a time.
    /// </summary>
    /// <remarks>
    /// The time is found by its colon rather than by position, because a sheet writes
    /// "12.09.2026 14:30" and another writes "14:30 12.09.2026", and a split on the first space
    /// would read the second as a date of "14:30".
    /// </remarks>
    private static (string Date, string Time) Divide(string tidy)
    {
        // An ISO cell separates the two with a T rather than a space, and reaches here whenever it
        // states no offset. Normalised to a space so there is one divider to reason about, and only
        // between two digits so a T inside a written word is left alone.
        var chars = tidy.ToCharArray();
        for (var i = 1; i < chars.Length - 1; i++)
        {
            if ((chars[i] == 'T' || chars[i] == 't')
                && char.IsAsciiDigit(chars[i - 1]) && char.IsAsciiDigit(chars[i + 1]))
            {
                chars[i] = ' ';
            }
        }

        var words = new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var timeWord = Array.FindIndex(words, w => w.Contains(':', StringComparison.Ordinal));

        if (timeWord < 0)
        {
            return (tidy, string.Empty);
        }

        var date = string.Join(' ', words.Where((_, i) => i != timeWord));
        return (date, words[timeWord]);
    }
}
