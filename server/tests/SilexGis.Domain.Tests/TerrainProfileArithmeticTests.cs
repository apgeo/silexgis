// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

public sealed class TerrainProfileArithmeticTests
{
    [Fact]
    public void A_line_is_resampled_evenly_from_its_start_to_its_end()
    {
        // About 1.1 km due east on the equator.
        var (stations, length) = TerrainLineProfile.Stations(
            [new DemSamplePoint(25.0, 0), new DemSamplePoint(25.005, 0), new DemSamplePoint(25.01, 0)]);

        length.ShouldBeInRange(1100, 1120);
        stations[0].DistanceAlongM.ShouldBe(0);
        stations[^1].DistanceAlongM.ShouldBe(length, 1e-9);
        stations[^1].Longitude.ShouldBe(25.01, 1e-9);
        stations.Count.ShouldBe(Math.Min(TerrainLineProfile.MaxStations, (int)(length / TerrainLineProfile.MinStepM) + 1));
        stations.Select(s => s.DistanceAlongM).ShouldBe(stations.Select(s => s.DistanceAlongM).Order());
    }

    [Fact]
    public void A_line_of_no_extent_has_no_stations_and_a_long_one_is_bounded()
    {
        TerrainLineProfile.Stations([new DemSamplePoint(25, 46), new DemSamplePoint(25, 46)])
            .Stations.ShouldBeEmpty();

        TerrainLineProfile.Stations([new DemSamplePoint(20, 46), new DemSamplePoint(26, 46)])
            .Stations.Count.ShouldBe(TerrainLineProfile.MaxStations);
    }

    [Fact]
    public void Ascent_and_descent_are_never_summed_across_a_station_with_no_height()
    {
        var summary = TerrainLineProfile.Summarise([100d, 110d, null, 200d, 190d]);

        summary.ReadCount.ShouldBe(4);
        summary.LowestM.ShouldBe(100d);
        summary.HighestM.ShouldBe(200d);
        summary.AscentM.ShouldBe(10d);
        summary.DescentM.ShouldBe(10d);

        TerrainLineProfile.Summarise([null, null]).LowestM.ShouldBeNull();
    }

    [Fact]
    public void A_depth_profile_measures_below_the_highest_point_and_refuses_a_plan_drawing()
    {
        PassageSegment Leg(int i, double fromZ, double toZ) => new(
            0, i, false, 25 + (i * 0.0005), 46, 25 + ((i + 1) * 0.0005), 46, 40, 40,
            toZ - fromZ, (fromZ + toZ) / 2);

        var profile = CaveDepthProfile.Build([Leg(0, 1000, 980), Leg(1, 980, 940)]);

        profile.HighestM.ShouldBe(1000d);
        profile.LowestM!.Value.ShouldBe(940d, 1e-6);
        profile.DeepestM!.Value.ShouldBe(60d, 1e-6);
        profile.Samples[0].DepthM.ShouldBe(0, 1e-6);
        profile.Samples.ShouldAllBe(s => s.DepthM >= 0);

        var plan = new PassageSegment(0, 0, false, 25, 46, 25.001, 46, 80, null, null, null);
        var none = CaveDepthProfile.Build([plan]);
        none.Samples.ShouldBeEmpty();
        none.HighestM.ShouldBeNull();
    }

    [Fact]
    public void Classes_are_cut_where_the_documentation_says_and_facing_wraps_at_north()
    {
        TerrainClassStatistics.ClassOf(TerrainMeasure.Slope, 4.9).ShouldBe(0);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Slope, 5).ShouldBe(1);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Slope, 80).ShouldBe(4);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Aspect, 359).ShouldBe(0);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Aspect, 10).ShouldBe(0);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Aspect, 90).ShouldBe(2);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Aspect, 180).ShouldBe(4);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Position, -1.5).ShouldBe(0);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Position, 1).ShouldBe(1);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Position, 3).ShouldBe(2);
    }

    [Fact]
    public void A_ratio_says_nothing_where_there_is_nothing_to_divide_by()
    {
        TerrainClassStatistics.Ratio(5, 10, 25, 100).ShouldBe(2d);
        TerrainClassStatistics.Ratio(0, 10, 25, 100).ShouldBe(0d);
        TerrainClassStatistics.Ratio(5, 10, 0, 100).ShouldBeNull();
        TerrainClassStatistics.Ratio(0, 0, 25, 100).ShouldBeNull();
    }
}
