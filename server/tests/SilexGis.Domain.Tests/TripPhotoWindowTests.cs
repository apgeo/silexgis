// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The stretch of time a photo library is asked about for one trip.
///
/// <para>
/// Every failure this guards against looks like the feature working. A window an hour short at one
/// edge draws a panel with a number under it and nothing saying the number is small; a window that
/// ran to now draws the whole library under a heading naming one weekend; a window built from a pair
/// of dates read in the wrong order draws nothing and reads as a club that took no pictures. None of
/// them raises anything, so they are pinned here rather than left to a screen.
/// </para>
/// <para>
/// Every date below is invented and belongs to no trip.
/// </para>
/// </summary>
public class TripPhotoWindowTests
{
    private static readonly DateOnly Saturday = new(2026, 3, 14);
    private static readonly DateOnly Sunday = new(2026, 3, 15);

    /// <summary>
    /// A trip on one day is a whole day, plus a day of margin at each end.
    /// </summary>
    /// <remarks>
    /// The margin is the part worth pinning. A trip's dates carry no zone and a camera's timestamps
    /// are instants, so the two frames can be displaced by up to fourteen hours — and a window built
    /// on the trip's own midnights would drop the end of a long push, report a smaller number and
    /// say nothing about having done so.
    /// </remarks>
    [Fact]
    public void A_trip_on_one_day_is_asked_about_from_the_day_before_to_the_day_after()
    {
        var window = TripPhotoWindow.For(Saturday, tripDateEnd: null);

        window.ShouldNotBeNull();
        window.Value.From.ShouldBe(new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero));
        window.Value.To.ShouldBe(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Both ends are stated in UTC, whatever the machine working them out is set to.
    /// </summary>
    /// <remarks>
    /// A window carrying the running machine's own offset is a window that means one stretch of time
    /// on a developer's box and another on a server, and both products compare against a moment they
    /// hold in UTC. The symptom would be a panel quietly off by hours, on one deployment only.
    /// </remarks>
    [Fact]
    public void Both_ends_are_stated_in_UTC()
    {
        var window = TripPhotoWindow.For(Saturday, Sunday);

        window.ShouldNotBeNull();
        window.Value.From.Offset.ShouldBe(TimeSpan.Zero);
        window.Value.To.Offset.ShouldBe(TimeSpan.Zero);
    }

    /// <summary>
    /// A trip that ran on to a second day covers both of them, and the margin does not grow with the
    /// trip.
    /// </summary>
    [Fact]
    public void A_trip_that_ran_on_covers_every_day_it_names()
    {
        var window = TripPhotoWindow.For(Saturday, Sunday);

        window.ShouldNotBeNull();
        window.Value.From.ShouldBe(new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero));
        window.Value.To.ShouldBe(new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// An end equal to the start is one day, not two, and reads exactly as a trip that named no end.
    /// </summary>
    /// <remarks>
    /// Both spellings of "one day" reach the store — the end is written as nothing when it equals the
    /// start, but a row imported from elsewhere can carry the repeat — and a panel that showed a day
    /// more for one of them would disagree with itself about the same trip.
    /// </remarks>
    [Fact]
    public void An_end_on_the_starting_day_is_the_same_window_as_no_end_at_all()
    {
        TripPhotoWindow.For(Saturday, Saturday).ShouldBe(TripPhotoWindow.For(Saturday, null));
    }

    /// <summary>
    /// The upper end is the start of the day after the last one the trip covers, so the whole of that
    /// last day is inside the window.
    /// </summary>
    /// <remarks>
    /// Stated as its own case because it is the boundary a product's own convention could otherwise
    /// decide: one that reads its upper bound as an instant is given exactly this, and one that
    /// rounds up to the end of a day is given one more. Neither loses the evening of the last day,
    /// which is when a trip's photographs are most likely to have been taken.
    /// </remarks>
    [Fact]
    public void The_last_day_of_the_trip_is_wholly_inside_the_window()
    {
        var window = TripPhotoWindow.For(Saturday, Sunday);

        window.ShouldNotBeNull();

        // The last minute of the day after the trip's own last day, which the margin covers.
        var lateOnTheDayAfter = new DateTimeOffset(2026, 3, 16, 23, 59, 0, TimeSpan.Zero);
        window.Value.To.ShouldBeGreaterThan(lateOnTheDayAfter);
    }

    /// <summary>
    /// A trip that names no end is closed on its own start day rather than left running to now.
    /// </summary>
    /// <remarks>
    /// The case this rules out is the one that would look most like a working feature: a plan or an
    /// unfinished write has a start and no end, and an open window would draw every photograph taken
    /// since, paged and counted like any other answer, under a heading saying they were taken on that
    /// trip.
    /// </remarks>
    [Fact]
    public void A_trip_with_no_end_is_not_an_open_window()
    {
        var window = TripPhotoWindow.For(Saturday, tripDateEnd: null);

        window.ShouldNotBeNull();

        // Days, not years: whatever the margin is, the answer is a bounded stretch around the trip.
        (window.Value.To - window.Value.From).ShouldBeLessThan(TimeSpan.FromDays(7));
    }

    /// <summary>
    /// An end before the start gives no window at all, rather than one of the two readings of it.
    /// </summary>
    [Fact]
    public void An_end_before_the_start_is_refused()
    {
        TripPhotoWindow.For(Sunday, Saturday).ShouldBeNull();
    }

    /// <summary>
    /// A trip with no start day recorded gives no window.
    /// </summary>
    /// <remarks>
    /// A day nobody filled in arrives as the calendar's own first day rather than as nothing, and a
    /// window around the year one is a question worth not putting to somebody else's server. It is
    /// also the one date the margin cannot be subtracted from.
    /// </remarks>
    [Fact]
    public void A_trip_whose_start_day_was_never_filled_in_is_refused()
    {
        TripPhotoWindow.For(default, tripDateEnd: null).ShouldBeNull();
    }

    /// <summary>
    /// A span longer than the cap is refused rather than trimmed.
    /// </summary>
    /// <remarks>
    /// The case is a year typed wrongly into the end date. Nothing refuses that record, it looks
    /// ordinary in a list, and the window it would otherwise produce asks a club's library for a
    /// decade — which pages and counts correctly and would be presented as the photographs of one
    /// weekend. Trimming it to something plausible would answer a question nobody asked and leave the
    /// wrong date in place.
    /// </remarks>
    [Fact]
    public void A_span_longer_than_a_year_is_refused()
    {
        TripPhotoWindow.For(Saturday, Saturday.AddDays(TripPhotoWindow.MaxSpanDays)).ShouldBeNull();
    }

    /// <summary>
    /// The longest span still allowed is answered, so the cap refuses only what is past it.
    /// </summary>
    /// <remarks>
    /// Asserted beside the refusal because an off-by-one in the other direction turns the guard into
    /// a rule that quietly refuses long expeditions, and a refused panel on a real trip looks like a
    /// library that is down.
    /// </remarks>
    [Fact]
    public void The_longest_allowed_span_is_still_answered()
    {
        TripPhotoWindow.For(Saturday, Saturday.AddDays(TripPhotoWindow.MaxSpanDays - 1))
            .ShouldNotBeNull();
    }

    /// <summary>
    /// The margin never runs off either end of the calendar.
    /// </summary>
    /// <remarks>
    /// Not a real trip, and that is the point: the arithmetic has to be total, because a date this
    /// far out arrives from an import or a mistyped field rather than from a person, and an
    /// exception thrown here would come back as the library having failed.
    /// </remarks>
    [Fact]
    public void A_date_at_the_edge_of_the_calendar_is_refused_rather_than_throwing()
    {
        TripPhotoWindow.For(DateOnly.MinValue, tripDateEnd: null).ShouldBeNull();
        TripPhotoWindow.For(DateOnly.MaxValue, tripDateEnd: null).ShouldBeNull();
        TripPhotoWindow.For(DateOnly.MaxValue.AddDays(-1), DateOnly.MaxValue).ShouldBeNull();
    }
}
