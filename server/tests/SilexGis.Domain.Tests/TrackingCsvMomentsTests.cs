// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>Reading the instant a tracking report was made off a spreadsheet cell.</summary>
public class TrackingCsvMomentsTests
{
    private static TrackingCsvMoment Read(string? text, TripCsvDateOrder order = TripCsvDateOrder.DayFirst) =>
        TrackingCsvMoments.Read(text, order);

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
}
