// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A remark one member writes on a document for the others to read. It hangs on
/// <see cref="DocumentId"/> — the document's stable identity — and not on a file or a
/// version, so a re-upload replaces the bytes without orphaning the conversation about
/// them.
/// <para>
/// Threading is one level deep and no more: a comment either stands on its own or is a
/// reply to one that does. That is a product decision rather than a storage limit, and it
/// is enforced by the rules class rather than by the database, because "the parent has no
/// parent of its own" is not something a check constraint can express.
/// </para>
/// <para>
/// Who may read a comment is decided entirely by who may read its document — there is no
/// second rule here, and no per-comment grant. Deletion is physical, as it is for every
/// other child row in this system except features: the audit trail keeps a snapshot of the
/// body, which is what a deleted comment leaves behind. Replies go with their parent, and
/// each leaves a snapshot of its own — somebody else's words disappearing is exactly the
/// event a timeline has to keep.
/// </para>
/// </summary>
public class DocumentComment : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The document being commented on. Real FK; the row goes with the document.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// The comment this one replies to, or null for a comment that starts a thread. A
    /// reply's parent must itself be a thread starter on the same document.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// What was written, as plain text. It is stored and shown exactly as typed: nothing
    /// between here and the reader parses it as markup, so a body cannot become markup in
    /// somebody else's browser.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>
    /// Who wrote it. Null once that account is gone — the remark stays, its attribution
    /// does not, which is the same trade every other authored child row here makes.
    /// </summary>
    public Guid? AuthorId { get; set; }

    /// <summary>
    /// Which part of the document the remark is about. <see cref="DocumentAnchorKind.Whole"/>
    /// — the default — means the document as a whole, and is the only kind carrying no
    /// payload. The column exists from the first migration so that anchoring a comment to a
    /// page or a passage later is a user-interface change rather than a schema change.
    /// </summary>
    public DocumentAnchorKind AnchorKind { get; set; } = DocumentAnchorKind.Whole;

    /// <summary>
    /// The anchor payload as jsonb, kind-discriminated; null exactly when the kind is
    /// <see cref="DocumentAnchorKind.Whole"/>.
    /// </summary>
    public string? Anchor { get; set; }

    /// <summary>
    /// The immutable file the anchor's coordinates were measured against, when it names a
    /// part. It survives a new version by pointing at the old file rather than by moving,
    /// so a stale selection can be reported as stale instead of silently highlighting the
    /// wrong words. Null once that file is deleted.
    /// </summary>
    public Guid? AnchorFileId { get; set; }

    /// <summary>
    /// When the body was last rewritten by its author, or null while it still says what it
    /// first said. Distinct from <see cref="UpdatedAt"/>, which any write touches.
    /// </summary>
    public DateTimeOffset? EditedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // A comment is an audited child of its document, so writing, editing and deleting one
    // surfaces in that document's timeline rather than in a timeline of its own.
    public string RootEntityType => nameof(Document);

    public string RootEntityId => DocumentId.ToString();
}
