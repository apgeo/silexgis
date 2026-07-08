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

    /// <summary>Opens the stored content for reading; throws FileNotFoundException when missing.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Deletes stored content; missing content is not an error.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Absolute filesystem path for tools that need direct file access (e.g. GDAL).</summary>
    string GetAbsolutePath(string storagePath);
}
