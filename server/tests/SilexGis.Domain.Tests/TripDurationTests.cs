// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How long somebody was underground. The cases mirror the ones the browser applies to a trip's
/// own times, deliberately and to the minute: the figure on a trip's page and the figure in a
/// total over that trip are read side by side, and a rule expressed twice that disagrees by one
/// midnight is a defect the moment anybody compares them.
/// </summary>
public class TripDurationTests
{
    [Fact]
    public void A_day_trip_is_the_difference_between_its_two_times()
    {
        TripDuration.UndergroundMinutes(new DateOnly(2026, 5, 14), null, new TimeOnly(9, 0), new TimeOnly(17, 30))
            .ShouldBe(510);
    }

    /// <summary>
    /// One day, and out before one went in: the only reading that makes sense is the next morning,
    /// which is an ordinary night trip rather than a mistake to refuse.
    /// </summary>
    [Fact]
    public void A_single_day_trip_that_ends_before_it_starts_crossed_one_midnight()
    {
        TripDuration.UndergroundMinutes(new DateOnly(2026, 5, 14), null, new TimeOnly(22, 0), new TimeOnly(6, 0))
            .ShouldBe(480);
        // The same trip written with an end date equal to its start is the same trip.
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 5, 14), new DateOnly(2026, 5, 14), new TimeOnly(22, 0), new TimeOnly(6, 0))
            .ShouldBe(480);
    }

    /// <summary>
    /// The case a rule written when a trip lasted one day gets wrong. A push that goes in on the
    /// 14th and comes out on the 15th is thirty hours, and it is exactly the trip somebody wants
    /// counted; guessing a midnight on top of a range that already says which day it ended would
    /// halve it, and a range of several days would be lost altogether.
    /// </summary>
    [Theory]
    [InlineData(2026, 5, 15, 9, 0, 15, 0, 1800)]   // two calendar days, 09:00 to 15:00
    [InlineData(2026, 5, 15, 22, 0, 6, 0, 480)]    // two days, and the clock still turns over once
    [InlineData(2026, 5, 17, 8, 0, 8, 0, 4320)]    // four days
    public void A_trip_that_says_which_day_it_ended_is_as_long_as_the_range(
        int endYear, int endMonth, int endDay, int entryHour, int entryMinute, int exitHour, int exitMinute, int expected)
    {
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 5, 14),
                new DateOnly(endYear, endMonth, endDay),
                new TimeOnly(entryHour, entryMinute),
                new TimeOnly(exitHour, exitMinute))
            .ShouldBe(expected);
    }

    /// <summary>
    /// A calendar day is a day even when the clock changes under it, so the days are counted as
    /// days and never as elapsed hours.
    /// </summary>
    [Fact]
    public void A_range_across_a_clock_change_is_still_whole_days()
    {
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30), new TimeOnly(10, 0), new TimeOnly(10, 0))
            .ShouldBe(2880);
    }

    /// <summary>
    /// Nothing rather than zero. A zero would claim the trip took no time, and a sum that absorbed
    /// it would report a plausible total made partly of trips nobody timed.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(9, null)]
    [InlineData(null, 17)]
    public void A_missing_time_says_nothing_at_all(int? entryHour, int? exitHour)
    {
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 5, 14),
                null,
                entryHour is { } e ? new TimeOnly(e, 0) : null,
                exitHour is { } x ? new TimeOnly(x, 0) : null)
            .ShouldBeNull();
    }

    /// <summary>
    /// Nothing at the database level forbids an end before the start, so a row that has one — an
    /// import, or one written before the rule existed — has to say nothing rather than produce a
    /// negative that a sum would take off somebody else's hours.
    /// </summary>
    [Fact]
    public void A_range_that_ends_before_it_begins_says_nothing()
    {
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 5, 14), new DateOnly(2026, 5, 12), new TimeOnly(9, 0), new TimeOnly(17, 0))
            .ShouldBeNull();
    }

    /// <summary>
    /// Times are entered and shown to the minute. A row carrying seconds must not make one surface
    /// disagree with another by the part of a minute nobody typed.
    /// </summary>
    [Fact]
    public void Seconds_are_dropped_rather_than_rounded()
    {
        TripDuration.UndergroundMinutes(
                new DateOnly(2026, 5, 14), null, new TimeOnly(9, 0, 59), new TimeOnly(9, 30, 1))
            .ShouldBe(30);
    }
}
