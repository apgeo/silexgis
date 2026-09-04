// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The cross-section arithmetic. Most of what these pin is the absence path, because that is where
/// this class can be wrong without anybody being able to see it: a wall the surveyor never reached
/// is not a wall at distance zero, and a volume that treats it as one comes out smaller than the
/// cave, always, and looks entirely reasonable. The measurement tests are here to prove the refusals
/// are refusals of the right thing rather than a class that refuses everything.
/// </summary>
public class CrossSectionMorphometryTests
{
    // -----------------------------------------------------------------------
    // What "not measured" does
    // -----------------------------------------------------------------------

    [Fact]
    public void A_station_that_found_one_side_wall_has_no_width_and_a_station_that_found_both_does()
    {
        // Half a measurement is not a width. The refusal has to be shown beside the case that
        // succeeds, or it is indistinguishable from a class that never produces a width at all.
        var stations = CrossSectionMorphometry.Stations([
            new("half", LeftM: 3, RightM: null, UpM: 1, DownM: 1, ElevationM: null),
            new("whole", LeftM: 3, RightM: 2, UpM: 1, DownM: 1, ElevationM: null),
        ]);

        var half = stations.Single(s => s.StationName == "half");
        half.WidthM.ShouldBeNull();
        half.AreaM2.ShouldBeNull();
        half.HasArea.ShouldBeFalse();
        half.HeightM.ShouldNotBeNull().ShouldBe(2, tolerance: 1e-9);

        var whole = stations.Single(s => s.StationName == "whole");
        whole.WidthM.ShouldNotBeNull().ShouldBe(5, tolerance: 1e-9);
        whole.HasArea.ShouldBeTrue();
    }

    [Fact]
    public void A_leg_whose_far_end_was_never_measured_leaves_the_volume_rather_than_flattening_it()
    {
        // The failure this whole class is shaped against. The unmeasured end is unknown, not zero,
        // so the leg contributes nothing at all — and the length it covers is reported as not
        // covered, so a reader can see the volume describes half the cave.
        var readings = new CrossSectionReading[]
        {
            new("a", 1, 1, 1, 1, null),
            new("b", 1, 1, 1, 1, null),
            new("c", 1, null, 1, 1, null),
        };
        var legs = new CrossSectionLeg[]
        {
            new("a", "b", 10, IsDuplicate: false),
            new("b", "c", 30, IsDuplicate: false),
        };

        var volume = CrossSectionMorphometry.Volume(CrossSectionMorphometry.Stations(readings), legs);

        // One leg of 10 m between two stations each measuring a 2 m by 2 m section.
        volume.VolumeM3.ShouldNotBeNull().ShouldBe(10 * Math.PI, tolerance: 1e-9);
        volume.LegCount.ShouldBe(2);
        volume.MeasuredLegCount.ShouldBe(1);
        volume.LengthM.ShouldBe(40, tolerance: 1e-9);
        volume.MeasuredLengthM.ShouldBe(10, tolerance: 1e-9);
        volume.LengthFraction.ShouldNotBeNull().ShouldBe(0.25, tolerance: 1e-9);
    }

    [Fact]
    public void A_cave_whose_walls_were_never_measured_has_no_volume_rather_than_a_volume_of_zero()
    {
        // A cave nobody measured the walls of does not enclose nothing. Zero here would be a
        // confident statement about a cave that was never looked at, and it would sort, average and
        // chart alongside real figures without ever announcing itself.
        var volume = CrossSectionMorphometry.Volume(
            CrossSectionMorphometry.Stations([]),
            [new("a", "b", 25, IsDuplicate: false)]);

        volume.VolumeM3.ShouldBeNull();
        volume.MeasuredLegCount.ShouldBe(0);
        volume.MeasuredLengthM.ShouldBe(0);
        volume.LengthM.ShouldBe(25, tolerance: 1e-9);
        volume.LengthFraction.ShouldNotBeNull().ShouldBe(0);
    }

    [Fact]
    public void A_wall_found_at_the_station_itself_is_a_measurement_and_produces_a_volume_of_zero()
    {
        // The other half of the same distinction, and the reason the sentinel rule tests on the sign
        // rather than on the value. Zero is what a surveyor records standing against the wall. A
        // cave of such stations really does enclose nothing measurable, and that answer is a number
        // rather than a null — which is exactly what an unmeasured cave must not be.
        var readings = new CrossSectionReading[]
        {
            new("a", 0, 0, 0, 0, null),
            new("b", 0, 0, 0, 0, null),
        };

        var stations = CrossSectionMorphometry.Stations(readings);
        stations.ShouldAllBe(s => s.HasArea);
        stations[0].WidthM.ShouldNotBeNull().ShouldBe(0);

        var volume = CrossSectionMorphometry.Volume(stations, [new("a", "b", 10, IsDuplicate: false)]);
        volume.VolumeM3.ShouldNotBeNull().ShouldBe(0);
        volume.MeasuredLegCount.ShouldBe(1);
    }

    [Fact]
    public void A_leg_whose_end_the_file_never_named_counts_towards_the_length_and_never_towards_the_volume()
    {
        // A leg with an unresolved endpoint cannot be matched to a cross-section however well the
        // rest of the cave is measured, so it belongs in the denominator and nowhere else.
        var stations = CrossSectionMorphometry.Stations([new("a", 1, 1, 1, 1, null)]);

        var volume = CrossSectionMorphometry.Volume(stations, [new("a", null, 12, IsDuplicate: false)]);

        volume.VolumeM3.ShouldBeNull();
        volume.LegCount.ShouldBe(1);
        volume.MeasuredLegCount.ShouldBe(0);
        volume.LengthM.ShouldBe(12, tolerance: 1e-9);
    }

    [Fact]
    public void A_dimension_that_is_not_a_number_is_absence_and_so_is_a_negative_one()
    {
        // Nothing stored can be negative and nothing stored should be a non-number, so this is the
        // guard against a reading that arrived from somewhere with weaker promises. Letting either
        // through poisons every figure over the whole cave rather than the one station.
        var stations = CrossSectionMorphometry.Stations([
            new("bad", LeftM: double.NaN, RightM: -1, UpM: double.PositiveInfinity, DownM: 2, ElevationM: null),
        ]);

        var station = stations.Single();
        station.WidthM.ShouldBeNull();
        station.HeightM.ShouldBeNull();
        station.AreaM2.ShouldBeNull();
    }

    [Fact]
    public void The_answer_says_how_many_stations_each_figure_was_computed_over()
    {
        // Every distribution here has its own denominator, and they are not the same number. A
        // reader given a median width and a station count has no way to tell that the width came
        // from a third of them.
        var summary = CrossSectionMorphometry.Summarize(
            [
                new("full", 1, 1, 2, 2, null),
                new("widthOnly", 1, 1, null, 2, null),
                new("heightOnly", null, 1, 2, 2, null),
            ],
            []);

        summary.ReadingCount.ShouldBe(3);
        summary.StationCount.ShouldBe(3);
        summary.WidthStationCount.ShouldBe(2);
        summary.HeightStationCount.ShouldBe(2);
        summary.AreaStationCount.ShouldBe(1);
        summary.Width.ShouldNotBeNull().Count.ShouldBe(2);
        summary.Area.ShouldNotBeNull().Count.ShouldBe(1);
    }

    [Fact]
    public void A_cave_with_no_readings_at_all_reports_nothing_rather_than_zeroes()
    {
        var summary = CrossSectionMorphometry.Summarize([], []);

        summary.StationCount.ShouldBe(0);
        summary.Width.ShouldBeNull();
        summary.Height.ShouldBeNull();
        summary.WidthHeightRatio.ShouldBeNull();
        summary.Area.ShouldBeNull();
        summary.Volume.VolumeM3.ShouldBeNull();
        summary.Volume.LengthFraction.ShouldBeNull();
        summary.Bands.ShouldBeEmpty();
        summary.BandWidthM.ShouldBe(0);
    }

    // -----------------------------------------------------------------------
    // What the measurements produce
    // -----------------------------------------------------------------------

    [Fact]
    public void A_cave_of_known_dimensions_yields_the_hand_computed_volume()
    {
        // Two stations, each measuring one metre to every wall — a section two metres across and two
        // metres high — joined by ten metres of passage. The section is the ellipse inscribed in
        // that box, area pi, so the passage holds ten pi cubic metres. Working it the other way, as
        // four quarter-ellipses of semi-axes one and one, gives (pi/4)(1+1+1+1) = pi as well.
        var summary = CrossSectionMorphometry.Summarize(
            [new("a", 1, 1, 1, 1, null), new("b", 1, 1, 1, 1, null)],
            [new("a", "b", 10, IsDuplicate: false)]);

        summary.Area.ShouldNotBeNull().Median.ShouldBe(Math.PI, tolerance: 1e-9);
        summary.Volume.VolumeM3.ShouldNotBeNull().ShouldBe(31.41592653589793, tolerance: 1e-9);
        summary.Volume.LengthFraction.ShouldNotBeNull().ShouldBe(1, tolerance: 1e-9);
    }

    [Fact]
    public void A_passage_that_narrows_along_its_length_holds_the_mean_of_its_two_ends()
    {
        // The prism, and the reason it is a prism rather than a bundle of tubes: each leg owns the
        // passage between its own two ends and nothing else, so nothing near a junction is counted
        // by more than one of them.
        var summary = CrossSectionMorphometry.Summarize(
            [new("wide", 2, 2, 2, 2, null), new("narrow", 0.5, 0.5, 0.5, 0.5, null)],
            [new("wide", "narrow", 8, IsDuplicate: false)]);

        var wide = Math.PI / 4d * 4 * 4;
        var narrow = Math.PI / 4d * 1 * 1;
        summary.Volume.VolumeM3.ShouldNotBeNull().ShouldBe(8 * (wide + narrow) / 2d, tolerance: 1e-9);
    }

    [Fact]
    public void A_junction_divides_its_passage_among_the_legs_that_meet_there_rather_than_giving_each_a_copy()
    {
        // Three legs of ten metres meeting at one station, every station measuring the same section.
        // The answer is thirty metres of passage and no more: the alternative reading, in which each
        // leg carries an independently capped tube of its own, counts the cave around the junction
        // three times and is worst in exactly the mazy caves where a volume is most interesting.
        var section = Math.PI / 4d * 2 * 2;
        var summary = CrossSectionMorphometry.Summarize(
            [
                new("hub", 1, 1, 1, 1, null),
                new("n", 1, 1, 1, 1, null),
                new("e", 1, 1, 1, 1, null),
                new("s", 1, 1, 1, 1, null),
            ],
            [
                new("hub", "n", 10, IsDuplicate: false),
                new("hub", "e", 10, IsDuplicate: false),
                new("hub", "s", 10, IsDuplicate: false),
            ]);

        summary.Volume.VolumeM3.ShouldNotBeNull().ShouldBe(30 * section, tolerance: 1e-9);
    }

    [Fact]
    public void A_duplicate_leg_is_passage_already_counted_and_adds_no_volume()
    {
        var summary = CrossSectionMorphometry.Summarize(
            [new("a", 1, 1, 1, 1, null), new("b", 1, 1, 1, 1, null)],
            [new("a", "b", 10, IsDuplicate: false), new("a", "b", 10, IsDuplicate: true)]);

        summary.Volume.LegCount.ShouldBe(1);
        summary.Volume.VolumeM3.ShouldNotBeNull().ShouldBe(10 * Math.PI, tolerance: 1e-9);
    }

    [Fact]
    public void Two_readings_of_one_station_are_averaged_wall_by_wall()
    {
        // Two legs meeting at a station each measure its walls and they need not agree; the
        // difference is measurement spread, not a conflict with a right answer. Averaging per wall
        // rather than per reading is what lets a station whose one reading found the left wall and
        // whose other found the right have a width at all.
        var stations = CrossSectionMorphometry.Stations([
            new("a", LeftM: 1, RightM: null, UpM: 2, DownM: 2, ElevationM: null),
            new("a", LeftM: 3, RightM: 5, UpM: null, DownM: null, ElevationM: null),
        ]);

        var station = stations.ShouldHaveSingleItem();
        station.ReadingCount.ShouldBe(2);
        station.WidthM.ShouldNotBeNull().ShouldBe(7, tolerance: 1e-9); // mean left 2, right 5
        station.HeightM.ShouldNotBeNull().ShouldBe(4, tolerance: 1e-9);
    }

    [Fact]
    public void A_wide_tube_and_a_tall_canyon_separate_on_the_ratio()
    {
        // The figure this exists to produce: the shape of the section, independent of its size.
        var tube = CrossSectionMorphometry.Stations([new("t", 4, 4, 1, 1, null)]).Single();
        var canyon = CrossSectionMorphometry.Stations([new("c", 0.5, 0.5, 5, 5, null)]).Single();

        tube.WidthHeightRatio.ShouldNotBeNull().ShouldBe(4, tolerance: 1e-9);
        canyon.WidthHeightRatio.ShouldNotBeNull().ShouldBe(0.1, tolerance: 1e-9);
    }

    [Fact]
    public void A_section_with_no_height_at_all_has_no_ratio_rather_than_an_infinite_one()
    {
        // Both ceiling and floor found at the station: a real, if unusual, measurement. The ratio
        // against it is a division by zero, and an infinity propagates through every mean and
        // quartile computed from it.
        var station = CrossSectionMorphometry.Stations([new("flat", 2, 2, 0, 0, null)]).Single();

        station.HeightM.ShouldNotBeNull().ShouldBe(0);
        station.WidthHeightRatio.ShouldBeNull();
        station.AreaM2.ShouldNotBeNull().ShouldBe(0);
    }

    [Fact]
    public void The_quartiles_are_read_off_the_sorted_measurements_by_interpolation()
    {
        // Pinned by hand rather than against the implementation: four values put both quartiles
        // between two order statistics, which is the case a different quartile convention changes.
        var distribution = CrossSectionMorphometry.Distribution([4d, 1d, 3d, 2d]).ShouldNotBeNull();

        distribution.Count.ShouldBe(4);
        distribution.Minimum.ShouldBe(1);
        distribution.LowerQuartile.ShouldBe(1.75, tolerance: 1e-9);
        distribution.Median.ShouldBe(2.5, tolerance: 1e-9);
        distribution.UpperQuartile.ShouldBe(3.25, tolerance: 1e-9);
        distribution.Maximum.ShouldBe(4);
        distribution.Mean.ShouldBe(2.5, tolerance: 1e-9);
    }

    // -----------------------------------------------------------------------
    // Elevation bands
    // -----------------------------------------------------------------------

    [Fact]
    public void Passage_sizes_are_reported_by_height_and_the_empty_slices_between_are_absent()
    {
        // Two occupied levels fifty metres apart. The slices in between hold nothing, and reporting
        // them would draw a run of zero-sized passage where there is simply no survey.
        var summary = CrossSectionMorphometry.Summarize(
            [
                new("low1", 1, 1, 1, 1, ElevationM: 10),
                new("low2", 1, 1, 1, 1, ElevationM: 12),
                new("high1", 3, 3, 1, 1, ElevationM: 60),
                new("high2", 3, 3, 1, 1, ElevationM: 62),
            ],
            []);

        summary.BandWidthM.ShouldBe(5, tolerance: 1e-9);
        summary.Bands.Count.ShouldBe(2);
        summary.Bands[0].FromM.ShouldBe(10, tolerance: 1e-9);
        summary.Bands[0].StationCount.ShouldBe(2);
        summary.Bands[0].Width.ShouldNotBeNull().Median.ShouldBe(2, tolerance: 1e-9);
        summary.Bands[1].FromM.ShouldBe(60, tolerance: 1e-9);
        summary.Bands[1].Width.ShouldNotBeNull().Median.ShouldBe(6, tolerance: 1e-9);
        summary.Bands.ShouldAllBe(b => b.AreaStationCount == 2);
    }

    [Fact]
    public void A_station_with_no_altitude_sits_in_no_band_and_still_counts_everywhere_else()
    {
        // A plan drawing carries no third coordinate. Placing its stations at zero would stack the
        // whole cave into one band at sea level and label it a level of the cave.
        var summary = CrossSectionMorphometry.Summarize(
            [
                new("placed", 1, 1, 1, 1, ElevationM: 100),
                new("unplaced", 2, 2, 1, 1, ElevationM: null),
            ],
            []);

        summary.StationCount.ShouldBe(2);
        summary.Width.ShouldNotBeNull().Count.ShouldBe(2);
        summary.Bands.ShouldHaveSingleItem().StationCount.ShouldBe(1);
    }

    /// <summary>
    /// A cave built so that height is exactly the square root of width recovers that exponent, and
    /// says so with a fit that accounts for all of the spread. Anything less than an exact recovery
    /// here would be arithmetic, not judgement.
    /// </summary>
    [Fact]
    public void Height_scaling_with_width_is_recovered_from_the_stations()
    {
        var stations = new List<StationCrossSection>();
        foreach (var width in new[] { 1d, 2, 4, 8, 16, 32 })
        {
            var height = Math.Sqrt(width);
            stations.Add(new StationCrossSection(
                $"s{width}", null, 1, width, height, width / height,
                CrossSectionMorphometry.AreaOf(width, height)));
        }

        var scaling = CrossSectionMorphometry.Scaling(stations).ShouldNotBeNull();

        scaling.StationCount.ShouldBe(6);
        scaling.Exponent.ShouldBe(0.5, tolerance: 1e-9);
        scaling.Coefficient.ShouldBe(1, tolerance: 1e-9);
        scaling.RSquared.ShouldBe(1, tolerance: 1e-9);
    }

    /// <summary>
    /// Too few stations is refused rather than fitted. A line through a handful of points is those
    /// points, and an exponent quoted from them looks exactly as authoritative as one quoted from a
    /// whole cave.
    /// </summary>
    [Fact]
    public void A_scaling_relationship_is_not_claimed_from_a_handful_of_stations()
    {
        var stations = Enumerable.Range(1, CrossSectionMorphometry.MinimumScalingStations - 1)
            .Select(i => new StationCrossSection($"s{i}", null, 1, i, i * 2d, 0.5, null))
            .ToList();

        CrossSectionMorphometry.Scaling(stations).ShouldBeNull();
    }

    /// <summary>
    /// A station hard against both walls has a width of zero, which is a measurement and not a
    /// missing one — but its logarithm is not a very small passage, so it takes no part in the fit
    /// rather than dragging it to an exponent nobody measured.
    /// </summary>
    [Fact]
    public void A_station_of_no_size_takes_no_part_in_the_scaling()
    {
        var stations = new List<StationCrossSection>();
        foreach (var width in new[] { 1d, 2, 4, 8, 16, 32 })
        {
            stations.Add(new StationCrossSection($"s{width}", null, 1, width, width, 1, null));
        }

        stations.Add(new StationCrossSection("flat", null, 1, 0, 0, null, null));

        var scaling = CrossSectionMorphometry.Scaling(stations).ShouldNotBeNull();

        scaling.StationCount.ShouldBe(6);
        scaling.Exponent.ShouldBe(1, tolerance: 1e-9);
    }

    /// <summary>
    /// Every station the same width says nothing about how height changes with width, and the slope
    /// through them is a division by zero rather than a very steep relationship.
    /// </summary>
    [Fact]
    public void Stations_that_are_all_one_width_yield_no_scaling()
    {
        var stations = Enumerable.Range(1, 8)
            .Select(i => new StationCrossSection($"s{i}", null, 1, 3, i, 3d / i, null))
            .ToList();

        CrossSectionMorphometry.Scaling(stations).ShouldBeNull();
    }

    [Fact]
    public void A_station_that_measured_nothing_usable_is_in_no_band()
    {
        var summary = CrossSectionMorphometry.Summarize(
            [new("nothing", LeftM: 3, RightM: null, UpM: null, DownM: null, ElevationM: 100)],
            []);

        summary.StationCount.ShouldBe(1);
        summary.Bands.ShouldBeEmpty();
        summary.BandWidthM.ShouldBe(0);
    }
}
