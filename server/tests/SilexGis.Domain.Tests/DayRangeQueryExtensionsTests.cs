// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Whether a span of whole days overlaps a window of them. Every edge is asserted here because
/// the failures this rule has are all silent: a bound read exclusively drops the rows that only
/// touch it, and an absent end read as no end at all drops every single-day row out of every
/// window without anything saying so.
/// </summary>
/// <remarks>
/// These run the predicate in memory, which pins what it means. That it survives translation to
/// SQL against the columns of a real table is pinned separately, by the listings that use it.
/// </remarks>
public class DayRangeQueryExtensionsTests
{
    private sealed record Span(string Name, DateOnly Start, DateOnly? End);

    private static readonly Span Fortnight =
        new("fortnight", new DateOnly(2032, 3, 10), new DateOnly(2032, 3, 20));

    private static readonly Span OneDay = new("one day", new DateOnly(2032, 3, 15), null);

    private static readonly Span Ended = new("ended on its start", new DateOnly(2032, 3, 15), new DateOnly(2032, 3, 15));

    private static List<string> Overlapping(string from, string to) =>
        new[] { Fortnight, OneDay, Ended }
            .AsQueryable()
            .OverlappingDays(x => x.Start, x => x.End, Day(from), Day(to))
            .Select(x => x.Name)
            .ToList();

    private static DateOnly? Day(string value) =>
        value.Length == 0 ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_window_meeting_only_the_first_day_holds_the_span() =>
        Overlapping("2032-03-01", "2032-03-10").ShouldBe(["fortnight"]);

    [Fact]
    public void A_window_meeting_only_the_last_day_holds_the_span() =>
        Overlapping("2032-03-20", "2032-03-31").ShouldBe(["fortnight"]);

    [Fact]
    public void A_window_wholly_inside_the_span_holds_it() =>
        Overlapping("2032-03-13", "2032-03-14").ShouldBe(["fortnight"]);

    /// <summary>
    /// A span with no end date ran for one day, so its end is its start. A span whose stored end
    /// equals its start is the same span written the long way — storage normalises that to no end
    /// on one of the two tables this rule serves and not on the other, so both must read alike.
    /// </summary>
    [Fact]
    public void A_span_of_one_day_is_held_by_the_window_over_that_day()
    {
        Overlapping("2032-03-15", "2032-03-15").ShouldBe(["fortnight", "one day", "ended on its start"]);
        Overlapping("2032-03-16", "2032-03-16").ShouldBe(["fortnight"]);
        Overlapping("2032-03-14", "2032-03-14").ShouldBe(["fortnight"]);
    }

    [Fact]
    public void One_day_past_either_end_holds_nothing()
    {
        Overlapping("2032-03-21", "2032-03-31").ShouldBeEmpty();
        Overlapping("2032-01-01", "2032-03-09").ShouldBeEmpty();
    }

    /// <summary>
    /// An absent bound is unbounded on that side rather than a bound of nothing, and a window
    /// with neither narrows nothing at all.
    /// </summary>
    [Fact]
    public void An_absent_bound_narrows_nothing_on_that_side()
    {
        Overlapping("", "2032-03-14").ShouldBe(["fortnight"]);
        Overlapping("2032-03-16", "").ShouldBe(["fortnight"]);
        Overlapping("", "").Count.ShouldBe(3);
    }

    /// <summary>
    /// The two bounds are independent clauses rather than a range checked for sense, so a window
    /// whose end precedes its start holds the spans covering both of its days and no others.
    /// Refusing such a window belongs to the validation a request goes through, and none of the
    /// listings does it — this pins what they answer meanwhile.
    /// </summary>
    [Fact]
    public void A_window_that_runs_backwards_holds_what_covers_both_of_its_days()
    {
        Overlapping("2032-03-20", "2032-03-10").ShouldBe(["fortnight"]);
        Overlapping("2032-03-31", "2032-03-01").ShouldBeEmpty();
    }
}
