// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads files that are already text — notes, comma-separated tables, markup, the interchange
/// formats that are XML or JSON underneath.
/// <para>
/// There is no page structure to find, so the file is one page. The work is entirely in the
/// character encoding: nothing about an uploaded file records which one it was written in, and
/// the archives this serves predate UTF-8 becoming the default, so the bytes have to be
/// examined rather than assumed. Reading Windows-1250 as UTF-8 does not fail loudly; it
/// produces text with the diacritics replaced or dropped, which then goes into the search index
/// and is never found again.
/// </para>
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    /// <summary>
    /// Media types that are text without saying so in their top-level type. Everything under
    /// <c>text/</c> is covered by the prefix test and is not repeated here.
    /// </summary>
    private static readonly string[] TextApplicationTypes =
    [
        "application/json", "application/xml", "application/x-yaml", "application/yaml",
        "application/gpx+xml", "application/vnd.google-earth.kml+xml", "application/geo+json",
    ];

    /// <inheritdoc />
    public string Name => "plain-text";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.Ordinal)
        || TextApplicationTypes.Contains(mimeType, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A text file is read front to back and its characters are kept in order, so a bound on
        // the bytes is a bound on the same text the page's own character limit would have cut
        // anyway — the limit is reached long before the bytes run out, and nothing that could
        // have been stored is lost by stopping early.
        var bounded = await ExtractionStreams.ReadBoundedAsync(content, ct);
        var decoded = TextEncodings.Decode(bounded.Bytes);
        return new ExtractedText(this.Name, this.Version,
            [new ExtractedPage(1, PageText.Normalize(decoded.Text))]);
    }
}
