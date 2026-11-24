// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The flat frame a 3D scene is laid out in when it leaves the globe: where points fall, which way
/// a camera of the globe looks in it, and how a model drawn about its own point is stood in it.
/// </summary>
public class SceneTangentFrameTests
{
    /// <summary>An invented place, nowhere in particular.</summary>
    private const double Lon = 11.25;
    private const double Lat = 43.5;
    private const double Height = 700;

    /// <summary>Metres along a meridian for one degree of latitude, near enough for a test's distances.</summary>
    private const double MetresPerDegree = 111_132;

    [Fact]
    public void The_origin_is_zero_and_says_nothing_of_where_it_is()
    {
        var here = new SceneTangentFrame(Lon, Lat, Height);
        var elsewhere = new SceneTangentFrame(-70.5, -12.25, 3200);

        Close(here.PointAt(Lon, Lat, Height), new SceneVector(0, 0, 0), 1e-6);
        Close(elsewhere.PointAt(-70.5, -12.25, 3200), new SceneVector(0, 0, 0), 1e-6);

        // The same offsets from two different origins are the same numbers: a hundred metres up is
        // a hundred metres up wherever the frame stands.
        Close(here.PointAt(Lon, Lat, Height + 100), new SceneVector(0, 0, 100), 1e-6);
        Close(elsewhere.PointAt(-70.5, -12.25, 3300), new SceneVector(0, 0, 100), 1e-6);
    }

    [Fact]
    public void East_is_x_north_is_y_and_up_is_z()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);

        var north = frame.PointAt(Lon, Lat + 0.001, Height);
        north.Y.ShouldBe(0.001 * MetresPerDegree, 1.0);
        Math.Abs(north.X).ShouldBeLessThan(1e-6);

        var east = frame.PointAt(Lon + 0.001, Lat, Height);
        east.X.ShouldBe(0.001 * MetresPerDegree * Math.Cos(Lat * Math.PI / 180), 1.0);
        east.X.ShouldBeGreaterThan(0);
        Math.Abs(east.Y).ShouldBeLessThan(0.01);
    }

    [Fact]
    public void A_point_far_away_sinks_below_the_plane_by_the_curve_of_the_earth()
    {
        var frame = new SceneTangentFrame(Lon, Lat, 0);

        // Half a degree of latitude is about 55.6 km; the drop is the distance squared over twice
        // the radius, about 242 m — which a flat offset would leave out entirely.
        var far = frame.PointAt(Lon, Lat + 0.5, 0);
        var along = Math.Sqrt((far.X * far.X) + (far.Y * far.Y));
        along.ShouldBe(55_566, 30);
        far.Z.ShouldBe(-(along * along) / (2 * 6_371_000), 3);
    }

    [Fact]
    public void Relief_multiplies_heights_about_the_ellipsoid_and_leaves_the_ground_plan_alone()
    {
        var plain = new SceneTangentFrame(Lon, Lat, Height);
        var tall = new SceneTangentFrame(Lon, Lat, Height, relief: 2);

        // The origin itself is drawn at twice its height, and is still the zero of its frame.
        Close(tall.PointAt(Lon, Lat, Height), new SceneVector(0, 0, 0), 1e-6);

        // Thirty metres above it is drawn sixty above; a point beside it stays beside it.
        tall.PointAt(Lon, Lat, Height + 30).Z.ShouldBe(60, 1e-6);
        var aside = tall.PointAt(Lon + 0.001, Lat, Height);
        aside.X.ShouldBe(plain.PointAt(Lon + 0.001, Lat, Height).X, 0.05);
        Math.Abs(aside.Z).ShouldBeLessThan(0.01);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1)] // facing north, level: looks along +Y, top of the picture up
    [InlineData(90, 0, 1, 0, 0, 0, 0, 1)] // facing east
    [InlineData(180, 0, 0, -1, 0, 0, 0, 1)] // facing south
    [InlineData(0, -90, 0, 0, -1, 0, 1, 0)] // straight down, heading north: north at the top of the picture
    [InlineData(90, -90, 0, 0, -1, 1, 0, 0)] // straight down, heading east: east at the top
    [InlineData(270, -90, 0, 0, -1, -1, 0, 0)] // straight down, heading west
    public void A_camera_at_the_origin_looks_where_its_heading_and_tilt_say(
        double heading, double pitch, double fx, double fy, double fz, double ux, double uy, double uz)
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);

        var view = frame.ViewAt(Lon, Lat, Height, heading, pitch, rollDeg: 0);

        Close(view.Eye, new SceneVector(0, 0, 0), 1e-6);
        Close(view.Forward, new SceneVector(fx, fy, fz), 1e-9);
        Close(view.Up, new SceneVector(ux, uy, uz), 1e-9);
    }

    [Fact]
    public void A_tilted_camera_keeps_its_two_directions_at_right_angles_and_one_metre_long()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);

        var view = frame.ViewAt(Lon + 0.3, Lat - 0.2, 5000, headingDeg: 217, pitchDeg: -37, rollDeg: 12);

        view.Forward.Length.ShouldBe(1, 1e-12);
        view.Up.Length.ShouldBe(1, 1e-12);
        view.Forward.Dot(view.Up).ShouldBe(0, 1e-12);

        // Thirty-seven degrees below the horizon — its own horizon, which this far from the origin
        // is not quite the frame's.
        var down = Math.Asin(-view.Forward.Z) * 180 / Math.PI;
        down.ShouldBe(37, 0.5);
    }

    [Fact]
    public void A_positive_roll_lowers_the_right_hand_side_of_the_camera()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);

        // Facing north, level: the right-hand side is east. Rolled by thirty degrees, the top of
        // the picture leans east by the sine of thirty.
        var view = frame.ViewAt(Lon, Lat, Height, headingDeg: 0, pitchDeg: 0, rollDeg: 30);

        Close(view.Forward, new SceneVector(0, 1, 0), 1e-9);
        Close(view.Up, new SceneVector(0.5, 0, Math.Sqrt(3) / 2), 1e-9);
    }

    [Fact]
    public void A_camera_far_from_the_origin_keeps_its_own_vertical()
    {
        var frame = new SceneTangentFrame(Lon, Lat, 0);

        // Two degrees of latitude north, looking straight down: "down" there leans from the
        // frame's own by two degrees, towards the origin's side.
        var view = frame.ViewAt(Lon, Lat + 2, 10_000, headingDeg: 0, pitchDeg: -90, rollDeg: 0);

        var lean = Math.Acos(-view.Forward.Z) * 180 / Math.PI;
        lean.ShouldBe(2, 0.02);
        view.Forward.Y.ShouldBeLessThan(0);
        Math.Abs(view.Forward.X).ShouldBeLessThan(1e-9);
    }

    [Fact]
    public void A_camera_under_relief_stands_at_the_stretched_height_and_looks_the_same_way()
    {
        var plain = new SceneTangentFrame(Lon, Lat, 0);
        var tall = new SceneTangentFrame(Lon, Lat, 0, relief: 3);

        var one = plain.ViewAt(Lon, Lat, 400, headingDeg: 40, pitchDeg: -20, rollDeg: 0);
        var three = tall.ViewAt(Lon, Lat, 400, headingDeg: 40, pitchDeg: -20, rollDeg: 0);

        three.Eye.Z.ShouldBe(1200, 1e-6);
        one.Eye.Z.ShouldBe(400, 1e-6);
        Close(three.Forward, one.Forward, 1e-9);
        Close(three.Up, one.Up, 1e-9);
    }

    [Fact]
    public void A_model_at_the_origin_is_not_turned_and_one_far_away_is_turned_by_how_far()
    {
        var frame = new SceneTangentFrame(Lon, 0, 0);

        frame.PlacementAt(Lon, 0, 0).Degrees.ShouldBe(0);
        Close(frame.PlacementAt(Lon, 0, 0).Translation, new SceneVector(0, 0, 0), 1e-6);

        // On the equator, one degree of longitude east: that place's "up" leans east by one degree,
        // which is a turn about the north axis.
        var east = frame.PlacementAt(Lon + 1, 0, 0);
        east.Degrees.ShouldBe(1, 1e-9);
        Close(east.Axis, new SceneVector(0, 1, 0), 1e-9);

        // One degree of latitude north: "up" leans north, a turn about the axis pointing west.
        var north = frame.PlacementAt(Lon, 1, 0);
        north.Degrees.ShouldBe(1, 1e-9);
        Close(north.Axis, new SceneVector(-1, 0, 0), 1e-9);
    }

    [Fact]
    public void A_model_a_kilometre_east_is_turned_about_the_earths_axis_by_the_longitude_between()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);

        // Two places on one parallel differ by a turn of the whole Earth: about its axis, which
        // from here points north and up by the latitude, through the difference in longitude.
        var placed = frame.PlacementAt(Lon + 0.0125, Lat, Height + 40);

        placed.Degrees.ShouldBe(0.0125, 1e-9);
        var latitude = Lat * Math.PI / 180;
        Close(placed.Axis, new SceneVector(0, Math.Cos(latitude), Math.Sin(latitude)), 1e-6);
        Close(placed.Translation, frame.PointAt(Lon + 0.0125, Lat, Height + 40), 1e-9);
    }

    [Fact]
    public void A_model_stood_in_the_scene_keeps_its_corners_where_the_globe_has_them()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height);
        var (lon, lat, height) = (Lon + 0.21, Lat - 0.17, 950.0);
        var placed = frame.PlacementAt(lon, lat, height);

        // A corner of the model eighty metres east, sixty north and twenty-five up of its own zero:
        // turned and moved as the placement says, it lands where the globe puts that point.
        var there = new LocalTangentFrame(lon, lat, height);
        var (cornerLon, cornerLat, cornerHeight) = there.GeodeticAt(80, 60, 25);
        var onTheGlobe = frame.PointAt(cornerLon, cornerLat, cornerHeight);

        var turned = Turned(new SceneVector(80, 60, 25), placed.Axis, placed.Degrees);
        Close(placed.Translation + turned, onTheGlobe, 1e-6);
    }

    [Fact]
    public void A_frame_for_another_relief_is_about_the_same_place_drawn_at_that_relief()
    {
        var frame = new SceneTangentFrame(Lon, Lat, Height, 1.5);
        var taller = frame.WithRelief(3);

        taller.Relief.ShouldBe(3);
        frame.WithRelief(1.5).ShouldBeSameAs(frame);
        // Its own zero is still its zero, and a point 100 m above the origin is three times as far up.
        Close(taller.PointAt(Lon, Lat, Height), new SceneVector(0, 0, 0), 1e-6);
        Close(taller.PointAt(Lon, Lat, Height + 100), new SceneVector(0, 0, 300), 1e-6);
        Close(frame.PointAt(Lon, Lat, Height + 100), new SceneVector(0, 0, 150), 1e-6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(3)]
    public void A_model_on_the_origins_vertical_stretched_about_the_origins_level_stands_right_at_every_relief(double relief)
    {
        // Placed once, in the frame of a relief of 2; then drawn at another.
        var frame = new SceneTangentFrame(Lon, Lat, Height, 2);
        var placed = frame.PlacementAbout(Lon, Lat, Height + 260);

        // It stands 260 m above the origin's level, and that is all the placement says of heights.
        placed.LiftM.ShouldBe(260, 1e-9);
        Close(placed.Translation, new SceneVector(0, 0, 0), 1e-6);
        placed.Degrees.ShouldBe(0);

        // A corner 80 east, 60 north and 25 below the model's zero: lifted, stretched along the
        // model's up by the relief, turned and moved — where the globe draws it at that relief.
        var there = new LocalTangentFrame(Lon, Lat, Height + 260);
        var (cornerLon, cornerLat, cornerHeight) = there.GeodeticAt(80, 60, -25);
        var stretched = new SceneVector(80, 60, (-25 + placed.LiftM) * relief);
        var inPlan = placed.Translation + Turned(stretched, placed.Axis, placed.Degrees);
        // The corner's own vertical leans from the model's by the 100 m between them: a millimetre
        // or two over these heights, which is the model being stretched along one up.
        Close(inPlan, frame.WithRelief(relief).PointAt(cornerLon, cornerLat, cornerHeight), 0.05);
    }

    [Fact]
    public void A_model_away_from_the_origin_is_off_only_by_the_lean_between_the_two_verticals()
    {
        // Two kilometres north of an origin 700 m up, 150 m lower, in a clip whose relief runs
        // from 1 to 3: placed in the frame of the middle of that, 2.
        var frame = new SceneTangentFrame(Lon, Lat, Height, 2);
        var lat = Lat + (2000 / MetresPerDegree);
        var placed = frame.PlacementAbout(Lon, lat, Height - 150);

        // Level with the origin but for the drop of the Earth's curve between them, a third of a
        // metre at two kilometres, the model's zero is 150 m below its stretching point.
        placed.LiftM.ShouldBe(-150, 0.001);

        foreach (var relief in (double[])[1, 1.5, 2, 2.5, 3])
        {
            var zero = placed.Translation + Turned(new SceneVector(0, 0, placed.LiftM * relief), placed.Axis, placed.Degrees);
            var onTheGlobe = frame.WithRelief(relief).PointAt(Lon, lat, Height - 150);
            var off = (zero - onTheGlobe).Length;

            // Exact at the frame's own relief; elsewhere off by the change of relief times the
            // origin's height times the lean — 700 m × 2 km / 6,371 km = 0.22 m for a whole factor.
            off.ShouldBe(Math.Abs(relief - 2) * 700 * 2000 / 6_371_000, 0.002);
        }
    }

    /// <summary>A vector turned about an axis, right-handed: Rodrigues' formula, written out here and not borrowed.</summary>
    private static SceneVector Turned(SceneVector v, SceneVector axis, double degrees)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        return (v * cos) + (axis.Cross(v) * sin) + (axis * (axis.Dot(v) * (1 - cos)));
    }

    [Theory]
    [InlineData(60, 1920, 1080, 60)] // landscape: the lens is across the width already
    [InlineData(60, 1080, 1080, 60)] // square
    [InlineData(60, 1080, 1920, 35.983)] // portrait: the lens is down the height, the width sees less
    [InlineData(90, 864, 1080, 77.32)]
    public void A_lens_across_the_wider_side_is_turned_into_the_angle_across_the_width(
        double lens, int width, int height, double expected)
    {
        SceneTangentFrame.HorizontalFieldOfView(lens, width, height).ShouldBe(expected, 0.001);
    }

    [Fact]
    public void A_focal_length_covers_the_angle_on_the_sensor()
    {
        // Ninety degrees on a 36 mm sensor is an 18 mm lens.
        SceneTangentFrame.FocalLength(90, 36).ShouldBe(18, 1e-9);
        SceneTangentFrame.FocalLength(60, 36).ShouldBe(31.1769, 1e-4);
    }

    [Theory]
    [InlineData(true, 0, 1180, 0)] // relief under the survey: drawn where it was surveyed
    [InlineData(true, 42.5, 1180, 42.5)] // an elevation model on the ellipsoid: raised by its offset
    [InlineData(false, 42.5, 1180, -1180)] // no relief: hung from the surface by its highest point
    public void A_cave_is_drawn_where_it_was_surveyed_or_hung_from_the_surface(
        bool absolute, double offset, double top, double expected)
    {
        SceneAltitude.DrawnShift(absolute, offset, top).ShouldBe(expected);
    }

    private static void Close(SceneVector actual, SceneVector expected, double tolerance)
    {
        actual.X.ShouldBe(expected.X, tolerance);
        actual.Y.ShouldBe(expected.Y, tolerance);
        actual.Z.ShouldBe(expected.Z, tolerance);
    }
}
