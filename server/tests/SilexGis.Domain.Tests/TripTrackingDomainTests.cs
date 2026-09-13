// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public class TripTrackingDomainTests
{
    private static readonly TrackingDepthResolver.Station[] Shaft =
    [
        new("cave.ent.0", "cave.ent", 350, IsEntrance: true),
        new("cave.upper.1", "cave.upper", 340, IsEntrance: false),
        new("cave.upper.2", "cave.upper", 300, IsEntrance: false),
        new("cave.parallel.2", "cave.parallel", 300, IsEntrance: false),
        new("cave.deep.3", "cave.deep", 230, IsEntrance: false),
    ];

    [Fact]
    public void The_depth_datum_is_the_named_station_when_one_is_configured_and_the_highest_entrance_otherwise()
    {
        TrackingDepthResolver.ReferenceZ(Shaft, "cave.upper.1").ShouldBe(340);
        TrackingDepthResolver.ReferenceZ(Shaft, null).ShouldBe(350);
        // A name that is not a station answers null rather than quietly measuring from elsewhere.
        TrackingDepthResolver.ReferenceZ(Shaft, "cave.nowhere").ShouldBeNull();
    }

    [Fact]
    public void A_survey_with_no_entrance_flags_still_gets_a_datum_and_an_empty_one_gets_none()
    {
        TrackingDepthResolver.Station[] unflagged =
        [
            new("a.1", "a", 120, false),
            new("a.2", "a", 180, false),
        ];
        TrackingDepthResolver.ReferenceZ(unflagged, null).ShouldBe(180);
        TrackingDepthResolver.ReferenceZ([], null).ShouldBeNull();
    }

    [Fact]
    public void The_closest_station_wins_and_a_signed_depth_means_the_same_place()
    {
        var down = TrackingDepthResolver.Resolve(Shaft, 350, 115, [], take: 2);
        down[0].Name.ShouldBe("cave.deep.3");
        down[0].DepthM.ShouldBe(120);

        var signed = TrackingDepthResolver.Resolve(Shaft, 350, -115, [], take: 2);
        signed[0].Name.ShouldBe(down[0].Name);
        signed[0].DeltaM.ShouldBe(down[0].DeltaM);
    }

    [Fact]
    public void Stations_at_the_same_depth_come_back_in_name_order_so_the_answer_is_stable()
    {
        var tied = TrackingDepthResolver.Resolve(Shaft, 350, 50, [], take: 3);
        tied[0].Name.ShouldBe("cave.parallel.2");
        tied[1].Name.ShouldBe("cave.upper.2");
        tied[0].DeltaM.ShouldBe(tied[1].DeltaM);
    }

    [Fact]
    public void The_filter_keeps_depth_matching_inside_the_parts_the_party_is_actually_in()
    {
        // Both branches have a station at −50; the filter names the branch, so the parallel
        // shaft at the same depth cannot claim the report.
        var filtered = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["cave.upper"], take: 3);
        filtered.ShouldAllBe(c => c.Name.StartsWith("cave.upper"));
        filtered[0].Name.ShouldBe("cave.upper.2");

        var none = TrackingDepthResolver.Resolve(Shaft, 350, 50, ["cave.absent"], take: 3);
        none.ShouldBeEmpty();
    }

    [Fact]
    public void Tracking_arms_closes_and_rearms_and_never_returns_to_off()
    {
        TripTrackingRules.MayTransition(TripTrackingState.Off, TripTrackingState.Armed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Closed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Closed, TripTrackingState.Armed).ShouldBeTrue();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Armed).ShouldBeTrue();

        TripTrackingRules.MayTransition(TripTrackingState.Off, TripTrackingState.Closed).ShouldBeFalse();
        TripTrackingRules.MayTransition(TripTrackingState.Armed, TripTrackingState.Off).ShouldBeFalse();
        TripTrackingRules.MayTransition(TripTrackingState.Closed, TripTrackingState.Off).ShouldBeFalse();
    }
}
