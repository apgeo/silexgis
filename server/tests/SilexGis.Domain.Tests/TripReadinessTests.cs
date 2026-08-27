// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;
using Xunit;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How much of a list a trip has settled: the whole truth table of a figure that is derived on
/// every reading and written down nowhere.
/// </summary>
public class TripReadinessTests
{
    [Fact]
    public void Settled_lines_are_counted_against_the_lines_that_are_on_the_list()
    {
        var permit = Guid.CreateVersion7();
        var key = Guid.CreateVersion7();
        var callout = Guid.CreateVersion7();

        TripReadiness.Of([permit, key, callout], [permit]).ShouldBe(new TripReadiness(1, 3));
        TripReadiness.Of([permit, key, callout], [permit, key, callout])
            .ShouldBe(new TripReadiness(3, 3));
        TripReadiness.Of([permit, key, callout], []).ShouldBe(new TripReadiness(0, 3));
    }

    /// <summary>
    /// A list with nothing on it is settled, and says so with a total of zero rather than with a
    /// figure that reads as a failure. Nothing expected and everything done are the same state.
    /// </summary>
    [Fact]
    public void A_list_with_no_lines_is_settled()
    {
        var stray = Guid.CreateVersion7();

        TripReadiness.Of([], []).ShouldBe(new TripReadiness(0, 0));
        TripReadiness.Of([], [stray]).ShouldBe(new TripReadiness(0, 0));
        TripReadiness.Of([], []).IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// The figure can fall behind what is really settled and can never run ahead of it. A
    /// confirmation naming a line that is not on the list being measured — a line taken off it,
    /// or a line of some other list — counts for nothing, so the answer never reads more settled
    /// than the list is. A mark that is sometimes behind is worth having; one that could be ahead
    /// would not be.
    /// </summary>
    [Fact]
    public void A_confirmation_of_something_not_on_the_list_counts_for_nothing()
    {
        var permit = Guid.CreateVersion7();
        var somebodyElsesLine = Guid.CreateVersion7();

        TripReadiness.Of([permit], [permit, somebodyElsesLine]).ShouldBe(new TripReadiness(1, 1));
        TripReadiness.Of([permit], [somebodyElsesLine]).ShouldBe(new TripReadiness(0, 1));
        TripReadiness.Of([permit], [somebodyElsesLine]).IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// The same confirmation offered twice is one line settled. The rows themselves cannot carry
    /// it twice, and the rule does not depend on that being true elsewhere.
    /// </summary>
    [Fact]
    public void The_same_line_confirmed_twice_is_one_line()
    {
        var permit = Guid.CreateVersion7();
        var key = Guid.CreateVersion7();

        TripReadiness.Of([permit, key], [permit, permit]).ShouldBe(new TripReadiness(1, 2));
    }
}
