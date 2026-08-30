// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads a link-annotated text body into the one page of prose it is.
///
/// <para>
/// Every other reader in this folder is doing archaeology on somebody else's file format and
/// producing the best text it can. This one is not: the body was written by this application,
/// in a format whose whole design is that the character stream is defined rather than
/// recovered. So the reading is one function call — and that call is the same
/// <see cref="AnnotatedText.CanonicalText"/> the writer used and the browser reproduces, which
/// is what makes a text-range anchor authored in the browser resolve, to the character, against
/// what search and every other reader sees.
/// </para>
///
/// <para>
/// It reads the prose, never the structure it is stored in. A reader that let the JSON through
/// would put every key and every brace into the search index, so a member searching for a word
/// that happens to be a field name would be handed every annotated document in the
/// installation. That is also why the format has a media type of its own: filed as generic
/// JSON, the plain-text reader would claim it and do precisely that.
/// </para>
///
/// <para>
/// A body that will not parse throws rather than returning nothing. "Unreadable" and "nothing
/// to read" are different answers about a file and only one of them is worth looking into, and
/// for this format — written only by this application — unreadable means something is wrong,
/// not that somebody uploaded a scan.
/// </para>
/// </summary>
public sealed class AnnotatedTextExtractor : ITextExtractor
{
    /// <inheritdoc />
    public string Name => "annotated-text";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        string.Equals(mimeType, AnnotatedText.MediaType, StringComparison.Ordinal);

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        var body = await AnnotatedText.ReadAsync(content, ct)
            ?? throw new InvalidDataException("The stored body is not a link-annotated text document.");

        // Normalising is a no-op on a valid body — the format's rules are chosen so that it is
        // — and is applied anyway because every other page in the database went through it and
        // the offsets must be measured against the stored form, not against the form on its way
        // there. If a body ever did change under it, the anchors would be measured against
        // whatever came out, which is the only self-consistent answer available here.
        return new ExtractedText(
            this.Name,
            this.Version,
            [new ExtractedPage(1, PageText.Normalize(AnnotatedText.CanonicalText(body.Blocks)))]);
    }
}
