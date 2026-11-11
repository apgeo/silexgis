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
    /// Opens content for writing with nobody else able to open it until the writer is disposed,
    /// creating it empty when it does not exist yet. Used by resumable uploads, where a large
    /// file arrives a piece at a time. Throws <see cref="IOException"/> when another writer
    /// holds it.
    /// </summary>
    /// <remarks>
    /// Exclusive for the whole of what the caller does while it holds the writer, not only for
    /// the copy. A resumable upload reads how far its session has got, writes the piece and
    /// records the new length while holding it, so two copies of one piece cannot both be
    /// judged against the same stale length and both be written.
    /// </remarks>
    Task<IExclusiveContentWriter> OpenExclusiveAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Opens the stored content for reading; throws FileNotFoundException when missing.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Deletes stored content; missing content is not an error.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Absolute filesystem path for tools that need direct file access (e.g. GDAL).</summary>
    string GetAbsolutePath(string storagePath);
}

/// <summary>Content held open by one writer; see <see cref="IFileStore.OpenExclusiveAsync"/>.</summary>
public interface IExclusiveContentWriter : IAsyncDisposable
{
    /// <summary>How many bytes the content holds now.</summary>
    long Length { get; }

    /// <summary>
    /// Writes exactly <paramref name="length"/> bytes read from <paramref name="content"/> at
    /// <paramref name="offset"/>, after discarding anything stored from that offset on. Answers
    /// false, and keeps nothing of the piece, when the content ends before that many bytes or
    /// fails to be read — the sender went away part way.
    /// </summary>
    /// <remarks>
    /// The piece is all or nothing. When the copy is cancelled, or the store itself cannot write
    /// or flush, the content is cut back to <paramref name="offset"/> and the exception leaves:
    /// a failing store is a fault to report, not a piece to ask for again. Either way a piece
    /// cut short never sits in the file ahead of the copy that is sent again. The offset may not
    /// lie beyond the current end: that would leave a hole in the file.
    /// </remarks>
    Task<bool> WriteAtAsync(long offset, Stream content, long length, CancellationToken ct = default);
}
