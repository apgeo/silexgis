// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a span of whole days is stored. The rule readers depend on is that a stored end date
/// always means "and it ran on to" — so one day never reads as a range of itself.
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
}
