// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Turns bytes into the facts a document row is written from: where they landed, what they
/// hash to, what format they actually are, and — for a photograph — where and how it was
/// taken.
///
/// <para>
/// One home for this, because there are now four ways content arrives — a browser upload, the
/// last piece of a resumable one, an entry of an expanded archive, a file on a directory the
/// server walked — and every one of them must describe what it received in exactly the same
/// way. A path that sniffed the format differently, or skipped reading a photograph's
/// position, would produce files that behave differently for no reason a user could see: the
/// same picture would be protected on one route and not on another.
/// </para>
/// <para>
/// Everything here is read from the stored bytes rather than from what the caller claimed.
/// The browser's media type, the archive entry's name and the disk file's extension are all
/// hints; the format is decided by looking at the content, because text extraction later
/// dispatches on the recorded format and a file filed under the wrong one is handed to a
/// reader that cannot read it and silently produces nothing.
/// </para>
/// </summary>
public sealed class ContentIntake(
    IFileStore fileStore,
    IPhotoGeotagReader geotagReader,
    IContentMetadataReader metadataReader)
{
    /// <summary>
    /// Writes a stream into the store and describes what landed.
    /// </summary>
    /// <param name="fileName">
    /// The name the source gave it, used for its extension and as the document's title. Any
    /// folder part is dropped: the storage path is server-generated and never contains
    /// anything the source wrote.
    /// </param>
    /// <param name="declaredMediaType">
    /// What the source said the content is, offered to the sniffer as a hint it may disregard.
    /// </param>
    public async Task<StoredContent> FromStreamAsync(
        Stream content, string fileName, string? declaredMediaType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        var bareName = Path.GetFileName(fileName);
        var storagePath = await fileStore.SaveAsync(content, Path.GetExtension(bareName), ct);
        return await DescribeAsync(storagePath, bareName, declaredMediaType, ct);
    }

    /// <summary>
    /// Describes content that is already in the store — the assembled blob of a resumable
    /// upload, which was written a piece at a time and has never been read as a whole.
    /// </summary>
    public Task<StoredContent> FromStoredAsync(
        string storagePath, string fileName, string? declaredMediaType, CancellationToken ct) =>
        DescribeAsync(storagePath, Path.GetFileName(fileName), declaredMediaType, ct);

    private async Task<StoredContent> DescribeAsync(
        string storagePath, string bareName, string? declaredMediaType, CancellationToken ct)
    {
        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        FileFormat format;
        long sizeBytes;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sizeBytes = saved.Length;
            format = await ContentSniffer.DetectAsync(saved, declaredMediaType, bareName, ct);
        }

        var absolutePath = fileStore.GetAbsolutePath(storagePath);
        return new StoredContent(
            storagePath,
            bareName,
            format.MimeType,
            sizeBytes,
            sha256,
            format.Kind,
            // Gated on the sniffed kind rather than the claimed one, so a photograph uploaded
            // under the wrong media type still has its capture location found — and, more
            // importantly, that location is then protected like any other instead of quietly
            // going unread.
            format.Kind == FileKind.Image ? geotagReader.Read(absolutePath) : null,
            await metadataReader.ReadAsync(absolutePath, format.Kind, ct));
    }
}
