// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Calendar;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The feed as text: the format's own rules, which a strict calendar reader refuses the whole
/// file over, and the one decision about what a row says.
/// </summary>
public class CalendarFeedWriterTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 5, 12, 30, 0, TimeSpan.FromHours(3));

    private static CalendarFeedEntry Row(
        string title = "Club night",
        DateOnly? start = null,
        DateOnly? end = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        bool cancelled = false) =>
        new(
            "event-1@silexgis.example",
            title,
            start ?? new DateOnly(2026, 10, 6),
            end,
            startTime,
            endTime,
            "https://silexgis.example/events/1",
            Stamp,
            cancelled);

    private static string[] Lines(string feed) => feed.Split("\r\n");

    [Fact]
    public void The_envelope_is_a_published_calendar_with_one_event_per_row()
    {
        var feed = CalendarFeedWriter.Write("Silex", [Row(), Row() with { Uid = "event-2@silexgis.example" }]);
        var lines = Lines(feed);

        lines[0].ShouldBe("BEGIN:VCALENDAR");
        lines.ShouldContain("VERSION:2.0");
        lines.ShouldContain("PRODID:" + CalendarFeedWriter.ProductId);
        lines.ShouldContain("METHOD:PUBLISH");
        lines.ShouldContain("X-WR-CALNAME:Silex");
        lines.Count(l => l == "BEGIN:VEVENT").ShouldBe(2);
        lines.Count(l => l == "END:VEVENT").ShouldBe(2);
        // The last line is the end of the calendar, and the text ends with the line break.
        lines[^2].ShouldBe("END:VCALENDAR");
        lines[^1].ShouldBe(string.Empty);
        // Every line break is the format's own; a bare LF anywhere is a parse error for some readers.
        feed.Replace("\r\n", string.Empty).ShouldNotContain("\n");
    }

    [Fact]
    public void A_row_says_its_title_its_days_a_link_back_and_a_stamp_and_nothing_else()
    {
        var feed = CalendarFeedWriter.Write("Silex", [Row()]);
        var lines = Lines(feed);

        lines.ShouldContain("UID:event-1@silexgis.example");
        lines.ShouldContain("SUMMARY:Club night");
        lines.ShouldContain("URL:https://silexgis.example/events/1");
        // The stamp is written in UTC: 12:30 at +03:00 is 09:30Z.
        lines.ShouldContain("DTSTAMP:20261005T093000Z");
        lines.ShouldContain("LAST-MODIFIED:20261005T093000Z");

        // The disclosure boundary, asserted as absence: nothing here can grow a place, a
        // position, a party or a description without this test naming it.
        var properties = lines
            .Where(l => l.Length > 0 && l[0] != ' ')
            .Select(l => l.Split([':', ';'], 2)[0])
            .ToHashSet();
        properties.ShouldBe(
            [
                "BEGIN", "END", "VERSION", "PRODID", "CALSCALE", "METHOD", "X-WR-CALNAME",
                "UID", "DTSTAMP", "LAST-MODIFIED", "DTSTART", "DTEND", "SUMMARY", "URL",
            ],
            ignoreOrder: true);
        // And no repetition rule, ever: an occurrence is a row.
        properties.ShouldNotContain("RRULE");
    }

    [Fact]
    public void A_row_without_a_time_is_a_whole_day_with_the_exclusive_end_the_format_wants()
    {
        var lines = Lines(CalendarFeedWriter.Write("Silex", [Row()]));

        lines.ShouldContain("DTSTART;VALUE=DATE:20261006");
        lines.ShouldContain("DTEND;VALUE=DATE:20261007");
    }

    [Fact]
    public void A_run_of_days_is_a_block_over_them_whatever_times_it_carries()
    {
        // A camp from the 6th to the 9th, and a two-day trip with underground entry and exit
        // times: both are blocks, because a time on the first day says nothing about the second.
        var camp = Row(start: new DateOnly(2026, 10, 6), end: new DateOnly(2026, 10, 9));
        var trip = Row(
            start: new DateOnly(2026, 10, 6),
            end: new DateOnly(2026, 10, 7),
            startTime: new TimeOnly(9, 0),
            endTime: new TimeOnly(16, 0));

        var lines = Lines(CalendarFeedWriter.Write("Silex", [camp, trip]));

        lines.ShouldContain("DTEND;VALUE=DATE:20261010");
        lines.ShouldContain("DTEND;VALUE=DATE:20261008");
        lines.ShouldNotContain(l => l.StartsWith("DTSTART:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_single_day_with_a_start_time_is_a_floating_time_with_no_zone()
    {
        var lines = Lines(CalendarFeedWriter.Write(
            "Silex", [Row(startTime: new TimeOnly(19, 0), endTime: new TimeOnly(21, 30))]));

        // No `Z`, no `TZID`: a wall clock is read in the subscriber's own zone.
        lines.ShouldContain("DTSTART:20261006T190000");
        lines.ShouldContain("DTEND:20261006T213000");
        lines.ShouldNotContain(l => l.Contains("TZID", StringComparison.Ordinal));
    }

    [Fact]
    public void An_end_at_or_before_the_start_is_the_next_morning()
    {
        var lines = Lines(CalendarFeedWriter.Write(
            "Silex", [Row(startTime: new TimeOnly(21, 0), endTime: new TimeOnly(0, 30))]));

        lines.ShouldContain("DTSTART:20261006T210000");
        lines.ShouldContain("DTEND:20261007T003000");
    }

    [Fact]
    public void A_timed_row_with_no_end_has_no_end_written_rather_than_an_invented_one()
    {
        var lines = Lines(CalendarFeedWriter.Write("Silex", [Row(startTime: new TimeOnly(19, 0))]));

        lines.ShouldContain("DTSTART:20261006T190000");
        lines.ShouldNotContain(l => l.StartsWith("DTEND", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_called_off_says_so_and_a_live_one_says_nothing()
    {
        var lines = Lines(CalendarFeedWriter.Write("Silex", [Row(cancelled: true), Row()]));

        lines.Count(l => l == "STATUS:CANCELLED").ShouldBe(1);
    }

    [Fact]
    public void Reserved_characters_in_a_title_are_escaped()
    {
        var feed = CalendarFeedWriter.Write("Silex", [Row(title: "Peștera; a, b\\c\nline two")]);

        feed.ShouldContain("SUMMARY:Peștera\\; a\\, b\\\\c\\nline two\r\n");
    }

    [Fact]
    public void A_long_line_is_folded_on_octets_without_splitting_a_character()
    {
        // Diacritics are two octets each, so a title that is under the limit in characters is
        // over it in octets — the case a character count would get wrong.
        var title = string.Concat(Enumerable.Repeat("Peșteră ", 20));
        var feed = CalendarFeedWriter.Write("Silex", [Row(title: title)]);

        foreach (var line in Lines(feed))
        {
            Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(75, line);
        }

        // Unfolding — removing every CRLF-space — gives back exactly the line that was written,
        // so no character was cut in two and no octet was lost.
        var unfolded = feed.Replace("\r\n ", string.Empty);
        unfolded.ShouldContain("SUMMARY:" + title + "\r\n");
        // And the fold is a real fold: the summary did not fit on one line.
        feed.ShouldContain("\r\n ");
    }

    [Fact]
    public void A_line_exactly_at_the_limit_is_not_folded()
    {
        var text = new StringBuilder();
        CalendarFeedWriter.Line(text, new string('x', 75));

        text.ToString().ShouldBe(new string('x', 75) + "\r\n");
    }
}
