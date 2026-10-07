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

    /// <summary>
    /// A time of day was read and no date with it, and nobody named the day it was on. Told apart
    /// from <see cref="Unreadable"/> because what is wrong is not the cell: it is a perfectly good
    /// time, and the missing half is a date.
    /// </summary>
    TimeWithoutDate,

    /// <summary>
    /// A date and a time were read, and in the zone the sheet was kept in no clock ever showed
    /// them: the clocks were put forward over that hour. There is no instant to file it under.
    /// </summary>
    SkippedByClockChange,
}

/// <summary>What one cell of a moment column turned out to be.</summary>
/// <param name="RepeatedByClockChange">
/// Read, but the sheet's zone showed that date and time twice — the clocks were put back over it —
/// and <paramref name="At"/> is the first of the two. Only ever set beside
/// <see cref="TrackingCsvMomentKind.Read"/>, so "was it read" stays one question with one answer.
/// </param>
/// <param name="OnNamedDay">
/// Read, from a cell that wrote a time and no date, onto the day the importer named for the sheet.
/// The date of such a moment is the importer's word and not the sheet's, which is what a caller
/// checking whether the sheet ran past midnight has to know.
/// </param>
public readonly record struct TrackingCsvMoment(
    TrackingCsvMomentKind Kind, DateTimeOffset At, bool RepeatedByClockChange = false, bool OnNamedDay = false)
{
    public static TrackingCsvMoment Empty { get; } = new(TrackingCsvMomentKind.Empty, default);

    public static TrackingCsvMoment Unreadable { get; } = new(TrackingCsvMomentKind.Unreadable, default);

    public static TrackingCsvMoment DateWithoutTime { get; } = new(TrackingCsvMomentKind.DateWithoutTime, default);

    public static TrackingCsvMoment TimeWithoutDate { get; } = new(TrackingCsvMomentKind.TimeWithoutDate, default);

    public static TrackingCsvMoment SkippedByClockChange { get; } =
        new(TrackingCsvMomentKind.SkippedByClockChange, default);
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
/// <b>Whose clock a time is on is said by the importer, never supplied by the server.</b> A club
/// writes the time on its own wall and says nothing about offset. Inventing the server's zone for
/// it is a lie that changes when the server moves, so that is never done. A cell that states its
/// offset is the instant it says, whatever else was chosen. A cell that states none is read in the
/// zone the importer named for the sheet, at the offset that zone kept on that date
/// (<see cref="TrackingCsvZones.Resolve"/> holds the two hours a year where that is not one
/// answer). Where the importer named no zone the cell is stamped as it stands, as UTC: wrong by
/// the club's offset for a sheet kept on local time, but stable, reversible, the same on every
/// installation — and said plainly on the screen, which is where the choice of a zone is offered.
/// </para>
/// <para>
/// <b>A day is never invented either.</b> A cell that writes a time and no date is a moment only
/// when the importer has named the day the sheet was kept on; then it is that day's time, read
/// under the same offset and zone rules as any other. Otherwise it is reported as a time without a
/// date. No day is carried down from the row above and none is rolled forward past midnight: both
/// are guesses, and a guessed day files a report twenty-four hours from where it happened with
/// nothing on the screen to show it.
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
    /// <param name="zone">
    /// The zone the sheet's times were written in, applied to a cell that states no offset of its
    /// own; null to stamp such a cell as it stands, as UTC.
    /// </param>
    /// <param name="day">
    /// The day a cell that writes only a time of day is on, where the importer named one; null
    /// where nobody did, and such a cell is then a time without a date. A cell that writes its own
    /// date is on the date it wrote, whatever is given here.
    /// </param>
    public static TrackingCsvMoment Read(
        string? text, TripCsvDateOrder order, TimeZoneInfo? zone, DateOnly? day = null)
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

        // A time and nothing else. Four bare digits count as one only where a day was named —
        // that is, in a column the sheet itself heads as a time. Anywhere else four digits are a
        // year, and "2026" in a moment column goes on being a date with no time.
        var onNamedDay = false;
        DateOnly onDay;
        if (datePart.Length == 0 && timePart.Length > 0)
        {
            if (day is not { } named)
            {
                // Said to be a time without a date only where it is a time: a cell that merely
                // has a colon in it is as unreadable as it ever was.
                return TimeOnly.TryParseExact(timePart, TimeFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out _)
                    && TryReadOffset(offsetPart, out _)
                    ? TrackingCsvMoment.TimeWithoutDate
                    : TrackingCsvMoment.Unreadable;
            }

            onDay = named;
            onNamedDay = true;
        }
        else if (day is { } namedDay && timePart.Length == 0 && IsBareClock(tidy))
        {
            onDay = namedDay;
            onNamedDay = true;
            timePart = tidy;
        }
        else
        {
            var date = TripCsvDates.Read(datePart);
            if (date.Kind == TripCsvDateKind.Unreadable || date.Kind == TripCsvDateKind.Empty)
            {
                return TrackingCsvMoment.Unreadable;
            }

            if (!TripCsvDates.TryResolve(date, order, out onDay, out _))
            {
                return TrackingCsvMoment.Unreadable;
            }

            if (timePart.Length == 0)
            {
                return TrackingCsvMoment.DateWithoutTime;
            }
        }

        if (!TimeOnly.TryParseExact(timePart, TimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            return TrackingCsvMoment.Unreadable;
        }

        // An instant that states its own offset, which is what an export or a device writes, is
        // taken as given and converted — it is the one case where the cell says what zone it
        // means, and the zone chosen for the sheet has no say over it. That includes a cell that
        // writes Z: it has stated an offset of nothing, which is not the same as stating none.
        if (!TryReadOffset(offsetPart, out var offset))
        {
            return TrackingCsvMoment.Unreadable;
        }

        if (offsetPart.Length > 0 || zone is null)
        {
            // Either the cell's own offset, or no offset and no zone: stamped as it stands. A
            // cell on the first or last day of the calendar whose offset carries the instant off
            // it is not a moment anybody can file, and is refused like any other cell that is not
            // one.
            return TrackingCsvZones.TryInstant(onDay.ToDateTime(time), offset, out var stamped)
                ? new TrackingCsvMoment(TrackingCsvMomentKind.Read, stamped, OnNamedDay: onNamedDay)
                : TrackingCsvMoment.Unreadable;
        }

        var onTheWall = TrackingCsvZones.Resolve(onDay.ToDateTime(time), zone);
        return onTheWall.Kind switch
        {
            TrackingCsvWallClockKind.Never => TrackingCsvMoment.SkippedByClockChange,
            TrackingCsvWallClockKind.OffTheCalendar => TrackingCsvMoment.Unreadable,
            TrackingCsvWallClockKind.Twice => new TrackingCsvMoment(
                TrackingCsvMomentKind.Read, onTheWall.At, RepeatedByClockChange: true, OnNamedDay: onNamedDay),
            _ => new TrackingCsvMoment(TrackingCsvMomentKind.Read, onTheWall.At, OnNamedDay: onNamedDay),
        };
    }

    /// <summary>
    /// Whether a cell of a time column writes a time of day and no date — the cell that needs a
    /// day from somewhere else before it is a moment.
    /// </summary>
    /// <remarks>
    /// Asked of a column the sheet heads as a time, so four bare digits are "0815" here and not a
    /// year. A cell that carries its own date beside the time answers no: it already is a moment,
    /// whatever its column is called.
    /// </remarks>
    public static bool IsTimeAlone(string? text)
    {
        var tidy = TripCsvValues.Tidy(text);
        if (tidy.Length == 0 || !TripCsvValues.CarriesMeaning(tidy))
        {
            return false;
        }

        var (datePart, timePart, _) = Divide(tidy);
        return (datePart.Length == 0 && timePart.Length > 0) || (timePart.Length == 0 && IsBareClock(tidy));
    }

    /// <summary>Four digits and nothing else: a time written without its colon, or a year.</summary>
    private static bool IsBareClock(string tidy) => tidy.Length == 4 && tidy.All(char.IsAsciiDigit);

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
