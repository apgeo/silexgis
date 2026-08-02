// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Entities;

/// <summary>
/// One revision of a document: the version-detail fields live here, and so do the files
/// that carry its bytes. Exactly one version of a document is <see cref="IsCurrent"/> —
/// that is the one attachments, taggings and every ordinary read path point at; the rest
/// are superseded and are shown only to callers who may write the document, because a
/// superseded version may exist precisely because the current one had something removed.
/// <para>
/// "Which version is current" is stored, not derived: a partial unique index over
/// <see cref="DocumentId"/> where <see cref="IsCurrent"/> holds makes a second current
/// version impossible, and the write service that owns the flag is the only thing allowed
/// to move it.
/// </para>
/// </summary>
public class DocumentVersion : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DocumentId { get; set; }

    /// <summary>1-based position in the document's version sequence; unique per document.</summary>
    public int VersionNumber { get; set; } = DocumentVersionRules.FirstVersionNumber;

    /// <summary>
    /// Whether this is the version the document currently serves. At most one row per
    /// document may carry it (database-enforced); the write service keeps at least one.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>Short user-set name for the revision ("field notes, corrected").</summary>
    public string? Label { get; set; }

    /// <summary>What changed relative to the version this one superseded.</summary>
    public string? ChangeNote { get; set; }

    /// <summary>
    /// Calendar date this revision of the content is *from*, distinct from
    /// <see cref="CreatedAt"/> (the upload time). Carried across to a new version, which
    /// may then correct it — a re-scan of the same 1974 survey keeps 1974.
    /// </summary>
    public DateOnly? DocumentDate { get; set; }

    /// <summary>Who created this version; null once that account is gone.</summary>
    public Guid? UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Versions surface in their document's timeline rather than carrying one each.
    public string RootEntityType => nameof(Document);

    public string RootEntityId => DocumentId.ToString();
}
