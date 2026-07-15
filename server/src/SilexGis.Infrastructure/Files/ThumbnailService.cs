// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Image thumbnails, generated on first request and cached in the file store under
/// thumbs/. WebP output; EXIF orientation applied; aspect ratio preserved
/// (bounding box of size×size, no upscaling).
/// </summary>
public sealed class ThumbnailService(IFileStore fileStore)
{
    /// <summary>Allowed thumbnail bounding-box sizes (px) — a fixed set keeps the cache small.</summary>
    public static readonly int[] AllowedSizes = [160, 480, 1200];

    /// <summary>Deletes every cached thumbnail size for a file (no-op when none exist).</summary>
    public void Purge(Guid fileId)
    {
        foreach (var size in AllowedSizes)
        {
            var path = fileStore.GetAbsolutePath($"thumbs/{fileId:N}-{size}.webp");
            if (File.Exists(path))
            {
                File.Delete(path);
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

        var cachePath = fileStore.GetAbsolutePath($"thumbs/{fileId:N}-{size}.webp");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        var sourcePath = fileStore.GetAbsolutePath(sourceStoragePath);
        using var image = new MagickImage(sourcePath);
        image.AutoOrient();
        if (image.Width > size || image.Height > size)
        {
            image.Thumbnail(new MagickGeometry((uint)size, (uint)size)); // keeps aspect ratio
        }

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
