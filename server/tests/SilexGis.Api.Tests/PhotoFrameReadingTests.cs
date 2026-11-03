// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Tests;

/// <summary>
/// The frame a photograph is described by — its width, its height and the orientation code the
/// renderer obeys — is read from the file's header without decoding its pixels, and must be the
/// frame a full decode reports, for every format whose decoder could disagree with its header.
/// Places and pins are put on that frame, and renderings are drawn from the decode, so the two
/// disagreeing would turn every position placed on the picture.
/// </summary>
public sealed class PhotoFrameReadingTests : IDisposable
{
    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-photoframe-" + Guid.NewGuid().ToString("N"));

    public PhotoFrameReadingTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that outlives the run is not worth failing a test over.
        }
    }

    [Theory]
    [InlineData(MagickFormat.Avif, "avif", 6)]
    [InlineData(MagickFormat.Avif, "avif", 3)]
    [InlineData(MagickFormat.Jpeg, "jpg", 6)]
    [InlineData(MagickFormat.Png, "png", 8)]
    [InlineData(MagickFormat.Tiff, "tif", 6)]
    public void The_frame_read_from_the_header_is_the_frame_a_decode_reports(
        MagickFormat format, string extension, int code)
    {
        using var image = new MagickImage(MagickColors.White, 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "SilexCam 2.1");
        exif.SetValue(ExifTag.Orientation, (ushort)code);
        image.SetProfile(exif);
        image.Orientation = (OrientationType)code;
        var path = Path.Combine(root, $"picture-{code}.{extension}");
        image.Write(path, format);

        using var decoded = new MagickImage(path);
        var read = new MagickPhotoGeotagReader().Read(path);

        read.WidthPixels.ShouldBe((int)decoded.Width);
        read.HeightPixels.ShouldBe((int)decoded.Height);
        read.Orientation.ShouldBe((int)decoded.Orientation is var o and >= 1 and <= 8 ? o : 1);
    }
}
