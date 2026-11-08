// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The scanner's satellite log and trajectory read, and a first placement taken from them. Every
/// fix and pose here is invented: a walk laid out under a known placement and read back.
/// </summary>
public class MandeyeGnssTests
{
    private static readonly (double X, double Y, double Z) Zero = (100, 200, 5);

    [Fact]
    public void A_logged_line_is_read_field_by_field_and_a_line_without_a_fix_keeps_no_position()
    {
        var fixes = MandeyeGnss.Parse(
            "1000000000 45.5 25.25 812.5 1.2 9 37.1 nan 10:00:00 1 1700000000000\n"
            + "2000000000 nan nan nan 99.99 0 nan nan 10:00:01 0 1700000001000\n"
            + "not a fix\n");

        fixes.Count.ShouldBe(2);
        fixes[0].ShouldBe(new GnssFix(1_000_000_000, 45.5, 25.25, 812.5, 1.2, 9, 1));
        MandeyeGnss.IsGood(fixes[0]).ShouldBeTrue();
        double.IsNaN(fixes[1].Latitude).ShouldBeTrue();
        MandeyeGnss.IsGood(fixes[1]).ShouldBeFalse();
    }

    [Fact]
    public void A_fix_with_too_few_satellites_or_a_wide_dilution_is_not_trusted()
    {
        MandeyeGnss.IsGood(new GnssFix(1, 45, 25, 800, 1, MandeyeGnss.MinimumSatellites, 1)).ShouldBeTrue();
        MandeyeGnss.IsGood(new GnssFix(1, 45, 25, 800, 1, MandeyeGnss.MinimumSatellites - 1, 1)).ShouldBeFalse();
        MandeyeGnss.IsGood(new GnssFix(1, 45, 25, 800, MandeyeGnss.MaximumHdop + 0.1, 9, 1)).ShouldBeFalse();
        MandeyeGnss.IsGood(new GnssFix(1, 45, 25, 800, 1, 9, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Both_trajectory_layouts_are_read_and_a_pose_between_two_is_interpolated()
    {
        var exported = MandeyeGnss.ParseTrajectory("2000,2,10,20,30,0,0,0,1\n1000,1,0,0,0,0,0,0,1\n");
        exported.Count.ShouldBe(2);
        exported[0].TimeNs.ShouldBe(1000);
        MandeyeGnss.At(exported, 1500).ShouldBe((5.0, 10.0, 15.0));
        MandeyeGnss.At(exported, 2500).ShouldBeNull();

        var odometry = MandeyeGnss.ParseTrajectory(
            "timestamp_nanoseconds pose00 pose01 pose02 pose03 pose10 pose11 pose12 pose13 pose20 pose21 pose22 pose23\n"
            + "1000 1 0 0 7 0 1 0 8 0 0 1 9\n");
        odometry.ShouldHaveSingleItem().ShouldBe(new TrajectoryPoint(1000, 7, 8, 9));
    }

    [Fact]
    public void A_walk_on_the_surface_gives_its_heading_and_puts_the_last_fix_s_pose_on_that_fix()
    {
        var truth = new ScanPlacement(25.3, 45.6, 900, 30, ScanPlacementSource.Manual);
        var (trajectory, fixes) = Walk(truth, metres: 40);

        var placed = MandeyeGnss.Place(fixes, trajectory, Zero).ShouldNotBeNull();

        placed.Source.ShouldBe(ScanPlacementSource.Gnss);
        placed.HeadingDeg.ShouldBe(30, tolerance: 0.01);
        placed.PitchDeg.ShouldBe(0);
        placed.RollDeg.ShouldBe(0);
        var last = trajectory[^1];
        var (lon, lat, h) = placed.PositionOf(last.X - Zero.X, last.Y - Zero.Y, last.Z - Zero.Z);
        lon.ShouldBe(fixes[^1].Longitude, tolerance: 1e-8);
        lat.ShouldBe(fixes[^1].Latitude, tolerance: 1e-8);
        h.ShouldBe(fixes[^1].AltitudeM, tolerance: 1e-3);
    }

    [Fact]
    public void A_scanner_that_stood_still_or_logged_no_good_fix_is_not_placed()
    {
        var truth = new ScanPlacement(25.3, 45.6, 900, 30, ScanPlacementSource.Manual);
        var (shortTrajectory, shortFixes) = Walk(truth, metres: MandeyeGnss.MinimumTrackM / 2);
        MandeyeGnss.Place(shortFixes, shortTrajectory, Zero).ShouldBeNull();

        var (trajectory, fixes) = Walk(truth, metres: 40);
        var blind = fixes.Select(f => f with { Quality = 0 }).ToList();
        MandeyeGnss.Place(blind, trajectory, Zero).ShouldBeNull();
        MandeyeGnss.Place(fixes, trajectory, Zero).ShouldNotBeNull();
    }

    [Fact]
    public void A_fix_outside_the_trajectory_s_time_is_not_used()
    {
        var truth = new ScanPlacement(25.3, 45.6, 900, 30, ScanPlacementSource.Manual);
        var (trajectory, fixes) = Walk(truth, metres: 40);
        var later = fixes.Select(f => f with { TimeNs = f.TimeNs + 1_000_000_000_000 }).ToList();
        MandeyeGnss.Place(later, trajectory, Zero).ShouldBeNull();
    }

    /// <summary>A straight walk along the frame's +X with a little climb, one pose and one fix a second.</summary>
    private static (List<TrajectoryPoint> Trajectory, List<GnssFix> Fixes) Walk(ScanPlacement truth, double metres)
    {
        var trajectory = new List<TrajectoryPoint>();
        var fixes = new List<GnssFix>();
        var steps = 20;
        for (var i = 0; i <= steps; i++)
        {
            var x = Zero.X + (metres * i / steps);
            var y = Zero.Y + (0.1 * i);
            var z = Zero.Z + (0.05 * i);
            var time = (long)(i * 1e9) + 5_000_000_000L;
            trajectory.Add(new TrajectoryPoint(time, x, y, z));
            var (lon, lat, h) = truth.PositionOf(x - Zero.X, y - Zero.Y, z - Zero.Z);
            fixes.Add(MandeyeGnss.Parse(string.Create(
                CultureInfo.InvariantCulture,
                $"{time} {lat:R} {lon:R} {h:R} 1.1 10 37 nan 10:00:00 1 0"))[0]);
        }

        return (trajectory, fixes);
    }
}
