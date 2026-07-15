// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using NetTopologySuite.Geometries;

namespace SilexGis.Infrastructure.Files;

/// <summary>Reads a photo's capture location from its EXIF GPS tags.</summary>
public interface IPhotoGeotagReader
{
    /// <summary>
    /// The EXIF GPS point of the image at <paramref name="absolutePath"/> as a WGS84
    /// <see cref="Point"/>, or null when the file is not a readable image, carries no GPS,
    /// or the coordinates are out of range.
    /// </summary>
    Point? TryReadPoint(string absolutePath);
}

/// <summary>Magick.NET-backed EXIF GPS reader.</summary>
public sealed class MagickPhotoGeotagReader : IPhotoGeotagReader
{
    public Point? TryReadPoint(string absolutePath)
    {
        try
        {
            using var image = new MagickImage(absolutePath);
            var exif = image.GetExifProfile();
            if (exif is null)
            {
                return null;
            }

            var latitude = ReadDegrees(exif, ExifTag.GPSLatitude, exif.GetValue(ExifTag.GPSLatitudeRef)?.Value, 'S');
            var longitude = ReadDegrees(exif, ExifTag.GPSLongitude, exif.GetValue(ExifTag.GPSLongitudeRef)?.Value, 'W');
            if (latitude is null || longitude is null
                || latitude is < -90 or > 90 || longitude is < -180 or > 180
                || (latitude == 0 && longitude == 0)) // null-island: almost always a zeroed tag, not a real fix
            {
                return null;
            }

            return new Point(longitude.Value, latitude.Value) { SRID = 4326 };
        }
        catch (MagickException)
        {
            // Corrupt or non-image content — no geotag rather than a failed upload.
            return null;
        }
    }

    /// <summary>
    /// EXIF stores a coordinate as three rationals (degrees, minutes, seconds) plus a hemisphere
    /// reference ("N"/"S", "E"/"W"); the reference letters marking the negative axis are passed
    /// in <paramref name="negativeRef"/>.
    /// </summary>
    private static double? ReadDegrees(
        IExifProfile exif, ExifTag<Rational[]> tag, string? reference, char negativeRef)
    {
        var dms = exif.GetValue(tag)?.Value;
        if (dms is null || dms.Length < 3 || string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var degrees = dms[0].ToDouble() + (dms[1].ToDouble() / 60d) + (dms[2].ToDouble() / 3600d);
        return char.ToUpperInvariant(reference[0]) == negativeRef ? -degrees : degrees;
    }
}
