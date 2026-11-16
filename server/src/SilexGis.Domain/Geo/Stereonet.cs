// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>A pole as it lands on the net: unit-circle coordinates, east to the right and north up.</summary>
/// <param name="X">East coordinate, in [-1, 1].</param>
/// <param name="Y">North coordinate, in [-1, 1].</param>
public readonly record struct StereonetPoint(double X, double Y);

/// <summary>
/// The density of poles over the net, counted by Kamb's method and expressed in standard
/// deviations of the count expected from poles scattered at random.
/// </summary>
/// <param name="Size">Nodes along each side of the square grid laid over the net.</param>
/// <param name="Values">
/// Row-major, north row first and west column first: node (row r, column c) sits at
/// x = -1 + 2c/(Size-1), y = 1 - 2r/(Size-1). A node outside the net is null.
/// </param>
/// <param name="CountingAreaFraction">The share of the hemisphere one counting circle covers.</param>
/// <param name="ExpectedSigma">
/// The value a node takes when the poles are uniform — three, by the construction of the method.
/// </param>
/// <param name="ContourIntervalSigma">The step between drawn contour levels.</param>
/// <param name="MaxSigma">The densest node's value.</param>
public sealed record StereonetDensity(
    int Size,
    IReadOnlyList<double?> Values,
    double CountingAreaFraction,
    double ExpectedSigma,
    double ContourIntervalSigma,
    double MaxSigma);

/// <summary>
/// The equal-area (Schmidt) net, lower hemisphere: where a line plots, and how densely a set of
/// poles crowds each part of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why equal-area.</b> A density contour compares how many poles fall in equal pieces of the
/// sphere. On the equal-angle (Wulff) net equal pieces of the sphere are drawn at different sizes —
/// four times larger at the rim than at the centre — so a cluster near the rim would look sparse
/// and one near the centre dense for no reason in the rock. On the equal-area net a counting circle
/// covers the same share of the sphere wherever it is placed.
/// </para>
/// <para>
/// <b>Kamb's counting circle.</b> The circle's size is chosen from the number of poles so that the
/// count expected in it from poles scattered at random is three standard deviations of that count
/// (Kamb 1959): its area is a share <c>A = 9 / (N + 9)</c> of the hemisphere. The density at a
/// node is the number of poles within that circle divided by the standard deviation
/// <c>sqrt(N·A·(1 − A))</c>, so a uniform scatter reads three everywhere and a contour at six says
/// "three standard deviations more than chance would put here". Fixing the circle at one per cent
/// of the net instead, as the older Schmidt method did, makes the same rock look clustered or
/// smooth depending only on how many readings were taken.
/// </para>
/// <para>
/// Poles are axes, not directions: a pole and its opposite are one pole. The count therefore uses
/// the absolute value of the cosine, so a pole near the rim counts towards the node on the
/// opposite rim as well, which is where the lower hemisphere would show it had it plunged the
/// other way by a hair.
/// </para>
/// </remarks>
public static class Stereonet
{
    /// <summary>Kamb's choice: the expected count in a counting circle is three standard deviations of it.</summary>
    public const double KambSigma = 3d;

    /// <summary>The usual step between contour levels on a Kamb plot.</summary>
    public const double ContourIntervalSigma = 2d;

    /// <summary>Grid nodes along each side of the net. Odd, so a node sits on the centre.</summary>
    public const int DefaultGridSize = 41;

    private const double DegreesToRadians = Math.PI / 180d;

    /// <summary>
    /// Where a line plots on the lower-hemisphere equal-area net of radius one: at a distance
    /// <c>√2 · sin((90° − plunge) / 2)</c> from the centre, in the direction of its trend. A
    /// horizontal line lands on the rim, a vertical one at the centre.
    /// </summary>
    public static StereonetPoint Project(LineOrientation line)
    {
        if (!double.IsFinite(line.TrendDegrees) || !double.IsFinite(line.PlungeDegrees)
            || line.PlungeDegrees < 0 || line.PlungeDegrees > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "a plunge must lie between 0 and 90 degrees.");
        }

        var radius = Math.Sqrt(2d) * Math.Sin((90d - line.PlungeDegrees) * DegreesToRadians / 2d);
        var trend = line.TrendDegrees * DegreesToRadians;
        return new StereonetPoint(radius * Math.Sin(trend), radius * Math.Cos(trend));
    }

    /// <summary>
    /// The line a point of the net stands for — the inverse of <see cref="Project"/>. A point
    /// outside the net is refused.
    /// </summary>
    public static LineOrientation Unproject(StereonetPoint point)
    {
        var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        if (!double.IsFinite(radius) || radius > 1d + 1e-12)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "the point lies outside the net.");
        }

        var plunge = 90d - (2d * Math.Asin(Math.Min(radius, 1d) / Math.Sqrt(2d)) / DegreesToRadians);
        var trend = radius < 1e-15 ? 0d : Math.Atan2(point.X, point.Y) / DegreesToRadians;
        return new LineOrientation(StructuralMeasurement.NormalizeAzimuth(trend), Math.Clamp(plunge, 0d, 90d));
    }

    /// <summary>The share of the hemisphere Kamb's counting circle covers for <paramref name="count"/> poles.</summary>
    public static double KambCountingAreaFraction(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return KambSigma * KambSigma / (count + (KambSigma * KambSigma));
    }

    /// <summary>
    /// The Kamb density of <paramref name="poles"/> at every node of a square grid laid over the
    /// net, or null when there are no poles to count.
    /// </summary>
    public static StereonetDensity? KambDensity(IReadOnlyList<LineOrientation> poles, int gridSize = DefaultGridSize)
    {
        if (gridSize < 3 || gridSize % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), "the grid needs an odd number of nodes, at least three.");
        }

        if (poles.Count == 0)
        {
            return null;
        }

        var n = poles.Count;
        var area = KambCountingAreaFraction(n);

        // Two antipodal caps of half-angle θ cover a share 1 − cos θ of the sphere, which is the
        // same share of the hemisphere one axis is drawn on.
        var cosLimit = 1d - area;
        var sigma = Math.Sqrt(n * area * (1d - area));
        var vectors = poles.Select(Vector).ToArray();

        var values = new double?[gridSize * gridSize];
        var max = 0d;
        for (var row = 0; row < gridSize; row++)
        {
            var y = 1d - (2d * row / (gridSize - 1));
            for (var column = 0; column < gridSize; column++)
            {
                var x = -1d + (2d * column / (gridSize - 1));
                if ((x * x) + (y * y) > 1d + 1e-12)
                {
                    continue;
                }

                var node = Vector(Unproject(new StereonetPoint(x, y)));
                var count = 0;
                foreach (var v in vectors)
                {
                    var cos = Math.Abs((v.E * node.E) + (v.N * node.N) + (v.D * node.D));
                    if (cos >= cosLimit - 1e-12)
                    {
                        count++;
                    }
                }

                var value = count / sigma;
                values[(row * gridSize) + column] = value;
                max = Math.Max(max, value);
            }
        }

        return new StereonetDensity(gridSize, values, area, KambSigma, ContourIntervalSigma, max);
    }

    private static (double E, double N, double D) Vector(LineOrientation line)
    {
        var trend = line.TrendDegrees * DegreesToRadians;
        var plunge = line.PlungeDegrees * DegreesToRadians;
        return (Math.Cos(plunge) * Math.Sin(trend), Math.Cos(plunge) * Math.Cos(trend), Math.Sin(plunge));
    }
}
