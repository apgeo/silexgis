// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
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
    /// typed from a phone call carries minutes, and a sheet exported from a device carries seconds
    /// — and sometimes fractions of one, which an export writes and nobody types.
    /// </summary>
    private static readonly string[] TimeFormats =
        ["HH:mm:ss.FFFFFFF", "H:mm:ss.FFFFFFF", "HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "HHmm"];

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

        // The cell taken apart into its date, its time, and the offset the time carries if it
        // carries one. Never handed whole to the framework's parser: asked to read a cell, that
        // parser supplies the server's own offset for one that states none and reads an ambiguous
        // date month-first whatever the file decided, so the same sheet would import three hours
        // out on one installation and on the wrong day on another. Every part is read here, under
        // the file's decided order, and only an offset the cell actually wrote is applied.
        var (datePart, timePart, offsetPart) = Divide(tidy);

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

        // An instant that states its own offset, which is what an export or a device writes, is
        // taken as given and converted — it is the one case where the cell says what zone it
        // means. A cell that states none is stamped as it stands, without shifting.
        if (!TryReadOffset(offsetPart, out var offset))
        {
            return TrackingCsvMoment.Unreadable;
        }

        return new TrackingCsvMoment(
            TrackingCsvMomentKind.Read,
            new DateTimeOffset(day.ToDateTime(time), offset).ToUniversalTime());
    }

    /// <summary>
    /// The date half of a cell, read but not yet resolved — the evidence this row contributes to
    /// the file's day-first-or-month-first question.
    /// </summary>
    /// <remarks>
    /// Exposed separately because the order has to be settled over the whole column before any row
    /// can be read, and <see cref="Read"/> already needs the answer. A cell that states its own
    /// offset abstains: it is what an export writes, so it proves nothing about how this club
    /// writes an ambiguous date, and counting it as evidence would let one exported row decide the
    /// reading of a hundred hand-typed ones.
    /// </remarks>
    public static TripCsvDateReading DatePartOf(string? text)
    {
        var tidy = TripCsvValues.Tidy(text);
        if (tidy.Length == 0 || !TripCsvValues.CarriesMeaning(tidy))
        {
            return TripCsvDateReading.Empty;
        }

        var (datePart, _, offsetPart) = Divide(tidy);
        return offsetPart.Length > 0 ? TripCsvDateReading.Empty : TripCsvDates.Read(datePart);
    }

    /// <summary>
    /// An offset as a cell writes one beside the time — a Z, or a sign with hours and optional
    /// minutes — with nothing else allowed to pass for one.
    /// </summary>
    private const string OffsetPattern = @"(?<offset>[Zz]|[+-]\d{2}(:?\d{2})?)";

    private static readonly Regex OffsetSuffix = new(
        OffsetPattern + "$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static readonly Regex OffsetWord = new(
        "^" + OffsetPattern + "$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>
    /// The offset an offset word states, or zero for a cell that wrote none.
    /// </summary>
    /// <remarks>
    /// False for an offset no clock has, which means the cell was not a moment after all: a sign
    /// after the time has to be an offset, and one that is not a real offset is a cell to report.
    /// </remarks>
    private static bool TryReadOffset(string offsetPart, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (offsetPart.Length == 0 || offsetPart is "Z" or "z")
        {
            return true;
        }

        var digits = offsetPart[1..].Replace(":", string.Empty, StringComparison.Ordinal);
        var hours = int.Parse(digits[..2], NumberStyles.None, CultureInfo.InvariantCulture);
        var minutes = digits.Length == 4
            ? int.Parse(digits[2..], NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
        if (hours > 14 || minutes > 59)
        {
            return false;
        }

        var magnitude = new TimeSpan(hours, minutes, 0);
        offset = offsetPart[0] == '-' ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// The cell split into what looks like a date, what looks like a time, and the offset written
    /// on or after the time, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The time is found by its colon rather than by position, because a sheet writes
    /// "12.09.2026 14:30" and another writes "14:30 12.09.2026", and a split on the first space
    /// would read the second as a date of "14:30". A time written without its colon is found by
    /// <see cref="ClockWord"/>, on the stricter terms that method states.
    /// </para>
    /// <para>
    /// The offset is looked for only where a clock writes it — glued to the end of the time word,
    /// as in "14:30:00+03:00", or as the word right after it, as in "14:30 +03:00". Looked for
    /// anywhere else, the dashes of "2026-09-12" and "12-09-2026" pass for offsets, and a cell
    /// that merely writes its date with dashes gets read as if it had named a zone.
    /// </para>
    /// </remarks>
    private static (string Date, string Time, string Offset) Divide(string tidy)
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

        var words = new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var timeWord = words.FindIndex(w => w.Contains(':', StringComparison.Ordinal));
        if (timeWord < 0)
        {
            timeWord = ClockWord(words);
        }

        if (timeWord < 0)
        {
            return (tidy, string.Empty, string.Empty);
        }

        var time = words[timeWord];
        var offset = string.Empty;

        // Glued on: the suffix has to leave a time in front of it, so "14:30" alone — whose
        // trailing ":30" is not an offset — is never cut short.
        var glued = OffsetSuffix.Match(time);
        if (glued.Success && glued.Index > 0 && time[..glued.Index].Contains(':', StringComparison.Ordinal))
        {
            offset = glued.Groups["offset"].Value;
            time = time[..glued.Index];
        }
        else if (timeWord + 1 < words.Count && OffsetWord.Match(words[timeWord + 1]) is { Success: true } apart)
        {
            offset = apart.Groups["offset"].Value;
            words.RemoveAt(timeWord + 1);
        }

        words.RemoveAt(timeWord);
        return (string.Join(' ', words), time, offset);
    }

    /// <summary>
    /// The word that is a time written without its colon — the "0815" of "12.09.2026 0815" — or
    /// -1 when the cell has none.
    /// </summary>
    /// <remarks>
    /// Four digits on their own are a year as readily as a time, so a word is taken as the time
    /// only when it is one of exactly two and the other reads as a date by itself. The trailing
    /// word is tried first because a sheet writes the date before the time, and "2026 0815" would
    /// otherwise read either way round. Without this the colon-less spelling among the accepted
    /// time formats could never be reached: the cell stayed whole and was refused as a date of four
    /// parts.
    /// </remarks>
    private static int ClockWord(List<string> words)
    {
        if (words.Count != 2)
        {
            return -1;
        }

        foreach (var candidate in new[] { 1, 0 })
        {
            var word = words[candidate];
            var rest = TripCsvDates.Read(words[1 - candidate]);
            if (word.Length == 4 && word.All(char.IsAsciiDigit)
                && rest.Kind is not (TripCsvDateKind.Unreadable or TripCsvDateKind.Empty))
            {
                return candidate;
            }
        }

        return -1;
    }
}
