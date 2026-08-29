// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class CenterlineSkeletonTests
{
    /// <summary>A single shot between two stations, with altitudes.</summary>
    private static LineString Shot(
        double x1, double y1, double z1, double x2, double y2, double z2) =>
        new([new CoordinateZ(x1, y1, z1), new CoordinateZ(x2, y2, z2)]) { SRID = 4326 };

    private static LineString Polyline(params (double X, double Y, double Z)[] points) =>
        new([.. points.Select(p => (Coordinate)new CoordinateZ(p.X, p.Y, p.Z))]) { SRID = 4326 };

    /// <summary>
    /// A traverse of <paramref name="legs"/> shots running east along 45°N, one shot per
    /// component the way survey exports write them. Station i sits at 25.0000 + i/10000.
    /// </summary>
    private static LineString[] Traverse(int legs) =>
        [.. Enumerable.Range(0, legs).Select(i =>
            Shot(Station(i), 45.0, 100 + i, Station(i + 1), 45.0, 101 + i))];

    private static double Station(int index) => 25.0 + 0.0001 * index;

    /// <summary>A fan of short loose shots off one station — what a splay bundle looks like.</summary>
    private static LineString[] Splays(double x, double y, double z, int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            Shot(x, y, z, x + 0.000005 * i, y + 0.000004 * i, z))];

    private static MultiLineString Lines(params LineString[] lines) => new(lines) { SRID = 4326 };

    private static IEnumerable<LineString> Components(MultiLineString geometry) =>
        Enumerable.Range(0, geometry.NumGeometries).Select(i => (LineString)geometry.GetGeometryN(i));

    [Fact]
    public void A_splay_fan_is_dropped_and_the_traverse_survives_as_one_polyline()
    {
        var traverse = Traverse(3);
        var source = Lines([.. traverse, .. Splays(Station(1), 45.0, 101, 8)]);

        var skeleton = CenterlineSkeleton.Build(source);

        skeleton.NumGeometries.ShouldBe(1);
        // The passage is sewn back into one polyline. Its first leg went with the fan: that leg
        // ends at the fan's station, so it is one of the loose shots there — the documented and
        // accepted loss at the tip of a dead end.
        Components(skeleton).Single().NumPoints.ShouldBe(3);
        skeleton.Length.ShouldBe(traverse[1].Length + traverse[2].Length, 1e-12);
    }

    [Fact]
    public void A_survey_without_splays_keeps_every_shot_and_only_sews_the_chain()
    {
        var traverse = Traverse(5);

        var skeleton = CenterlineSkeleton.Build(Lines(traverse));

        skeleton.NumGeometries.ShouldBe(1);
        skeleton.NumPoints.ShouldBe(6);
        skeleton.Length.ShouldBe(Lines(traverse).Length, 1e-12);
    }

    [Fact]
    public void Two_polylines_meeting_at_a_junction_are_both_kept()
    {
        // The shape an uploaded GeoJSON/GPX centerline usually has: whole passages as polylines
        // that share a station. A rule tested on whole components instead of single shots
        // deleted all of it.
        var west = Polyline((25.000, 45.000, 100), (25.001, 45.000, 101), (25.002, 45.000, 102));
        var north = Polyline((25.002, 45.000, 102), (25.002, 45.001, 103), (25.002, 45.002, 104));

        var skeleton = CenterlineSkeleton.Build(Lines(west, north));

        skeleton.Length.ShouldBe(Lines(west, north).Length, 1e-12);
        skeleton.NumGeometries.ShouldBe(1); // sewn through the junction
        skeleton.NumPoints.ShouldBe(5);
    }

    [Fact]
    public void A_single_polyline_is_never_erased()
    {
        // Every shot here is loose at one end and they all hang off the same middle station, so
        // the fan test alone would erase the whole centerline. The guard keeps it.
        var only = Polyline((25.000, 45.000, 100), (25.001, 45.000, 101), (25.002, 45.001, 102));

        var skeleton = CenterlineSkeleton.Build(Lines(only));

        skeleton.NumGeometries.ShouldBe(1);
        skeleton.NumPoints.ShouldBe(3);
        skeleton.Length.ShouldBe(only.Length, 1e-12);
    }

    [Fact]
    public void Splays_hanging_off_an_interior_vertex_of_a_polyline_are_found()
    {
        // Exploding into single shots is what makes this work: the station carrying the fan is an
        // interior vertex of the passage, invisible to a rule that only looks at component ends.
        var passage = Polyline(
            (Station(0), 45.0, 100), (Station(1), 45.0, 101), (Station(2), 45.0, 102), (Station(3), 45.0, 103));
        var source = Lines([passage, .. Splays(Station(1), 45.0, 101, 6)]);

        var skeleton = CenterlineSkeleton.Build(source);

        skeleton.NumGeometries.ShouldBe(1);
        skeleton.NumPoints.ShouldBe(3); // the passage from the fan's station onward
        skeleton.Length.ShouldBeLessThan(passage.Length);
        skeleton.Length.ShouldBeGreaterThan(passage.Length * 0.6);
    }

    [Fact]
    public void A_lone_dead_end_shot_is_kept_but_a_bunch_at_the_same_station_is_not()
    {
        var traverse = Traverse(3); // the last shot ends at a station nothing else touches

        var withLoneTip = CenterlineSkeleton.Build(Lines(traverse));
        withLoneTip.NumPoints.ShouldBe(4);
        withLoneTip.Length.ShouldBe(Lines(traverse).Length, 1e-12);

        // A second loose shot at that same station makes the pair read as a fan.
        var source = Lines([.. traverse, .. Splays(Station(2), 45.0, 102, 1)]);
        var withFan = CenterlineSkeleton.Build(source);

        withFan.NumPoints.ShouldBe(3);
        withFan.Length.ShouldBe(traverse[0].Length + traverse[1].Length, 1e-12);
    }

    [Fact]
    public void Stations_sharing_a_plan_position_at_different_altitudes_stay_distinct()
    {
        // A deep cave stacks passages over each other: ignoring altitude would fuse these two
        // shots into one polyline and invent a junction that is not there.
        var upper = Shot(25.0000, 45.0000, 500, 25.0010, 45.0000, 500);
        var lower = Shot(25.0010, 45.0000, 100, 25.0020, 45.0000, 100);

        var skeleton = CenterlineSkeleton.Build(Lines(upper, lower));

        skeleton.NumGeometries.ShouldBe(2);
        skeleton.Length.ShouldBe(Lines(upper, lower).Length, 1e-12);
    }

    [Fact]
    public void Zero_length_components_are_dropped_without_disturbing_the_rest()
    {
        // Real exports contain these (a station written twice). They made the upload fail
        // outright, and left in place they would give a splay a second shot at its station and
        // so disguise it as traverse.
        var traverse = Traverse(3);
        var degenerate = Shot(Station(1), 45.0, 101, Station(1), 45.0, 101);
        var source = Lines([degenerate, .. traverse, .. Splays(Station(1), 45.0, 101, 4)]);

        var skeleton = CenterlineSkeleton.Build(source);

        CenterlineSkeleton.PathCount(source).ShouldBe(8);
        skeleton.NumGeometries.ShouldBe(1);
        skeleton.NumPoints.ShouldBe(3);
        skeleton.Length.ShouldBe(traverse[1].Length + traverse[2].Length, 1e-12);
    }

    [Fact]
    public void A_closed_loop_is_kept_as_a_single_ring()
    {
        var loop = new[]
        {
            Shot(25.000, 45.000, 100, 25.001, 45.000, 100),
            Shot(25.001, 45.000, 100, 25.001, 45.001, 100),
            Shot(25.001, 45.001, 100, 25.000, 45.001, 100),
            Shot(25.000, 45.001, 100, 25.000, 45.000, 100),
        };

        var skeleton = CenterlineSkeleton.Build(Lines(loop));

        skeleton.NumGeometries.ShouldBe(1);
        Components(skeleton).Single().IsClosed.ShouldBeTrue();
        skeleton.Length.ShouldBe(Lines(loop).Length, 1e-12);
    }

    [Fact]
    public void A_second_pass_is_a_no_op_only_where_every_dead_end_is_already_a_lone_tip()
    {
        // Narrow claim on purpose. This holds for a plain traverse, but NOT in general: on a real
        // survey a second pass keeps eating whatever the first pass exposed (measured: 880 paths
        // down to 854 on a 15.7 km cave). That is why the skeleton is built once, from the stored
        // survey geometry, and never rebuilt from a skeleton.
        var once = CenterlineSkeleton.Build(Lines(Traverse(4)));
        var twice = CenterlineSkeleton.Build(once);

        twice.NumGeometries.ShouldBe(once.NumGeometries);
        twice.NumPoints.ShouldBe(once.NumPoints);
        twice.Length.ShouldBe(once.Length, 1e-12);
    }

    [Fact]
    public void The_skeleton_is_flattened_to_two_dimensions()
    {
        var skeleton = CenterlineSkeleton.Build(Lines(Traverse(3)));

        skeleton.Coordinates.ShouldAllBe(c => double.IsNaN(c.Z));
        skeleton.SRID.ShouldBe(4326);
    }

    [Fact]
    public void Empty_input_yields_an_empty_skeleton()
    {
        var skeleton = CenterlineSkeleton.Build(new MultiLineString([]) { SRID = 4326 });

        skeleton.IsEmpty.ShouldBeTrue();
        CenterlineSkeleton.PathCount(skeleton).ShouldBe(0);
        CenterlineSkeleton.PathCount(null).ShouldBe(0);
    }

    [Fact]
    public void IsWorthStoring_only_when_the_rule_actually_reduced_something()
    {
        var source = Lines([.. Traverse(3), .. Splays(Station(1), 45.0, 101, 4)]);
        var skeleton = CenterlineSkeleton.Build(source);

        CenterlineSkeleton.PathCount(source).ShouldBe(7);
        CenterlineSkeleton.PathCount(skeleton).ShouldBe(1);
        CenterlineSkeleton.IsWorthStoring(source, skeleton).ShouldBeTrue();

        // One polyline with nothing to prune and nothing to sew is already its own skeleton;
        // storing a second copy of it buys nothing.
        var plain = Lines(Polyline((25.0, 45.0, 100), (25.001, 45.0, 101)));
        CenterlineSkeleton.IsWorthStoring(plain, CenterlineSkeleton.Build(plain)).ShouldBeFalse();
    }

    [Fact]
    public void The_altitude_keeping_skeleton_reduces_identically_and_keeps_the_altitudes()
    {
        var source = Lines([.. Traverse(4), .. Splays(Station(2), 45.0, 102, 5)]);

        var flat = CenterlineSkeleton.Build(source);
        var withAltitude = CenterlineSkeleton.Build3D(source);

        // The same reduction: the two must not disagree about which shots are passage, or a
        // measurement taken over one would describe a different cave from the map drawn off
        // the other.
        withAltitude.NumGeometries.ShouldBe(flat.NumGeometries);
        withAltitude.NumPoints.ShouldBe(flat.NumPoints);
        for (var i = 0; i < flat.NumPoints; i++)
        {
            withAltitude.Coordinates[i].X.ShouldBe(flat.Coordinates[i].X);
            withAltitude.Coordinates[i].Y.ShouldBe(flat.Coordinates[i].Y);
        }

        flat.Coordinates.ShouldAllBe(c => double.IsNaN(c.Z));
        withAltitude.Coordinates.ShouldAllBe(c => !double.IsNaN(c.Z));

        // The traverse climbs one metre per station from 100, and the retained network is the
        // whole of it, so its ends say so.
        withAltitude.Coordinates.Min(c => c.Z).ShouldBe(100);
        withAltitude.Coordinates.Max(c => c.Z).ShouldBe(104);
        withAltitude.SRID.ShouldBe(4326);
    }

    [Fact]
    public void Sewing_a_survey_whose_splays_are_already_known_prunes_nothing()
    {
        // What an extraction hands over: the legs the file itself says are passage, and no
        // others. Sewing only joins them up.
        var traverse = Lines(Traverse(4));

        var sewn = CenterlineSkeleton.Sew(traverse);

        sewn.NumGeometries.ShouldBe(1);
        sewn.NumPoints.ShouldBe(5);

        // Flat, like the guessed skeleton: this is the same overlay geometry and the same column
        // holds it. A survey's altitudes are kept on its shot rows, not here.
        sewn.Coordinates.ShouldAllBe(c => double.IsNaN(c.Z));

        // And the difference that matters: the guess, shown the same four legs with a splay fan
        // at the second-to-last station, cannot tell the final leg from one more shot in the fan
        // and takes it too. Sewing keeps every leg it was handed, so the two answers differ.
        var withFan = Lines([.. Traverse(4), .. Splays(Station(3), 45.0, 103, 5)]);
        CenterlineSkeleton.Build(withFan).NumPoints.ShouldBe(4);
        CenterlineSkeleton.Sew(traverse).NumPoints.ShouldBe(5);
    }
}
