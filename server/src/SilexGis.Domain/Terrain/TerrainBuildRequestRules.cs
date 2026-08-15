// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Terrain;

/// <summary>Why a request to build terrain was refused, as a stable code and a sentence.</summary>
/// <param name="Code">The reason, in a form a screen can translate.</param>
/// <param name="Detail">The same reason in words, naming the limit that was passed.</param>
public sealed record TerrainBuildRefusal(string Code, string Detail);

/// <summary>
/// What an installation will accept as a request to build terrain over a rectangle.
/// </summary>
/// <remarks>
/// The rules live in one place because the same rectangle is judged more than once — when the
/// request arrives, and again by anything that later re-runs or copies a build — and two copies of
/// "how big is too big" would drift apart into a surface that accepts what the worker then refuses.
/// They are also the only thing standing between a mis-drawn rectangle and hours of work: an area
/// asked for by dragging on a world map is very easy to get wrong by a factor of a hundred, and
/// nothing downstream would notice until the disk filled.
/// </remarks>
public static class TerrainBuildRequestRules
{
    /// <summary>The rectangle is not a rectangle, or does not lie on the Earth.</summary>
    public const string ExtentInvalidCode = "terrain_build.extent_invalid";

    /// <summary>The rectangle is larger than this installation will build in one go.</summary>
    public const string ExtentTooLargeCode = "terrain_build.extent_too_large";

    /// <summary>The deepest level asked for is outside the range that can be built.</summary>
    public const string DepthInvalidCode = "terrain_build.depth_invalid";

    /// <summary>
    /// The largest rectangle accepted, in square degrees.
    /// </summary>
    /// <remarks>
    /// A one-degree square is roughly twelve thousand square kilometres at the latitudes this is
    /// built for, so this admits a whole country and refuses a continent. It is a guard against a
    /// rectangle drawn wrong, not a ration: cost tracks the number of tiles, which tracks how much
    /// fine data the area holds, so a large area of coarse data is cheap and a small area of half-
    /// metre lidar is not. Area alone cannot express that, and pretending otherwise would refuse
    /// the cheap request and admit the expensive one.
    /// </remarks>
    public const double MaxAreaSquareDegrees = 25d;

    /// <summary>
    /// The shallowest and deepest pyramid levels a build may ask for.
    /// </summary>
    /// <remarks>
    /// Thirty-metre elevation data runs out of detail around level 13, which is the working
    /// default; half-metre lidar earns 16 or 17. Past 18 the tile count multiplies with nothing in
    /// any input to fill the extra levels with, and below 6 a single tile covers more ground than
    /// any elevation model is asked to describe.
    /// </remarks>
    public const int MinDepth = 6;

    /// <inheritdoc cref="MinDepth"/>
    public const int MaxDepth = 18;

    /// <summary>
    /// The reason this request cannot be built, or null when it can be.
    /// </summary>
    public static TerrainBuildRefusal? Refuse(
        double west, double south, double east, double north, int maxDepth)
    {
        if (!IsFinite(west) || !IsFinite(south) || !IsFinite(east) || !IsFinite(north))
        {
            return new TerrainBuildRefusal(
                ExtentInvalidCode, "The area must be given as four finite coordinates.");
        }

        // Ordered corners, not merely distinct ones: a rectangle whose east edge is west of its
        // west edge describes the rest of the world rather than nothing, and a zero-width one is a
        // line with no ground under it.
        if (west >= east || south >= north)
        {
            return new TerrainBuildRefusal(
                ExtentInvalidCode,
                "The area must have its east edge east of its west edge and its north edge north of its south edge.");
        }

        if (west < -180d || east > 180d || south < -90d || north > 90d)
        {
            return new TerrainBuildRefusal(
                ExtentInvalidCode,
                "The area must lie within longitude -180 to 180 and latitude -90 to 90.");
        }

        var area = (east - west) * (north - south);
        if (area > MaxAreaSquareDegrees)
        {
            return new TerrainBuildRefusal(
                ExtentTooLargeCode,
                $"The area covers {area:0.##} square degrees; the most this installation builds in "
                    + $"one go is {MaxAreaSquareDegrees:0.##}. Build it as several smaller areas.");
        }

        if (maxDepth < MinDepth || maxDepth > MaxDepth)
        {
            return new TerrainBuildRefusal(
                DepthInvalidCode,
                $"The deepest level must be between {MinDepth} and {MaxDepth}.");
        }

        return null;
    }

    /// <summary>
    /// The rectangle as the geometry a build stores, in WGS84.
    /// </summary>
    /// <remarks>
    /// Wound anticlockwise from the south-west corner and closed on that corner, which is the ring
    /// order a valid polygon's outer boundary takes. Callers pass corners that
    /// <see cref="Refuse"/> has already accepted.
    /// </remarks>
    public static Polygon Rectangle(double west, double south, double east, double north) =>
        NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePolygon(
        [
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south),
        ]);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
