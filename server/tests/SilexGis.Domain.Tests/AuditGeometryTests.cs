// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class AuditGeometryTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void A_small_geometry_is_recorded_in_full()
    {
        var point = Factory.CreatePoint(new CoordinateZ(25.44721, 45.53127, 812));

        AuditGeometry.Record(point).ShouldBe(point.AsText());
    }

    [Fact]
    public void A_large_geometry_is_recorded_as_a_summary_without_any_position()
    {
        var line = Survey(points: 2000, z: 700);

        var recorded = AuditGeometry.Record(line).ShouldBeOfType<Dictionary<string, object?>>();

        recorded.Keys.Order(StringComparer.Ordinal).ShouldBe(["geometryType", "omitted", "points", "sha256"]);
        recorded["geometryType"].ShouldBe("LineString");
        recorded["points"].ShouldBe(2000);
        recorded["omitted"].ShouldBe(true);
        recorded["sha256"].ShouldBeOfType<string>().Length.ShouldBe(64);

        // Not a digit of a coordinate anywhere in it: the summary must not carry a position.
        var text = string.Join(' ', recorded.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
        text.ShouldNotContain("25.4");
        text.ShouldNotContain("45.5");
    }

    [Fact]
    public void The_limit_is_on_the_text_and_a_geometry_just_under_it_stays_in_full()
    {
        // Short coordinates, so many points fit under the limit: the point count alone must not
        // decide, or a modest outline written with few digits would lose its history.
        var coordinates = Enumerable.Range(0, 400).Select(i => new Coordinate(i % 10, i / 10)).ToArray();
        var line = Factory.CreateLineString(coordinates);
        line.AsText().Length.ShouldBeLessThanOrEqualTo(AuditGeometry.TextLimit);

        AuditGeometry.Record(line).ShouldBeOfType<string>();
    }

    [Fact]
    public void The_fingerprint_tells_versions_apart_and_matches_an_identical_one()
    {
        var first = AuditGeometry.Summary(Survey(points: 2000, z: 700));
        var same = AuditGeometry.Summary(Survey(points: 2000, z: 700));
        var deeper = AuditGeometry.Summary(Survey(points: 2000, z: 690));

        same["sha256"].ShouldBe(first["sha256"]);
        // Only the altitudes differ: the fingerprint covers Z.
        deeper["sha256"].ShouldNotBe(first["sha256"]);
    }

    private static LineString Survey(int points, double z) => Factory.CreateLineString(
        [.. Enumerable.Range(0, points).Select(i => new CoordinateZ(25.4 + (i * 0.00001), 45.5 + (i * 0.000007), z - (i * 0.01)))]);
}
