// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using ImageMagick;
using Shouldly;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Tests;

/// <summary>
/// A picture handed over by whoever asked for a document is somebody's upload, and it is about to
/// be put inside a file that leaves. These cases are stated over the bytes that come back out:
/// that what was accepted is a picture this application drew, carrying none of what the upload
/// carried, and that everything else is turned away for the reason that is actually true of it.
/// </summary>
/// <remarks>
/// No host and no database: the reader takes bytes and answers bytes or a reason, so every case
/// here is arithmetic on a byte array.
/// </remarks>
public sealed class SuppliedPictureTests
{
    private const long Plenty = 10_000_000;

    [Fact]
    public void A_png_is_drawn_again_and_keeps_its_picture()
    {
        using var source = new MagickImage(MagickColors.SaddleBrown, 64, 48);
        var upload = source.ToByteArray(MagickFormat.Png);

        var reading = SuppliedPicture.Read(upload, Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.None);
        reading.Width.ShouldBe(64u);
        reading.Height.ShouldBe(48u);

        using var redrawn = new MagickImage(reading.Image.ShouldNotBeNull());
        redrawn.Format.ShouldBe(MagickFormat.Png);
        redrawn.Width.ShouldBe(64u);
        redrawn.Height.ShouldBe(48u);
        using var pixels = redrawn.GetPixels();
        var pixel = pixels.GetPixel(10, 10).ToColor().ShouldNotBeNull();
        pixel.R.ShouldBe(MagickColors.SaddleBrown.R);
        pixel.G.ShouldBe(MagickColors.SaddleBrown.G);
        pixel.B.ShouldBe(MagickColors.SaddleBrown.B);
    }

    /// <summary>
    /// Whatever travelled inside the upload stays behind. A picture drawn by a browser carries
    /// nothing, but nothing says the bytes came from the browser: this is a route, and a route is
    /// called by whoever calls it.
    /// </summary>
    [Fact]
    public void A_jpeg_arrives_without_the_position_and_the_words_it_carried()
    {
        using var source = new MagickImage(MagickColors.SteelBlue, 80, 60);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(45u), new Rational(8u), new Rational(22u)]);
        exif.SetValue(ExifTag.ImageDescription, "entrance, north side");
        source.SetProfile(exif);
        source.Comment = "taken at the entrance";
        var upload = source.ToByteArray(MagickFormat.Jpeg);

        // The upload really does carry them, so their absence below is a removal.
        using (var carried = new MagickImage(upload))
        {
            carried.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
            carried.Comment.ShouldBe("taken at the entrance");
        }

        var reading = SuppliedPicture.Read(upload, Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.None);
        var redrawn = reading.Image.ShouldNotBeNull();
        redrawn.ShouldNotBe(upload);
        using var image = new MagickImage(redrawn);
        image.GetExifProfile().ShouldBeNull();
        image.GetXmpProfile().ShouldBeNull();
        image.GetIptcProfile().ShouldBeNull();
        image.Comment.ShouldBeNull();
        Encoding.Latin1.GetString(redrawn).ShouldNotContain("entrance");
    }

    /// <summary>
    /// A see-through picture comes back opaque, on white: the document's pictures are written in
    /// a format with no transparency, and what was stored under a transparent area is usually
    /// black.
    /// </summary>
    [Fact]
    public void A_see_through_picture_is_laid_on_white()
    {
        using var source = new MagickImage(MagickColors.Transparent, 20, 20);
        var reading = SuppliedPicture.Read(source.ToByteArray(MagickFormat.Png), Plenty);

        using var image = new MagickImage(reading.Image.ShouldNotBeNull());
        image.HasAlpha.ShouldBeFalse();
        using var pixels = image.GetPixels();
        var pixel = pixels.GetPixel(5, 5).ToColor().ShouldNotBeNull();
        pixel.R.ShouldBe((byte)255);
        pixel.G.ShouldBe((byte)255);
        pixel.B.ShouldBe((byte)255);
    }

    [Fact]
    public void Nothing_at_all_is_refused_as_empty()
    {
        var reading = SuppliedPicture.Read([], Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.Empty);
        reading.Image.ShouldBeNull();
    }

    /// <summary>
    /// Judged by how the bytes begin and by nothing else. Each of these is a real file of a kind
    /// the imaging library would happily read if it were left to choose a reader for itself —
    /// which is exactly why it is not left to.
    /// </summary>
    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("bmp")]
    [InlineData("svg")]
    [InlineData("vector script")]
    [InlineData("words")]
    public void Anything_that_does_not_begin_as_a_png_or_a_jpeg_is_refused_as_not_a_picture(string kind)
    {
        using var source = new MagickImage(MagickColors.Olive, 16, 16);
        var bytes = kind switch
        {
            "gif" => source.ToByteArray(MagickFormat.Gif),
            "webp" => source.ToByteArray(MagickFormat.WebP),
            "bmp" => source.ToByteArray(MagickFormat.Bmp),
            "svg" => Encoding.UTF8.GetBytes(
                """<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8"/></svg>"""),
            "vector script" => Encoding.UTF8.GetBytes("push graphic-context\nviewbox 0 0 8 8\npop graphic-context\n"),
            _ => Encoding.UTF8.GetBytes("this is not a picture"),
        };

        var reading = SuppliedPicture.Read(bytes, Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.NotAPicture);
        reading.Image.ShouldBeNull();
    }

    /// <summary>
    /// Beginning like a picture is not being one. The reader named by the first bytes is the only
    /// one tried, so a file that opens as one format and continues as something else is a broken
    /// file of that format — never a working file of another.
    /// </summary>
    [Theory]
    [InlineData("png start, then noise")]
    [InlineData("jpeg start, then noise")]
    [InlineData("png start, then a jpeg")]
    [InlineData("png start, then a vector script")]
    [InlineData("a png whose header is whole and whose picture is missing")]
    public void Something_that_begins_like_a_picture_and_is_not_one_is_refused_as_unreadable(string kind)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] jpeg = [0xFF, 0xD8, 0xFF];
        var noise = Enumerable.Range(0, 400).Select(i => (byte)(i * 37 % 251)).ToArray();
        using var other = new MagickImage(MagickColors.Olive, 16, 16);

        byte[] bytes = kind switch
        {
            "png start, then noise" => [.. png, .. noise],
            "jpeg start, then noise" => [.. jpeg, .. noise],
            "png start, then a jpeg" => [.. png, .. other.ToByteArray(MagickFormat.Jpeg)],
            // The first forty bytes of a real file are its signature, the whole of the header
            // that states its size, and the start of the next part — so the size reads
            // perfectly well and it is only the decoding that finds nothing there.
            "a png whose header is whole and whose picture is missing" =>
                [.. other.ToByteArray(MagickFormat.Png)[..40], .. new byte[200]],
            _ => [.. png, .. Encoding.UTF8.GetBytes("push graphic-context\nviewbox 0 0 8 8\npop graphic-context\n")],
        };

        var reading = SuppliedPicture.Read(bytes, Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.Unreadable);
        reading.Image.ShouldBeNull();
    }

    [Fact]
    public void A_picture_of_more_pixels_than_allowed_is_refused_and_says_how_large_it_is()
    {
        using var source = new MagickImage(MagickColors.Gray, 400, 300);
        var upload = source.ToByteArray(MagickFormat.Png);

        var refused = SuppliedPicture.Read(upload, 400 * 300 - 1);
        refused.Fault.ShouldBe(SuppliedPictureFault.TooManyPixels);
        refused.Image.ShouldBeNull();
        refused.Width.ShouldBe(400u);
        refused.Height.ShouldBe(300u);

        // Exactly at the bound is inside it, so the refusal above is the bound and not an
        // off-by-one a real picture would trip.
        SuppliedPicture.Read(upload, 400 * 300).Fault.ShouldBe(SuppliedPictureFault.None);
    }

    /// <summary>
    /// The size a picture claims is read before the picture is decoded.
    /// </summary>
    /// <remarks>
    /// The file here is a few hundred bytes and its header declares thirty thousand pixels a
    /// side. Decoding it would mean asking for the memory nine hundred million pixels take, and a
    /// reader that decoded first and measured afterwards would find out the size only by having
    /// already paid for it. That it is refused on the declared size — the width and height in the
    /// answer are the header's — is the whole of what this asserts.
    /// </remarks>
    [Fact]
    public void A_small_file_that_declares_an_enormous_picture_is_refused_on_what_it_declares()
    {
        using var source = new MagickImage(MagickColors.Gray, 8, 8);
        var upload = WithDeclaredSize(source.ToByteArray(MagickFormat.Png), 30_000, 30_000);
        upload.Length.ShouldBeLessThan(4096);

        var reading = SuppliedPicture.Read(upload, Plenty);

        reading.Fault.ShouldBe(SuppliedPictureFault.TooManyPixels);
        reading.Width.ShouldBe(30_000u);
        reading.Height.ShouldBe(30_000u);
        reading.Image.ShouldBeNull();
    }

    /// <summary>
    /// The same PNG with another width and height written into its header, and the header's
    /// checksum made right again so that the lie is the only thing wrong with the file.
    /// </summary>
    private static byte[] WithDeclaredSize(byte[] png, uint width, uint height)
    {
        // Eight bytes of signature, four of length, then "IHDR" and its thirteen bytes of data;
        // the checksum that follows covers the type and the data.
        var altered = (byte[])png.Clone();
        Encoding.ASCII.GetString(altered, 12, 4).ShouldBe("IHDR");
        BinaryPrimitives.WriteUInt32BigEndian(altered.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(altered.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(altered.AsSpan(29, 4), Crc32(altered.AsSpan(12, 17)));
        return altered;
    }

    /// <summary>The checksum the PNG format uses, computed the slow, obvious way.</summary>
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
