// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;

namespace SilexGis.Infrastructure.Documents;

/// <summary>Why a picture somebody handed over was not taken.</summary>
public enum SuppliedPictureFault
{
    /// <summary>It was taken.</summary>
    None = 0,

    /// <summary>There were no bytes at all.</summary>
    Empty = 1,

    /// <summary>The bytes do not begin the way a PNG or a JPEG begins.</summary>
    NotAPicture = 2,

    /// <summary>The picture declares more pixels than the caller allows.</summary>
    TooManyPixels = 3,

    /// <summary>It begins like a picture and cannot be read as one.</summary>
    Unreadable = 4,
}

/// <summary>What became of a picture somebody handed over.</summary>
/// <param name="Image">
/// The picture redrawn by this application as a PNG, or null when it was not taken. Never the
/// bytes that arrived.
/// </param>
/// <param name="Fault">Why it was not taken; <see cref="SuppliedPictureFault.None"/> when it was.</param>
/// <param name="Width">Its width in pixels, once that could be read; zero before.</param>
/// <param name="Height">Its height in pixels, once that could be read; zero before.</param>
public sealed record SuppliedPictureReading(
    byte[]? Image, SuppliedPictureFault Fault, uint Width, uint Height);

/// <summary>
/// A picture handed to this application by whoever is asking it for a document, made fit to be
/// placed in one.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in a generated document is drawn or written here, from what the application
/// holds. A picture a caller supplies is the exception: it is somebody's upload, and it is about
/// to be put inside a file that is forwarded and opened by word processors of every vintage. So
/// nothing of what arrived is passed on. The picture is decoded and drawn again, which leaves
/// behind every metadata profile, every comment and every trailing byte the upload carried, and
/// turns a file that is subtly wrong into either a plain picture or a refusal.
/// </para>
/// <para>
/// Engine-facing and I/O-free: bytes in, bytes or a reason out. How large an upload may be in
/// bytes is the caller's to check before it reads one into memory at all.
/// </para>
/// </remarks>
public static class SuppliedPicture
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads a picture and draws it again, or says why not.
    /// </summary>
    /// <param name="bytes">The upload, whole.</param>
    /// <param name="maxPixels">The most pixels the picture may declare, width times height.</param>
    public static SuppliedPictureReading Read(byte[] bytes, long maxPixels)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            return Refused(SuppliedPictureFault.Empty);
        }

        if (FormatOf(bytes) is not { } format)
        {
            return Refused(SuppliedPictureFault.NotAPicture);
        }

        // The decoder is told which format to read and is never left to work it out. Left to
        // itself it picks a reader by what the bytes look like, and it has readers for formats
        // that are instructions rather than pictures — vector scripts, formats that name other
        // files to pull in. The two formats named here are plain rasters; the signature above
        // decides which, and nothing after it can change the answer.
        var settings = new MagickReadSettings { Format = format };

        try
        {
            uint width;
            uint height;

            // Measured before it is decoded. The header says how large the picture claims to
            // be, and a file of a few kilobytes can claim a size whose pixels would take
            // gigabytes to hold — so the claim is read on its own and judged first.
            using (var probe = new MagickImage())
            {
                probe.Ping(bytes, settings);
                width = probe.Width;
                height = probe.Height;
            }

            if (width == 0 || height == 0)
            {
                return Refused(SuppliedPictureFault.Unreadable);
            }

            if ((long)width * height > maxPixels)
            {
                return new SuppliedPictureReading(null, SuppliedPictureFault.TooManyPixels, width, height);
            }

            using var image = new MagickImage(bytes, settings);

            // Asked again of what was actually decoded: the bound is on the pixels held, and the
            // first answer was only what the header said.
            if ((long)image.Width * image.Height > maxPixels)
            {
                return new SuppliedPictureReading(
                    null, SuppliedPictureFault.TooManyPixels, image.Width, image.Height);
            }

            image.Strip();

            // Laid on white, so that what leaves here is opaque. A document's pictures are
            // written in a format with no transparency, and a see-through area handed to it
            // comes out as whatever colour happened to be stored underneath — usually black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);

            return new SuppliedPictureReading(
                image.ToByteArray(MagickFormat.Png), SuppliedPictureFault.None, image.Width, image.Height);
        }
        catch (MagickException)
        {
            // Began like a picture and is not one the decoder can finish. Nothing about why is
            // passed on: the decoder's own message quotes the input.
            return Refused(SuppliedPictureFault.Unreadable);
        }
    }

    /// <summary>
    /// Which of the two accepted formats the bytes begin as, read off the bytes themselves.
    /// </summary>
    /// <remarks>
    /// Never off a file name or a declared media type: both are words the sender chose, and a
    /// sender who wanted another reader used would simply choose different ones.
    /// </remarks>
    private static MagickFormat? FormatOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngSignature.AsSpan()))
        {
            return MagickFormat.Png;
        }

        // Start-of-image, then the first marker's lead byte.
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return MagickFormat.Jpeg;
        }

        return null;
    }

    private static SuppliedPictureReading Refused(SuppliedPictureFault fault) =>
        new(null, fault, 0, 0);
}
