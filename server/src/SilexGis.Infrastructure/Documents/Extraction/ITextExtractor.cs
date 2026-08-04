// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>Text read out of one page of a file.</summary>
/// <param name="PageNumber">1-based position within the file.</param>
/// <param name="Text">
/// The page's text, or null where the page carries none. A page of a scanned document is the
/// ordinary case: it exists, it is numbered, and there is nothing on it to read.
/// </param>
public sealed record ExtractedPage(int PageNumber, string? Text);

/// <summary>
/// What a reader made of one file: its pages in order, and how it identified itself.
/// </summary>
/// <param name="Extractor">The reader's stable name, stored against every page it wrote.</param>
/// <param name="Version">
/// The reader's output version, bumped whenever a change makes it produce different text from
/// the same bytes. Stored beside the name so a page read by an older version is identifiable
/// without re-reading the file, which is what makes a re-extraction a decidable question and a
/// half-finished one resumable.
/// </param>
/// <param name="Pages">
/// The pages, in order and starting at one. Empty means the file was read and holds no pages
/// at all — not that reading failed, which is reported by an exception.
/// </param>
public sealed record ExtractedText(string Extractor, int Version, IReadOnlyList<ExtractedPage> Pages);

/// <summary>
/// Reads the text layer out of one family of file formats.
/// <para>
/// Dispatch is on the format decided from the file's own bytes when it was stored, never on
/// what the upload claimed to be: a reader handed a format it cannot read produces nothing,
/// silently, and nothing downstream can tell that apart from a file that genuinely has no text.
/// </para>
/// <para>
/// An implementation reads; it does not decide anything about the document. It may throw when
/// the bytes are not what the format says they are, and the caller records that against the
/// file — it must not swallow a damaged file into an empty result, because "unreadable" and
/// "nothing to read" are different answers and only one of them is worth retrying.
/// </para>
/// </summary>
public interface ITextExtractor
{
    /// <summary>Stable name recorded against every page this reader writes.</summary>
    string Name { get; }

    /// <summary>Output version; see <see cref="ExtractedText.Version"/>.</summary>
    int Version { get; }

    /// <summary>Whether this reader handles files recorded under the given media type.</summary>
    bool Handles(string mimeType);

    /// <summary>Reads the file. The stream is positioned at its start and is not disposed here.</summary>
    Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct);
}
