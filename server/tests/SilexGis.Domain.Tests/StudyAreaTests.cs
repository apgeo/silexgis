// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>What may stand as the ground a question is asked over, and how its refusal is named.</summary>
public class StudyAreaTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private static Polygon Square(double x) => Factory.CreatePolygon(
    [
        new Coordinate(x, 0), new Coordinate(x + 1, 0), new Coordinate(x + 1, 1),
        new Coordinate(x, 1), new Coordinate(x, 0),
    ]);

    [Fact]
    public void Polygons_and_multipolygons_are_outlines()
    {
        StudyArea.IsOutline(Square(0)).ShouldBeTrue();
        StudyArea.IsOutline(Factory.CreateMultiPolygon([Square(0), Square(3)])).ShouldBeTrue();
    }

    [Fact]
    public void Points_lines_collections_and_nothing_are_not()
    {
        StudyArea.IsOutline(Factory.CreatePoint(new Coordinate(1, 1))).ShouldBeFalse();
        StudyArea.IsOutline(Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)])).ShouldBeFalse();
        StudyArea.IsOutline(Factory.CreateGeometryCollection([Square(0)])).ShouldBeFalse();
        StudyArea.IsOutline(null).ShouldBeFalse();
    }

    [Fact]
    public void The_not_an_outline_code_keeps_each_routes_prefix()
    {
        StudyArea.NotAnOutlineCode("density").ShouldBe("density.study_area_not_an_outline");
        StudyArea.NotAnOutlineCode("point_pattern").ShouldBe("point_pattern.study_area_not_an_outline");
    }
}
