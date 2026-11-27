// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>
/// How much scratch space a render needs on its node, estimated from its bundle: what a node that
/// says it has less free is not given.
/// </summary>
/// <remarks>
/// <para>
/// The node unpacks the bundle into its scratch folder and renders a film in two stages, every
/// frame as a picture first and the film from them, so the frames stay there until the film is
/// made. The estimate is the bundle unpacked, plus every frame as a PNG (about 2 MB at 1920×1080,
/// scaled by the pixels and never under 64 KB, plus 32 MB for what surrounds them), plus the film
/// (about a fifth of a byte a pixel a frame), all one and a half times over: the figures the render
/// worker's own disk check counts frames and films by.
/// </para>
/// <para>
/// It is meant to catch "nowhere near enough", not to be exact: a node whose disk is a little
/// short is still told no rather than given a render that fails at its last frame.
/// </para>
/// </remarks>
public static class RenderScratch
{
    /// <summary>A PNG frame at 1920×1080, in bytes.</summary>
    public const double FrameBytesAt1080p = 2_000_000;

    /// <summary>The least a frame is counted at.</summary>
    public const double MinFrameBytes = 64_000;

    /// <summary>What surrounds the frames: the film's other files, the logs, the job's folder.</summary>
    public const long FixedMarginBytes = 32_000_000;

    /// <summary>How many times over the estimate is asked for.</summary>
    public const double SafetyFactor = 1.5;

    /// <summary>
    /// The estimate for a bundle of <paramref name="frames"/> frames at
    /// <paramref name="width"/>×<paramref name="height"/>; a draft is rendered at half that.
    /// </summary>
    public static long Bytes(byte[] bundle, int frames, int width, int height, bool draft)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var scale = draft ? 0.5 : 1;
        var pixels = Math.Max(1L, (long)Math.Round(width * scale)) * Math.Max(1L, (long)Math.Round(height * scale));
        var count = Math.Max(1, frames);
        var frameBytes = Math.Max(MinFrameBytes, FrameBytesAt1080p * pixels / (1920.0 * 1080.0)) * count + FixedMarginBytes;
        var filmBytes = pixels * 0.2 * count;
        return (long)((Unpacked(bundle) + frameBytes + filmBytes) * SafetyFactor);
    }

    /// <summary>What the bundle takes unpacked: its entries' own lengths, read from its directory.</summary>
    private static long Unpacked(byte[] bundle)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bundle, writable: false), ZipArchiveMode.Read);
            return Math.Max(bundle.LongLength, zip.Entries.Sum(e => e.Length));
        }
        catch (InvalidDataException)
        {
            return bundle.LongLength;
        }
    }
}
