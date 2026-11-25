// SPDX-License-Identifier: AGPL-3.0-or-later
using Orientation = Therion.Orientation;

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
/// The arithmetic lives in the dependency-free orientation-statistics module of the survey-format
/// library (lower-hemisphere equal-area projection, Kamb's counting circle of area 9 / (N + 9) of
/// the hemisphere, density in standard deviations of the count expected at random). This class
/// only carries it across into this application's own types, which are what the API answers with:
/// a type of the library's in a response would put its name into the published contract, and the
/// API layer may not name the library at all.
/// </remarks>
public static class Stereonet
{
    /// <summary>Kamb's choice: the expected count in a counting circle is three standard deviations of it.</summary>
    public const double KambSigma = Orientation.KambDensity.Sigma;

    /// <summary>The usual step between contour levels on a Kamb plot.</summary>
    public const double ContourIntervalSigma = Orientation.KambDensity.ContourIntervalSigma;

    /// <summary>Grid nodes along each side of the net. Odd, so a node sits on the centre.</summary>
    public const int DefaultGridSize = Orientation.KambDensity.DefaultGridSize;

    /// <summary>
    /// Where a line plots on the lower-hemisphere equal-area net of radius one: at a distance
    /// <c>√2 · sin((90° − plunge) / 2)</c> from the centre, in the direction of its trend. A
    /// horizontal line lands on the rim, a vertical one at the centre.
    /// </summary>
    public static StereonetPoint Project(LineOrientation line)
    {
        var point = Orientation.EqualAreaNet.Project(line.ToLibrary());
        return new StereonetPoint(point.X, point.Y);
    }

    /// <summary>
    /// The line a point of the net stands for — the inverse of <see cref="Project"/>. A point
    /// outside the net is refused.
    /// </summary>
    public static LineOrientation Unproject(StereonetPoint point) =>
        Orientation.EqualAreaNet.Unproject(new Orientation.NetPoint(point.X, point.Y)).FromLibrary();

    /// <summary>The share of the hemisphere Kamb's counting circle covers for <paramref name="count"/> poles.</summary>
    public static double KambCountingAreaFraction(int count) =>
        Orientation.KambDensity.CountingAreaFraction(count);

    /// <summary>
    /// The Kamb density of <paramref name="poles"/> at every node of a square grid laid over the
    /// net, or null when there are no poles to count.
    /// </summary>
    public static StereonetDensity? KambDensity(IReadOnlyList<LineOrientation> poles, int gridSize = DefaultGridSize)
    {
        ArgumentNullException.ThrowIfNull(poles);
        var grid = Orientation.KambDensity.Compute(poles.Select(p => p.ToLibrary()).ToList(), gridSize);
        return grid is null
            ? null
            : new StereonetDensity(
                grid.Size,
                grid.Values,
                grid.CountingAreaFraction,
                grid.ExpectedSigma,
                grid.ContourIntervalSigma,
                grid.MaxSigma);
    }
}
