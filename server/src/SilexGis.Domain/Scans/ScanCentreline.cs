// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>One vertex of a scan's centreline, in metres, in the frame the line is written in.</summary>
public readonly record struct CentrelinePoint(double X, double Y, double Z);

/// <summary>
/// The line a scan's trajectory reduces to: the path the scanner was carried along, with every
/// pose that adds nothing to its shape dropped, so what is left is a centreline of the passages
/// that a map can draw and a viewer can lay over the points.
/// </summary>
/// <remarks>
/// <para>
/// The line is written in the frame of the cloud the trajectory came with — the scan's own local
/// frame, not the tiles' (which are shifted by a point the tiler chooses) and not the world
/// (which is a placement that may change at any time without the line being made again). Each
/// reader converts it on the way out: by the tiles' zero point for a viewer that draws it over
/// the tiles, and through the placement for a map.
/// </para>
/// <para>
/// A centreline is the cave's shape and, once the scan is placed, its position, whatever frame
/// it is written in; it is handed out only where the scanner's trajectory itself would be. It is
/// never written into the cave's survey: it is a product of the scan, kept beside it.
/// </para>
/// </remarks>
public static class ScanCentreline
{
    /// <summary>The default tolerance, in metres: well under a passage's width, well over the scanner's sway as it is carried.</summary>
    public const double DefaultToleranceM = 0.2;

    /// <summary>
    /// The trajectory simplified with Douglas–Peucker in three dimensions: a pose is kept when it
    /// stands further than <paramref name="toleranceM"/> from the straight line between the poses
    /// kept either side of it. The first and the last pose are always kept. The poses are taken in
    /// the order given, which for a trajectory is the order they were recorded in.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive: an hour's walk is hundreds of thousands of poses, and a
    /// passage that doubles back on itself can make the recursion as deep as the line is long.
    /// </remarks>
    public static IReadOnlyList<CentrelinePoint> Simplify(IReadOnlyList<CentrelinePoint> points, double toleranceM)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(toleranceM) || toleranceM < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceM), toleranceM, "The tolerance must be a finite distance of zero or more.");
        }

        if (points.Count < 3)
        {
            return [.. points];
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        var squaredTolerance = toleranceM * toleranceM;
        var spans = new Stack<(int First, int Last)>();
        spans.Push((0, points.Count - 1));
        while (spans.Count > 0)
        {
            var (first, last) = spans.Pop();
            if (last - first < 2)
            {
                continue;
            }

            var farthest = -1;
            var farthestDistance = squaredTolerance;
            for (var i = first + 1; i < last; i++)
            {
                var distance = SquaredDistanceToSegment(points[i], points[first], points[last]);
                if (distance > farthestDistance)
                {
                    farthest = i;
                    farthestDistance = distance;
                }
            }

            if (farthest < 0)
            {
                continue;
            }

            keep[farthest] = true;
            spans.Push((first, farthest));
            spans.Push((farthest, last));
        }

        var kept = new List<CentrelinePoint>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                kept.Add(points[i]);
            }
        }

        return kept;
    }

    /// <summary>The trajectory's positions as centreline vertices, in the order recorded.</summary>
    public static IReadOnlyList<CentrelinePoint> FromTrajectory(IReadOnlyList<TrajectoryPoint> trajectory)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        return [.. trajectory.Select(p => new CentrelinePoint(p.X, p.Y, p.Z))];
    }

    /// <summary>The length along the line, in metres.</summary>
    public static double LengthM(IReadOnlyList<CentrelinePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var length = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            var (dx, dy, dz) = (points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y, points[i].Z - points[i - 1].Z);
            length += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        return length;
    }

    /// <summary>
    /// The line as a GeoJSON feature: a LineString in the scan's local frame, metres, rounded to
    /// the millimetre. Its properties say so, with the tolerance it was simplified at and how many
    /// poses it was made from — GeoJSON's own convention is longitude and latitude, and a reader
    /// that took these numbers for those would put the cave in the Gulf of Guinea.
    /// </summary>
    public static byte[] ToGeoJson(IReadOnlyList<CentrelinePoint> line, double toleranceM, int sourcePoseCount)
    {
        ArgumentNullException.ThrowIfNull(line);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "Feature");
            writer.WriteStartObject("properties");
            writer.WriteString("frame", "scan-local");
            writer.WriteString("units", "m");
            writer.WriteNumber("toleranceM", toleranceM);
            writer.WriteNumber("sourcePoses", sourcePoseCount);
            writer.WriteNumber("lengthM", Math.Round(LengthM(line), 3));
            writer.WriteEndObject();
            writer.WriteStartObject("geometry");
            writer.WriteString("type", "LineString");
            writer.WriteStartArray("coordinates");
            foreach (var p in line)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(Math.Round(p.X, 3));
                writer.WriteNumberValue(Math.Round(p.Y, 3));
                writer.WriteNumberValue(Math.Round(p.Z, 3));
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The vertices of a centreline written by <see cref="ToGeoJson"/>, or null when the text is
    /// not one: not JSON, not a LineString, or a vertex that is not three finite numbers.
    /// </summary>
    public static IReadOnlyList<CentrelinePoint>? ParseGeoJson(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("geometry", out var geometry)
                || geometry.ValueKind != JsonValueKind.Object
                || !geometry.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "LineString", StringComparison.Ordinal)
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var line = new List<CentrelinePoint>(coordinates.GetArrayLength());
            foreach (var vertex in coordinates.EnumerateArray())
            {
                if (vertex.ValueKind != JsonValueKind.Array || vertex.GetArrayLength() != 3)
                {
                    return null;
                }

                var values = new double[3];
                var i = 0;
                foreach (var value in vertex.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Number || !double.IsFinite(values[i] = value.GetDouble()))
                    {
                        return null;
                    }

                    i++;
                }

                line.Add(new CentrelinePoint(values[0], values[1], values[2]));
            }

            return line;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The line moved into the tiles' frame: every vertex less the point the tiler wrote the tiles about.</summary>
    public static IReadOnlyList<CentrelinePoint> InTilesFrame(IReadOnlyList<CentrelinePoint> line, (double X, double Y, double Z) zero)
    {
        ArgumentNullException.ThrowIfNull(line);
        return [.. line.Select(p => new CentrelinePoint(p.X - zero.X, p.Y - zero.Y, p.Z - zero.Z))];
    }

    /// <summary>
    /// The line set down on the earth: each vertex taken into the tiles' frame and carried out
    /// through the placement, as longitude, latitude and height. The same arithmetic the scene
    /// applies to the tiles, so the line lies in the passages the points draw.
    /// </summary>
    public static IReadOnlyList<CentrelinePoint> Placed(
        IReadOnlyList<CentrelinePoint> line, (double X, double Y, double Z) zero, ScanPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(placement);
        return [.. InTilesFrame(line, zero).Select(p =>
        {
            var (longitude, latitude, height) = placement.PositionOf(p.X, p.Y, p.Z);
            return new CentrelinePoint(longitude, latitude, height);
        })];
    }

    private static double SquaredDistanceToSegment(CentrelinePoint p, CentrelinePoint a, CentrelinePoint b)
    {
        var (abx, aby, abz) = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        var (apx, apy, apz) = (p.X - a.X, p.Y - a.Y, p.Z - a.Z);
        var squaredLength = (abx * abx) + (aby * aby) + (abz * abz);
        var t = squaredLength == 0 ? 0 : Math.Clamp(((apx * abx) + (apy * aby) + (apz * abz)) / squaredLength, 0, 1);
        var (dx, dy, dz) = (apx - (t * abx), apy - (t * aby), apz - (t * abz));
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
