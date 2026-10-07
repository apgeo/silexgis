// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>Reading the instant a tracking report was made off a spreadsheet cell.</summary>
public class TrackingCsvMomentsTests
{
    private static TrackingCsvMoment Read(
        string? text, TripCsvDateOrder order = TripCsvDateOrder.DayFirst, TimeZoneInfo? zone = null) =>
        TrackingCsvMoments.Read(text, order, zone);

    [Fact]
    public void A_date_and_a_time_beside_it_are_read_as_one_instant()
    {
        var read = Read("12.09.2026 14:30");
        read.Kind.ShouldBe(TrackingCsvMomentKind.Read);
        read.At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_spellings_a_club_and_a_device_both_write_are_understood()
    {
        Read("12/09/2026 14:30:45").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 45, TimeSpan.Zero));
        Read("12.09.2026 9:05").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 9, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_instant_carrying_its_own_offset_is_taken_as_given_rather_than_reinterpreted()
    {
        // An export says what zone it means. Nothing here should second-guess that; what it says
        // is converted rather than relabelled.
        Read("2026-09-12T14:30:00+03:00").At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 11, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_cell_at_the_edge_of_the_calendar_whose_instant_falls_off_it_is_unreadable_and_not_a_failure()
    {
        // A mistyped year is one bad cell. Read in a zone, or with an offset of its own, its
        // instant can fall before the first date there is or after the last — and that has to be
        // a finding on its row, not an exception that takes the whole sheet's reading with it.
        var bucharest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        Read("01.01.0001 00:30", zone: bucharest).Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("31.12.9999 23:30", zone: newYork).Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("0001-01-01T00:30+02:00").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("9999-12-31T23:30-05:00").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        // The cell's own offset decides, whatever zone the sheet is in.
        Read("0001-01-01T00:30+02:00", zone: newYork).Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);

        // The twins: the same cells are moments where nothing carries them off the calendar — as
        // written, in a zone on the other side of Greenwich, and at an offset pointing inward.
        Read("01.01.0001 00:30").At.ShouldBe(new DateTimeOffset(1, 1, 1, 0, 30, 0, TimeSpan.Zero));
        Read("31.12.9999 23:30").At.ShouldBe(new DateTimeOffset(9999, 12, 31, 23, 30, 0, TimeSpan.Zero));
        Read("01.01.0001 00:30", zone: newYork).Kind.ShouldBe(TrackingCsvMomentKind.Read);
        Read("31.12.9999 23:30", zone: bucharest).Kind.ShouldBe(TrackingCsvMomentKind.Read);
        Read("0001-01-01T00:30-05:00").Kind.ShouldBe(TrackingCsvMomentKind.Read);
        Read("9999-12-31T23:30+02:00").Kind.ShouldBe(TrackingCsvMomentKind.Read);
    }

    [Fact]
    public void The_time_is_found_by_its_colon_rather_than_by_its_position()
    {
        // One sheet writes the date first and another the time first. A split on the first space
        // would read the second as a date of "14:30".
        Read("14:30 12.09.2026").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_date_with_no_time_is_said_to_be_that_rather_than_filed_at_midnight()
    {
        // <b>The load-bearing one.</b> The upsert key is the caver and the instant, so reading a
        // bare date as midnight would collapse a whole day's reports onto one key — and a
        // re-import would then overwrite each row with the next, leaving one report where there
        // were nine. Named as its own outcome so the importer can refuse the row and say why.
        Read("12.09.2026").Kind.ShouldBe(TrackingCsvMomentKind.DateWithoutTime);

        // Asserted beside a full one, so this is a rule about the missing time rather than a
        // reader that fails on dates.
        Read("12.09.2026 00:00").Kind.ShouldBe(TrackingCsvMomentKind.Read);
    }

    [Fact]
    public void The_decided_order_is_applied_strictly_rather_than_re_read_the_other_way()
    {
        // Ambiguous: the order decides, and the two readings are different days.
        Read("03.04.2026 10:00", TripCsvDateOrder.DayFirst).At.Month.ShouldBe(4);
        Read("03.04.2026 10:00", TripCsvDateOrder.MonthFirst).At.Month.ShouldBe(3);

        // And a cell the decided order cannot make a real date of is unreadable rather than
        // quietly re-read the other way round. Deciding the order is a question about the whole
        // column — one row cannot settle it, since 5/11 is a real date either way — so it is asked
        // once by the caller and applied here without second-guessing. A row that disagrees with
        // the answer is a row to report.
        Read("13.04.2026 10:00", TripCsvDateOrder.MonthFirst).Kind
            .ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("13.04.2026 10:00", TripCsvDateOrder.DayFirst).At.Day.ShouldBe(13);
    }

    [Fact]
    public void A_cell_with_no_offset_is_not_shifted_by_where_the_server_happens_to_be()
    {
        // <b>The bug this test exists for.</b> Handing a cell with no offset to the framework's
        // parser gets one back in the server's own zone, so "14:30" imported three hours out on
        // this machine and correctly on another — and would change meaning if the server moved. A
        // club writes wall-clock time and says nothing about zone; what it wrote is what is stored.
        Read("2026-09-12 14:30").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("2026-09-12T14:30:00").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));

        // The twin: a cell that does state a zone is converted rather than relabelled.
        Read("2026-09-12T14:30:00Z").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("2026-09-12T14:30:00+03:00").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 11, 30, 0, TimeSpan.Zero));
    }

    private static readonly TimeZoneInfo Bucharest = ZoneOf("Europe/Bucharest");

    private static TimeZoneInfo ZoneOf(string name)
    {
        TrackingCsvZones.TryFind(name, out var zone).ShouldBeTrue(name);
        return zone!;
    }

    [Fact]
    public void A_cell_with_no_offset_is_read_on_the_clocks_of_the_zone_the_sheet_was_kept_in()
    {
        // The same cell, three hours apart in summer and two in winter: the offset is the one the
        // zone kept on the cell's own date, not one figure applied to the whole sheet.
        Read("12.07.2026 14:05", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero));
        Read("12.01.2026 14:05", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 1, 12, 12, 5, 0, TimeSpan.Zero));

        // The twin, on the same cell: with no zone named it stands as written.
        Read("12.07.2026 14:05").At.ShouldBe(new DateTimeOffset(2026, 7, 12, 14, 5, 0, TimeSpan.Zero));

        // Every spelling of a moment goes through the same reading, the ISO one and the one
        // written time-first without a colon included.
        Read("2026-07-12T14:05:00", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero));
        Read("1405 12.07.2026", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_cell_that_states_its_own_offset_is_never_moved_by_the_sheets_zone()
    {
        // An export says what it means, and the zone chosen for the hand-typed rows beside it has
        // no say. A Z is an offset stated — of nothing — and not an offset left out.
        var written = new DateTimeOffset(2026, 7, 12, 14, 5, 0, TimeSpan.Zero);
        Read("2026-07-12T14:05:00Z", zone: Bucharest).At.ShouldBe(written);
        Read("2026-07-12T14:05:00+00:00", zone: Bucharest).At.ShouldBe(written);
        Read("2026-07-12 14:05 +05:30", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 7, 12, 8, 35, 0, TimeSpan.Zero));
        Read("2026-07-12T14:05:00Z", zone: Bucharest).ShouldBe(Read("2026-07-12T14:05:00Z"));
    }

    [Fact]
    public void A_time_the_zones_clocks_skipped_is_said_to_be_one_rather_than_given_an_instant()
    {
        // 03:30 on the night Bucharest's clocks go forward was never on any clock there.
        var skipped = Read("29.03.2026 03:30", zone: Bucharest);
        skipped.Kind.ShouldBe(TrackingCsvMomentKind.SkippedByClockChange);
        skipped.RepeatedByClockChange.ShouldBeFalse();

        // The same cell is an ordinary moment where no zone, or a zone without that change, reads
        // it — and where the cell says which offset it means.
        Read("29.03.2026 03:30").Kind.ShouldBe(TrackingCsvMomentKind.Read);
        Read("29.03.2026 03:30", zone: ZoneOf("Asia/Kolkata")).Kind.ShouldBe(TrackingCsvMomentKind.Read);
        Read("29.03.2026 03:30 +02:00", zone: Bucharest).At
            .ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_time_the_zones_clocks_showed_twice_is_read_as_the_first_and_marked()
    {
        // 03:30 on the night Bucharest's clocks go back happened at 00:30 and again at 01:30 UTC.
        var repeated = Read("25.10.2026 03:30", zone: Bucharest);
        repeated.Kind.ShouldBe(TrackingCsvMomentKind.Read);
        repeated.RepeatedByClockChange.ShouldBeTrue();
        repeated.At.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));

        // Writing the offset settles which of the two it was, and nothing is left to mark.
        var second = Read("25.10.2026 03:30 +02:00", zone: Bucharest);
        second.RepeatedByClockChange.ShouldBeFalse();
        second.At.ShouldBe(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));

        // An ordinary time is not marked, and neither is that hour read with no zone.
        Read("25.10.2026 14:05", zone: Bucharest).RepeatedByClockChange.ShouldBeFalse();
        Read("25.10.2026 03:30").RepeatedByClockChange.ShouldBeFalse();
    }

    [Fact]
    public void A_zone_changes_only_the_instant_and_not_what_counts_as_a_moment()
    {
        Read("12.07.2026", zone: Bucharest).Kind.ShouldBe(TrackingCsvMomentKind.DateWithoutTime);
        Read("", zone: Bucharest).Kind.ShouldBe(TrackingCsvMomentKind.Empty);
        Read("la prânz", zone: Bucharest).Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("12.07.2026 14:05 +99:00", zone: Bucharest).Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
    }

    [Fact]
    public void A_dash_in_the_date_is_not_taken_for_an_offset_wherever_the_date_is_written()
    {
        // <b>The bug these exist for.</b> An offset was looked for as any sign after the first
        // colon, so a time-first cell with a dashed date "stated an offset" and the whole cell went
        // to the framework's parser — which shifted it into the server's zone and read the day and
        // the month the other way round from what the file decided. All three are silent wrong
        // answers on the one field the upsert key is built from.
        Read("14:30 2026-09-12").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("14:30 12-09-2026", TripCsvDateOrder.DayFirst).At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("12-09-2026 14:30", TripCsvDateOrder.DayFirst).At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_day_first_date_beside_a_real_offset_is_read_in_the_decided_order_and_then_shifted()
    {
        // The date half follows the file's order like every other cell, and only the offset the
        // cell wrote is applied — glued to the time or a word after it, both as a device writes.
        var expected = new DateTimeOffset(2026, 9, 12, 11, 30, 0, TimeSpan.Zero);
        Read("12.09.2026 14:30 +03:00", TripCsvDateOrder.DayFirst).At.ShouldBe(expected);
        Read("12.09.2026 14:30+03:00", TripCsvDateOrder.DayFirst).At.ShouldBe(expected);
        Read("12.09.2026 14:30:00 +0300", TripCsvDateOrder.DayFirst).At.ShouldBe(expected);
        Read("12.09.2026 14:30 Z", TripCsvDateOrder.DayFirst).At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("2026-09-12T14:30:00.000Z").At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
        Read("2026-09-12T14:30:00-05:00").At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 19, 30, 0, TimeSpan.Zero));

        // And an offset no clock has is not a moment.
        Read("12.09.2026 14:30 +25:00").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
    }

    [Fact]
    public void A_cell_that_states_an_offset_contributes_no_evidence_about_the_days_order_and_one_that_does_not_does()
    {
        TrackingCsvMoments.DatePartOf("2026-09-12T14:30:00+03:00").Kind.ShouldBe(TripCsvDateKind.Empty);
        TrackingCsvMoments.DatePartOf("14:30 13-09-2026").ProvenOrder.ShouldBe(TripCsvDateOrder.DayFirst);
        TrackingCsvMoments.DatePartOf("14:30 2026-09-12").Kind.ShouldBe(TripCsvDateKind.Settled);
    }

    [Fact]
    public void A_time_written_without_its_colon_is_read_beside_a_date_wherever_it_stands()
    {
        // The colon-less spelling was listed among the accepted times and could never be reached:
        // the time word was found by its colon alone, so "0815" stayed glued to the date and the
        // whole cell was refused as a date of four parts.
        Read("12.09.2026 0815").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        Read("0815 12.09.2026").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        Read("2026-09-12 1645").At.ShouldBe(new DateTimeOffset(2026, 9, 12, 16, 45, 0, TimeSpan.Zero));
        TrackingCsvMoments.DatePartOf("13.09.2026 0815").ProvenOrder.ShouldBe(TripCsvDateOrder.DayFirst);

        // Three digits are never a time, four digits no clock shows are not one either, and four
        // digits beside something that is not a date are left where they stand.
        Read("12.09.2026 815").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("2500 12.09.2026").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("Meandru 0815").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
    }

    [Fact]
    public void A_blank_cell_is_nothing_written_and_not_a_fault()
    {
        // A sheet has blank rows and trailing lines, and an importer that called them faults would
        // report a screenful of problems about the end of every file.
        Read(null).Kind.ShouldBe(TrackingCsvMomentKind.Empty);
        Read("").Kind.ShouldBe(TrackingCsvMomentKind.Empty);
        Read("   ").Kind.ShouldBe(TrackingCsvMomentKind.Empty);
        Read("-").Kind.ShouldBe(TrackingCsvMomentKind.Empty);
    }

    [Fact]
    public void Something_written_that_is_not_a_moment_is_unreadable_rather_than_guessed()
    {
        Read("la prânz").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("12.09.2026 seara").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("99.99.2026 14:30").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
    }

    [Fact]
    public void A_time_with_no_date_is_said_to_be_that_until_a_day_is_named_for_it()
    {
        // Not unreadable: it is a perfectly good time, and what is missing is a date.
        Read("08:15").Kind.ShouldBe(TrackingCsvMomentKind.TimeWithoutDate);
        Read("08:15 +03:00").Kind.ShouldBe(TrackingCsvMomentKind.TimeWithoutDate);
        // Only where it is a time, though: a colon alone does not make one.
        Read("ora: seara").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
        Read("25:99").Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);

        var day = new DateOnly(2026, 9, 12);
        var read = TrackingCsvMoments.Read("08:15", TripCsvDateOrder.DayFirst, zone: null, day);
        read.Kind.ShouldBe(TrackingCsvMomentKind.Read);
        read.At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        read.OnNamedDay.ShouldBeTrue();

        // The cell's own offset still decides the instant; the day only supplies the date.
        TrackingCsvMoments.Read("08:15 +03:00", TripCsvDateOrder.DayFirst, zone: null, day)
            .At.ShouldBe(new DateTimeOffset(2026, 9, 12, 5, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Four_bare_digits_are_a_time_only_where_a_day_was_named_and_a_year_everywhere_else()
    {
        // In a moment column "2026" is a year, and a date with no time.
        Read("2026").Kind.ShouldBe(TrackingCsvMomentKind.DateWithoutTime);

        var read = TrackingCsvMoments.Read(
            "0815", TripCsvDateOrder.DayFirst, zone: null, new DateOnly(2026, 9, 12));
        read.At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        read.OnNamedDay.ShouldBeTrue();

        // And digits that are no time of day are no moment, named day or not.
        TrackingCsvMoments.Read("2575", TripCsvDateOrder.DayFirst, zone: null, new DateOnly(2026, 9, 12))
            .Kind.ShouldBe(TrackingCsvMomentKind.Unreadable);
    }

    [Fact]
    public void A_cell_that_writes_its_own_date_is_on_that_date_whatever_day_was_named()
    {
        var read = TrackingCsvMoments.Read(
            "12.09.2026 08:15", TripCsvDateOrder.DayFirst, zone: null, new DateOnly(2020, 1, 1));

        read.At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        read.OnNamedDay.ShouldBeFalse();
    }

    [Fact]
    public void A_named_days_time_meets_the_zones_two_edge_hours_like_any_other()
    {
        // The clocks in Bucharest went forward at 03:00 on 29 March 2026 and back at 04:00 on
        // 25 October 2026: 03:30 never happened on the first and happened twice on the second.
        TrackingCsvMoments.Read("03:30", TripCsvDateOrder.DayFirst, Bucharest, new DateOnly(2026, 3, 29))
            .Kind.ShouldBe(TrackingCsvMomentKind.SkippedByClockChange);

        var twice = TrackingCsvMoments.Read(
            "03:30", TripCsvDateOrder.DayFirst, Bucharest, new DateOnly(2026, 10, 25));
        twice.Kind.ShouldBe(TrackingCsvMomentKind.Read);
        twice.RepeatedByClockChange.ShouldBeTrue();
        twice.OnNamedDay.ShouldBeTrue();
    }

    [Fact]
    public void Whether_a_time_cell_needs_a_day_is_asked_of_the_cell_and_not_of_its_column()
    {
        TrackingCsvMoments.IsTimeAlone("08:15").ShouldBeTrue();
        TrackingCsvMoments.IsTimeAlone("0815").ShouldBeTrue();
        TrackingCsvMoments.IsTimeAlone(" 8:15:30 ").ShouldBeTrue();

        TrackingCsvMoments.IsTimeAlone("12.09.2026 08:15").ShouldBeFalse();
        TrackingCsvMoments.IsTimeAlone("12.09.2026").ShouldBeFalse();
        TrackingCsvMoments.IsTimeAlone("").ShouldBeFalse();
        TrackingCsvMoments.IsTimeAlone(null).ShouldBeFalse();
    }
}
