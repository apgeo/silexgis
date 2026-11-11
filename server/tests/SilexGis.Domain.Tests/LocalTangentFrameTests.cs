// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The east-north-up frame on the WGS 84 ellipsoid: the ellipsoid's own figures at the equator
/// and the pole, a round trip to and from earth-centred coordinates, and a frame whose axes point
/// where their names say.
/// </summary>
public class LocalTangentFrameTests
{
    [Fact]
    public void The_equator_and_the_pole_sit_at_the_ellipsoid_s_axes()
    {
        var equator = LocalTangentFrame.ToEcef(0, 0, 0);
        equator.X.ShouldBe(6_378_137.0, tolerance: 1e-6);
        equator.Y.ShouldBe(0, tolerance: 1e-6);
        equator.Z.ShouldBe(0, tolerance: 1e-6);

        LocalTangentFrame.ToEcef(0, 90, 0).Z.ShouldBe(6_356_752.314245, tolerance: 1e-5);
    }

    [Theory]
    [InlineData(22.7351, 46.5432, 812.4)]
    [InlineData(-71.2, -33.9, -120)]
    [InlineData(179.99, 0.01, 4000)]
    [InlineData(10, 89.999, 30)]
    public void A_position_survives_a_round_trip(double lon, double lat, double h)
    {
        var (backLon, backLat, backH) = LocalTangentFrame.ToGeodetic(LocalTangentFrame.ToEcef(lon, lat, h));

        backLon.ShouldBe(lon, tolerance: 1e-10);
        backLat.ShouldBe(lat, tolerance: 1e-10);
        backH.ShouldBe(h, tolerance: 1e-6);
    }

    [Fact]
    public void The_frame_s_axes_point_east_north_and_up()
    {
        var frame = new LocalTangentFrame(22.7351, 46.5432, 812.4);

        frame.ToLocal(22.7351, 46.5432, 812.4).East.ShouldBe(0, tolerance: 1e-6);

        var north = frame.ToLocal(22.7351, 46.5442, 812.4);
        north.North.ShouldBeGreaterThan(110);
        Math.Abs(north.East).ShouldBeLessThan(1e-6);

        var east = frame.ToLocal(22.7361, 46.5432, 812.4);
        east.East.ShouldBeGreaterThan(70);
        Math.Abs(east.North).ShouldBeLessThan(0.01);

        frame.ToLocal(22.7351, 46.5432, 822.4).Up.ShouldBe(10, tolerance: 1e-6);

        var (e, n, u) = frame.ToLocal(frame.EcefAt(3, -4, 5));
        e.ShouldBe(3, tolerance: 1e-9);
        n.ShouldBe(-4, tolerance: 1e-9);
        u.ShouldBe(5, tolerance: 1e-9);
    }
}
