// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Everything a photograph states about where and how it was taken.
///
/// <para>
/// The two halves are kept apart on purpose. <see cref="Point"/>, <see cref="AltitudeMeters"/>,
/// <see cref="DirectionDegrees"/> and <see cref="AccuracyMeters"/> describe a position, and a
/// position is protected; <see cref="Exif"/> describes a camera, and a camera is not. A
/// response that may show one and withhold the other needs them separable, and the cheapest
/// place to separate them is where they are read.
/// </para>
/// </summary>
public sealed record PhotoCapture(
    Point? Point = null,
    double? AltitudeMeters = null,
    double? DirectionDegrees = null,
    bool DirectionIsMagnetic = false,
    double? Dop = null,
    PhotoExif? Exif = null)
{
    /// <summary>A file that stated nothing, or one nothing here could read.</summary>
    public static readonly PhotoCapture None = new();
}

/// <summary>Reads what a photograph records about its own capture.</summary>
public interface IPhotoGeotagReader
{
    /// <summary>
    /// What the image at <paramref name="absolutePath"/> states about itself. Never throws:
    /// content that is not a readable image yields <see cref="PhotoCapture.None"/>, because
    /// failing to describe an upload is not a reason to refuse it.
    /// </summary>
    PhotoCapture Read(string absolutePath);
}

/// <summary>Magick.NET-backed EXIF reader.</summary>
public sealed class MagickPhotoGeotagReader : IPhotoGeotagReader
{
    public PhotoCapture Read(string absolutePath)
    {
        try
        {
            using var image = new MagickImage(absolutePath);
            var exif = image.GetExifProfile();
            if (exif is null)
            {
                return PhotoCapture.None;
            }

            return new PhotoCapture(
                ReadPoint(exif),
                ReadAltitude(exif),
                ReadDirection(exif, out var magnetic),
                magnetic,
                // What the fix says about its own quality. EXIF's metres-valued horizontal
                // error is a tag the imaging library this project carries does not know, and
                // deriving metres from this number would mean assuming a receiver error figure
                // — a number that looks measured and is not, which is worse than the honest
                // dimensionless one the file actually states.
                Positive(Rational(exif, ExifTag.GPSDOP)),
                ReadCamera(exif, image));
        }
        catch (MagickException)
        {
            // Corrupt or non-image content — nothing recorded rather than a failed upload.
            return PhotoCapture.None;
        }
    }

    // ---------- position ----------

    private static Point? ReadPoint(IExifProfile exif)
    {
        var latitude = ReadDegrees(exif, ExifTag.GPSLatitude, exif.GetValue(ExifTag.GPSLatitudeRef)?.Value, 'S');
        var longitude = ReadDegrees(exif, ExifTag.GPSLongitude, exif.GetValue(ExifTag.GPSLongitudeRef)?.Value, 'W');
        if (latitude is null || longitude is null
            // A zero-denominator rational (common in malformed EXIF) yields NaN, which slips
            // past the range check below (every comparison against NaN is false) and would
            // otherwise be stored as POINT(NaN NaN); reject non-finite values up front.
            || !double.IsFinite(latitude.Value) || !double.IsFinite(longitude.Value)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180
            || (latitude == 0 && longitude == 0)) // null-island: almost always a zeroed tag, not a real fix
        {
            return null;
        }

        return new Point(longitude.Value, latitude.Value) { SRID = 4326 };
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

    /// <summary>
    /// Capture altitude in metres. The reference byte says which side of sea level the value is
    /// on — one means below, and a cave photographed from inside a doline is exactly where that
    /// is not merely theoretical.
    /// </summary>
    private static double? ReadAltitude(IExifProfile exif)
    {
        var altitude = Rational(exif, ExifTag.GPSAltitude);
        if (altitude is null || !double.IsFinite(altitude.Value))
        {
            return null;
        }

        var belowSeaLevel = exif.GetValue(ExifTag.GPSAltitudeRef)?.Value == 1;
        var metres = belowSeaLevel ? -altitude.Value : altitude.Value;

        // Beyond these an "altitude" is a damaged tag rather than a place anyone stood.
        return metres is < -12_000 or > 12_000 ? null : metres;
    }

    /// <summary>
    /// Which way the camera was pointing, normalised into 0–360. The reference tag says whether
    /// the bearing is from true or magnetic north; the two differ by enough here to matter when
    /// somebody is using the photograph to find a hole in a forest, so which one it is travels
    /// with the number rather than being assumed.
    /// </summary>
    private static double? ReadDirection(IExifProfile exif, out bool magnetic)
    {
        magnetic = string.Equals(
            exif.GetValue(ExifTag.GPSImgDirectionRef)?.Value, "M", StringComparison.OrdinalIgnoreCase);

        var direction = Rational(exif, ExifTag.GPSImgDirection);
        if (direction is null || !double.IsFinite(direction.Value))
        {
            magnetic = false;
            return null;
        }

        var degrees = direction.Value % 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    // ---------- camera ----------

    private static PhotoExif ReadCamera(IExifProfile exif, IMagickImage image)
    {
        var camera = new PhotoExif(
            CameraMake: Text(exif, ExifTag.Make),
            CameraModel: Text(exif, ExifTag.Model),
            Lens: Text(exif, ExifTag.LensModel),
            Orientation: exif.GetValue(ExifTag.Orientation)?.Value is { } orientation and >= 1 and <= 8
                ? orientation
                : null,
            ExposureSeconds: Positive(Rational(exif, ExifTag.ExposureTime)),
            FNumber: Positive(Rational(exif, ExifTag.FNumber)),
            Iso: exif.GetValue(ExifTag.ISOSpeedRatings)?.Value is { Length: > 0 } iso ? iso[0] : null,
            FocalLengthMm: Positive(Rational(exif, ExifTag.FocalLength)),
            WidthPixels: image.Width > 0 ? (int)image.Width : null,
            HeightPixels: image.Height > 0 ? (int)image.Height : null);

        return camera == PhotoExif.None ? PhotoExif.None : camera;
    }

    private static string? Text(IExifProfile exif, ExifTag<string> tag)
    {
        var value = exif.GetValue(tag)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : Truncate(value);
    }

    /// <summary>
    /// Bounded because it is uploader-supplied text that ends up in a stored document; a camera
    /// name longer than this is a damaged tag, not a camera.
    /// </summary>
    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200];

    private static double? Rational(IExifProfile exif, ExifTag<Rational> tag) =>
        exif.GetValue(tag)?.Value.ToDouble();

    private static double? Positive(double? value) =>
        value is { } number && double.IsFinite(number) && number > 0 ? number : null;
}
