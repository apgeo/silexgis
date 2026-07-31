// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain;

/// <summary>
/// OGC geometry class a feature type accepts (multi-part variants are distinct classes —
/// a kind must opt into them). Stored values — do not renumber.
/// </summary>
public enum GeometryClass : short
{
    Point = 0,
    LineString = 1,
    Polygon = 2,
    MultiPoint = 3,
    MultiLineString = 4,
    MultiPolygon = 5,
}

public static class GeometryClasses
{
    /// <summary>The class of an NTS geometry instance, or null for kinds we never accept (collections).</summary>
    public static GeometryClass? Of(Geometry geometry) => geometry switch
    {
        Point => GeometryClass.Point,
        LineString => GeometryClass.LineString,
        Polygon => GeometryClass.Polygon,
        MultiPoint => GeometryClass.MultiPoint,
        MultiLineString => GeometryClass.MultiLineString,
        MultiPolygon => GeometryClass.MultiPolygon,
        _ => null,
    };

    /// <summary>
    /// Location-protection floor: only a plain point can be safely snapped to the
    /// obfuscation grid. Every other class — lines, polygons and all multi-part
    /// geometries — leaks shape, extent or part constellation even when snapped, so
    /// protected features carrying them are withheld entirely.
    /// </summary>
    public static bool CanSnap(GeometryClass geometryClass) => geometryClass == GeometryClass.Point;
}
