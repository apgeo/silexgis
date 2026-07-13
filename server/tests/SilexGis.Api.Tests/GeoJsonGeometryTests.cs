// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

public sealed class GeoJsonGeometryTests
{
    private static GeoJsonGeometry Geo(string type, string coordinatesJson) =>
        new(type, JsonSerializer.Deserialize<JsonElement>(coordinatesJson));

    [Fact]
    public void Point_round_trips()
    {
        var geom = Geo("Point", "[25.5, 45.25]").ToGeometryOrNull();
        var point = geom.ShouldBeOfType<Point>();
        point.X.ShouldBe(25.5);
        point.Y.ShouldBe(45.25);
        point.SRID.ShouldBe(4326);

        var back = GeoJsonGeometry.From(point);
        back.Type.ShouldBe("Point");
        back.Coordinates.GetRawText().ShouldBe("[25.5,45.25]");
    }

    [Fact]
    public void LineString_round_trips()
    {
        var geom = Geo("LineString", "[[25.0,45.0],[25.1,45.1],[25.2,45.15]]").ToGeometryOrNull();
        var line = geom.ShouldBeOfType<LineString>();
        line.NumPoints.ShouldBe(3);

        GeoJsonGeometry.From(line).Type.ShouldBe("LineString");
    }

    [Fact]
    public void Polygon_round_trips_with_hole()
    {
        var geom = Geo(
            "Polygon",
            "[[[0,0],[4,0],[4,4],[0,4],[0,0]],[[1,1],[2,1],[2,2],[1,2],[1,1]]]").ToGeometryOrNull();
        var polygon = geom.ShouldBeOfType<Polygon>();
        polygon.NumInteriorRings.ShouldBe(1);

        var back = GeoJsonGeometry.From(polygon);
        back.Type.ShouldBe("Polygon");
        back.ToGeometryOrNull().ShouldBeOfType<Polygon>().EqualsTopologically(polygon).ShouldBeTrue();
    }

    [Fact]
    public void MultiPoint_round_trips()
    {
        var geom = Geo("MultiPoint", "[[25.0,45.0],[25.1,45.1]]").ToGeometryOrNull();
        var multi = geom.ShouldBeOfType<MultiPoint>();
        multi.NumGeometries.ShouldBe(2);
        multi.SRID.ShouldBe(4326);

        GeoJsonGeometry.From(multi).Type.ShouldBe("MultiPoint");
    }

    [Fact]
    public void MultiLineString_round_trips()
    {
        var geom = Geo("MultiLineString", "[[[25.0,45.0],[25.1,45.1]],[[26.0,46.0],[26.1,46.1]]]").ToGeometryOrNull();
        var multi = geom.ShouldBeOfType<MultiLineString>();
        multi.NumGeometries.ShouldBe(2);

        var back = GeoJsonGeometry.From(multi);
        back.Type.ShouldBe("MultiLineString");
        back.ToGeometryOrNull().ShouldBeOfType<MultiLineString>().EqualsTopologically(multi).ShouldBeTrue();
    }

    [Fact]
    public void MultiPolygon_round_trips()
    {
        var geom = Geo(
            "MultiPolygon",
            "[[[[0,0],[4,0],[4,4],[0,4],[0,0]]],[[[5,5],[6,5],[6,6],[5,6],[5,5]]]]").ToGeometryOrNull();
        var multi = geom.ShouldBeOfType<MultiPolygon>();
        multi.NumGeometries.ShouldBe(2);
        multi.SRID.ShouldBe(4326);

        var back = GeoJsonGeometry.From(multi);
        back.Type.ShouldBe("MultiPolygon");
        back.ToGeometryOrNull().ShouldBeOfType<MultiPolygon>().EqualsTopologically(multi).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Point", "[25.5]")] // too few numbers
    [InlineData("Point", "\"nope\"")] // not an array
    [InlineData("LineString", "[[25.0,45.0]]")] // one-point line
    [InlineData("Polygon", "[[[0,0],[4,0],[4,4]]]")] // unclosed ring
    [InlineData("Polygon", "[]")] // no rings
    [InlineData("MultiPolygon", "[]")] // empty multipolygon (no parts)
    [InlineData("MultiPoint", "[]")] // empty multipoint (no parts)
    [InlineData("GeometryCollection", "[]")] // intentionally unsupported type
    [InlineData("Polygon", "[[[0,0],[4,0],[0,4],[4,4],[0,0]]]")] // self-intersecting (bow-tie)
    public void Malformed_geometry_returns_null(string type, string coordinates) =>
        Geo(type, coordinates).ToGeometryOrNull().ShouldBeNull();

    [Fact]
    public void Kind_matching_follows_the_taxonomy_contract()
    {
        var point = Geo("Point", "[1,1]").ToGeometryOrNull()!;
        var line = Geo("LineString", "[[0,0],[1,1]]").ToGeometryOrNull()!;

        GeoJsonGeometry.MatchesKind(point, GeometryKind.Point).ShouldBeTrue();
        GeoJsonGeometry.MatchesKind(point, GeometryKind.Line).ShouldBeFalse();
        GeoJsonGeometry.MatchesKind(line, GeometryKind.Line).ShouldBeTrue();
        GeoJsonGeometry.MatchesKind(line, GeometryKind.Polygon).ShouldBeFalse();
        GeoJsonGeometry.MatchesKind(point, GeometryKind.Any).ShouldBeTrue();
        GeoJsonGeometry.MatchesKind(line, GeometryKind.Any).ShouldBeTrue();
    }

    [Fact]
    public void Kind_matching_accepts_multi_variants_of_the_same_kind()
    {
        var multiPoint = Geo("MultiPoint", "[[1,1],[2,2]]").ToGeometryOrNull()!;
        var multiLine = Geo("MultiLineString", "[[[0,0],[1,1]]]").ToGeometryOrNull()!;
        var multiPolygon = Geo("MultiPolygon", "[[[[0,0],[4,0],[4,4],[0,4],[0,0]]]]").ToGeometryOrNull()!;

        // A multi-part geometry is editable under its single-part feature-type kind.
        GeoJsonGeometry.MatchesKind(multiPoint, GeometryKind.Point).ShouldBeTrue();
        GeoJsonGeometry.MatchesKind(multiLine, GeometryKind.Line).ShouldBeTrue();
        GeoJsonGeometry.MatchesKind(multiPolygon, GeometryKind.Polygon).ShouldBeTrue();

        // But not under a different kind.
        GeoJsonGeometry.MatchesKind(multiPoint, GeometryKind.Line).ShouldBeFalse();
        GeoJsonGeometry.MatchesKind(multiPolygon, GeometryKind.Point).ShouldBeFalse();
    }
}
