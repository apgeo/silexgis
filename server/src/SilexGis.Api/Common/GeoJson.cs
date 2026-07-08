// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Api.Common;

/// <summary>
/// Minimal GeoJSON contract types (RFC 7946) used in DTOs and map responses.
/// Deliberately plain records so the OpenAPI schema — and the generated TS client —
/// stay exact; NTS types never appear in contracts.
/// </summary>
public sealed record GeoJsonPoint(string Type, double[] Coordinates)
{
    public static GeoJsonPoint From(Point point) => new("Point", [point.X, point.Y]);

    public Point ToPoint() => new(Coordinates[0], Coordinates[1]) { SRID = 4326 };
}

public sealed record GeoFeature(string Type, GeoJsonGeometry Geometry, Dictionary<string, object?> Properties)
{
    public static GeoFeature Of(Geometry geometry, Dictionary<string, object?> properties) =>
        new("Feature", GeoJsonGeometry.From(geometry), properties);
}

public sealed record FeatureCollection(string Type, IReadOnlyList<GeoFeature> Features)
{
    public static FeatureCollection Of(IReadOnlyList<GeoFeature> features) => new("FeatureCollection", features);
}

/// <summary>Bounding box query parameter: "west,south,east,north" (WGS84).</summary>
public readonly record struct Bbox(double West, double South, double East, double North)
{
    public static bool TryParse(string? value, out Bbox bbox)
    {
        bbox = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        double[] numbers = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        bbox = new Bbox(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public Polygon ToPolygon() => new(new LinearRing(
    [
        new Coordinate(West, South),
        new Coordinate(East, South),
        new Coordinate(East, North),
        new Coordinate(West, North),
        new Coordinate(West, South),
    ]))
    { SRID = 4326 };
}
