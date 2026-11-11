// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using SilexGis.Domain.Scans;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// One entry of a scan's stored tileset archive, opened for reading. Disposing the content
/// closes the archive behind it, so a caller that hands the stream on to be written out needs
/// to hold nothing else.
/// </summary>
/// <param name="Content">The entry's bytes, inflated as they are read; not seekable.</param>
/// <param name="Length">The entry's size once inflated, as the archive's directory states it.</param>
/// <param name="Crc32">The archive's checksum of the entry, which names these exact bytes.</param>
/// <param name="ContentType">The media type the entry is served as.</param>
public sealed record TilesetEntry(Stream Content, long Length, uint Crc32, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}

/// <summary>
/// Reads single entries out of the zip archive a tiling run stored as a scan's converted file.
/// The archive is never unpacked: a viewer asks for the root tileset and then for each tile by
/// the name the tileset gives it, and each is inflated straight from the stored file.
/// </summary>
public static class TilesetArchive
{
    /// <summary>
    /// Opens the named entry of the archive at <paramref name="archivePath"/>, or answers null when
    /// the name is not one this application serves, the archive is missing or unreadable, or it
    /// holds no such entry. Every way of not having the entry is the same null on purpose: the
    /// caller answers "not found" and must not be able to tell them apart.
    /// </summary>
    public static TilesetEntry? Open(string archivePath, string? entryName)
    {
        var contentType = TilesetEntryNames.ContentTypeOf(entryName);
        if (contentType is null)
        {
            return null;
        }

        ZipArchive? archive = null;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry(entryName!);
            if (entry is null || entry.FullName != entryName)
            {
                archive.Dispose();
                return null;
            }

            // The stream handed out owns the archive: a tile is read once, from start to end,
            // and whoever reads it disposes it, which is when the stored file is closed.
            return new TilesetEntry(new OwningStream(entry.Open(), archive), entry.Length, entry.Crc32, contentType);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            archive?.Dispose();
            return null;
        }
    }

    /// <summary>A read-only stream that disposes the archive it was opened from along with itself.</summary>
    private sealed class OwningStream(Stream inner, ZipArchive archive) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                archive.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
