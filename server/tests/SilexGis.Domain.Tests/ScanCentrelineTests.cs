// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A scan's trajectory simplified into a centreline, written as GeoJSON and read back, and moved
/// into the tiles' frame and onto the earth. Every pose here is invented.
/// </summary>
public class ScanCentrelineTests
{
    private static CentrelinePoint P(double x, double y, double z = 0) => new(x, y, z);

    [Fact]
    public void A_straight_walk_with_sway_under_the_tolerance_keeps_only_its_ends()
    {
        var walk = Enumerable.Range(0, 101).Select(i => P(i * 0.5, (i % 2 == 0 ? 0.05 : -0.05), (i % 3) * 0.03)).ToList();

        var line = ScanCentreline.Simplify(walk, 0.2);

        line.ShouldBe([walk[0], walk[^1]]);
    }

    [Fact]
    public void A_corner_further_than_the_tolerance_is_kept_and_one_inside_it_is_dropped()
    {
        // An L: ten metres east, then ten metres north. The corner stands 7 m off the chord.
        var corner = Enumerable.Range(0, 11).Select(i => P(i, 0)).Concat(Enumerable.Range(1, 10).Select(i => P(10, i))).ToList();
        ScanCentreline.Simplify(corner, 0.2).ShouldBe([P(0, 0), P(10, 0), P(10, 10)]);

        // A bump of 0.15 m is within a 0.2 m tolerance and gone; at 0.1 m it is kept.
        var bump = new List<CentrelinePoint> { P(0, 0), P(5, 0.15), P(10, 0) };
        ScanCentreline.Simplify(bump, 0.2).Count.ShouldBe(2);
        ScanCentreline.Simplify(bump, 0.1).Count.ShouldBe(3);
    }

    [Fact]
    public void A_rise_is_measured_in_three_dimensions()
    {
        // Flat on the plan, but a pitch of 3 m in the middle: a profile would lose it if the
        // simplification looked only from above.
        var pitch = new List<CentrelinePoint> { P(0, 0, 0), P(5, 0, -3), P(10, 0, 0) };
        ScanCentreline.Simplify(pitch, 0.2).Count.ShouldBe(3);
    }

    [Fact]
    public void A_walk_that_returns_to_where_it_started_keeps_its_far_end()
    {
        // In and out of a dead end: the chord from the first pose to the last has no length.
        var back = Enumerable.Range(0, 21).Select(i => P(i <= 10 ? i : 20 - i, 0)).ToList();
        ScanCentreline.Simplify(back, 0.2).ShouldBe([P(0, 0), P(10, 0), P(0, 0)]);
    }

    [Fact]
    public void A_long_winding_walk_is_simplified_without_running_out_of_stack()
    {
        // A spiral of 200,000 poses whose every turn is wider than the tolerance.
        var spiral = Enumerable.Range(0, 200_000)
            .Select(i => P(Math.Cos(i * 0.01) * (5 + (i * 0.001)), Math.Sin(i * 0.01) * (5 + (i * 0.001)), i * 0.0001))
            .ToList();

        var line = ScanCentreline.Simplify(spiral, 0.2);

        line.Count.ShouldBeGreaterThan(100);
        line.Count.ShouldBeLessThan(spiral.Count / 5);
        line[0].ShouldBe(spiral[0]);
        line[^1].ShouldBe(spiral[^1]);
    }

    [Fact]
    public void Fewer_than_three_poses_are_kept_as_they_are_and_a_negative_tolerance_is_refused()
    {
        ScanCentreline.Simplify([], 0.2).ShouldBeEmpty();
        ScanCentreline.Simplify([P(1, 2, 3)], 0.2).ShouldBe([P(1, 2, 3)]);
        Should.Throw<ArgumentOutOfRangeException>(() => ScanCentreline.Simplify([P(0, 0)], -1));
        Should.Throw<ArgumentOutOfRangeException>(() => ScanCentreline.Simplify([P(0, 0)], double.NaN));
    }

    [Fact]
    public void The_line_is_written_as_geojson_in_the_local_frame_and_read_back()
    {
        var line = new List<CentrelinePoint> { P(0, 0, 0), P(3, 4, 0), P(3, 4, -12.3456) };

        var json = ScanCentreline.ToGeoJson(line, 0.2, sourcePoseCount: 1234);
        var text = Encoding.UTF8.GetString(json);

        text.ShouldContain("\"frame\":\"scan-local\"");
        text.ShouldContain("\"sourcePoses\":1234");
        text.ShouldContain("\"lengthM\":17.346");
        var read = ScanCentreline.ParseGeoJson(json).ShouldNotBeNull();
        read.ShouldBe([P(0, 0, 0), P(3, 4, 0), P(3, 4, -12.346)]);
        ScanCentreline.LengthM(read).ShouldBe(5 + 12.346, 1e-9);
    }

    [Fact]
    public void Text_that_is_not_a_centreline_reads_as_none()
    {
        ScanCentreline.ParseGeoJson("not json"u8).ShouldBeNull();
        ScanCentreline.ParseGeoJson("""{"type":"Feature","geometry":{"type":"Point","coordinates":[1,2,3]}}"""u8).ShouldBeNull();
        ScanCentreline.ParseGeoJson("""{"type":"Feature","geometry":{"type":"LineString","coordinates":[[1,2]]}}"""u8).ShouldBeNull();
    }

    [Fact]
    public void The_line_is_moved_into_the_tiles_frame_and_set_down_through_the_placement()
    {
        var line = new List<CentrelinePoint> { P(100.5, 200.5, 5.5), P(100.5, 210.5, 5.5) };
        var zero = (100.5, 200.5, 5.5);

        ScanCentreline.InTilesFrame(line, zero).ShouldBe([P(0, 0, 0), P(0, 10, 0)]);

        // The frame's +Y turned to face east: ten metres along it is ten metres east of the anchor.
        var placement = ScanPlacement.Manual(25.0, 45.5, 800, 90);
        var placed = ScanCentreline.Placed(line, zero, placement);
        placed[0].X.ShouldBe(25.0, 1e-9);
        placed[0].Y.ShouldBe(45.5, 1e-9);
        placed[0].Z.ShouldBe(800, 1e-6);
        var east = (placed[1].X - 25.0) * 111_320 * Math.Cos(45.5 * Math.PI / 180);
        var north = (placed[1].Y - 45.5) * 111_320;
        east.ShouldBe(10, 0.1);
        Math.Abs(north).ShouldBeLessThan(0.1);
    }

    [Fact]
    public void A_trajectory_without_a_clock_keeps_its_poses_in_the_order_they_were_written()
    {
        // Every pose at time zero: the order in the file is the only order there is.
        var text = string.Concat(Enumerable.Range(0, 50).Select(i => $"0,0,{i},0,0,0,0,0,1\n"));

        var poses = MandeyeGnss.ParseTrajectory(text);

        poses.Select(p => p.X).ShouldBe(Enumerable.Range(0, 50).Select(i => (double)i));
    }
}
