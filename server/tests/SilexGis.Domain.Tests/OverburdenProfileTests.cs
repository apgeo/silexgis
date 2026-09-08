// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Where an overburden profile asks the ground about, which is the whole of its arithmetic that
/// does not need an elevation model under it. What these pin is that the horizontal axis means what
/// it says — the length the cave itself claims, with a leg walked twice counted once — and that a
/// drawing with no altitudes produces no stations at all rather than a row of them at sea level.
/// </summary>
public class OverburdenProfileTests
{
    /// <summary>
    /// A straight, level segment running east, one degree of latitude's worth of nothing: the
    /// arithmetic is checked against numbers that can be worked out on paper.
    /// </summary>
    private static PassageSegment Leg(
        int index,
        double fromLon,
        double toLon,
        double? midZ = 0,
        double? deltaZ = 0,
        double lengthM = 100,
        bool duplicate = false,
        int? path = 0) =>
        new(path, index, duplicate, fromLon, 46.0, toLon, 46.0, lengthM, lengthM, deltaZ, midZ);

    [Fact]
    public void The_axis_runs_the_length_of_the_passage_and_the_ends_sit_on_its_ends()
    {
        // Two legs of a hundred metres each. Whatever step is chosen, the first station is at the
        // start and the last is exactly at two hundred metres — not a hair short of it, which is
        // what accumulating a step two hundred times would leave.
        var (stations, length) = OverburdenProfile.Stations(
            [Leg(0, 25.000, 25.001), Leg(1, 25.001, 25.002)]);

        length.ShouldBe(200);
        stations.Count.ShouldBeGreaterThan(2);
        stations[0].DistanceAlongM.ShouldBe(0);
        stations[^1].DistanceAlongM.ShouldBe(200);
        stations.Select(s => s.DistanceAlongM).ShouldBeInOrder();
    }

    [Fact]
    public void A_leg_the_file_marks_as_walked_twice_advances_the_axis_by_nothing()
    {
        // The same passage recorded on a second trip. Counting it would report three hundred metres
        // of cave where the cave's own length statistic reports two hundred, and the two figures
        // would disagree about one cave with nothing in either to say why.
        var (_, withDuplicate) = OverburdenProfile.Stations(
            [Leg(0, 25.000, 25.001), Leg(1, 25.001, 25.002), Leg(2, 25.001, 25.002, duplicate: true)]);

        var (_, without) = OverburdenProfile.Stations(
            [Leg(0, 25.000, 25.001), Leg(1, 25.001, 25.002)]);

        withDuplicate.ShouldBe(200);
        withDuplicate.ShouldBe(without);
    }

    [Fact]
    public void A_drawing_with_no_altitudes_yields_no_stations_rather_than_a_row_of_them_at_nought()
    {
        // The positive control first: the same line work with altitudes does produce a profile, so
        // the emptiness below is the absent altitudes and not the fixture failing to describe a cave.
        var (measured, measuredLength) = OverburdenProfile.Stations([Leg(0, 25.000, 25.001, midZ: 500)]);
        measured.ShouldNotBeEmpty();
        measuredLength.ShouldBe(100);

        var (plan, planLength) = OverburdenProfile.Stations(
            [Leg(0, 25.000, 25.001, midZ: null, deltaZ: null)]);

        plan.ShouldBeEmpty();
        planLength.ShouldBe(0);
    }

    [Fact]
    public void The_altitude_at_a_station_follows_the_leg_it_fell_on()
    {
        // One leg, a hundred metres long, rising twenty metres and centred on five hundred: its
        // start is at 490 and its end at 510, so the first station reads 490 and the last 510.
        var (stations, _) = OverburdenProfile.Stations([Leg(0, 25.000, 25.001, midZ: 500, deltaZ: 20)]);

        stations[0].PassageAltitudeM.ShouldBe(490, 1e-9);
        stations[^1].PassageAltitudeM.ShouldBe(510, 1e-9);

        // And the position walks the leg with it.
        stations[0].Longitude.ShouldBe(25.000, 1e-12);
        stations[^1].Longitude.ShouldBe(25.001, 1e-12);
    }

    [Fact]
    public void A_long_cave_is_drawn_at_a_coarser_step_rather_than_asked_about_leg_by_leg()
    {
        // Five thousand legs of ten metres. Asked leg by leg this would hold the installation's one
        // elevation reader for five thousand reads; the bound is what stops it.
        var legs = Enumerable.Range(0, 5000)
            .Select(i => Leg(i, 25.0 + (i * 0.0001), 25.0 + ((i + 1) * 0.0001), midZ: 500, lengthM: 10))
            .ToArray();

        var (stations, length) = OverburdenProfile.Stations(legs);

        length.ShouldBe(50_000);
        stations.Count.ShouldBe(OverburdenProfile.MaxStations);
        stations[^1].DistanceAlongM.ShouldBe(50_000);
    }

    [Fact]
    public void A_cave_shorter_than_the_step_is_read_at_its_two_ends_and_not_four_hundred_times()
    {
        // A one-metre passage. Below the minimum step the stations would land inside a single pixel
        // of any elevation model this reads, repeating one number four hundred times at the cost of
        // four hundred waits on a reader everybody shares.
        var (stations, length) = OverburdenProfile.Stations([Leg(0, 25.0, 25.00001, midZ: 500, lengthM: 1)]);

        length.ShouldBe(1);
        stations.Count.ShouldBe(2);
    }

    [Fact]
    public void Legs_are_taken_in_the_order_the_line_work_records_them()
    {
        // Handed back to front and out of path order. The axis must still run start to end, because
        // the order is the only thing the line work says about how the pieces follow each other.
        var (stations, _) = OverburdenProfile.Stations(
        [
            Leg(1, 25.001, 25.002, midZ: 200, path: 1),
            Leg(0, 25.000, 25.001, midZ: 100, path: 0),
        ]);

        stations[0].PathIndex.ShouldBe(0);
        stations[0].SegmentIndex.ShouldBe(0);
        stations[^1].PathIndex.ShouldBe(1);
        stations[^1].SegmentIndex.ShouldBe(1);
    }

    [Fact]
    public void Line_work_with_no_extent_yields_nothing()
    {
        var (stations, length) = OverburdenProfile.Stations([Leg(0, 25.0, 25.0, midZ: 500, lengthM: 0)]);

        stations.ShouldBeEmpty();
        length.ShouldBe(0);
    }
}
