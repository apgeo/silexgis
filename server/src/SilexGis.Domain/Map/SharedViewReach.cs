// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Map;

/// <summary>
/// The part of the world a shared view's link answers about, and the lowest zoom it answers at:
/// the view's own extent on a reference screen, with one extent of margin on every side.
/// </summary>
/// <remarks>
/// <para>
/// <b>A link shares a view, not the installation.</b> Without a reach of its own, the link's data
/// routes take any window, and a link to one valley becomes an anonymous API over every public row
/// in the register — a window moved across the globe reads them all, exact positions of the
/// unprotected ones included. So the data routes clip every window to this and answer nothing
/// outside it, and the zoom they are asked at is held to <see cref="LowestZoom"/> or closer.
/// </para>
/// <para>
/// <b>Worked out on the server, from the stored camera.</b> The page's own screen size is not
/// asked: whatever a page says about itself is something its holder can say too. The reference
/// screen is a large desktop one, so a link opened at the saved zoom draws to the edges of any
/// screen it is likely to meet, and the margin lets its reader pan about a screen's width in each
/// direction before the data stops and only the background goes on.
/// </para>
/// <para>
/// Longitudes are held at the antimeridian rather than wrapped round it: a view saved beside it
/// shares its own side. A view saved far enough out covers the whole world, and its link then
/// answers for the whole world — what its owner chose to share.
/// </para>
/// </remarks>
/// <param name="LowestZoom">The furthest out the data routes answer at; a request further out is answered at this.</param>
public sealed record SharedViewReach(double West, double South, double East, double North, int LowestZoom)
{
    /// <summary>The width, in screen pixels, of the screen the saved view's extent is measured on.</summary>
    public const int ReferenceWidthPixels = 1920;

    /// <summary>The height, in screen pixels, of that screen.</summary>
    public const int ReferenceHeightPixels = 1080;

    /// <summary>How many extents of the view are added on each side of it.</summary>
    public const int MarginExtents = 1;

    /// <summary>How many zoom levels out from the saved zoom the data routes still answer at.</summary>
    public const int ZoomOutLevels = 2;

    private const double EarthRadiusMeters = 6378137d;
    private const double TilePixels = 256d;

    /// <summary>The web-mercator pyramid's own edge: the latitude its square world ends at.</summary>
    private const double MercatorLatitudeLimit = 85.0511287798066;

    /// <summary>The reach of a view saved at this centre and zoom.</summary>
    public static SharedViewReach Around(double longitude, double latitude, double zoom)
    {
        var metersPerPixel = 2 * Math.PI * EarthRadiusMeters / (TilePixels * Math.Pow(2, zoom));
        var extents = 1 + (2 * MarginExtents);
        var halfWidth = extents * ReferenceWidthPixels / 2d * metersPerPixel;
        var halfHeight = extents * ReferenceHeightPixels / 2d * metersPerPixel;

        var x = EarthRadiusMeters * ToRadians(longitude);
        var clampedLatitude = Math.Clamp(latitude, -MercatorLatitudeLimit, MercatorLatitudeLimit);
        var y = EarthRadiusMeters * Math.Log(Math.Tan((Math.PI / 4) + (ToRadians(clampedLatitude) / 2)));

        return new SharedViewReach(
            Math.Max(-180d, ToDegrees((x - halfWidth) / EarthRadiusMeters)),
            LatitudeAt(y - halfHeight),
            Math.Min(180d, ToDegrees((x + halfWidth) / EarthRadiusMeters)),
            LatitudeAt(y + halfHeight),
            Math.Max(0, (int)Math.Floor(zoom) - ZoomOutLevels));
    }

    /// <summary>
    /// The part of a requested window inside the reach, or null when the two do not overlap —
    /// which the data routes answer with nothing, in the shape they always answer.
    /// </summary>
    public (double West, double South, double East, double North)? Clip(
        double west, double south, double east, double north)
    {
        var clippedWest = Math.Max(west, West);
        var clippedSouth = Math.Max(south, South);
        var clippedEast = Math.Min(east, East);
        var clippedNorth = Math.Min(north, North);
        return clippedWest < clippedEast && clippedSouth < clippedNorth
            ? (clippedWest, clippedSouth, clippedEast, clippedNorth)
            : null;
    }

    private static double LatitudeAt(double mercatorY) =>
        ToDegrees((2 * Math.Atan(Math.Exp(mercatorY / EarthRadiusMeters))) - (Math.PI / 2));

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    private static double ToDegrees(double radians) => radians * 180d / Math.PI;
}
