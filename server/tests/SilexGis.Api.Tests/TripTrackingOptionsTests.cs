// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.TripTracking;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two list sizes an operator sets for the published trip surface, and the bounds they are
/// held inside whatever is set.
/// </summary>
/// <remarks>
/// <para>
/// Both lists are read anonymously, by anybody holding a link somebody put in an article, and the
/// bound on each is what keeps such a link from becoming the cheapest way to read a club's whole
/// register in one request, or to have an unbounded number of parties folded for one. The bound is
/// promised in the settings' own remarks, in the sample environment file and in the route
/// summaries — and it was asserted nowhere: the integration tests only ever narrow a list, so a
/// clamp rewritten as a floor would have passed every one of them.
/// </para>
/// <para>
/// No database, because the bound is a property of the setting and not of any query; these are the
/// ceiling's own tests, with the floor and a value inside the range asserted beside it so that the
/// ceiling cannot pass by the property answering a constant.
/// </para>
/// </remarks>
public class TripTrackingOptionsTests
{
    [Fact]
    public void The_archive_list_size_is_held_to_its_bound_however_high_it_is_set()
    {
        // The number itself is the promise made to operators, so it is asserted as a number and
        // not only through the constant.
        TripPastTrackOptions.MaxListSize.ShouldBe(200);

        new TripPastTrackOptions { ListSize = 10_000 }.EffectiveListSize.ShouldBe(200);
        new TripPastTrackOptions { ListSize = 201 }.EffectiveListSize.ShouldBe(200);
        new TripPastTrackOptions { ListSize = 200 }.EffectiveListSize.ShouldBe(200);
    }

    [Fact]
    public void The_archive_list_size_never_falls_to_nothing_and_is_served_as_asked_in_between()
    {
        // Zero would be a way to turn the feature half off, which is what the enabled switch is
        // for; a negative number is a typo, and a typo serves a sane list.
        new TripPastTrackOptions { ListSize = 0 }.EffectiveListSize.ShouldBe(1);
        new TripPastTrackOptions { ListSize = -3 }.EffectiveListSize.ShouldBe(1);
        new TripPastTrackOptions { ListSize = 1 }.EffectiveListSize.ShouldBe(1);

        new TripPastTrackOptions { ListSize = 25 }.EffectiveListSize.ShouldBe(25);
        new TripPastTrackOptions().EffectiveListSize.ShouldBe(50);
    }

    [Fact]
    public void The_followed_list_size_is_held_to_its_lower_bound_however_high_it_is_set()
    {
        // Lower than the archive's, because a row here is a whole party folded from its own log
        // rather than a title and a headcount.
        TripTrackingOptions.MaxFollowedListSize.ShouldBe(50);
        TripTrackingOptions.MaxFollowedListSize.ShouldBeLessThan(TripPastTrackOptions.MaxListSize);

        new TripTrackingOptions { FollowedListSize = 10_000 }.EffectiveFollowedListSize.ShouldBe(50);
        new TripTrackingOptions { FollowedListSize = 51 }.EffectiveFollowedListSize.ShouldBe(50);
        new TripTrackingOptions { FollowedListSize = 50 }.EffectiveFollowedListSize.ShouldBe(50);
    }

    [Fact]
    public void The_followed_list_size_never_falls_to_nothing_and_is_served_as_asked_in_between()
    {
        new TripTrackingOptions { FollowedListSize = 0 }.EffectiveFollowedListSize.ShouldBe(1);
        new TripTrackingOptions { FollowedListSize = -3 }.EffectiveFollowedListSize.ShouldBe(1);
        new TripTrackingOptions { FollowedListSize = 1 }.EffectiveFollowedListSize.ShouldBe(1);

        new TripTrackingOptions { FollowedListSize = 7 }.EffectiveFollowedListSize.ShouldBe(7);
        new TripTrackingOptions().EffectiveFollowedListSize.ShouldBe(20);
    }
}
