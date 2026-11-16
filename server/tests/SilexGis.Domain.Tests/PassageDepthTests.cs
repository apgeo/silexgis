// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The colour classes and the runs behind the depth-coloured passage layer. What these pin is that a
/// legend edge and a colour agree, that negative cover keeps its own class instead of being clamped,
/// and that an unread station or a jump between unrelated branches is a gap in the line rather than
/// a stretch drawn at nought or a line invented between two places.
/// </summary>
public class PassageDepthTests
{
    private static OverburdenStation At(double lon, double lat = 46.0) =>
        new(0, lon, lat, 900, 0, 0);

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-0.001, 0)]
    [InlineData(0, 1)]
    [InlineData(9.99, 1)]
    [InlineData(10, 2)]
    [InlineData(49.9, 3)]
    [InlineData(199.9, 5)]
    [InlineData(200, 6)]
    [InlineData(5000, 6)]
    public void A_thickness_falls_in_the_class_between_its_edges(double cover, int expected) =>
        PassageDepth.ClassOf(cover).ShouldBe(expected);

    [Fact]
    public void There_is_one_more_class_than_there_are_edges() =>
        PassageDepth.ClassCount.ShouldBe(PassageDepth.ClassEdgesM.Count + 1);

    [Fact]
    public void Neighbouring_stations_of_one_class_form_one_run()
    {
        // About 7.7 m apart at latitude 46, covers 12 and 14: all in class 2.
        var stations = new[] { At(25.0000), At(25.0001), At(25.0002) };
        var runs = PassageDepth.Runs(stations, [12d, 13d, 14d], passageLengthM: 15.4);

        runs.Count.ShouldBe(1);
        runs[0].DepthClass.ShouldBe(2);
        runs[0].Points.Count.ShouldBe(3);
    }

    [Fact]
    public void A_change_of_class_starts_a_new_run_from_the_shared_station()
    {
        var stations = new[] { At(25.0000), At(25.0001), At(25.0002) };
        var runs = PassageDepth.Runs(stations, [12d, 14d, 60d], passageLengthM: 15.4);

        runs.Select(r => r.DepthClass).ShouldBe([2, 3]);
        runs[1].Points[0].ShouldBe(runs[0].Points[^1]);
    }

    [Fact]
    public void An_unread_station_is_a_gap_and_never_a_stretch_at_nought()
    {
        var stations = new[] { At(25.0000), At(25.0001), At(25.0002), At(25.0003) };
        var runs = PassageDepth.Runs(stations, [12d, null, 12d, 12d], passageLengthM: 23);

        runs.Count.ShouldBe(1);
        runs[0].Points.Count.ShouldBe(2);
        runs[0].Points[0].Longitude.ShouldBe(25.0002);
    }

    [Fact]
    public void A_jump_between_unrelated_branches_is_not_joined()
    {
        // The second station is a kilometre away from the first although the stations were meant to
        // be about 8 m apart along the passage: the line work changed branch between them.
        var stations = new[] { At(25.0000), At(25.0001), At(25.0150), At(25.0151) };
        var runs = PassageDepth.Runs(stations, [12d, 12d, 12d, 12d], passageLengthM: 23);

        runs.Count.ShouldBe(2);
        runs.All(r => r.Points.Count == 2).ShouldBeTrue();
    }

    [Fact]
    public void Cover_above_zero_and_below_it_are_different_classes()
    {
        var stations = new[] { At(25.0000), At(25.0001) };
        PassageDepth.Runs(stations, [-3d, -1d], 7.7).Single().DepthClass.ShouldBe(0);
        PassageDepth.Runs(stations, [1d, 3d], 7.7).Single().DepthClass.ShouldBe(1);
    }

    [Fact]
    public void Fewer_than_two_stations_draw_nothing() =>
        PassageDepth.Runs([At(25.0)], [5d], 0).ShouldBeEmpty();

    [Fact]
    public void A_station_budget_caps_the_profile_below_the_shared_ceiling()
    {
        var legs = Enumerable.Range(0, 100)
            .Select(i => new PassageSegment(
                0, i, false, 25.0 + i * 0.001, 46.0, 25.0 + (i + 1) * 0.001, 46.0, 100, 100, 0, 900))
            .ToArray();

        OverburdenProfile.Stations(legs, 30).Stations.Count.ShouldBe(30);
        OverburdenProfile.Stations(legs).Stations.Count.ShouldBe(OverburdenProfile.MaxStations);
    }
}
