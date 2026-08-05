// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Which part of a document something points at. Stored as smallint; the values are a
/// schema contract — append only, never renumber. The names and payload shapes follow the
/// W3C Web Annotation selector vocabulary where it has a word for the thing, so that a
/// selection made in one place means the same in another.
/// </summary>
public enum DocumentAnchorKind : short
{
    /// <summary>The document as a whole. The only kind with no payload.</summary>
    Whole = 0,

    /// <summary>A passage of extracted text: offsets plus the quote and its context.</summary>
    TextRange = 1,

    /// <summary>A single page, 1-based.</summary>
    Page = 2,

    /// <summary>A run of pages, 1-based and forward.</summary>
    PageRange = 3,

    /// <summary>A rectangle or polygon in the pixels of one concrete rendition.</summary>
    ImageRegion = 4,

    /// <summary>An instant in a media file, in seconds from its start.</summary>
    TimePoint = 5,

    /// <summary>A span of a media file, in seconds, forward.</summary>
    TimeRange = 6,
}

/// <summary>
/// What makes an anchor well formed, independent of who is anchoring what. Parts are
/// addressed declaratively — page numbers, offsets, quotes — and never by a key into the
/// derived page rows, because those are wiped and rebuilt wholesale whenever a document is
/// re-read and their identifiers are explicitly not contracts.
/// </summary>
public static class DocumentAnchorRules
{
    /// <summary>
    /// Cap on the stored payload. Roomy enough for any of the defined shapes plus the
    /// context a durable text anchor needs, small enough that the column cannot quietly
    /// become a blob store.
    /// </summary>
    public const int MaxAnchorLength = 8000;

    /// <summary>
    /// Everything wrong with an anchor — empty when it is well formed. Per-kind payload
    /// shapes are not checked here yet; what is checked is the part the database cannot
    /// recover from being wrong: that a payload exists exactly when the kind needs one, that
    /// it fits, and that a pin is present when the coordinates are meaningless without one.
    /// </summary>
    public static IReadOnlyList<string> Validate(DocumentAnchorKind kind, string? anchor, Guid? anchorFileId)
    {
        var problems = new List<string>();
        var whole = kind == DocumentAnchorKind.Whole;

        if (whole && !string.IsNullOrEmpty(anchor))
        {
            problems.Add("an anchor covering the whole document carries no payload");
        }

        if (!whole && string.IsNullOrWhiteSpace(anchor))
        {
            problems.Add($"an anchor of kind {kind} needs a payload");
        }

        if (anchor is { Length: > MaxAnchorLength })
        {
            problems.Add($"the anchor payload is longer than {MaxAnchorLength} characters");
        }

        if (whole && anchorFileId is not null)
        {
            problems.Add("only an anchor naming a part of a document is pinned to a file");
        }

        // A region is pixels of one concrete rendition, so an unpinned one has no meaning
        // at all — unlike a page or a quote, which can still be looked for in a new version.
        if (kind == DocumentAnchorKind.ImageRegion && anchorFileId is null)
        {
            problems.Add("an image-region anchor names the file its coordinates were measured against");
        }

        return problems;
    }
}
