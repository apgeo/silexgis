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

    /// <summary>
    /// Whether the box is a real window on the globe: inside the WGS84 limits, and with each pair
    /// the right way round.
    ///
    /// <para>
    /// Parsing four numbers is deliberately separate from believing them, because most callers of
    /// this type hand the box straight to PostGIS, which is content to build an envelope spanning
    /// several thousand times the planet. Anything that then does arithmetic per unit of the window
    /// — enumerating grid cells, segmentising an edge — turns such a box into work measured in
    /// millions of years, so those callers check this first and refuse. It is not applied inside
    /// <c>TryParse</c> because that would change the answer for every existing caller at once.
    /// </para>
    /// </summary>
    public bool IsWithinWorld =>
        West >= -180d && East <= 180d && South >= -90d && North <= 90d
        && West < East && South < North;

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
