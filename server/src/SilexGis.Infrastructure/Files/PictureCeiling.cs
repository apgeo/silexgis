// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// The most pixels a stored picture may declare and still be decoded on the server, judged from
/// its header before a single pixel is read.
/// </summary>
/// <remarks>
/// A header states how large the picture claims to be, and a file of a few hundred kilobytes can
/// claim a size whose pixels take gigabytes of memory and then of temporary disk to hold —
/// decoded inside the request that asked for it. Reading the claim costs nothing, so every path
/// that decodes a stored picture whole asks this first.
/// </remarks>
public static class PictureCeiling
{
    /// <summary>
    /// Above the two hundred million the largest phone sensors record, below what one decode
    /// could hold in memory without crowding out everything else the server is doing.
    /// </summary>
    public const long MaxPixels = 256L * 1000 * 1000;

    /// <summary>
    /// Refuses a picture whose header declares more pixels than may be decoded.
    /// </summary>
    /// <param name="everyFrame">
    /// Counts every frame of an animated picture rather than the first alone, for a reader that
    /// decodes them all.
    /// </param>
    /// <exception cref="MagickResourceLimitErrorException">
    /// The picture declares more than <see cref="MaxPixels"/>. A resource-limit failure of the
    /// imaging library, because that is what a decode past its limits would have been, and so
    /// every caller that already contains a picture the library could not handle contains this.
    /// </exception>
    /// <exception cref="MagickException">The header could not be read.</exception>
    public static void Require(string absolutePath, bool everyFrame = false)
    {
        long pixels = 0;
        if (everyFrame)
        {
            using var frames = new MagickImageCollection();
            frames.Ping(absolutePath);
            foreach (var frame in frames)
            {
                pixels += (long)frame.Width * frame.Height;
            }
        }
        else
        {
            using var probe = new MagickImage();
            probe.Ping(absolutePath);
            pixels = (long)probe.Width * probe.Height;
        }

        if (pixels > MaxPixels)
        {
            throw new MagickResourceLimitErrorException(
                "The picture declares more pixels than may be decoded.");
        }
    }
}
