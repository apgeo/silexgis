// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Domain.Calendar;

/// <summary>
/// One row of a subscription feed: everything the feed is allowed to say about a dated record,
/// which is its title, when it is, where to read it, and whether it was called off.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the disclosure boundary, and it is deliberately this narrow. A feed address lives
/// for years inside calendar services that store and sync whatever they are handed, so a row
/// carries no place, no position, no participants and no description — the title is the one
/// thing the person who wrote the record chose to say in a sentence, and the link is where the
/// rest is read, under that reader's own sign-in. A member added here is a member handed to a
/// third party for the lifetime of every feed ever minted.
/// </para>
/// <para>
/// Dates are calendar days and times are wall-clock without a zone, exactly as the rows hold
/// them. The consequence is stated rather than discovered: a subscriber's calendar reads a timed
/// entry in the device's own zone, which is right for a club that meets in one place and wrong
/// for a member abroad.
/// </para>
/// </remarks>
/// <param name="Uid">What makes this row the same row on every poll; stable, and unique across the feed.</param>
/// <param name="Title">The summary line, as its author wrote it.</param>
/// <param name="StartDate">The first day.</param>
/// <param name="EndDate">The last day, or null when it lasts a single day.</param>
/// <param name="StartTime">When it starts on a single day, or null for an all-day entry.</param>
/// <param name="EndTime">When it ends, read against <paramref name="StartTime"/>.</param>
/// <param name="Url">The absolute address of the record's own page.</param>
/// <param name="Stamp">When the row last changed, in UTC — what tells a subscriber to re-read it.</param>
/// <param name="Cancelled">Whether the record was called off.</param>
public sealed record CalendarFeedEntry(
    string Uid,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string Url,
    DateTimeOffset Stamp,
    bool Cancelled);

/// <summary>
/// Writes a subscription feed in the calendar interchange format (RFC 5545), one VEVENT per row.
/// </summary>
/// <remarks>
/// <para>
/// <b>No recurrence rule is ever written.</b> A repeating event in this application is the rows
/// it is — every occurrence is an ordinary event with its own identifier, answers and page — so
/// the feed says each one, and a subscriber sees exactly the rows the calendar page shows. A rule
/// here would be a second statement of the repetition, free to disagree with the first.
/// </para>
/// <para>
/// <b>When a row is drawn as a whole day and when as a time.</b> A row with no start time is a
/// whole day, or a run of whole days. A row spanning more than one day is a run of whole days
/// too, whatever times it carries: a trip's entry and exit times are about the underground part
/// of its first day and say nothing about where its second day begins, and a block over the days
/// it covers is what a reader glancing at a week needs. Only a single-day row with a start time
/// is written as a time, floating — no zone designator and no UTC marker — because that is what
/// a zoneless wall clock means. A timed row with no end time has no end written, which the format
/// reads as an instant; nothing is invented to fill it.
/// </para>
/// <para>
/// The format's own rules are kept here: lines end in CRLF, text values escape the characters the
/// grammar reserves, and a line longer than 75 octets is folded on an octet count that never
/// splits a multi-byte character. Those rules are what a strict parser refuses the whole feed
/// over, so they are tested on their own.
/// </para>
/// </remarks>
public static class CalendarFeedWriter
{
    /// <summary>
    /// The longest a content line may be before it is folded, in octets, excluding the line
    /// break — the format's own figure.
    /// </summary>
    private const int MaxLineOctets = 75;

    /// <summary>Who wrote the feed, in the registered form the format asks for.</summary>
    public const string ProductId = "-//SilexGIS//Calendar feed//EN";

    /// <summary>
    /// The whole feed as text.
    /// </summary>
    /// <param name="name">
    /// What a calendar application should call the subscription when it is added — the
    /// installation's own name. Carried on the widely-honoured extension property, since the
    /// standard has no name for a whole calendar.
    /// </param>
    /// <param name="entries">The rows, in the order they should appear.</param>
    public static string Write(string name, IEnumerable<CalendarFeedEntry> entries)
    {
        var text = new StringBuilder();
        Line(text, "BEGIN:VCALENDAR");
        Line(text, "VERSION:2.0");
        Line(text, "PRODID:" + ProductId);
        Line(text, "CALSCALE:GREGORIAN");
        // Published rather than requested or replied to: a subscriber reads it, nobody answers it.
        Line(text, "METHOD:PUBLISH");
        Line(text, "X-WR-CALNAME:" + Escape(name));

        foreach (var entry in entries)
        {
            Line(text, "BEGIN:VEVENT");
            Line(text, "UID:" + Escape(entry.Uid));
            Line(text, "DTSTAMP:" + Utc(entry.Stamp));
            Line(text, "LAST-MODIFIED:" + Utc(entry.Stamp));
            WriteWhen(text, entry);
            Line(text, "SUMMARY:" + Escape(entry.Title));
            // A URI is written as it is, not as text: the escaping rules for text would corrupt
            // the one character an address relies on.
            Line(text, "URL:" + entry.Url);
            if (entry.Cancelled)
            {
                Line(text, "STATUS:CANCELLED");
            }

            Line(text, "END:VEVENT");
        }

        Line(text, "END:VCALENDAR");
        return text.ToString();
    }

    private static void WriteWhen(StringBuilder text, CalendarFeedEntry entry)
    {
        var lastDay = entry.EndDate ?? entry.StartDate;
        var wholeDays = entry.StartTime is null || lastDay > entry.StartDate;
        if (wholeDays)
        {
            // An all-day end is exclusive in the format — the day after the last day — which is
            // why one day is written as start and start-plus-one rather than start alone.
            Line(text, "DTSTART;VALUE=DATE:" + Day(entry.StartDate));
            Line(text, "DTEND;VALUE=DATE:" + Day(lastDay.AddDays(1)));
            return;
        }

        var start = entry.StartTime!.Value;
        Line(text, "DTSTART:" + Floating(entry.StartDate, start));
        if (entry.EndTime is { } end)
        {
            // A wall-clock end at or before the start on a single-day row is the next morning:
            // a meeting that runs from 21:00 to 00:30 is an ordinary evening rather than a
            // mistake, which is the reading the rows themselves are written against.
            var endDay = end <= start ? entry.StartDate.AddDays(1) : entry.StartDate;
            Line(text, "DTEND:" + Floating(endDay, end));
        }
    }

    private static string Day(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Floating(DateOnly day, TimeOnly time) =>
        Day(day) + "T" + time.ToString("HHmmss", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A text value with the characters the grammar reserves escaped: the backslash, the
    /// semicolon, the comma and the line break.
    /// </summary>
    public static string Escape(string value)
    {
        var text = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    text.Append("\\\\");
                    break;
                case ';':
                    text.Append("\\;");
                    break;
                case ',':
                    text.Append("\\,");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Appends one content line, folded wherever it runs past the octet limit, CRLF-terminated.
    /// </summary>
    /// <remarks>
    /// Folded on octets rather than characters because the limit is an octet count, and the
    /// break is placed before a character rather than inside it: a line cut in the middle of a
    /// multi-byte sequence is two invalid sequences, and a title with one diacritic in the wrong
    /// place would then break the whole feed for a strict reader.
    /// </remarks>
    public static void Line(StringBuilder text, string content)
    {
        var budget = MaxLineOctets;
        var used = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            var width = rune.Utf8SequenceLength;
            if (used + width > budget)
            {
                text.Append("\r\n ");
                // A continuation line begins with the fold's own space, which counts.
                budget = MaxLineOctets - 1;
                used = 0;
            }

            text.Append(rune.ToString());
            used += width;
        }

        text.Append("\r\n");
    }
}
