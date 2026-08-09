// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Content-addressed blob storage seam. Implementations own the physical layout;
/// callers only keep the returned relative storage path (persisted on StoredFile).
/// </summary>
public interface IFileStore
{
    /// <summary>Writes the stream and returns the relative storage path.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Appends to existing content and returns its new total length. Used by resumable
    /// uploads, where a large file arrives a piece at a time and each piece has to land
    /// before the next one is asked for.
    /// </summary>
    /// <remarks>
    /// The returned length is read back from the stored content rather than added up by the
    /// caller. What actually landed is the only thing a resuming client can be told to
    /// continue from — a number kept beside the bytes would, on the one occasion it disagreed
    /// with them, produce a file with a hole in it that nothing would notice until somebody
    /// opened it months later.
    /// </remarks>
    Task<long> AppendAsync(string storagePath, Stream content, CancellationToken ct = default);

    /// <summary>Opens the stored content for reading; throws FileNotFoundException when missing.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Deletes stored content; missing content is not an error.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Absolute filesystem path for tools that need direct file access (e.g. GDAL).</summary>
    string GetAbsolutePath(string storagePath);
}
