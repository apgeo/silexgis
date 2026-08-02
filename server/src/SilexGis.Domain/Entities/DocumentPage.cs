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
}
