// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads the text layer of a PDF, one result page per page of the file.
/// <para>
/// PDF is the only format here whose own structure is pages, and the page is the unit this
/// records text against, so the two line up exactly: page three of the file becomes page three
/// of the reading, and a reader that flattened the document into one blob would throw away the
/// only format-supplied answer to "where in this document is that sentence".
/// </para>
/// <para>
/// A PDF is a container, not a text format: pages holding only scanned photographs of paper
/// carry no text at all, and that is the ordinary condition of an archive's older material
/// rather than an error. Such a page comes back numbered and empty. Nothing here recognises
/// characters in a picture, so a file that never had a text layer will not acquire one.
/// </para>
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    /// <inheritdoc />
    public string Name => "pdf";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) => mimeType == "application/pdf";

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Read where it lies rather than copied: a paged document keeps the table that says
        // where its pages are at the end of the file, so a parser handed anything less than
        // all of it reports a perfectly good document as damaged.
        using var source = await ExtractionStreams.SeekableAsync(content, ct);

        // The file came from outside, so its cross-references and fonts are as likely to be
        // slightly wrong as deliberately hostile. Lenient parsing recovers the everyday damage
        // a generator left behind; a missing font stops that page rendering, never the reading.
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            ClipPaths = false,
        };

        using var document = PdfDocument.Open(source.Stream, options);

        var pages = new List<ExtractedPage>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            // Reading in the order the content stream draws in keeps columns and tables in the
            // order a person would read them; the raw letter sequence often is not that order.
            var text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);
            pages.Add(new ExtractedPage(page.Number, PageText.Normalize(text)));
        }

        return new ExtractedText(this.Name, this.Version, pages);
    }
}
