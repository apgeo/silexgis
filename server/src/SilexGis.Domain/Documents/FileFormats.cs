// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>A recognised file format: what to store as its media type, and its broad kind.</summary>
public sealed record FileFormat(string MimeType, FileKind Kind);

/// <summary>
/// Decides what an uploaded file actually is, from its first bytes.
/// <para>
/// The browser's <c>Content-Type</c> header and the file name are supplied by whoever is
/// uploading and are therefore hints, never facts. Text extraction dispatches on format, so
/// a file recorded under the wrong one is handed to the wrong reader and silently yields
/// nothing — which is why this is a correctness concern rather than a hardening measure.
/// The bytes decide the broad class; a hint may only refine a sub-type *within* the class
/// the bytes established (which flavour of plain text, which legacy office format).
/// </para>
/// <para>
/// Formats that are containers — anything built on ZIP — cannot be told apart from their
/// first bytes alone. <see cref="FromHeader"/> reports them as
/// <see cref="ZipContainerMimeType"/> and the caller opens the container to refine, except
/// for OpenDocument, whose specification puts an uncompressed <c>mimetype</c> entry first
/// precisely so it is readable from the header.
/// </para>
/// </summary>
public static class FileFormats
{
    /// <summary>
    /// Bytes a caller must read from the start of a file before calling <see cref="FromHeader"/>.
    /// Covers every signature checked here, including OpenDocument's leading mimetype entry
    /// and Matroska's doctype, and is small enough to read from any upload.
    /// </summary>
    public const int HeaderBytes = 512;

    /// <summary>
    /// Longest media type this project stores, and therefore the longest one worth reading.
    /// <para>
    /// Real media types are far shorter (the longest registered ones run to about eighty
    /// characters), but the value is not always ours: a container declares its own type in an
    /// entry whose length is whatever whoever built the file put there, and a request header
    /// is whatever the client sent. Both reach the same fixed-width column, so the bound is
    /// applied where the value is read rather than left to the database to refuse.
    /// </para>
    /// </summary>
    public const int MaxMediaTypeLength = 127;

    /// <summary>Media type used when the bytes are not recognised at all.</summary>
    public const string UnknownMimeType = "application/octet-stream";

    /// <summary>Plain text, the fallback for byte ranges that hold no signature but read as text.</summary>
    public const string TextMimeType = "text/plain";

    /// <summary>A ZIP-based container whose real format needs the archive's entry names.</summary>
    public const string ZipContainerMimeType = "application/zip";

    /// <summary>A legacy Microsoft compound file (pre-2007 Word/Excel/PowerPoint).</summary>
    public const string CompoundFileMimeType = "application/x-ole-storage";

    /// <summary>
    /// Media types that are genuinely text and worth keeping over the generic
    /// <see cref="TextMimeType"/> when the upload names one of them. Nothing outside this
    /// list survives: an arbitrary caller-supplied type is not evidence about the bytes.
    /// </summary>
    private static readonly string[] TextSubTypes =
    [
        "text/plain", "text/markdown", "text/csv", "text/tab-separated-values", "text/html",
        "text/xml", "text/calendar", "text/vcard", "text/x-log",
        "application/json", "application/xml", "application/x-yaml", "application/yaml",
        "application/gpx+xml", "application/vnd.google-earth.kml+xml", "application/geo+json",
    ];

    /// <summary>Extension → text media type, for uploads whose declared type says nothing.</summary>
    private static readonly (string Extension, string MimeType)[] TextExtensions =
    [
        (".md", "text/markdown"), (".markdown", "text/markdown"),
        (".csv", "text/csv"), (".tsv", "text/tab-separated-values"),
        (".json", "application/json"), (".geojson", "application/geo+json"),
        (".xml", "text/xml"), (".html", "text/html"), (".htm", "text/html"),
        (".gpx", "application/gpx+xml"), (".kml", "application/vnd.google-earth.kml+xml"),
        (".yaml", "application/x-yaml"), (".yml", "application/x-yaml"),
    ];

    /// <summary>Extension → legacy compound-file format, used only once the bytes said "compound file".</summary>
    private static readonly (string Extension, string MimeType)[] CompoundFileExtensions =
    [
        (".doc", "application/msword"),
        (".xls", "application/vnd.ms-excel"),
        (".ppt", "application/vnd.ms-powerpoint"),
        (".msg", "application/vnd.ms-outlook"),
    ];

    /// <summary>
    /// The format the first bytes of a file identify, or null when nothing matches. A null
    /// result is not "invalid" — plain text carries no signature at all — so callers fall
    /// back to <see cref="LooksLikeText"/> and then to the unknown format.
    /// </summary>
    public static FileFormat? FromHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
        {
            return null;
        }

        // Images.
        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new FileFormat("image/png", FileKind.Image);
        }

        if (StartsWith(header, [0xFF, 0xD8, 0xFF]))
        {
            return new FileFormat("image/jpeg", FileKind.Image);
        }

        if (StartsWithAscii(header, "GIF87a") || StartsWithAscii(header, "GIF89a"))
        {
            return new FileFormat("image/gif", FileKind.Image);
        }

        if (StartsWithAscii(header, "II*\0") || StartsWithAscii(header, "MM\0*"))
        {
            // GeoTIFF is an ordinary TIFF with extra tags; the raster paths label their own
            // uploads, so a TIFF arriving here is treated as a plain image.
            return new FileFormat("image/tiff", FileKind.Image);
        }

        // "BM" alone is two bytes of ASCII and would swallow text files; the four reserved
        // bytes of a bitmap header are zero, which text never is.
        if (StartsWithAscii(header, "BM") && header.Length >= 10
            && header[6] == 0 && header[7] == 0 && header[8] == 0 && header[9] == 0)
        {
            return new FileFormat("image/bmp", FileKind.Image);
        }

        if (IsRiff(header, "WEBP"))
        {
            return new FileFormat("image/webp", FileKind.Image);
        }

        // Documents.
        if (StartsWithAscii(header, "%PDF-"))
        {
            return new FileFormat("application/pdf", FileKind.Document);
        }

        if (StartsWithAscii(header, "{\\rtf"))
        {
            return new FileFormat("application/rtf", FileKind.Document);
        }

        if (StartsWith(header, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]))
        {
            return new FileFormat(CompoundFileMimeType, FileKind.Document);
        }

        // Audio.
        if (StartsWithAscii(header, "ID3") || StartsWith(header, [0xFF, 0xFB]) || StartsWith(header, [0xFF, 0xF3])
            || StartsWith(header, [0xFF, 0xF2]) || StartsWith(header, [0xFF, 0xE3]))
        {
            return new FileFormat("audio/mpeg", FileKind.Audio);
        }

        if (StartsWithAscii(header, "fLaC"))
        {
            return new FileFormat("audio/flac", FileKind.Audio);
        }

        if (StartsWithAscii(header, "OggS"))
        {
            return new FileFormat("audio/ogg", FileKind.Audio);
        }

        if (IsRiff(header, "WAVE"))
        {
            return new FileFormat("audio/wav", FileKind.Audio);
        }

        // Video.
        if (IsRiff(header, "AVI "))
        {
            return new FileFormat("video/x-msvideo", FileKind.Video);
        }

        if (header.Length >= 12 && AsciiAt(header, 4, "ftyp"))
        {
            return IsoBaseMediaFormat(header);
        }

        if (StartsWith(header, [0x1A, 0x45, 0xDF, 0xA3]))
        {
            // Both WebM and Matroska use the EBML header; the doctype string sits in it.
            return Contains(header, "webm")
                ? new FileFormat("video/webm", FileKind.Video)
                : new FileFormat("video/x-matroska", FileKind.Video);
        }

        if (StartsWith(header, [0x00, 0x00, 0x01, 0xBA]) || StartsWith(header, [0x00, 0x00, 0x01, 0xB3]))
        {
            return new FileFormat("video/mpeg", FileKind.Video);
        }

        if (StartsWith(header, [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]))
        {
            return new FileFormat("video/x-ms-asf", FileKind.Video);
        }

        // Archives and containers.
        if (StartsWith(header, [0x50, 0x4B, 0x03, 0x04])
            || StartsWith(header, [0x50, 0x4B, 0x05, 0x06])
            || StartsWith(header, [0x50, 0x4B, 0x07, 0x08]))
        {
            return OpenDocumentFromHeader(header) ?? new FileFormat(ZipContainerMimeType, FileKind.Other);
        }

        if (StartsWith(header, [0x1F, 0x8B]))
        {
            return new FileFormat("application/gzip", FileKind.Other);
        }

        if (StartsWithAscii(header, "7z") && header.Length >= 6
            && header[2] == 0xBC && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
        {
            return new FileFormat("application/x-7z-compressed", FileKind.Other);
        }

        if (StartsWithAscii(header, "Rar!"))
        {
            return new FileFormat("application/vnd.rar", FileKind.Other);
        }

        // SVG is markup, so it has no signature of its own; it is still an image.
        if (IsSvg(header))
        {
            return new FileFormat("image/svg+xml", FileKind.Image);
        }

        return null;
    }

    /// <summary>
    /// Whether a header reads as text: no NUL bytes and no control characters other than the
    /// usual whitespace. Deliberately permissive above 0x7F so UTF-8 in any language passes,
    /// and deliberately applied only after every signature check, so a binary format that
    /// happens to start with printable bytes is already claimed by the time it gets here.
    /// </summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        if (header.Length == 0)
        {
            return false;
        }

        var start = StartsWith(header, [0xEF, 0xBB, 0xBF]) ? 3 : 0;
        for (var i = start; i < header.Length; i++)
        {
            var b = header[i];
            if (b >= 0x20 || b is 0x09 or 0x0A or 0x0B or 0x0C or 0x0D)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// The text media type to record for bytes that read as text. The declared type wins when
    /// it names a text format we know; otherwise the file name's extension is consulted;
    /// otherwise plain text. Neither hint can change the fact that these bytes are text.
    /// </summary>
    public static string TextMimeTypeFor(string? declaredMimeType, string? fileName)
    {
        var declared = BaseType(declaredMimeType);
        foreach (var known in TextSubTypes)
        {
            if (string.Equals(declared, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        foreach (var (extension, mimeType) in TextExtensions)
        {
            if (HasExtension(fileName, extension))
            {
                return mimeType;
            }
        }

        return TextMimeType;
    }

    /// <summary>
    /// Which legacy office format a compound file holds. The container format is identical
    /// for Word, Excel and PowerPoint, and telling them apart means walking its directory
    /// stream — so the extension refines a class the bytes already established, rather than
    /// deciding one on its own.
    /// </summary>
    public static string CompoundFileMimeTypeFor(string? fileName)
    {
        foreach (var (extension, mimeType) in CompoundFileExtensions)
        {
            if (HasExtension(fileName, extension))
            {
                return mimeType;
            }
        }

        return CompoundFileMimeType;
    }

    /// <summary>
    /// The broad kind a media type implies, for formats no signature identified. Kept as the
    /// last resort behind <see cref="FromHeader"/>: it reasons about a caller-supplied string,
    /// so it can only ever be a guess.
    /// </summary>
    public static FileKind KindOf(string? mimeType)
    {
        var type = BaseType(mimeType) ?? string.Empty;
        return type switch
        {
            var m when m.StartsWith("image/", StringComparison.Ordinal) => FileKind.Image,
            var m when m.StartsWith("audio/", StringComparison.Ordinal) => FileKind.Audio,
            var m when m.StartsWith("video/", StringComparison.Ordinal) => FileKind.Video,
            var m when m.StartsWith("text/", StringComparison.Ordinal) => FileKind.Document,
            "application/pdf" or "application/rtf" => FileKind.Document,
            var m when m.Contains("word", StringComparison.Ordinal)
                || m.Contains("excel", StringComparison.Ordinal)
                || m.Contains("powerpoint", StringComparison.Ordinal)
                || m.Contains("opendocument", StringComparison.Ordinal)
                || m.Contains("officedocument", StringComparison.Ordinal) => FileKind.Document,
            _ => FileKind.Other,
        };
    }

    /// <summary>The media type without its parameters, lower-cased; null stays null.</summary>
    public static string? BaseType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        var semicolon = mimeType.IndexOf(';', StringComparison.Ordinal);
        var value = semicolon >= 0 ? mimeType[..semicolon] : mimeType;
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// OpenDocument's own rule: the first entry of the zip is an uncompressed <c>mimetype</c>
    /// whose contents start at a fixed offset, so the format is readable without unpacking.
    /// </summary>
    private static FileFormat? OpenDocumentFromHeader(ReadOnlySpan<byte> header)
    {
        const int compressionMethodOffset = 8;
        const int uncompressedSizeOffset = 22;
        const int nameOffset = 30;
        const string entryName = "mimetype";

        var valueOffset = nameOffset + entryName.Length;
        if (header.Length < valueOffset || !AsciiAt(header, nameOffset, entryName))
        {
            return null;
        }

        // The entry is stored uncompressed by specification, so its declared size is the
        // exact length of the media type that follows. Reading to the next non-media-type
        // byte instead would run straight into the following entry's own header, whose
        // leading bytes are letters.
        if (LittleEndian16(header, compressionMethodOffset) != 0)
        {
            return null;
        }

        var length = LittleEndian32(header, uncompressedSizeOffset);
        if (length is <= 0 or > MaxMediaTypeLength || header.Length < valueOffset + length)
        {
            return null;
        }

        var value = Ascii(header.Slice(valueOffset, (int)length));
        return value.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
            ? new FileFormat(value, FileKind.Document)
            : null;
    }

    private static int LittleEndian16(ReadOnlySpan<byte> bytes, int offset) =>
        bytes.Length < offset + 2 ? -1 : bytes[offset] | (bytes[offset + 1] << 8);

    private static long LittleEndian32(ReadOnlySpan<byte> bytes, int offset) =>
        bytes.Length < offset + 4
            ? -1
            : bytes[offset] | ((long)bytes[offset + 1] << 8) | ((long)bytes[offset + 2] << 16)
                | ((long)bytes[offset + 3] << 24);

    /// <summary>
    /// ISO base media: the brand that follows <c>ftyp</c> says what the container holds.
    /// <para>
    /// The family is much wider than MP4 — it also carries still images. HEIC is what a modern
    /// phone camera writes by default and AVIF is an ordinary web image format, so the brands
    /// have to be named: falling through to video would file a photograph as a recording, and
    /// everything that only happens for images — reading the capture location out of its EXIF,
    /// counting it as one page, offering a thumbnail — would then never happen for the single
    /// most common kind of photograph there is.
    /// </para>
    /// <para>
    /// Sequence brands (an image sequence rather than a single still) are treated as their
    /// still counterparts: nothing here distinguishes the two, and both are images.
    /// </para>
    /// </summary>
    private static FileFormat IsoBaseMediaFormat(ReadOnlySpan<byte> header)
    {
        var brand = Ascii(header.Slice(8, 4));
        return brand switch
        {
            "M4A " or "M4B " or "M4P " or "F4A " or "F4B " => new FileFormat("audio/mp4", FileKind.Audio),
            "heic" or "heix" or "hevc" or "hevx" => new FileFormat("image/heic", FileKind.Image),
            "mif1" or "msf1" => new FileFormat("image/heif", FileKind.Image),
            "avif" or "avis" => new FileFormat("image/avif", FileKind.Image),
            "qt  " => new FileFormat("video/quicktime", FileKind.Video),
            "3gp4" or "3gp5" or "3g2a" => new FileFormat("video/3gpp", FileKind.Video),
            _ => new FileFormat("video/mp4", FileKind.Video),
        };
    }

    /// <summary>Whether the header is XML or markup whose root element is <c>svg</c>.</summary>
    private static bool IsSvg(ReadOnlySpan<byte> header)
    {
        var text = Ascii(header).TrimStart();
        if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            && text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A RIFF container whose four-character form type matches.</summary>
    private static bool IsRiff(ReadOnlySpan<byte> header, string formType) =>
        header.Length >= 12 && StartsWithAscii(header, "RIFF") && AsciiAt(header, 8, formType);

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static bool StartsWithAscii(ReadOnlySpan<byte> header, string signature) =>
        AsciiAt(header, 0, signature);

    private static bool AsciiAt(ReadOnlySpan<byte> header, int offset, string signature)
    {
        if (header.Length < offset + signature.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (header[offset + i] != (byte)signature[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(ReadOnlySpan<byte> header, string needle) =>
        Ascii(header).Contains(needle, StringComparison.Ordinal);

    /// <summary>Header bytes as ASCII, with anything non-printable mapped to a dot.</summary>
    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return builder.ToString();
    }

    private static bool HasExtension(string? fileName, string extension) =>
        fileName is not null && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
