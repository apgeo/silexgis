// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.Calendar;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the merged answer keeps when the backstop cap bites, and what the shortfall it reports
/// counts. Exercised here at a size a test can reach: the shipped cap is a backstop no ordinary
/// window comes near, so a case driven through the endpoint could only ever prove the zero.
/// </summary>
public class CalendarWindowTests
{
    [Fact]
    public void The_cap_says_how_many_rows_it_could_not_carry()
    {
        var rows = Enumerable.Range(1, 5).Select(day => Row(day, $"Row {day}")).ToList();

        var capped = CalendarWindow.Merge(rows, found: 5, maxRows: 2, sort: null);

        // The number is the shortfall of this answer, so a reader is told how much of what they
        // may see is missing. A truncation nobody is told about reads exactly like a complete
        // answer, which is the one thing a record of a month must not do.
        capped.Entries.Count.ShouldBe(2);
        capped.Omitted.ShouldBe(3);

        // The window's chronological head, whatever else was asked for: the same window then
        // answers with the same rows every time it is opened.
        capped.Entries.Select(x => x.Start.Day).ShouldBe([1, 2]);

        var whole = CalendarWindow.Merge(rows, found: 5, maxRows: 10, sort: null);
        whole.Entries.Count.ShouldBe(5);

        // Zero is a real count and not a shrug — nothing was held back, and the answer says so
        // rather than leaving the field out.
        whole.Omitted.ShouldBe(0);
    }

    [Fact]
    public void The_cap_keeps_the_chronological_head_whatever_order_was_asked_for()
    {
        var rows = Enumerable.Range(1, 5).Select(day => Row(day, $"Row {6 - day}")).ToList();

        var capped = CalendarWindow.Merge(rows, found: 5, maxRows: 2, sort: "title");

        // Sorted as asked, but drawn from the two earliest days rather than the two rows that
        // happen to sort first: which rows come back is settled before how they are arranged, so
        // a change of order cannot change the set.
        capped.Entries.Select(x => x.Start.Day).ShouldBe([2, 1]);
        capped.Omitted.ShouldBe(3);
    }

    [Fact]
    public void An_order_this_code_does_not_know_falls_back_to_the_calendars_own()
    {
        var rows = new[] { Row(3, "Alpha"), Row(1, "Zulu") };

        CalendarWindow.Merge(rows, found: 2, maxRows: 10, sort: "; drop table")
            .Entries.Select(x => x.Title).ShouldBe(["Zulu", "Alpha"]);

        CalendarWindow.Merge(rows, found: 2, maxRows: 10, sort: "title")
            .Entries.Select(x => x.Title).ShouldBe(["Alpha", "Zulu"]);
    }

    /// <summary>
    /// The shortfall is a count of rows and so cannot be less than none. It is computed from a
    /// count and a page that are read from one snapshot precisely so the pair agrees; this pins
    /// what the answer says if a caller ever hands it a pair that does not, because a negative
    /// shortfall is a number no client can read and would be drawn as a warning about rows that
    /// were never missing.
    /// </summary>
    [Fact]
    public void A_shortfall_is_never_less_than_none()
    {
        var rows = new[] { Row(1, "Alpha"), Row(2, "Bravo"), Row(3, "Charlie") };

        CalendarWindow.Merge(rows, found: 1, maxRows: 10, sort: null).Omitted.ShouldBe(0);
    }

    private static CalendarEntryDto Row(int day, string title) => new(
        CalendarSource.TripLog,
        Guid.CreateVersion7(),
        title,
        new DateOnly(2059, 1, day),
        null,
        null,
        null,
        null,
        ActivityState.Planned,
        CalendarPlacement.Ahead,
        null,
        false);
}
