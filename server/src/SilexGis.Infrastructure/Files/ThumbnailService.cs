// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using SilexGis.Domain;

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
    public static readonly int[] AllowedSizes = [160, 480, 1200];

    /// <summary>
    /// Cache names of thumbnails written before metadata removal became unconditional.
    /// Those files can still hold the source's GPS, so the name they were written under is
    /// never read again — it is only ever deleted. Purging a file removes both names so an
    /// operator clearing one image clears its history of it too; anything else left behind
    /// is unreachable, because no code path constructs the old name.
    /// </summary>
    private static string LegacyCachePath(Guid fileId, int size) => $"thumbs/{fileId:N}-{size}.webp";

    private static string CachePath(Guid fileId, int size) => $"thumbs/{fileId:N}-{size}-nometa.webp";

    /// <summary>Deletes every cached thumbnail size for a file (no-op when none exist).</summary>
    public void Purge(Guid fileId)
    {
        foreach (var size in AllowedSizes)
        {
            foreach (var relative in new[] { CachePath(fileId, size), LegacyCachePath(fileId, size) })
            {
                var path = fileStore.GetAbsolutePath(relative);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>Returns the absolute path of the cached thumbnail, creating it when missing.</summary>
    public async Task<string> GetOrCreateAsync(Guid fileId, string sourceStoragePath, int size, CancellationToken ct)
    {
        if (!AllowedSizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported thumbnail size.");
        }

        var cachePath = fileStore.GetAbsolutePath(CachePath(fileId, size));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        var sourcePath = fileStore.GetAbsolutePath(sourceStoragePath);
        using var image = new MagickImage(sourcePath);
        image.AutoOrient(); // bakes the rotation in and clears the tag it read, so do it first

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
