// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a tracking log says about when somebody went in and came out, turned into the two clock
/// readings a roster holds. The roster's times have no day and no zone, and its length of stay is
/// worked out from them by a rule of its own — so every pair offered here is held to that rule:
/// what the roster would then count has to be the time that actually passed, or no pair is offered.
/// </summary>
public class TrackingRosterTimesTests
{
    private static readonly DateOnly Saturday = new(2026, 9, 12);

    private static TimeZoneInfo Zone(string name)
    {
        TrackingCsvZones.TryFind(name, out var zone).ShouldBeTrue($"this host has no zone data for {name}");
        return zone!;
    }

    private static TimeZoneInfo Bucharest => Zone("Europe/Bucharest");

    private static DateTimeOffset Utc(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.Zero);

    private static TrackingPassage In(DateTimeOffset at) => new(TripPositionEventKind.Entered, at);

    private static TrackingPassage Out(DateTimeOffset at) => new(TripPositionEventKind.Exited, at);

    private static TrackingEntryExit Pair(DateTimeOffset? entered, DateTimeOffset? exited) => new(entered, exited, 1);

    private static int Minutes(TrackingEntryExit watch) => new TrackingStay(watch.Entered!.Value, watch.Exited!.Value).Minutes;

    // ---- what the log says ---------------------------------------------------------------

    [Fact]
    public void The_first_entry_and_the_last_exit_are_what_the_log_says()
    {
        var watch = TrackingAttendance.EntryExitOf(
            [
                new TrackingPassage(TripPositionEventKind.AtStation, Utc(9, 12, 6, 30)),
                Out(Utc(9, 12, 14, 0)),
                In(Utc(9, 12, 6, 0)),
            ]);

        watch.Entered.ShouldBe(Utc(9, 12, 6, 0));
        watch.Exited.ShouldBe(Utc(9, 12, 14, 0));
        watch.Stays.ShouldBe(1);
    }

    /// <summary>
    /// A roster has room for one pair. Somebody who came up for lunch is given the outer one, and
    /// the count of stays is what lets a screen say that the pair covers time on the surface.
    /// </summary>
    [Fact]
    public void Two_stays_give_the_outer_pair_and_say_there_were_two()
    {
        var watch = TrackingAttendance.EntryExitOf(
            [In(Utc(9, 12, 6, 0)), Out(Utc(9, 12, 9, 0)), In(Utc(9, 12, 10, 0)), Out(Utc(9, 12, 13, 0))]);

        watch.ShouldBe(new TrackingEntryExit(Utc(9, 12, 6, 0), Utc(9, 12, 13, 0), 2));
    }

    /// <summary>
    /// Out at nine and back in at ten with nothing after: as far as the log knows the person is
    /// inside. Nine is not when they came out, and offering it would close a stay nobody closed.
    /// </summary>
    [Fact]
    public void An_exit_followed_by_another_entry_is_not_the_time_somebody_came_out()
    {
        var watch = TrackingAttendance.EntryExitOf([In(Utc(9, 12, 6, 0)), Out(Utc(9, 12, 9, 0)), In(Utc(9, 12, 10, 0))]);

        watch.Entered.ShouldBe(Utc(9, 12, 6, 0));
        watch.Exited.ShouldBeNull();
        watch.Stays.ShouldBe(1);

        // The positive case beside it: the same log once the second stay is closed.
        TrackingAttendance.EntryExitOf(
                [In(Utc(9, 12, 6, 0)), Out(Utc(9, 12, 9, 0)), In(Utc(9, 12, 10, 0)), Out(Utc(9, 12, 11, 0))])
            .Exited.ShouldBe(Utc(9, 12, 11, 0));
    }

    [Fact]
    public void An_exit_before_any_entry_and_a_log_with_neither_say_nothing()
    {
        TrackingAttendance.EntryExitOf([Out(Utc(9, 12, 5, 0)), In(Utc(9, 12, 6, 0))])
            .ShouldBe(new TrackingEntryExit(Utc(9, 12, 6, 0), null, 0));
        TrackingAttendance.EntryExitOf([Out(Utc(9, 12, 5, 0))]).ShouldBe(new TrackingEntryExit(null, null, 0));
        TrackingAttendance.EntryExitOf([new TrackingPassage(TripPositionEventKind.AtDepth, Utc(9, 12, 7, 0))])
            .ShouldBe(default(TrackingEntryExit));
        TrackingAttendance.EntryExitOf(null).ShouldBe(default(TrackingEntryExit));
        TrackingAttendance.EntryExitOf([]).ShouldBe(default(TrackingEntryExit));
    }

    // ---- what the roster can hold ----------------------------------------------------------

    [Fact]
    public void The_two_moments_are_read_on_the_zones_clocks_to_the_minute()
    {
        var watch = Pair(Utc(9, 12, 6, 10, 50), Utc(9, 12, 14, 45, 10));

        var times = TrackingAttendance.RosterTimesOf(watch, Saturday, null, Bucharest);

        times.ShouldBe(new TrackingRosterTimes(new TimeOnly(9, 10), new TimeOnly(17, 45), null));
        TripDuration.UndergroundMinutes(Saturday, null, times.Entry, times.Exit).ShouldBe(Minutes(watch));
        Minutes(watch).ShouldBe(515);
    }

    /// <summary>
    /// The same two moments on another zone's clocks are other readings and, here, another day:
    /// which zone is stated is not a matter of presentation.
    /// </summary>
    [Fact]
    public void The_stated_zone_decides_the_readings_and_the_day()
    {
        // Half past one on Saturday morning in Bucharest; half past ten on Friday night in UTC.
        var watch = Pair(Utc(9, 11, 22, 30), Utc(9, 12, 4, 0));

        TrackingAttendance.RosterTimesOf(watch, Saturday, null, Bucharest)
            .ShouldBe(new TrackingRosterTimes(new TimeOnly(1, 30), new TimeOnly(7, 0), null));
        TrackingAttendance.RosterTimesOf(watch, Saturday, null, TimeZoneInfo.Utc)
            .ShouldBe(new TrackingRosterTimes(new TimeOnly(22, 30), new TimeOnly(4, 0), TrackingRosterTimesProblem.EntryOffTripDate));
    }

    [Fact]
    public void A_night_on_a_one_day_trip_is_held_and_counts_the_same_minutes()
    {
        // In at ten on Saturday night, out at six on Sunday morning, Bucharest.
        var watch = Pair(Utc(9, 12, 19, 0), Utc(9, 13, 3, 0));

        foreach (var end in new DateOnly?[] { null, Saturday, Saturday.AddDays(1) })
        {
            var times = TrackingAttendance.RosterTimesOf(watch, Saturday, end, Bucharest);

            times.ShouldBe(new TrackingRosterTimes(new TimeOnly(22, 0), new TimeOnly(6, 0), null));
            TripDuration.UndergroundMinutes(Saturday, end, times.Entry, times.Exit).ShouldBe(480);
        }
    }

    [Fact]
    public void A_push_of_thirty_hours_is_held_only_by_a_trip_that_says_which_day_it_ended()
    {
        // Saturday 20:00 to Monday 02:00, Bucharest.
        var watch = Pair(Utc(9, 12, 17, 0), Utc(9, 13, 23, 0));

        var held = TrackingAttendance.RosterTimesOf(watch, Saturday, Saturday.AddDays(2), Bucharest);
        held.Problem.ShouldBeNull();
        TripDuration.UndergroundMinutes(Saturday, Saturday.AddDays(2), held.Entry, held.Exit).ShouldBe(1800);

        // On a trip that names no end, or the wrong one, the same pair would be read as six
        // hours, or as a day more or less — so it is not offered.
        TrackingAttendance.RosterTimesOf(watch, Saturday, null, Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitOffTripDate);
        TrackingAttendance.RosterTimesOf(watch, Saturday, Saturday.AddDays(1), Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitOffTripDate);
        TrackingAttendance.RosterTimesOf(watch, Saturday, Saturday.AddDays(3), Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitOffTripDate);
    }

    /// <summary>
    /// Out the next day at a later hour than the entry: the roster would read the pair as an hour,
    /// not as twenty-five. And out on the first day of a trip that says it ended on the second:
    /// the roster would add a day.
    /// </summary>
    [Fact]
    public void An_exit_on_a_day_the_roster_would_misread_is_a_problem()
    {
        var overADay = Pair(Utc(9, 12, 6, 0), Utc(9, 13, 7, 0));
        TrackingAttendance.RosterTimesOf(overADay, Saturday, null, Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitOffTripDate);

        var sameDay = Pair(Utc(9, 12, 6, 0), Utc(9, 12, 12, 0));
        TrackingAttendance.RosterTimesOf(sameDay, Saturday, Saturday.AddDays(1), Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitOffTripDate);
        // The same stay on the trip as a single day is the ordinary case.
        TrackingAttendance.RosterTimesOf(sameDay, Saturday, null, Bucharest).Problem.ShouldBeNull();
    }

    [Fact]
    public void An_entry_on_another_day_than_the_trips_first_is_a_problem()
    {
        // Sunday morning, on a trip dated Saturday to Sunday: the roster would date it Saturday.
        var watch = Pair(Utc(9, 13, 6, 0), Utc(9, 13, 12, 0));

        var times = TrackingAttendance.RosterTimesOf(watch, Saturday, Saturday.AddDays(1), Bucharest);

        times.Problem.ShouldBe(TrackingRosterTimesProblem.EntryOffTripDate);
        // What was found is still shown beside the reason.
        times.Entry.ShouldBe(new TimeOnly(9, 0));
        times.Exit.ShouldBe(new TimeOnly(15, 0));
    }

    [Fact]
    public void A_missing_or_inverted_moment_is_said_as_what_is_missing()
    {
        TrackingAttendance.RosterTimesOf(Pair(null, Utc(9, 12, 12, 0)), Saturday, null, Bucharest)
            .ShouldBe(new TrackingRosterTimes(null, new TimeOnly(15, 0), TrackingRosterTimesProblem.NoEntry));
        TrackingAttendance.RosterTimesOf(Pair(Utc(9, 12, 6, 0), null), Saturday, null, Bucharest)
            .ShouldBe(new TrackingRosterTimes(new TimeOnly(9, 0), null, TrackingRosterTimesProblem.NoExit));
        TrackingAttendance.RosterTimesOf(default, Saturday, null, Bucharest)
            .ShouldBe(new TrackingRosterTimes(null, null, TrackingRosterTimesProblem.NoEntry));
        TrackingAttendance.RosterTimesOf(Pair(Utc(9, 12, 12, 0), Utc(9, 12, 6, 0)), Saturday, null, Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ExitBeforeEntry);
    }

    // ---- clocks that change -----------------------------------------------------------------

    /// <summary>
    /// Each moment is read at the offset the zone kept then: the same hours of the day in July and
    /// in January are an hour apart as instants, and both come to the same readings and minutes.
    /// </summary>
    [Fact]
    public void Summer_and_winter_are_each_read_at_their_own_offset()
    {
        var july = Pair(Utc(7, 11, 6, 0), Utc(7, 11, 14, 0));
        var january = Pair(Utc(1, 10, 7, 0), Utc(1, 10, 15, 0));

        var summer = TrackingAttendance.RosterTimesOf(july, new DateOnly(2026, 7, 11), null, Bucharest);
        var winter = TrackingAttendance.RosterTimesOf(january, new DateOnly(2026, 1, 10), null, Bucharest);

        summer.ShouldBe(new TrackingRosterTimes(new TimeOnly(9, 0), new TimeOnly(17, 0), null));
        winter.ShouldBe(summer);
        TripDuration.UndergroundMinutes(new DateOnly(2026, 7, 11), null, summer.Entry, summer.Exit).ShouldBe(Minutes(july));
        TripDuration.UndergroundMinutes(new DateOnly(2026, 1, 10), null, winter.Entry, winter.Exit).ShouldBe(Minutes(january));
    }

    /// <summary>
    /// The night the clocks go back, ten at night to six in the morning is nine hours; the night
    /// they go forward it is seven. The two readings say eight both times, so neither pair is
    /// offered — and the day after, with both moments on the new clocks, it is offered again.
    /// </summary>
    [Fact]
    public void A_stay_across_a_change_of_the_clocks_is_not_offered_as_a_pair()
    {
        // 24 October 22:00 at +03:00, 25 October 06:00 at +02:00.
        var autumn = Pair(Utc(10, 24, 19, 0), Utc(10, 25, 4, 0));
        Minutes(autumn).ShouldBe(540);
        var back = TrackingAttendance.RosterTimesOf(autumn, new DateOnly(2026, 10, 24), null, Bucharest);
        back.ShouldBe(new TrackingRosterTimes(new TimeOnly(22, 0), new TimeOnly(6, 0), TrackingRosterTimesProblem.ClockChanged));

        // 28 March 22:00 at +02:00, 29 March 06:00 at +03:00.
        var spring = Pair(Utc(3, 28, 20, 0), Utc(3, 29, 3, 0));
        Minutes(spring).ShouldBe(420);
        TrackingAttendance.RosterTimesOf(spring, new DateOnly(2026, 3, 28), null, Bucharest)
            .ShouldBe(new TrackingRosterTimes(new TimeOnly(22, 0), new TimeOnly(6, 0), TrackingRosterTimesProblem.ClockChanged));

        // Inside the hour the clocks show twice: in on its first showing, out on its second, forty
        // minutes later by any watch and "twenty minutes earlier" by the readings.
        var twice = Pair(Utc(10, 25, 0, 30), Utc(10, 25, 1, 10));
        TrackingAttendance.RosterTimesOf(twice, new DateOnly(2026, 10, 25), null, Bucharest).Problem
            .ShouldBe(TrackingRosterTimesProblem.ClockChanged);

        // Sunday after the change, both moments on winter time.
        var after = Pair(Utc(10, 25, 7, 0), Utc(10, 25, 15, 0));
        var held = TrackingAttendance.RosterTimesOf(after, new DateOnly(2026, 10, 25), null, Bucharest);
        held.ShouldBe(new TrackingRosterTimes(new TimeOnly(9, 0), new TimeOnly(17, 0), null));
        TripDuration.UndergroundMinutes(new DateOnly(2026, 10, 25), null, held.Entry, held.Exit).ShouldBe(480);
    }

    /// <summary>
    /// The rule itself, swept across the weekend the clocks go back, for trips of one, two and
    /// three days and stays from minutes to days: whenever a pair is offered, the roster's own
    /// arithmetic over that pair and that trip's dates comes to the minutes between the two
    /// moments. A pair is offered for some of them and refused for some, or the sweep says nothing.
    /// </summary>
    [Fact]
    public void Whatever_pair_is_offered_the_roster_counts_the_minutes_that_passed()
    {
        var zone = Bucharest;
        var first = new DateOnly(2026, 10, 24);
        var start = Utc(10, 23, 18, 0);
        int offered = 0, refused = 0;

        foreach (var end in new DateOnly?[] { null, first, first.AddDays(1), first.AddDays(2) })
        {
            for (var entryStep = 0; entryStep < 40; entryStep++)
            {
                var entered = start.AddMinutes(entryStep * 97);
                for (var stayStep = 0; stayStep < 40; stayStep++)
                {
                    var watch = Pair(entered, entered.AddMinutes(stayStep * 83).AddSeconds(stayStep));
                    var times = TrackingAttendance.RosterTimesOf(watch, first, end, zone);
                    if (times.Problem is not null)
                    {
                        refused++;
                        continue;
                    }

                    offered++;
                    TripDuration.UndergroundMinutes(first, end, times.Entry, times.Exit).ShouldBe(
                        Minutes(watch),
                        $"in {watch.Entered:u}, out {watch.Exited:u}, trip {first:O} to {end:O}");
                }
            }
        }

        offered.ShouldBeGreaterThan(100);
        refused.ShouldBeGreaterThan(100);
    }
}
