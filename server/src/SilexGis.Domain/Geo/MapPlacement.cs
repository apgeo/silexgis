// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Geo;

/// <summary>
/// How a drawn sheet is placed on the ground: the points on the picture somebody matched to points
/// on the map, the transformation that carries the one onto the other, and an optional outline of
/// the part of the picture worth showing.
/// </summary>
/// <remarks>
/// <para>
/// A point on the picture is a fraction of its width and height (0 the left or top edge, 1 the
/// right or bottom), never a pixel: the same sheet is drawn at different resolutions, and a
/// placement must survive a redraw. Fractions are also what a pin on a picture already uses
/// elsewhere in this application, so the two kinds of point can be read into each other.
/// </para>
/// <para>
/// A point on the ground is written in the coordinate system the person typed it in, named by its
/// <c>EPSG:</c> code, so a sheet with a printed grid can be placed from the grid's own numbers.
/// The one exception is a document's own georeferencing in a system the library could not name
/// with a code: that is kept as <c>WKT:</c> followed by the definition, and is written only by the
/// server.
/// </para>
/// </remarks>
public sealed record MapPlacement(
    int Version,
    string Transformation,
    string Crs,
    IReadOnlyList<MapControlPoint> Gcps,
    IReadOnlyList<IReadOnlyList<double>>? Mask)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The stored placement, or null when the text holds none or is not one.</summary>
    public static MapPlacement? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MapPlacement>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The EPSG code the points are written in, or null when they are not written by one.</summary>
    public int? EpsgCode => MapPlacementRules.EpsgCodeOf(Crs);
}

/// <summary>One point on the picture matched to one point on the ground.</summary>
/// <param name="U">Across the picture, as a fraction of its width.</param>
/// <param name="V">Down the picture, as a fraction of its height.</param>
/// <param name="X">Easting or longitude, in the placement's coordinate system.</param>
/// <param name="Y">Northing or latitude, in the placement's coordinate system.</param>
/// <param name="Label">What the person called the point, if anything.</param>
public sealed record MapControlPoint(double U, double V, double X, double Y, string? Label = null);

/// <summary>The transformations a placement may name, and how many points each needs.</summary>
public static class MapTransformations
{
    /// <summary>Scale, turn and shift: two points.</summary>
    public const string Similarity = "similarity";

    /// <summary>Also shears: three points.</summary>
    public const string Affine = "affine";

    /// <summary>A photographed or skewed sheet; four points land exactly.</summary>
    public const string Homography = "homography";

    public const string Polynomial2 = "polynomial2";

    public const string Polynomial3 = "polynomial3";

    /// <summary>A rubber sheet: exact at every point.</summary>
    public const string ThinPlateSpline = "tps";

    public static readonly IReadOnlyList<string> All =
        [Similarity, Affine, Homography, Polynomial2, Polynomial3, ThinPlateSpline];

    /// <summary>The fewest points a transformation can be fitted to; 0 for a name that is none.</summary>
    public static int MinimumPoints(string transformation) => transformation switch
    {
        Similarity => 2,
        Affine => 3,
        Homography => 4,
        Polynomial2 => 6,
        Polynomial3 => 10,
        ThinPlateSpline => 3,
        _ => 0,
    };
}

/// <summary>A reason a placement cannot be used, with the code a client can translate.</summary>
public sealed record MapPlacementProblem(string Code, string Message);

/// <summary>The rules every placement is held to before anything draws from it.</summary>
public static class MapPlacementRules
{
    public const string TransformationUnknown = "georeferenced_map.placement_transformation_unknown";
    public const string TooFewPoints = "georeferenced_map.placement_too_few_points";
    public const string TooManyPoints = "georeferenced_map.placement_too_many_points";
    public const string PointsDegenerate = "georeferenced_map.placement_points_degenerate";
    public const string PointOutsidePicture = "georeferenced_map.placement_point_outside_picture";
    public const string PointNotANumber = "georeferenced_map.placement_point_invalid";
    public const string MaskInvalid = "georeferenced_map.placement_mask_invalid";
    public const string CrsUnknown = "georeferenced_map.placement_crs_unknown";
    public const string VersionUnknown = "georeferenced_map.placement_version_unknown";

    /// <summary>
    /// How far outside the picture a point may lie: a corner of a sheet whose paper was trimmed is
    /// legitimately a little off it, a point three pictures away is a typing error.
    /// </summary>
    private const double PointSlack = 0.5;

    private const double MaskSlack = 0.1;

    /// <summary>Points closer than this, as a fraction of the picture, are the same point.</summary>
    private const double SamePoint = 1e-6;

    /// <summary>
    /// The smallest area, as a fraction of the picture's, the points may span for any transformation
    /// that needs more than two: below it a fit is decided by rounding, not by the points.
    /// </summary>
    private const double MinimumSpan = 1e-6;

    /// <summary>The EPSG code of <c>EPSG:&lt;code&gt;</c>, or null.</summary>
    public static int? EpsgCodeOf(string? crs) =>
        crs is not null
        && crs.StartsWith("EPSG:", StringComparison.Ordinal)
        && int.TryParse(crs.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var code)
        && code > 0
            ? code
            : null;

    /// <summary>Every problem with the placement, in a stable order; empty when it can be used.</summary>
    /// <param name="maxPoints">The installation's ceiling on control points.</param>
    /// <param name="allowWkt">Whether a <c>WKT:</c> system is acceptable — true only for what the server wrote.</param>
    public static IReadOnlyList<MapPlacementProblem> Validate(MapPlacement placement, int maxPoints, bool allowWkt = false)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var problems = new List<MapPlacementProblem>();

        if (placement.Version != MapPlacement.CurrentVersion)
        {
            problems.Add(new(VersionUnknown, $"Placement version {placement.Version} is not one this server reads."));
        }

        var minimum = MapTransformations.MinimumPoints(placement.Transformation);
        if (minimum == 0)
        {
            problems.Add(new(
                TransformationUnknown,
                $"'{placement.Transformation}' is not a transformation; use one of {string.Join(", ", MapTransformations.All)}."));
        }

        if (EpsgCodeOf(placement.Crs) is null
            && !(allowWkt && placement.Crs?.StartsWith("WKT:", StringComparison.Ordinal) == true && placement.Crs.Length > 4))
        {
            problems.Add(new(CrsUnknown, "The coordinate system must be written EPSG:<code>."));
        }

        var points = placement.Gcps ?? [];
        if (points.Any(p => !double.IsFinite(p.U) || !double.IsFinite(p.V) || !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
        {
            problems.Add(new(PointNotANumber, "Every coordinate of every point must be a finite number."));
            return problems;
        }

        if (minimum > 0 && points.Count < minimum)
        {
            problems.Add(new(
                TooFewPoints,
                $"The {placement.Transformation} transformation needs at least {minimum} points; {points.Count} given."));
        }

        if (points.Count > maxPoints)
        {
            problems.Add(new(TooManyPoints, $"At most {maxPoints} control points; {points.Count} given."));
        }

        if (points.Any(p => p.U < -PointSlack || p.U > 1 + PointSlack || p.V < -PointSlack || p.V > 1 + PointSlack))
        {
            problems.Add(new(PointOutsidePicture, "A point lies far outside the picture."));
        }

        if (Degenerate(points, placement.Transformation))
        {
            problems.Add(new(
                PointsDegenerate,
                "The points on the picture coincide or lie on one line; spread them over the sheet."));
        }

        if (placement.Mask is { } mask && !MaskIsValid(mask))
        {
            problems.Add(new(
                MaskInvalid,
                "The outline needs at least three points, each a pair of numbers on or near the picture, and must not cross itself."));
        }

        return problems;
    }

    private static bool Degenerate(IReadOnlyList<MapControlPoint> points, string transformation)
    {
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                if (Math.Abs(points[i].U - points[j].U) < SamePoint && Math.Abs(points[i].V - points[j].V) < SamePoint)
                {
                    return true;
                }
            }
        }

        if (transformation == MapTransformations.Similarity || points.Count < 3)
        {
            return false;
        }

        // The largest triangle the points make: zero when they all lie on one line.
        var largest = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                for (var k = j + 1; k < points.Count; k++)
                {
                    var area = Math.Abs(
                        ((points[j].U - points[i].U) * (points[k].V - points[i].V))
                        - ((points[k].U - points[i].U) * (points[j].V - points[i].V))) / 2;
                    largest = Math.Max(largest, area);
                }
            }
        }

        return largest < MinimumSpan;
    }

    private static bool MaskIsValid(IReadOnlyList<IReadOnlyList<double>> mask)
    {
        if (mask.Count < 3 || mask.Any(p => p is null || p.Count != 2 || !double.IsFinite(p[0]) || !double.IsFinite(p[1])))
        {
            return false;
        }

        if (mask.Any(p => p[0] < -MaskSlack || p[0] > 1 + MaskSlack || p[1] < -MaskSlack || p[1] > 1 + MaskSlack))
        {
            return false;
        }

        // A ring that is closed by repeating its first point is the same ring.
        var ring = mask.Count > 3 && mask[0][0] == mask[^1][0] && mask[0][1] == mask[^1][1]
            ? mask.Take(mask.Count - 1).ToList()
            : mask.ToList();
        if (ring.Count < 3)
        {
            return false;
        }

        for (var i = 0; i < ring.Count; i++)
        {
            var a1 = ring[i];
            var a2 = ring[(i + 1) % ring.Count];
            for (var j = i + 1; j < ring.Count; j++)
            {
                // Neighbouring edges share a corner; that is not a crossing.
                if (j == i || (j + 1) % ring.Count == i || (i + 1) % ring.Count == j)
                {
                    continue;
                }

                if (Cross(a1, a2, ring[j], ring[(j + 1) % ring.Count]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Cross(
        IReadOnlyList<double> p1, IReadOnlyList<double> p2, IReadOnlyList<double> q1, IReadOnlyList<double> q2)
    {
        static double Side(IReadOnlyList<double> a, IReadOnlyList<double> b, IReadOnlyList<double> c) =>
            ((b[0] - a[0]) * (c[1] - a[1])) - ((b[1] - a[1]) * (c[0] - a[0]));

        var d1 = Side(q1, q2, p1);
        var d2 = Side(q1, q2, p2);
        var d3 = Side(p1, p2, q1);
        var d4 = Side(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}

/// <summary>The arithmetic of a placement that needs no raster library.</summary>
public static class MapPlacementMath
{
    /// <summary>
    /// The six-number affine transform (pixel column and row to ground x and y, in the order the
    /// raster library writes a geotransform) that scales, turns and shifts the picture so its
    /// first two control points land exactly on theirs.
    /// </summary>
    /// <remarks>
    /// The library fits every other transformation itself from control points; it has no fit for
    /// this one, which is the one a person makes with two clicks. Rows count downward and northings
    /// upward, so the picture's row axis is flipped before the fit: a sheet placed this way is never
    /// mirrored. Written as complex numbers, ground = c · picture + t, where c carries the scale and
    /// the turn together.
    /// </remarks>
    public static double[] SimilarityGeoTransform(IReadOnlyList<MapControlPoint> points, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new ArgumentException("Two points are needed.", nameof(points));
        }

        var (p1, p2) = (points[0], points[1]);

        // Picture point as a complex number with the row axis flipped: z = column − i·row.
        var (z1r, z1i) = (p1.U * width, -(p1.V * height));
        var (z2r, z2i) = (p2.U * width, -(p2.V * height));
        var (dzr, dzi) = (z2r - z1r, z2i - z1i);
        var (dwr, dwi) = (p2.X - p1.X, p2.Y - p1.Y);
        var norm = (dzr * dzr) + (dzi * dzi);
        if (norm == 0)
        {
            throw new ArgumentException("The two points coincide on the picture.", nameof(points));
        }

        // c = dw / dz
        var a = ((dwr * dzr) + (dwi * dzi)) / norm;
        var b = ((dwi * dzr) - (dwr * dzi)) / norm;

        // t = w1 − c·z1
        var tx = p1.X - ((a * z1r) - (b * z1i));
        var ty = p1.Y - ((a * z1i) + (b * z1r));

        // ground x = tx + a·col + b·row ; ground y = ty + b·col − a·row
        return [tx, a, b, ty, b, -a];
    }

    /// <summary>Where a picture fraction lands under a geotransform; for tests and for the footprint.</summary>
    public static (double X, double Y) Apply(double[] geoTransform, double u, double v, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(geoTransform);
        var (col, row) = (u * width, v * height);
        return (
            geoTransform[0] + (col * geoTransform[1]) + (row * geoTransform[2]),
            geoTransform[3] + (col * geoTransform[4]) + (row * geoTransform[5]));
    }
}
