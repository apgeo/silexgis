// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.Calendar;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Tests;

/// <summary>
/// The one rule the calendar's two set-valued narrowings are read by — the families of record and
/// the kinds of club date — exercised directly, an arm at a time. Three of its answers differ only
/// in what a caller did <em>not</em> write, and those are the ones that are easy to merge by
/// accident: an absent parameter narrows nothing, a list of real names narrows to them, and a
/// list that is present but names nothing is a mistake rather than a request for everything.
/// </summary>
public class CalendarNarrowingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_or_blank_list_is_the_absence_of_a_narrowing(string? written)
    {
        CalendarNarrowing.TryParseSet<EventKind>(written, out var chosen, out var unknown).ShouldBeTrue();

        // Null, and not an empty set: "narrow to nothing" and "do not narrow" are opposite
        // answers, and only one of them is what an absent parameter means.
        chosen.ShouldBeNull();
        unknown.ShouldBeNull();
    }

    [Fact]
    public void Each_word_is_read_by_name_in_either_spelling_and_any_case()
    {
        CalendarNarrowing.TryParseSet<EventKind>(
            "training, ClubMeeting,GEARCHECK", out var chosen, out var unknown).ShouldBeTrue();

        chosen.ShouldNotBeNull();
        chosen.ShouldBe([EventKind.Training, EventKind.ClubMeeting, EventKind.GearCheck], ignoreOrder: true);
        unknown.ShouldBeNull();
    }

    [Fact]
    public void A_member_named_twice_is_named_once_and_a_trailing_comma_is_not_a_mistake()
    {
        CalendarNarrowing.TryParseSet<EventKind>(
            "deadline,,deadline,", out var chosen, out _).ShouldBeTrue();

        chosen.ShouldNotBeNull();
        chosen.ShouldBe([EventKind.Deadline]);
    }

    /// <summary>
    /// One bad word refuses the whole list and is named, so the refusal can say which word. The
    /// rest of the list is not answered for: a narrowing with a word quietly dropped out of it is
    /// a wider answer than the one asked for.
    /// </summary>
    [Theory]
    [InlineData("banana", "banana")]
    [InlineData("training,banana,deadline", "banana")]
    // The underlying number of a real member, and a signed one: neither is a name.
    [InlineData("1", "1")]
    [InlineData("training,+2", "+2")]
    public void A_word_that_names_no_member_refuses_the_list_and_is_named(string written, string expected)
    {
        CalendarNarrowing.TryParseSet<EventKind>(written, out var chosen, out var unknown).ShouldBeFalse();

        chosen.ShouldBeNull();
        unknown.ShouldBe(expected);
    }

    /// <summary>
    /// Present, and naming nothing: refused, with no word to name. Answering everything here
    /// would be answering a question the caller did not write.
    /// </summary>
    [Theory]
    [InlineData(",")]
    [InlineData(" , ,")]
    public void A_list_that_names_nothing_is_refused_with_no_word_to_name(string written)
    {
        CalendarNarrowing.TryParseSet<EventKind>(written, out var chosen, out var unknown).ShouldBeFalse();

        chosen.ShouldBeNull();
        unknown.ShouldBeNull();
    }

    /// <summary>The same rule reads the families, so the two parameters cannot drift apart.</summary>
    [Fact]
    public void The_families_of_record_are_read_by_the_same_rule()
    {
        CalendarNarrowing.TryParseSet<CalendarSource>(
            "expedition,event", out var chosen, out _).ShouldBeTrue();
        chosen.ShouldNotBeNull();
        chosen.ShouldBe([CalendarSource.Expedition, CalendarSource.Event], ignoreOrder: true);

        CalendarNarrowing.TryParseSet<CalendarSource>("meetings", out _, out var unknown).ShouldBeFalse();
        unknown.ShouldBe("meetings");
    }
}
