// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One delivery of a stored file's original bytes to a named person. This is a record of
/// reading, not of changing, and the two are deliberately not the same table: a change is
/// rare and interesting and belongs on an object's timeline, while a read is frequent,
/// says nothing about the object, and says a great deal about the reader. Keeping them
/// apart is what lets reads be authorised differently, thinned aggressively and deleted on
/// a schedule without any of that touching the change trail.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not auditable: putting a read on the change timeline would bury the record
/// of what people did to a document under the record of who looked at it.
/// </para>
/// <para>
/// Renderings — thumbnails and drawn page pictures — are not recorded. They are produced
/// by this application from the stored bytes and are what the document page shows while it
/// is merely open; treating them as reading would count having a page on screen as having
/// taken a copy.
/// </para>
/// <para>
/// The document is stored beside the file rather than looked up through it, because the
/// history outlives the revision: a superseded version's file can be deleted, and the
/// record that somebody took a copy of it must not vanish with the thing they copied.
/// </para>
/// </remarks>
public class FileAccessEvent
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Who read it. Null is never written: a delivery whose caller cannot be named is not
    /// evidence about a person and would only make the history look more complete than it
    /// is. The column stays nullable so a deleted account's rows can be anonymised rather
    /// than losing the fact that the read happened.
    /// </summary>
    public Guid? UserId { get; set; }

    public Guid FileId { get; set; }

    /// <summary>The document the file belonged to when it was read.</summary>
    public Guid DocumentId { get; set; }
}
