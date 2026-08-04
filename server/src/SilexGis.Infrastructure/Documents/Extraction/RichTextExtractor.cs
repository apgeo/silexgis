// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads Rich Text Format documents. The format has no page structure a reader can recover —
/// where the pages fall is decided by the paper size and fonts of whoever prints it — so the
/// document is one page.
/// </summary>
public sealed class RichTextExtractor : ITextExtractor
{
    /// <inheritdoc />
    public string Name => "rich-text";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) => mimeType == "application/rtf";

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Read front to back like a text file, and bounded for the same reason: the format's
        // text is a subset of its bytes, so the page's own character limit is reached first and
        // stopping at the byte bound loses nothing that could have been stored.
        var bounded = await ExtractionStreams.ReadBoundedAsync(content, ct);
        var text = RichTextFormat.ToPlainText(bounded.Bytes);
        return new ExtractedText(this.Name, this.Version,
            [new ExtractedPage(1, PageText.Normalize(text))]);
    }
}
