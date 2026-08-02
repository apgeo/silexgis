// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Decides an upload's real format by reading it, rather than believing what the upload
/// says about itself. The signature knowledge is a pure table; this adds the one thing a
/// table cannot do — looking inside a ZIP container, which is what modern office documents
/// and e-books are, and whose format is only visible from the names of its entries.
/// </summary>
/// <remarks>
/// Reads the first few hundred bytes plus, for containers, the archive's central directory.
/// Nothing buffers the upload: the bytes are already in the file store by the time this runs.
/// </remarks>
public static class ContentSniffer
{
    /// <summary>Entry name → format, in the order they are worth testing for.</summary>
    private static readonly (string Entry, string MimeType)[] OfficeOpenXmlEntries =
    [
        ("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        ("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        ("ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
    ];

    /// <summary>
    /// The format of the content in <paramref name="content"/>. The declared media type and
    /// file name are consulted only where the bytes leave a genuine choice open — which
    /// flavour of plain text, which legacy office format — or where they say nothing at all.
    /// </summary>
    public static async Task<FileFormat> DetectAsync(
        Stream content, string? declaredMimeType, string? fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var buffer = new byte[FileFormats.HeaderBytes];
        var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        var header = buffer.AsSpan(0, read);

        var format = FileFormats.FromHeader(header);
        if (format is null)
        {
            return FileFormats.LooksLikeText(header)
                ? new FileFormat(FileFormats.TextMimeTypeFor(declaredMimeType, fileName), FileKind.Document)
                : new FileFormat(
                    FileFormats.BaseType(declaredMimeType) ?? FileFormats.UnknownMimeType,
                    FileFormats.KindOf(declaredMimeType));
        }

        if (format.MimeType == FileFormats.CompoundFileMimeType)
        {
            return format with { MimeType = FileFormats.CompoundFileMimeTypeFor(fileName) };
        }

        if (format.MimeType == FileFormats.ZipContainerMimeType && content.CanSeek)
        {
            content.Seek(0, SeekOrigin.Begin);
            return RefineZipContainer(content) ?? format;
        }

        return format;
    }

    /// <summary>
    /// What a ZIP archive really is, from the parts it contains; null when it is just a ZIP.
    /// A damaged archive is left as a plain ZIP rather than failing the upload — the bytes
    /// are still stored faithfully, and nothing downstream is served a format it cannot read.
    /// </summary>
    private static FileFormat? RefineZipContainer(Stream content)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var (entry, mimeType) in OfficeOpenXmlEntries)
            {
                if (archive.GetEntry(entry) is not null)
                {
                    return new FileFormat(mimeType, FileKind.Document);
                }
            }

            // OpenDocument normally announces itself in the header; this catches archives
            // written by tools that compressed the mimetype entry or reordered it.
            if (archive.GetEntry("mimetype") is { } declared)
            {
                var value = DeclaredMediaType(declared);
                if (value.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
                    || value == "application/epub+zip")
                {
                    return new FileFormat(value, FileKind.Document);
                }
            }

            return null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The media type a container declares in its <c>mimetype</c> entry, read under a hard
    /// cap; empty when the entry holds more than a media type can be.
    /// </summary>
    /// <remarks>
    /// The entry's contents come from whoever built the archive, and a compressed entry can
    /// inflate to any size at all from very few stored bytes — so this reads a bounded prefix
    /// rather than the entry. Over-length is reported as "no declaration" rather than
    /// truncated, because a truncated prefix would still match the tests above and the value
    /// that matched would then be a fabrication rather than what the file said.
    /// </remarks>
    private static string DeclaredMediaType(ZipArchiveEntry entry)
    {
        // One byte past the cap, so a value that is exactly one too long is visibly too long.
        var buffer = new byte[FileFormats.MaxMediaTypeLength + 1];
        using var stream = entry.Open();
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read > FileFormats.MaxMediaTypeLength
            ? string.Empty
            : Encoding.ASCII.GetString(buffer, 0, read).Trim();
    }
}
