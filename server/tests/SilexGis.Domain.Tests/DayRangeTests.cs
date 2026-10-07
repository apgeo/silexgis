// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a span of whole days is stored. For a span written down as finished the rule readers
/// depend on is that a stored end date always means "and it ran on to" — so one day never reads
/// as a range of itself. For a span that can be recorded while it is still going on, no end means
/// exactly that, so one day has to keep its day.
/// </summary>
public class DayRangeTests
{
    [Fact]
    public void A_span_ending_the_day_it_starts_stores_no_end()
    {
        var day = new DateOnly(2026, 7, 14);

        DayRange.EndForStorage(day, day).ShouldBeNull();
    }

    [Fact]
    public void A_span_that_ran_on_keeps_the_day_it_ran_on_to()
    {
        var start = new DateOnly(2026, 7, 14);

        DayRange.EndForStorage(start, new DateOnly(2026, 7, 28)).ShouldBe(new DateOnly(2026, 7, 28));
        DayRange.EndForStorage(start, new DateOnly(2026, 7, 15)).ShouldBe(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void Nothing_supplied_stays_nothing() =>
        DayRange.EndForStorage(new DateOnly(2026, 7, 14), null).ShouldBeNull();

    [Fact]
    public void An_end_before_its_start_is_handed_back_untouched()
    {
        // Quietly turning a mistyped date into "one day" would hide it. Refusing it belongs to
        // the validation the write goes through, with a database constraint behind that, so the
        // value has to survive this far to be refused there.
        var wrong = new DateOnly(2026, 7, 1);

        DayRange.EndForStorage(new DateOnly(2026, 7, 14), wrong).ShouldBe(wrong);
    }

    [Fact]
    public void A_span_that_may_still_be_running_keeps_a_last_day_equal_to_its_first()
    {
        // The whole difference between the two rules. Folded to nothing, a one-day stay would be
        // stored as the very thing that says "has not left".
        var day = new DateOnly(2026, 7, 14);

        DayRange.OpenEndForStorage(day, day).ShouldBe(day);
    }

    [Fact]
    public void A_span_that_may_still_be_running_stores_no_end_only_when_none_was_given()
    {
        var start = new DateOnly(2026, 7, 14);

        DayRange.OpenEndForStorage(start, null).ShouldBeNull();
        DayRange.OpenEndForStorage(start, new DateOnly(2026, 7, 28)).ShouldBe(new DateOnly(2026, 7, 28));
    }

    [Fact]
    public void A_span_that_may_still_be_running_also_hands_a_wrong_end_back_untouched()
    {
        var wrong = new DateOnly(2026, 7, 1);

        DayRange.OpenEndForStorage(new DateOnly(2026, 7, 14), wrong).ShouldBe(wrong);
    }

    [Fact]
    public void Only_an_end_before_its_start_is_a_mistyped_one()
    {
        var start = new DateOnly(2026, 7, 14);

        DayRange.EndsBeforeItStarts(start, new DateOnly(2026, 7, 13)).ShouldBeTrue();

        // The same day is one day under either rule, and no end is never a mistake.
        DayRange.EndsBeforeItStarts(start, start).ShouldBeFalse();
        DayRange.EndsBeforeItStarts(start, new DateOnly(2026, 7, 15)).ShouldBeFalse();
        DayRange.EndsBeforeItStarts(start, null).ShouldBeFalse();
    }
}
