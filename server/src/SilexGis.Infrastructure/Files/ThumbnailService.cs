// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using SilexGis.Domain;
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Image thumbnails, generated on first request and cached in the file store under
/// thumbs/. WebP output; EXIF orientation applied; aspect ratio preserved
/// (bounding box of size×size, no upscaling); every metadata profile removed.
/// </summary>
/// <remarks>
/// A derivative is handed out far more freely than the image it came from — it is what a
/// listing, a gallery tile and a map pin all load — so it carries none of the original's
/// metadata. That matters most for a photo taken at a cave: its EXIF holds the GPS fix,
/// which is the position itself and not a fact about it, so a thumbnail that kept the
/// profile would publish the entrance to anyone allowed to see a 160px picture of it.
/// </remarks>
public sealed class ThumbnailService(IFileStore fileStore)
{
    /// <summary>Allowed thumbnail bounding-box sizes (px) — a fixed set keeps the cache small.</summary>
    /// <remarks>
    /// The largest is what a full-screen viewer shows and what somebody zooming into a
    /// photograph is looking at, so it is well past a screen's own width: a 60-megapixel
    /// panorama shown at 1200 is unreadable the moment anybody enlarges it. Every size is still
    /// a rendering — none of them is the upload, and none carries its metadata.
    /// </remarks>
    public static readonly int[] AllowedSizes = [160, 480, 1200, 2400];

    /// <summary>
    /// Cache names of thumbnails written before metadata removal became unconditional.
    /// Those files can still hold the source's GPS, so the name they were written under is
    /// never read again — it is only ever deleted. Purging a file removes both names so an
    /// operator clearing one image clears its history of it too; anything else left behind
    /// is unreachable, because no code path constructs the old name.
    /// </summary>
    private static string LegacyCachePath(Guid fileId, int size) => $"thumbs/{fileId:N}-{size}.webp";

    /// <summary>
    /// Where a rendering is cached.
    /// </summary>
    /// <remarks>
    /// The turn is part of the name, so rotating a picture does not need the cache cleared and
    /// turning it back costs nothing — the earlier rendering is still there. It also makes the
    /// wrong answer impossible rather than unlikely: a cache keyed without it would serve the
    /// old orientation until something remembered to purge, and "something remembered" is what
    /// stale caches are made of.
    /// </remarks>
    private static string CachePath(Guid fileId, int size, int quarterTurns) =>
        quarterTurns == 0
            ? $"thumbs/{fileId:N}-{size}-nometa.webp"
            : $"thumbs/{fileId:N}-{size}-r{quarterTurns}-nometa.webp";

    /// <summary>Deletes every cached rendering of a file (no-op when none exist).</summary>
    public void Purge(Guid fileId)
    {
        foreach (var size in AllowedSizes)
        {
            var names = new List<string> { LegacyCachePath(fileId, size) };
            for (var turn = 0; turn < PhotoOrientation.Turns; turn++)
            {
                names.Add(CachePath(fileId, size, turn));
            }

            foreach (var relative in names)
            {
                var path = fileStore.GetAbsolutePath(relative);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>
    /// Returns the absolute path of the cached rendering, creating it when missing.
    /// </summary>
    /// <param name="quarterTurns">
    /// The turn somebody recorded for this picture, applied on top of whatever the camera said.
    /// Rotating is stored rather than written back into the upload, so this is where it takes
    /// effect — and it takes effect on every rendering, which is everything anybody is ever
    /// shown.
    /// </param>
    public async Task<string> GetOrCreateAsync(
        Guid fileId, string sourceStoragePath, int size, CancellationToken ct, int quarterTurns = 0)
    {
        if (!AllowedSizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported thumbnail size.");
        }

        var turns = PhotoOrientation.Normalize(quarterTurns);
        var cachePath = fileStore.GetAbsolutePath(CachePath(fileId, size, turns));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        var sourcePath = fileStore.GetAbsolutePath(sourceStoragePath);
        using var image = new MagickImage(sourcePath);
        image.AutoOrient(); // bakes the rotation in and clears the tag it read, so do it first

        // Then the turn somebody recorded, which is a correction on top of what the camera
        // said — a picture needs turning precisely when the camera got it wrong or said
        // nothing, so this is applied after the tag rather than instead of it. Before the
        // resize, so the bounding box is measured against the shape that will be shown.
        if (turns != 0)
        {
            image.Rotate(PhotoOrientation.Degrees(turns));
        }

        if (image.Width > size || image.Height > size)
        {
            image.Thumbnail(new MagickGeometry((uint)size, (uint)size)); // keeps aspect ratio
        }

        // Unconditional, and deliberately not folded into the branch above: resizing happens
        // to drop the profiles as a side effect, so an image already smaller than the box
        // would otherwise be copied out with its EXIF — including any GPS fix — intact. The
        // common case is the largest size, where most photos need no resizing at all.
        image.Strip();

        image.Quality = 82;
        // Write to a temp name then move — concurrent first requests must not serve half files.
        var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        await image.WriteAsync(tempPath, MagickFormat.WebP, ct);
        try
        {
            File.Move(tempPath, cachePath, overwrite: false);
        }
        catch (IOException)
        {
            File.Delete(tempPath); // another request won the race; its result is fine
        }

        return cachePath;
    }
}
