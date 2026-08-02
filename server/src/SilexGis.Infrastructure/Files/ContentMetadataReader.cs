// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using ImageMagick;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// What a file states about itself: who made it, what made it, when it says it was created
/// and last changed, and — for recordings — how long it runs and in what encoding.
/// </summary>
/// <remarks>
/// These are stored as columns rather than in the per-kind metadata bag because document
/// lists filter and order on them, and a jsonb bag is not indexed for that. Every member is
/// optional: most formats state none of this, and a file that says nothing is not an error.
/// </remarks>
public sealed record ContentFacts(
    string? Author = null,
    string? Producer = null,
    DateTimeOffset? ContentCreatedAt = null,
    DateTimeOffset? ContentModifiedAt = null,
    double? DurationSeconds = null,
    string? Codec = null)
{
    /// <summary>A file that stated nothing about itself, or one nothing here can read.</summary>
    public static readonly ContentFacts None = new();
}

/// <summary>Reads the facts a stored file states about itself.</summary>
public interface IContentMetadataReader
{
    /// <summary>
    /// The facts the file at <paramref name="absolutePath"/> carries. Never throws: an
    /// unreadable or damaged file yields <see cref="ContentFacts.None"/>, because failing to
    /// describe an upload is not a reason to refuse it.
    /// </summary>
    Task<ContentFacts> ReadAsync(string absolutePath, FileKind kind, CancellationToken ct = default);
}

/// <summary>
/// Reads embedded metadata from the formats this project can already open: images through
/// the imaging library it already carries, and the two recording containers whose headers
/// state their own length in a form that can be read without decoding anything.
/// </summary>
/// <remarks>
/// Deliberately not exhaustive. Naming the encoding of an arbitrary media file in general is
/// what a full multimedia framework is for, and pulling one in for two columns would be a
/// large dependency with real licensing care for a small gain. What is here covers the
/// containers a phone and a field recorder actually produce; everything else leaves the
/// columns null, which is what null is for.
/// </remarks>
public sealed class ContentMetadataReader : IContentMetadataReader
{
    /// <summary>
    /// Largest box read whole while looking for a recording's header. Real ones are a few
    /// hundred kilobytes; the cap is what keeps a malformed declaration from being believed.
    /// </summary>
    private const int MaxHeaderBoxBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Timestamps in ISO base media files count seconds from the start of 1904 in UTC — a
    /// convention inherited from QuickTime.
    /// </summary>
    private static readonly DateTimeOffset IsoBaseMediaEpoch = new(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Largest second count that still lands on a representable date. Truncated rather than
    /// rounded, so adding it is inside the range by construction.
    /// </summary>
    private static readonly ulong MaxSecondsSinceIsoBaseMediaEpoch =
        (ulong)(DateTimeOffset.MaxValue - IsoBaseMediaEpoch).TotalSeconds;

    public async Task<ContentFacts> ReadAsync(
        string absolutePath, FileKind kind, CancellationToken ct = default)
    {
        try
        {
            return kind switch
            {
                FileKind.Image => ReadImage(absolutePath),
                FileKind.Audio or FileKind.Video => await ReadRecordingAsync(absolutePath, ct),
                _ => ContentFacts.None,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A file we cannot describe is still a file we stored faithfully — and everything
            // read below comes out of bytes an uploader chose, so the ways a header can be
            // wrong are not a list anyone can finish enumerating. Describing an upload is
            // never a reason to refuse it, so nothing raised here escapes; a cancelled request
            // still cancels, because that is the caller leaving rather than the file being bad.
            return ContentFacts.None;
        }
    }

    // ---------- images ----------

    /// <summary>
    /// An image's own EXIF: the photographer, the software that wrote the file, and the two
    /// timestamps the format distinguishes — when the picture was taken and when the file was
    /// last changed.
    /// </summary>
    private static ContentFacts ReadImage(string absolutePath)
    {
        using var image = new MagickImage(absolutePath);
        var exif = image.GetExifProfile();
        if (exif is null)
        {
            return ContentFacts.None;
        }

        var offset = Text(exif, ExifTag.OffsetTimeOriginal) ?? Text(exif, ExifTag.OffsetTime);
        return new ContentFacts(
            Author: Text(exif, ExifTag.Artist),
            Producer: Text(exif, ExifTag.Software),
            ContentCreatedAt: ExifTimestamp(
                Text(exif, ExifTag.DateTimeOriginal) ?? Text(exif, ExifTag.DateTimeDigitized), offset),
            ContentModifiedAt: ExifTimestamp(Text(exif, ExifTag.DateTime), Text(exif, ExifTag.OffsetTime)));
    }

    private static string? Text(IExifProfile exif, ExifTag<string> tag)
    {
        var value = exif.GetValue(tag)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// An EXIF timestamp is written "yyyy:MM:dd HH:mm:ss" in the camera's own local time, with
    /// the zone in a separate tag that older cameras never wrote. Without that tag the reading
    /// is stored as if it were UTC — which is what the column holds — so a photo's stated time
    /// can be off by the photographer's offset. Recording the wrong zone is still far more
    /// useful than recording no date, and the tag is present on anything modern.
    /// </summary>
    private static DateTimeOffset? ExifTimestamp(string? value, string? offset)
    {
        if (value is null
            || !DateTime.TryParseExact(
                value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        if (offset is not null
            && TimeSpan.TryParseExact(offset.TrimStart('+'), @"hh\:mm", CultureInfo.InvariantCulture, out var span))
        {
            return new DateTimeOffset(local, offset.StartsWith('-') ? -span : span);
        }

        return new DateTimeOffset(local, TimeSpan.Zero);
    }

    // ---------- recordings ----------

    private static async Task<ContentFacts> ReadRecordingAsync(string absolutePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

        var header = new byte[16];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 12)
        {
            return ContentFacts.None;
        }

        if (Ascii(header.AsSpan(0, 4)) == "RIFF" && Ascii(header.AsSpan(8, 4)) == "WAVE")
        {
            stream.Position = 12;
            return ReadWave(stream);
        }

        if (Ascii(header.AsSpan(4, 4)) == "ftyp")
        {
            stream.Position = 0;
            return ReadIsoBaseMedia(stream);
        }

        return ContentFacts.None;
    }

    /// <summary>
    /// An ISO base media container (MP4 and its relatives). The movie header states the total
    /// running time as a count in its own time base; the sample description of the first track
    /// names the encoding as a four-character code.
    /// </summary>
    private static ContentFacts ReadIsoBaseMedia(Stream stream)
    {
        var moov = FindTopLevelBox(stream, "moov");
        if (moov is null)
        {
            return ContentFacts.None;
        }

        var span = moov.AsSpan();
        double? duration = null;
        DateTimeOffset? created = null;
        DateTimeOffset? modified = null;

        if (FindChild(span, "mvhd") is { Length: >= 4 } mvhd)
        {
            var version = mvhd[0];
            // The header's fixed layout: a version/flags word, then creation and modification
            // times, the time base, and the duration — all widened from 32 to 64 bits in
            // version 1, which is what long recordings use.
            if (version == 0 && mvhd.Length >= 20)
            {
                created = IsoTimestamp(BinaryPrimitives.ReadUInt32BigEndian(mvhd[4..]));
                modified = IsoTimestamp(BinaryPrimitives.ReadUInt32BigEndian(mvhd[8..]));
                duration = Seconds(
                    BinaryPrimitives.ReadUInt32BigEndian(mvhd[16..]),
                    BinaryPrimitives.ReadUInt32BigEndian(mvhd[12..]));
            }
            else if (version == 1 && mvhd.Length >= 32)
            {
                created = IsoTimestamp(BinaryPrimitives.ReadUInt64BigEndian(mvhd[4..]));
                modified = IsoTimestamp(BinaryPrimitives.ReadUInt64BigEndian(mvhd[12..]));
                duration = Seconds(
                    BinaryPrimitives.ReadUInt64BigEndian(mvhd[24..]),
                    BinaryPrimitives.ReadUInt32BigEndian(mvhd[20..]));
            }
        }

        return new ContentFacts(
            ContentCreatedAt: created,
            ContentModifiedAt: modified,
            DurationSeconds: duration,
            Codec: IsoBaseMediaCodec(span));
    }

    /// <summary>
    /// The first track's sample-description entry names the encoding. The path is fixed by the
    /// format, and each step is a container whose children are boxes of the same shape.
    /// </summary>
    private static string? IsoBaseMediaCodec(ReadOnlySpan<byte> moov)
    {
        var box = moov;
        foreach (var name in (string[])["trak", "mdia", "minf", "stbl", "stsd"])
        {
            box = FindChild(box, name);
            if (box.IsEmpty)
            {
                return null;
            }
        }

        // A version/flags word and an entry count precede the entries; each entry is a box of
        // the usual size-then-name shape, and its name is the codec.
        const int entryCountOffset = 4;
        const int firstEntryOffset = 8;
        const int codecOffset = firstEntryOffset + 4;
        if (box.Length < codecOffset + 4 || BinaryPrimitives.ReadUInt32BigEndian(box[entryCountOffset..]) == 0)
        {
            return null;
        }

        var codec = Ascii(box.Slice(codecOffset, 4)).Trim();
        return codec.Length == 0 ? null : codec;
    }

    /// <summary>
    /// A WAVE file's format chunk states the encoding and the bytes-per-second it plays at;
    /// dividing the sample data by that rate gives the running time exactly.
    /// </summary>
    private static ContentFacts ReadWave(Stream stream)
    {
        string? codec = null;
        long bytesPerSecond = 0;
        double? duration = null;

        var header = new byte[8];
        while (stream.Read(header, 0, header.Length) == header.Length)
        {
            var name = Ascii(header.AsSpan(0, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            var start = stream.Position;

            if (name == "fmt " && size >= 16)
            {
                var chunk = new byte[16];
                if (stream.Read(chunk, 0, chunk.Length) != chunk.Length)
                {
                    break;
                }

                codec = WaveCodec(BinaryPrimitives.ReadUInt16LittleEndian(chunk));
                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(8, 4));
            }
            else if (name == "data" && bytesPerSecond > 0)
            {
                duration = (double)size / bytesPerSecond;
                break;
            }

            // Chunks are word-aligned: an odd length is followed by one padding byte.
            var next = start + size + (size % 2);
            if (next <= start || next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        return duration is null && codec is null
            ? ContentFacts.None
            : new ContentFacts(DurationSeconds: duration, Codec: codec);
    }

    /// <summary>The encoding a WAVE format tag names; the numbers are the registered ones.</summary>
    private static string WaveCodec(int formatTag) => formatTag switch
    {
        0x0001 => "pcm",
        0x0002 => "adpcm",
        0x0003 => "pcm_float",
        0x0006 => "alaw",
        0x0007 => "mulaw",
        0x0011 => "ima_adpcm",
        0x0055 => "mp3",
        0xFFFE => "extensible",
        _ => $"0x{formatTag:x4}",
    };

    // ---------- box walking ----------

    /// <summary>
    /// The contents of the first top-level box with the given name, or null when the file has
    /// none. Boxes that are not wanted are skipped rather than read, so a large recording is
    /// walked without loading it.
    /// </summary>
    /// <remarks>
    /// An ISO base media file is built of length-prefixed boxes — a byte count, a
    /// four-character name, then the contents — and the box that describes the movie is
    /// routinely written after the samples, so finding it means walking the file rather than
    /// reading its head.
    /// </remarks>
    private static byte[]? FindTopLevelBox(Stream stream, string wanted)
    {
        while (ReadBoxHeader(stream, out var name, out var contentLength))
        {
            if (name == wanted)
            {
                if (contentLength is <= 0 or > MaxHeaderBoxBytes)
                {
                    return null;
                }

                var contents = new byte[contentLength];
                return stream.ReadAtLeast(contents, contents.Length, throwOnEndOfStream: false) == contents.Length
                    ? contents
                    : null;
            }

            var next = stream.Position + contentLength;
            if (contentLength <= 0 || next > stream.Length)
            {
                return null;
            }

            stream.Position = next;
        }

        return null;
    }

    private static bool ReadBoxHeader(Stream stream, out string name, out long contentLength)
    {
        name = string.Empty;
        contentLength = 0;
        var headerLength = 8;

        var header = new byte[8];
        if (stream.Read(header, 0, header.Length) != header.Length)
        {
            return false;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        name = Ascii(header.AsSpan(4, 4));

        if (size == 1)
        {
            // A declared size of one means the real one is a 64-bit value that follows.
            var extended = new byte[8];
            if (stream.Read(extended, 0, extended.Length) != extended.Length)
            {
                return false;
            }

            size = (long)BinaryPrimitives.ReadUInt64BigEndian(extended);
            headerLength = 16;
        }
        else if (size == 0)
        {
            // Zero means the box runs to the end of the file.
            size = stream.Length - stream.Position + headerLength;
        }

        contentLength = size - headerLength;
        return true;
    }

    /// <summary>The contents of the first child box with the given name, empty when absent.</summary>
    private static ReadOnlySpan<byte> FindChild(ReadOnlySpan<byte> parent, string wanted)
    {
        var offset = 0;
        while (offset + 8 <= parent.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(parent[offset..]);
            var name = Ascii(parent.Slice(offset + 4, 4));
            var headerLength = 8;

            if (size == 1)
            {
                if (offset + 16 > parent.Length)
                {
                    return default;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(parent[(offset + 8)..]);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = parent.Length - offset;
            }

            if (size < headerLength || offset + size > parent.Length)
            {
                return default;
            }

            if (name == wanted)
            {
                return parent.Slice(offset + headerLength, (int)(size - headerLength));
            }

            offset += (int)size;
        }

        return default;
    }

    private static double? Seconds(ulong duration, uint timescale) =>
        timescale == 0 || duration == 0 ? null : (double)duration / timescale;

    /// <summary>
    /// A second count from the file's own header turned into a date, or null when it is not
    /// one. The 64-bit field a long recording uses can hold a number no calendar reaches, and
    /// the number comes straight out of the uploaded bytes — so a value past the end of
    /// representable time means the header is damaged or made up, which is a file that states
    /// no date rather than a file that cannot be stored.
    /// </summary>
    private static DateTimeOffset? IsoTimestamp(ulong secondsSince1904) =>
        secondsSince1904 is 0 || secondsSince1904 > MaxSecondsSinceIsoBaseMediaEpoch
            ? null
            : IsoBaseMediaEpoch.AddSeconds(secondsSince1904);

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);
}
