// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class CaveSystemFigureArithmeticTests
{
    [Fact]
    public void Depth_runs_from_the_highest_entrance_and_height_from_the_lowest()
    {
        var figures = CaveSystemFigures.Compute([1200d, 1100d], 2, 1150d, 900d, 800d);

        figures.EntranceCount.ShouldBe(2);
        figures.EntrancesWithAltitude.ShouldBe(2);
        figures.HighestEntranceM.ShouldBe(1200d);
        figures.LowestEntranceM.ShouldBe(1100d);
        figures.EntranceSpreadM.ShouldBe(100d);
        figures.TotalDepthM.ShouldBe(300d);
        figures.HeightAboveLowestEntranceM.ShouldBe(50d);
        figures.ResurgenceLevelM.ShouldBe(800d);
        figures.PotentialDepthM.ShouldBe(400d);
    }

    [Fact]
    public void Without_a_survey_with_altitudes_the_figures_that_need_one_are_null_not_nought()
    {
        var figures = CaveSystemFigures.Compute([1200d], 1, null, null, null);

        figures.HighestEntranceM.ShouldBe(1200d);
        figures.TotalDepthM.ShouldBeNull();
        figures.HeightAboveLowestEntranceM.ShouldBeNull();
        figures.PotentialDepthM.ShouldBeNull();
    }

    [Fact]
    public void Without_an_entrance_altitude_nothing_is_measured_from()
    {
        var figures = CaveSystemFigures.Compute([], 3, 1150d, 900d, 800d);

        figures.EntranceCount.ShouldBe(3);
        figures.EntrancesWithAltitude.ShouldBe(0);
        figures.HighestEntranceM.ShouldBeNull();
        figures.EntranceSpreadM.ShouldBeNull();
        figures.TotalDepthM.ShouldBeNull();
        figures.PotentialDepthM.ShouldBeNull();
    }

    [Fact]
    public void A_survey_that_never_leaves_its_entrance_level_has_no_depth_or_height_and_not_a_negative_one()
    {
        var figures = CaveSystemFigures.Compute([1000d], 1, 1000d, 1000d, null);
        figures.TotalDepthM.ShouldBe(0d);
        figures.HeightAboveLowestEntranceM.ShouldBe(0d);

        // A survey lying wholly above its entrances is a datum problem, reported as none.
        CaveSystemFigures.Compute([1000d], 1, 1500d, 1400d, null).TotalDepthM.ShouldBe(0d);
    }

    [Fact]
    public void A_resurgence_level_not_below_the_highest_entrance_gives_no_potential_depth()
    {
        CaveSystemFigures.Compute([1000d], 1, null, null, 1000d).PotentialDepthM.ShouldBeNull();
        CaveSystemFigures.Compute([1000d], 1, null, null, 1200d).PotentialDepthM.ShouldBeNull();
        CaveSystemFigures.Compute([1000d], 1, null, null, 700d).PotentialDepthM.ShouldBe(300d);
    }
}
