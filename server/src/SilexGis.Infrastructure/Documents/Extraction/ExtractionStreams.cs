// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Raised when a reader would have to take more bytes into memory than it may. Distinct from a
/// parse failure on purpose: nothing is wrong with the file, and saying it is damaged when it is
/// merely large is a false statement about somebody's document.
/// </summary>
public sealed class ContentTooLargeException(string message) : Exception(message);

/// <summary>
/// A stream a reader may seek in, and whether this borrowed it or made it. Disposing releases
/// only what was made here, so the caller's own stream outlives the reading that used it.
/// </summary>
internal readonly record struct BorrowedStream(Stream Stream, bool Owned) : IDisposable
{
    public void Dispose()
    {
        if (this.Owned)
        {
            this.Stream.Dispose();
        }
    }
}

/// <summary>
/// What a reader made of a bounded read: the bytes, and whether the source had more of them.
/// The flag is the whole point — a plain byte array cannot tell a complete read apart from a
/// cut one, and a reader handed a cut file reports a damaged document.
/// </summary>
internal readonly record struct BoundedContent(byte[] Bytes, bool Truncated);

/// <summary>
/// Reading helpers shared by the readers that need the bytes rather than the stream.
/// <para>
/// A stored file is a file on disk: it can be read where it lies, at whatever size the
/// installation's upload limit accepted, and nothing has to be copied to read it. The bound
/// here therefore guards the two cases that are not that — a source that cannot be rewound, and
/// an entry <em>inside</em> a container, which declares its own size and can expand from a few
/// stored bytes into as many as the file that built it chose.
/// </para>
/// </summary>
internal static class ExtractionStreams
{
    /// <summary>
    /// Most bytes any single bounded read takes. Far above the two million characters a page
    /// keeps, so a linear format cut here loses only text the page could not have stored, and
    /// far below what a container could otherwise ask for.
    /// </summary>
    public const int MaxBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The stream's contents, up to <see cref="MaxBytes"/>, read from where it stands, together
    /// with whether the source went on past that.
    /// </summary>
    public static async Task<BoundedContent> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // One byte past the bound is read deliberately: it is the only difference between a
        // source that ended exactly at the limit and one that was cut off there.
        var limit = (long)MaxBytes + 1;
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (total < limit)
        {
            var wanted = (int)Math.Min(chunk.Length, limit - total);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), ct);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }

        var bytes = buffer.ToArray();
        return total > MaxBytes
            ? new BoundedContent(bytes[..MaxBytes], true)
            : new BoundedContent(bytes, false);
    }

    /// <summary>
    /// The content as something a parser may seek in, without copying it when it already is.
    /// </summary>
    /// <remarks>
    /// The formats whose parsers need the whole file at once — a paged document's cross-reference
    /// table, a package's central directory, a compound file's directory sector — all keep the
    /// part that says where everything is at the end. Handing such a parser a prefix of the file
    /// makes an intact document look damaged, which is why nothing is cut here: a stored file is
    /// read in place at whatever size it was accepted at, and only a source that cannot be
    /// rewound is taken into memory and bounded.
    /// </remarks>
    /// <exception cref="ContentTooLargeException">
    /// The source cannot be rewound and carries more than <see cref="MaxBytes"/>.
    /// </exception>
    public static async Task<BorrowedStream> SeekableAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.CanSeek)
        {
            content.Position = 0;
            return new BorrowedStream(content, Owned: false);
        }

        var bounded = await ReadBoundedAsync(content, ct);
        if (bounded.Truncated)
        {
            throw new ContentTooLargeException(
                $"The content is larger than the {MaxBytes / (1024 * 1024)} MB a reader may take "
                + "into memory from a source it cannot read a second time.");
        }

        return new BorrowedStream(new MemoryStream(bounded.Bytes, writable: false), Owned: true);
    }
}
