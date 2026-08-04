// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Xml;
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads OpenDocument files — the text documents, spreadsheets and presentations written by
/// LibreOffice and OpenOffice.
/// <para>
/// An OpenDocument file is a ZIP archive whose <c>content.xml</c> holds the whole document, so
/// reading one needs an archive reader and an XML reader and nothing else. Both are in the
/// framework, which is why this format costs no dependency at all while the equivalent
/// Microsoft formats do.
/// </para>
/// <para>
/// The format records no pagination: where the pages fall is decided when the document is laid
/// out for a particular paper size, and a stored file has not been laid out. It is therefore
/// one page — the same answer as for any other flowing format, and the reason page numbers here
/// mean "position in the file" rather than "sheet of paper".
/// </para>
/// </summary>
public sealed class OpenDocumentTextExtractor : ITextExtractor
{
    private const string ContentEntry = "content.xml";

    /// <summary>Elements whose end marks the end of a block, and therefore a line break.</summary>
    private static readonly string[] BlockElements = ["p", "h", "table-row", "list-item"];

    /// <summary>
    /// Elements whose contents are about the document rather than in it. Skipped whole: a
    /// comment's text is not the page's text, and an embedded object's base64 is not text at all.
    /// </summary>
    private static readonly string[] SkippedElements =
        ["annotation", "binary-data", "tracked-changes", "form", "event-listeners"];

    /// <inheritdoc />
    public string Name => "opendocument";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        mimeType.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal);

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(ContentEntry)
            ?? throw new InvalidDataException(
                $"The OpenDocument archive has no {ContentEntry} entry.");

        // The archive is read where it lies; only the entry inside it is bounded, because a few
        // stored bytes may declare themselves to be as many as the file that built them chose.
        await using var entryStream = entry.Open();
        var bounded = await ExtractionStreams.ReadBoundedAsync(entryStream, ct);

        using var buffer = new MemoryStream(bounded.Bytes, writable: false);
        var text = ReadContent(buffer, bounded.Truncated, ct);
        return new ExtractedText(this.Name, this.Version,
            [new ExtractedPage(1, PageText.Normalize(text))]);
    }

    /// <summary>The document's text, read out of its content part.</summary>
    /// <param name="truncated">
    /// Whether the content part went on past what could be taken into memory. Its closing tags
    /// are then missing, and the parser is entitled to say so — but what was read up to that
    /// point is still this document's text, and reporting the whole file as unreadable because
    /// its end could not be held would be a false statement about it.
    /// </param>
    private static string ReadContent(Stream xml, bool truncated, CancellationToken ct)
    {
        // The document is supplied by whoever uploaded it. A document type definition in it
        // would let it name external entities, so it is refused outright rather than resolved.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };

        var text = new StringBuilder();
        using var reader = XmlReader.Create(xml, settings);
        try
        {
            ReadNodes(reader, text, ct);
        }
        catch (XmlException) when (truncated)
        {
            // The document ran on past what could be held, so its end is missing and the parser
            // says so. What was read before that point is the text, and it is kept.
        }

        return text.ToString();
    }

    /// <summary>Walks the content part, collecting the characters that are the document's text.</summary>
    private static void ReadNodes(XmlReader reader, StringBuilder text, CancellationToken ct)
    {
        while (text.Length < PageText.MaxCharacters && reader.Read())
        {
            ct.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (SkippedElements.Contains(reader.LocalName, StringComparer.Ordinal))
                    {
                        reader.Skip();
                        continue;
                    }

                    AppendElement(reader, text);
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    text.Append(reader.Value);
                    break;
                case XmlNodeType.EndElement:
                    if (BlockElements.Contains(reader.LocalName, StringComparer.Ordinal))
                    {
                        text.Append('\n');
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Emits the whitespace an element stands for. OpenDocument writes runs of spaces, tabs and
    /// forced line breaks as elements rather than as characters, so a reader that only collected
    /// text nodes would run words together.
    /// </summary>
    private static void AppendElement(XmlReader reader, StringBuilder text)
    {
        switch (reader.LocalName)
        {
            case "s":
                var count = reader.GetAttribute("c", reader.LookupNamespace("text"));
                text.Append(' ', SpaceCount(count));
                break;
            case "tab":
                text.Append('\t');
                break;
            case "line-break":
                text.Append('\n');
                break;
            case "p" or "h":
                // A block that follows another with no text between them is still a break.
                if (text.Length > 0 && text[^1] is not '\n')
                {
                    text.Append('\n');
                }

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// How many spaces a run element stands for. The count is the file's own, so it is bounded
    /// here rather than believed — a single element may otherwise declare a very long run.
    /// </summary>
    private static int SpaceCount(string? declared) =>
        int.TryParse(declared, out var count) ? Math.Clamp(count, 1, 1000) : 1;
}
