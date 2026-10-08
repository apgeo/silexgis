// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Time underground read from a tracking log: an entry and the first exit after it. Each case is
/// a way a real log departs from one entry followed by one exit, and each would move a total
/// silently if the rule gave way — a doubled report counted twice, an open entry counted as
/// nothing-so-far, a night split at midnight.
/// </summary>
public class TrackingAttendanceTests
{
    private static readonly TimeSpan Bucharest = TimeSpan.FromHours(3);

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(2026, 9, day, hour, minute, second, Bucharest);

    private static TrackingPassage In(DateTimeOffset at) => new(TripPositionEventKind.Entered, at);

    private static TrackingPassage Out(DateTimeOffset at) => new(TripPositionEventKind.Exited, at);

    [Fact]
    public void An_entry_and_the_exit_after_it_are_one_stay()
    {
        TrackingAttendance.UndergroundMinutes([In(At(12, 9, 0)), Out(At(12, 17, 30))]).ShouldBe(510);
    }

    /// <summary>
    /// The case the rule exists for. Somebody whose exit was never written was inside for a time
    /// the log does not know; null keeps them out of the sum and out of the count of people the
    /// sum covers, where a zero would put them in both.
    /// </summary>
    [Fact]
    public void An_entry_nobody_closed_adds_nothing_and_is_not_a_zero()
    {
        TrackingAttendance.UndergroundMinutes([In(At(12, 9, 0))]).ShouldBeNull();
        TrackingAttendance.StaysOf([In(At(12, 9, 0))]).ShouldBeEmpty();

        // A closed stay followed by a second entry left open is the closed stay and no more.
        TrackingAttendance.UndergroundMinutes([In(At(12, 9, 0)), Out(At(12, 11, 0)), In(At(12, 13, 0))])
            .ShouldBe(120);
    }

    [Fact]
    public void An_exit_with_no_entry_before_it_adds_nothing()
    {
        TrackingAttendance.UndergroundMinutes([Out(At(12, 17, 0))]).ShouldBeNull();
        TrackingAttendance.UndergroundMinutes([Out(At(12, 8, 0)), In(At(12, 9, 0)), Out(At(12, 10, 0))])
            .ShouldBe(60);
    }

    /// <summary>
    /// A place reported before anybody wrote "entered" is the moment the person was first heard of
    /// inside, not the moment they went in — so it starts no stay, and a note never does.
    /// </summary>
    [Fact]
    public void A_place_or_a_note_neither_opens_nor_closes_a_stay()
    {
        TrackingAttendance.UndergroundMinutes(
            [
                new TrackingPassage(TripPositionEventKind.AtStation, At(12, 9, 0)),
                new TrackingPassage(TripPositionEventKind.Note, At(12, 10, 0)),
                Out(At(12, 17, 0)),
            ])
            .ShouldBeNull();

        TrackingAttendance.UndergroundMinutes(
            [
                In(At(12, 9, 0)),
                new TrackingPassage(TripPositionEventKind.AtDepth, At(12, 12, 0)),
                Out(At(12, 17, 0)),
            ])
            .ShouldBe(480);
    }

    /// <summary>
    /// The reports are instants, so a night is the difference between two moments — no midnight
    /// to infer, and no dependence on which day the trip says it ended.
    /// </summary>
    [Fact]
    public void A_stay_across_midnight_is_the_time_between_its_two_instants()
    {
        TrackingAttendance.UndergroundMinutes([In(At(12, 22, 0)), Out(At(13, 6, 0))]).ShouldBe(480);

        // Thirty hours, two midnights apart from the calendar's point of view.
        TrackingAttendance.UndergroundMinutes([In(At(12, 20, 0)), Out(At(14, 2, 0))]).ShouldBe(1800);
    }

    [Fact]
    public void Two_stays_on_one_trip_are_added()
    {
        var stays = TrackingAttendance.StaysOf(
            [In(At(12, 9, 0)), Out(At(12, 12, 0)), In(At(12, 14, 0)), Out(At(12, 15, 30))]);

        stays.Count.ShouldBe(2);
        stays.Sum(s => s.Minutes).ShouldBe(270);
    }

    /// <summary>
    /// Two reports about one person at one instant are legal in a log. A doubled entry or a
    /// doubled exit is one statement made twice: it can neither count the stay twice nor move
    /// either end of it.
    /// </summary>
    [Fact]
    public void Twin_reports_count_a_stay_once()
    {
        TrackingAttendance.UndergroundMinutes(
                [In(At(12, 9, 0)), In(At(12, 9, 0)), Out(At(12, 17, 0)), Out(At(12, 17, 0))])
            .ShouldBe(480);

        // An entry repeated later by somebody who had not seen the first: in since the first.
        TrackingAttendance.UndergroundMinutes([In(At(12, 9, 0)), In(At(12, 10, 0)), Out(At(12, 17, 0))])
            .ShouldBe(480);

        // An exit repeated later: out since the first.
        TrackingAttendance.UndergroundMinutes([In(At(12, 9, 0)), Out(At(12, 17, 0)), Out(At(12, 18, 0))])
            .ShouldBe(480);
    }

    /// <summary>
    /// Reports are read by the instant they speak of, whatever order they were typed in — a log
    /// is routinely completed afterwards — and two that share an instant keep the order they were
    /// handed in, so the same log always comes to the same figure.
    /// </summary>
    [Fact]
    public void Reports_are_read_in_time_order_and_ties_keep_the_order_given()
    {
        TrackingAttendance.UndergroundMinutes([Out(At(12, 17, 0)), In(At(12, 9, 0))]).ShouldBe(480);

        // Out and back in at one instant, listed in that order: the first stay closes there and
        // a second opens.
        TrackingAttendance.UndergroundMinutes(
                [In(At(12, 9, 0)), Out(At(12, 12, 0)), In(At(12, 12, 0)), Out(At(12, 15, 0))])
            .ShouldBe(360);

        // Listed the other way round the entry is a repeat of being in, and the exit ends it.
        TrackingAttendance.UndergroundMinutes(
                [In(At(12, 9, 0)), In(At(12, 12, 0)), Out(At(12, 12, 0)), Out(At(12, 15, 0))])
            .ShouldBe(180);
    }

    /// <summary>
    /// Each instant is cut to its minute before the two are subtracted, which is what a roster
    /// holding the same two moments to the minute would come to. Subtracting first would give 239.
    /// </summary>
    [Fact]
    public void Seconds_are_dropped_from_each_end_as_a_roster_drops_them()
    {
        var entered = At(12, 9, 0, 50);
        var exited = At(12, 13, 0, 10);

        TrackingAttendance.UndergroundMinutes([In(entered), Out(exited)]).ShouldBe(240);
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 9, 12),
                null,
                TimeOnly.FromTimeSpan(entered.TimeOfDay),
                TimeOnly.FromTimeSpan(exited.TimeOfDay))
            .ShouldBe(240);
    }

    /// <summary>The same two moments written against different offsets are the same stay.</summary>
    [Fact]
    public void An_instant_is_an_instant_whatever_offset_it_was_written_with()
    {
        var entered = new DateTimeOffset(2026, 9, 12, 9, 0, 0, Bucharest);
        var exited = new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero);

        TrackingAttendance.UndergroundMinutes([In(entered), Out(exited)]).ShouldBe(480);
    }

    [Fact]
    public void No_reports_at_all_is_nothing_rather_than_zero()
    {
        TrackingAttendance.UndergroundMinutes(null).ShouldBeNull();
        TrackingAttendance.UndergroundMinutes([]).ShouldBeNull();
    }
}
