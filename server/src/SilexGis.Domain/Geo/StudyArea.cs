// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Geo;

/// <summary>
/// The rules for using a feature as the area a question is asked over — a density divided by
/// an outline, a spacing measured inside one, a registry total or an index computed for one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement first, and one answer for every failure.</b> An outline scopes a question by
/// where it is, so a caller may use one only when they may both read it and place it exactly.
/// A feature that does not exist, one the caller cannot read and one they can read but not place
/// are refused with the same answer, because a refusal that told them apart would say which
/// outlines are protected, and answering for an unplaceable one would draw its edges a cell or a
/// count at a time.
/// </para>
/// <para>
/// <b>Only an outline encloses ground.</b> A point or a line has no area to divide by or measure
/// inside; the routes that need the ground refuse it, each under its own code, after — never
/// before — the placement question, so the shape of an unplaceable feature is never what answers.
/// </para>
/// </remarks>
public static class StudyArea
{
    /// <summary>The code every study-area refusal for an area the caller may not use carries.</summary>
    public const string NotFoundCode = "feature.not_found";

    /// <summary>
    /// The last segment of each route's "not an outline" code; the route's own prefix comes first,
    /// so a client can tell which question was refused.
    /// </summary>
    public const string NotAnOutlineCodeSuffix = "study_area_not_an_outline";

    /// <summary>The refusal code for a study area that is not an outline, under a route's prefix.</summary>
    public static string NotAnOutlineCode(string routePrefix) => $"{routePrefix}.{NotAnOutlineCodeSuffix}";

    /// <summary>
    /// Whether a geometry encloses ground: a polygon or a multipolygon. Its SQL twin is
    /// <c>ST_GeometryType(geom) IN ('ST_Polygon', 'ST_MultiPolygon')</c>, which a statement that
    /// chooses areas in bulk must spell exactly so, or the two would disagree about a collection.
    /// </summary>
    public static bool IsOutline(Geometry? geometry) => geometry is Polygon or MultiPolygon;
}
