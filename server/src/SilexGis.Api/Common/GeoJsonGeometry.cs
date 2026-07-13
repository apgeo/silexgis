// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using NetTopologySuite.Geometries;

namespace SilexGis.Api.Common;

/// <summary>
/// GeoJSON geometry contract for entities whose shape varies (Point/LineString/Polygon and
/// their Multi* variants — the latter appear in imported geodata and must round-trip through
/// edits). Coordinates stay a raw JSON element because GeoJSON coordinate arrays are
/// recursive — a fixed OpenAPI schema cannot express them; conversion to NTS validates
/// structure. GeometryCollection is intentionally unsupported (no coordinates array; nothing
/// produces one for these entities).
/// </summary>
public sealed record GeoJsonGeometry(string Type, JsonElement Coordinates)
{
    public static GeoJsonGeometry From(Geometry geometry)
    {
        // Serialize coordinates through a plain nested-array model, then parse to JsonElement.
        // Multi* variants appear in imported geodata (e.g. GPX tracks are MultiLineString);
        // GeometryCollection has no "coordinates" and is flattened before it gets here.
        object model = geometry switch
        {
            Point p => Position(p.Coordinate),
            LineString l => Line(l),
            Polygon poly => Rings(poly),
            MultiPoint mp => mp.Geometries.Select(g => Position(g.Coordinate)).ToArray(),
            MultiLineString ml => ml.Geometries.Cast<LineString>().Select(Line).ToArray(),
            MultiPolygon mpoly => mpoly.Geometries.Cast<Polygon>().Select(Rings).ToArray(),
            _ => throw new NotSupportedException($"Geometry type {geometry.GeometryType} is not supported."),
        };
        return new GeoJsonGeometry(geometry.GeometryType, JsonSerializer.SerializeToElement(model));

        // GeoJSON positions carry an optional third element; keep altitude when the
        // source geometry has it (centerlines are Z-typed) instead of flattening to 2D.
        static double[] Position(Coordinate c) => double.IsNaN(c.Z) ? [c.X, c.Y] : [c.X, c.Y, c.Z];
        static double[][] Line(LineString l) => [.. l.Coordinates.Select(Position)];
        static double[][][] Rings(Polygon poly) =>
            [.. new[] { poly.ExteriorRing }.Concat(poly.InteriorRings)
                .Select(r => r.Coordinates.Select(Position).ToArray())];
    }

    /// <summary>Converts to an NTS geometry (SRID 4326); returns null when malformed.</summary>
    public Geometry? ToGeometryOrNull()
    {
        try
        {
            Geometry geometry = Type switch
            {
                "Point" => new Point(ParsePosition(Coordinates)),
                "LineString" => new LineString(ParseLine(Coordinates)),
                "Polygon" => ParsePolygon(Coordinates),
                "MultiPoint" => new MultiPoint([.. Parts(Coordinates).Select(p => new Point(ParsePosition(p)))]),
                "MultiLineString" => new MultiLineString([.. Parts(Coordinates).Select(l => new LineString(ParseLine(l)))]),
                "MultiPolygon" => new MultiPolygon([.. Parts(Coordinates).Select(ParsePolygon)]),
                _ => throw new JsonException($"Unsupported geometry type '{Type}'."),
            };
            if (!geometry.IsValid)
            {
                return null;
            }

            geometry.SRID = 4326;
            return geometry;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        static Coordinate ParsePosition(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2)
            {
                throw new JsonException("A position must be an array of at least two numbers.");
            }

            return new Coordinate(e[0].GetDouble(), e[1].GetDouble());
        }

        static Coordinate[] ParseLine(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Coordinates must be an array.");
            }

            return [.. e.EnumerateArray().Select(ParsePosition)];
        }

        static Polygon ParsePolygon(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 1)
            {
                throw new JsonException("A polygon needs at least an exterior ring.");
            }

            var rings = e.EnumerateArray().Select(r => new LinearRing(ParseLine(r))).ToArray();
            return new Polygon(rings[0], [.. rings.Skip(1)]);
        }

        // The parts of a Multi* geometry: at least one, each parsed by the matching simple
        // parser above. An empty multi-geometry is rejected (a feature with no parts is
        // meaningless and would slip past the IsValid check).
        static JsonElement[] Parts(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 1)
            {
                throw new JsonException("A multi-geometry needs at least one part.");
            }

            return [.. e.EnumerateArray()];
        }
    }

    /// <summary>
    /// True when the geometry class is allowed for the given kind. A Multi* variant matches
    /// its single-part kind (a multi-part polygon is still polygonal), so imported multi-part
    /// features remain editable under their typed feature type.
    /// </summary>
    public static bool MatchesKind(Geometry geometry, Domain.GeometryKind kind) => kind switch
    {
        Domain.GeometryKind.Point => geometry is Point or MultiPoint,
        Domain.GeometryKind.Line => geometry is LineString or MultiLineString,
        Domain.GeometryKind.Polygon => geometry is Polygon or MultiPolygon,
        Domain.GeometryKind.Any => true,
        _ => false,
    };
}
