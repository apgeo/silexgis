// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Events;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The generator that turns a repetition into the days it falls on, and the ceilings that stop it
/// turning one form submission into a table full of rows.
/// </summary>
/// <remarks>
/// Each refusal is asserted beside the request that is one step inside the limit it breaks, so a
/// generator that refused everything — or that had quietly stopped generating at all — would fail
/// these tests rather than pass them.
/// </remarks>
public class EventRecurrenceTests
{
    private static readonly DateOnly Tuesday = new(2054, 3, 3);

    [Fact]
    public void A_weekly_series_falls_on_the_days_its_count_asked_for_and_no_others()
    {
        var plan = EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, count: 4, lastDay: null);

        plan.Refused.ShouldBeFalse(plan.RefusalDetail);
        plan.Days.ShouldBe(
        [
            new DateOnly(2054, 3, 3),
            new DateOnly(2054, 3, 10),
            new DateOnly(2054, 3, 17),
            new DateOnly(2054, 3, 24),
        ]);
    }

    [Fact]
    public void The_other_repetitions_step_by_what_they_are_called()
    {
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Daily, 3, null)
            .Days.ShouldBe([new(2054, 3, 3), new(2054, 3, 4), new(2054, 3, 5)]);

        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Fortnightly, 3, null)
            .Days.ShouldBe([new(2054, 3, 3), new(2054, 3, 17), new(2054, 3, 31)]);

        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Monthly, 3, null)
            .Days.ShouldBe([new(2054, 3, 3), new(2054, 4, 3), new(2054, 5, 3)]);
    }

    [Fact]
    public void A_monthly_series_keeps_the_day_of_the_month_it_started_on()
    {
        // Measured from the first occurrence every time rather than from the one before it. A
        // series stepping from its predecessor would meet February, lose the 31st for good, and
        // spend the rest of the year on the 28th — a quiet wrong answer nobody would go looking
        // for until the committee turned up on the wrong evening.
        var plan = EventRecurrence.Plan(
            new DateOnly(2054, 1, 31), EventRecurrenceFrequency.Monthly, count: 4, lastDay: null);

        plan.Days.ShouldBe(
        [
            new DateOnly(2054, 1, 31),
            new DateOnly(2054, 2, 28),
            new DateOnly(2054, 3, 31),
            new DateOnly(2054, 4, 30),
        ]);
    }

    [Fact]
    public void A_last_day_bounds_the_series_and_an_occurrence_landing_on_it_is_kept()
    {
        // The 24th is the fourth Tuesday. Naming it as the last day keeps it, because somebody
        // writing down a last day means "up to and including" — and the 31st, one step past it,
        // is not written.
        var upToTheDay = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Weekly, count: null, lastDay: new DateOnly(2054, 3, 24));

        upToTheDay.Refused.ShouldBeFalse(upToTheDay.RefusalDetail);
        upToTheDay.Days.Count.ShouldBe(4);
        upToTheDay.Days[^1].ShouldBe(new DateOnly(2054, 3, 24));

        // A day between two occurrences stops at the one before it rather than rounding up to the
        // one after.
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, null, new DateOnly(2054, 3, 23))
            .Days.Count.ShouldBe(3);
    }

    [Fact]
    public void A_repetition_that_says_nowhere_to_stop_is_refused()
    {
        var unbounded = EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, count: null, lastDay: null);

        unbounded.Refused.ShouldBeTrue();
        unbounded.RefusalCode.ShouldBe(EventRecurrence.UnboundedCode);
        unbounded.Days.ShouldBeEmpty();

        // And the same request with either bound supplied is written, so what was refused above
        // is the absence of a bound and not the repetition itself.
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, 2, null).Refused.ShouldBeFalse();
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, null, Tuesday.AddDays(7))
            .Refused.ShouldBeFalse();
    }

    [Fact]
    public void A_repetition_that_happens_once_is_not_a_series()
    {
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, count: 1, lastDay: null)
            .RefusalCode.ShouldBe(EventRecurrence.NotRepeatingCode);

        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, count: 0, lastDay: null)
            .RefusalCode.ShouldBe(EventRecurrence.NotRepeatingCode);

        // A last day no later than the first occurrence describes the same nothing.
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, null, Tuesday)
            .RefusalCode.ShouldBe(EventRecurrence.NotRepeatingCode);

        // Two is a series, and it is written.
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, 2, null).Days.Count.ShouldBe(2);
    }

    [Fact]
    public void A_count_past_the_ceiling_is_refused_and_the_ceiling_itself_is_written()
    {
        var over = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Weekly, EventRecurrence.MaxOccurrences + 1, null);

        over.Refused.ShouldBeTrue();
        over.RefusalCode.ShouldBe(EventRecurrence.TooManyCode);
        over.Days.ShouldBeEmpty();

        // The refusal says how many were asked for and how many are allowed, because a limit
        // somebody cannot see is a limit they hit again on the next attempt.
        over.RefusalDetail.ShouldNotBeNull();
        over.RefusalDetail.ShouldContain($"{EventRecurrence.MaxOccurrences + 1}");
        over.RefusalDetail.ShouldContain($"{EventRecurrence.MaxOccurrences}");

        var atTheLimit = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Weekly, EventRecurrence.MaxOccurrences, null);
        atTheLimit.Refused.ShouldBeFalse(atTheLimit.RefusalDetail);
        atTheLimit.Days.Count.ShouldBe(EventRecurrence.MaxOccurrences);
    }

    [Fact]
    public void The_count_ceiling_applies_to_a_series_bounded_only_by_a_last_day()
    {
        // Both ceilings hold whichever of the two bounds was named. A year away is well inside
        // the horizon, and a daily repetition still describes a year of rows — so it is the count
        // ceiling that catches this one, and it catches it rather than writing the first hundred
        // and quietly dropping the rest.
        var daily = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Daily, count: null, lastDay: Tuesday.AddDays(364));

        daily.Refused.ShouldBeTrue();
        daily.RefusalCode.ShouldBe(EventRecurrence.TooManyCode);
        daily.Days.ShouldBeEmpty();

        // The same last day with a weekly repetition is 53 occurrences and is written, so what
        // was refused is the number of rows and not the distance.
        var weekly = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Weekly, null, Tuesday.AddDays(364));
        weekly.Refused.ShouldBeFalse(weekly.RefusalDetail);
        weekly.Days.Count.ShouldBe(53);
    }

    [Fact]
    public void A_last_day_past_the_horizon_is_refused_and_the_horizon_itself_is_written()
    {
        var beyond = EventRecurrence.Plan(
            Tuesday,
            EventRecurrenceFrequency.Monthly,
            count: null,
            lastDay: Tuesday.AddDays(EventRecurrence.MaxHorizonDays + 1));

        beyond.Refused.ShouldBeTrue();
        beyond.RefusalCode.ShouldBe(EventRecurrence.HorizonTooFarCode);

        var atTheHorizon = EventRecurrence.Plan(
            Tuesday,
            EventRecurrenceFrequency.Monthly,
            null,
            Tuesday.AddDays(EventRecurrence.MaxHorizonDays));
        atTheHorizon.Refused.ShouldBeFalse(atTheHorizon.RefusalDetail);
        atTheHorizon.Days.Count.ShouldBe(24);
    }

    [Fact]
    public void The_horizon_ceiling_applies_to_a_series_bounded_only_by_a_count()
    {
        // The mirror of the count ceiling over a last day, and the one the form actually reaches:
        // a number of occurrences names no date at all, so nothing about the request looks far
        // away — and a hundred monthly committee meetings still run more than eight years out.
        var monthly = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Monthly, EventRecurrence.MaxOccurrences, lastDay: null);

        monthly.Refused.ShouldBeTrue();
        monthly.RefusalCode.ShouldBe(EventRecurrence.HorizonTooFarCode);
        monthly.Days.ShouldBeEmpty();

        // Twenty-four monthly occurrences is the longest run that stays inside the horizon, and
        // it is written — so what was refused above is the distance and not the repetition.
        var twoYears = EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Monthly, 24, null);
        twoYears.Refused.ShouldBeFalse(twoYears.RefusalDetail);
        twoYears.Days.Count.ShouldBe(24);
    }

    [Fact]
    public void A_far_off_last_day_is_allowed_when_the_count_stops_the_run_short_of_it()
    {
        // Both bounds given, the date used as a safety stop rather than as the end. Twelve weekly
        // occurrences span eleven weeks and break no ceiling, so the request is what it asks for
        // — measuring the horizon against the date instead of against the days would refuse a
        // series that is nowhere near it.
        var plan = EventRecurrence.Plan(
            Tuesday,
            EventRecurrenceFrequency.Weekly,
            count: 12,
            lastDay: Tuesday.AddDays(EventRecurrence.MaxHorizonDays * 3));

        plan.Refused.ShouldBeFalse(plan.RefusalDetail);
        plan.Days.Count.ShouldBe(12);
        plan.Days[^1].ShouldBe(Tuesday.AddDays(77));
    }

    [Fact]
    public void A_last_day_leaving_one_occurrence_is_not_a_series()
    {
        // The last day is after the first occurrence, so nothing about the bounds is wrong — and
        // it falls before the second, so what was asked for happens once. A single row carrying a
        // grouping key would offer every surface the acts that reach a run and give them nothing
        // to reach.
        var justOne = EventRecurrence.Plan(
            Tuesday, EventRecurrenceFrequency.Weekly, count: null, lastDay: Tuesday.AddDays(3));

        justOne.Refused.ShouldBeTrue();
        justOne.RefusalCode.ShouldBe(EventRecurrence.NotRepeatingCode);
        justOne.Days.ShouldBeEmpty();

        // And with a count as well, so a series is never quietly trimmed down to one row.
        EventRecurrence.Plan(Tuesday, EventRecurrenceFrequency.Weekly, 5, Tuesday.AddDays(3))
            .RefusalCode.ShouldBe(EventRecurrence.NotRepeatingCode);
    }

    [Fact]
    public void A_first_day_at_the_end_of_the_calendar_is_refused_rather_than_thrown_at()
    {
        // Nothing bounds the first day a request may name, so a date at the very end of the
        // calendar arrives here and the arithmetic that works out the second occurrence would run
        // off it. That is a distance refusal like any other, not a failure.
        var weekly = EventRecurrence.Plan(
            DateOnly.MaxValue.AddDays(-3), EventRecurrenceFrequency.Weekly, count: 4, lastDay: null);
        weekly.Refused.ShouldBeTrue();
        weekly.RefusalCode.ShouldBe(EventRecurrence.HorizonTooFarCode);

        var monthly = EventRecurrence.Plan(
            new DateOnly(9999, 12, 1), EventRecurrenceFrequency.Monthly, count: 2, lastDay: null);
        monthly.Refused.ShouldBeTrue();
        monthly.RefusalCode.ShouldBe(EventRecurrence.HorizonTooFarCode);

        // The horizon the ceiling is measured from is past the end of the calendar here too, and
        // working it out must not overflow either: this run is short and is written.
        var inRange = EventRecurrence.Plan(
            new DateOnly(9999, 6, 1),
            EventRecurrenceFrequency.Weekly,
            count: null,
            lastDay: new DateOnly(9999, 12, 31));
        inRange.Refused.ShouldBeFalse(inRange.RefusalDetail);
        inRange.Days.Count.ShouldBe(31);
    }

    [Fact]
    public void A_repetition_this_calendar_does_not_know_is_refused_rather_than_thrown_at()
    {
        // The value arrives over the wire, so a fifth number is somebody's request and not a
        // programming error. It is answered with a refusal a surface can show, and the day a
        // fifth repetition is added to the vocabulary without a spacing this is what stops it
        // silently becoming one of the four.
        var unknown = EventRecurrence.Plan(Tuesday, (EventRecurrenceFrequency)99, count: 4, lastDay: null);

        unknown.Refused.ShouldBeTrue();
        unknown.RefusalCode.ShouldBe(EventRecurrence.FrequencyInvalidCode);
        unknown.Days.ShouldBeEmpty();
    }
}
