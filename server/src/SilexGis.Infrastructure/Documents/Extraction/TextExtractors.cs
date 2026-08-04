// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Chooses the reader for a stored file's format.
/// <para>
/// The registered readers are the whole answer: a format nothing claims is a format nothing
/// reads, and saying so is the point. Formats that carry a text layer are listed separately,
/// where a file is stored, so the two can disagree — and when they do, the disagreement is
/// visible as a file that was queued for reading and found no reader, rather than as a file
/// that quietly produced nothing.
/// </para>
/// </summary>
public sealed class TextExtractorSelector(IEnumerable<ITextExtractor> extractors)
{
    private readonly IReadOnlyList<ITextExtractor> extractors = extractors.ToList();

    /// <summary>The reader for this media type, or null when none handles it.</summary>
    public ITextExtractor? For(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return null;
        }

        foreach (var extractor in this.extractors)
        {
            if (extractor.Handles(mimeType))
            {
                return extractor;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a file recorded under this media type is worth queueing for reading at all:
    /// the format carries a text layer, and something here can read it.
    /// </summary>
    public bool CanExtract(string? mimeType) =>
        TextExtractionFormats.CarriesText(mimeType) && For(mimeType) is not null;

    /// <summary>
    /// Whether text stamped with this reader and version was produced by the reader that is
    /// registered now, at the version it emits now.
    /// </summary>
    /// <remarks>
    /// False covers three cases that all mean the same thing to whatever asks — a reader that
    /// has since been improved, one that has been withdrawn, and a page nothing ever stamped.
    /// Each is a page whose text is not what this installation would produce from those bytes
    /// today, which is the only question a re-reading needs answered.
    /// </remarks>
    public bool IsCurrent(string? extractor, int? version) =>
        extractor is not null
        && version is { } stamped
        && this.extractors.Any(e => e.Name == extractor && e.Version == stamped);
}
