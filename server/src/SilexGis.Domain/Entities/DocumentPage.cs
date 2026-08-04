// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One page of a stored file. Derived, re-derivable data — bigint key, no audit trail,
/// access control inherited from the file's document — so text extraction may replace a
/// file's page rows wholesale without disturbing anything that references the document.
/// <para>
/// An image is definitionally a single page and gets its row at upload; paged formats get
/// theirs from text extraction, which is the only thing that knows the real count.
/// <see cref="Text"/> stays null until then.
/// </para>
/// </summary>
public class DocumentPage
{
    public long Id { get; set; }

    public Guid FileId { get; set; }

    /// <summary>1-based page position within the file.</summary>
    public int PageNumber { get; set; }

    /// <summary>Extracted plain text of the page; null when nothing has extracted it yet.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Stable name of the reader that produced <see cref="Text"/>, or null where nothing has
    /// read the page. Recorded because a page with no text and a page nothing has looked at
    /// are the same row otherwise, and only one of them is worth reading again.
    /// </summary>
    public string? Extractor { get; set; }

    /// <summary>
    /// Output version of the reader named in <see cref="Extractor"/>, bumped whenever a change
    /// makes it produce different text from the same bytes. Stored beside the name so pages
    /// left behind by an older reader are findable without opening a single file, which is
    /// what makes a re-reading a decidable question and a half-finished one resumable.
    /// </summary>
    public int? ExtractorVersion { get; set; }
}
