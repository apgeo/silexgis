// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The equal-area net and Kamb's density against values worked by hand from the published
/// formulas: the Schmidt net places a line at √2·sin((90° − plunge)/2) of the radius from the
/// centre (so a 45° plunge sits at 0.5412, Lisle and Leyshon's worked net), and Kamb's counting
/// circle covers 9/(N + 9) of the hemisphere (Kamb 1959 — 8.26 % for a hundred poles).
/// </summary>
public class StereonetTests
{
    [Theory]
    [InlineData(0d, 0d, 0d, 1d)]          // horizontal, trending north: on the rim, at the top
    [InlineData(90d, 0d, 1d, 0d)]         // horizontal, trending east: on the rim, at the right
    [InlineData(123d, 90d, 0d, 0d)]       // vertical: the centre, whatever its trend
    [InlineData(180d, 45d, 0d, -0.541196)] // 45° plunge: √2·sin(22.5°) = 0.541196, due south
    [InlineData(270d, 30d, -0.707107, 0d)] // 30° plunge: √2·sin(30°) = 0.707107, due west
    public void A_line_lands_where_the_equal_area_net_puts_it(double trend, double plunge, double x, double y)
    {
        var point = Stereonet.Project(new LineOrientation(trend, plunge));

        point.X.ShouldBe(x, 1e-6);
        point.Y.ShouldBe(y, 1e-6);
    }

    [Fact]
    public void The_pole_of_a_plane_striking_030_and_dipping_40_plots_north_west_of_the_centre()
    {
        // Under the right-hand rule the plane dips towards 120, its pole plunges 50 towards 300,
        // and the equal-area radius of a 50° plunge is √2·sin(20°) = 0.483690.
        var pole = StructuralMeasurement.Pole(new PlaneOrientation(120d, 40d));
        var point = Stereonet.Project(pole);

        var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        radius.ShouldBe(0.483690, 1e-6);
        point.X.ShouldBeLessThan(0d);
        point.Y.ShouldBeGreaterThan(0d);
        (Math.Atan2(point.X, point.Y) * 180d / Math.PI + 360d).ShouldBe(300d, 1e-9);
    }

    [Theory]
    [InlineData(10d, 0d)]
    [InlineData(200d, 12.5d)]
    [InlineData(359d, 63d)]
    [InlineData(75d, 89d)]
    public void Unprojecting_a_projected_line_gives_the_line_back(double trend, double plunge)
    {
        var back = Stereonet.Unproject(Stereonet.Project(new LineOrientation(trend, plunge)));

        back.TrendDegrees.ShouldBe(trend, 1e-9);
        back.PlungeDegrees.ShouldBe(plunge, 1e-9);
    }

    [Fact]
    public void A_point_outside_the_net_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Stereonet.Unproject(new StereonetPoint(0.9, 0.9)));
        Should.Throw<ArgumentOutOfRangeException>(() => Stereonet.Project(new LineOrientation(0d, 91d)));
    }

    [Theory]
    [InlineData(9, 0.5)]
    [InlineData(100, 0.082569)]
    [InlineData(50, 0.152542)]
    public void Kambs_counting_circle_shrinks_as_the_readings_grow(int count, double share)
    {
        Stereonet.KambCountingAreaFraction(count).ShouldBe(share, 1e-6);
    }

    [Fact]
    public void Nine_identical_poles_read_six_standard_deviations_at_their_node()
    {
        // N = 9: the counting circle covers half the hemisphere, the standard deviation of the
        // count is sqrt(9 · 0.5 · 0.5) = 1.5, and a node with all nine poles within reach reads
        // 9 / 1.5 = 6.
        var poles = Enumerable.Repeat(new LineOrientation(0d, 90d), 9).ToList();

        var density = Stereonet.KambDensity(poles, 5)!;

        density.CountingAreaFraction.ShouldBe(0.5, 1e-12);
        var centre = density.Values[(2 * 5) + 2];
        centre!.Value.ShouldBe(6d, 1e-9);
        density.MaxSigma.ShouldBe(6d, 1e-9);
        density.ExpectedSigma.ShouldBe(3d);
    }

    [Fact]
    public void A_uniform_scatter_reads_about_three_standard_deviations_everywhere()
    {
        // Kamb's construction sets the expected count at three standard deviations: poles spread
        // evenly over the hemisphere must average close to three across the nodes.
        var poles = new List<LineOrientation>();
        var golden = Math.PI * (3d - Math.Sqrt(5d));
        const int n = 2000;
        for (var i = 0; i < n; i++)
        {
            var z = (i + 0.5) / n; // even in the sine of the plunge: even over the hemisphere
            var plunge = Math.Asin(z) * 180d / Math.PI;
            var trend = (i * golden * 180d / Math.PI) % 360d;
            poles.Add(new LineOrientation(trend, plunge));
        }

        var density = Stereonet.KambDensity(poles, 21)!;
        var inside = density.Values.Where(v => v is not null).Select(v => v!.Value).ToList();

        inside.Average().ShouldBe(3d, 0.15);
    }

    [Fact]
    public void A_pole_near_the_rim_counts_towards_the_opposite_rim_as_well()
    {
        // A horizontal axis trending north is the same axis as one trending south: both rim nodes
        // must count it.
        var poles = Enumerable.Repeat(new LineOrientation(0d, 0d), 9).ToList();

        var density = Stereonet.KambDensity(poles, 5)!;

        density.Values[2]!.Value.ShouldBe(6d, 1e-9);            // top centre: north rim
        density.Values[(4 * 5) + 2]!.Value.ShouldBe(6d, 1e-9);  // bottom centre: south rim
        density.Values[0].ShouldBeNull();                       // a corner is off the net
    }

    [Fact]
    public void No_poles_give_no_density()
    {
        Stereonet.KambDensity([]).ShouldBeNull();
    }
}
