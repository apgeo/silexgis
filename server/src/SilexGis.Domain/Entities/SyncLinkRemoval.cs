// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The record that an attachment once existed and was removed — kept so that a phone holding the
/// link is told it has gone. Written by the database, never by the application: a trigger on the
/// attachments table adds one row for every attachment row deleted, by whichever path deleted it.
/// </summary>
/// <remarks>
/// <para>
/// Attachments are deleted outright, not marked, and by several unrelated paths — detaching a
/// file, a new revision collapsing duplicate links, a report regenerated over the one it replaces,
/// an import undone in bulk without loading the rows, and the cascades when a file or a feature
/// row itself goes. A record written by each of those paths would be a record the next one forgets
/// to write; a trigger sees every deletion, including the ones no application code touches.
/// </para>
/// <para>
/// The target and file are copied from the deleted row because nothing else will still name
/// them. The document is looked up while the row is being deleted, and is absent when the file it
/// belonged to was deleted in the same statement — which is why nothing reads it as a key.
/// </para>
/// </remarks>
public class SyncLinkRemoval
{
    /// <summary>Insertion order. Not an identity the outside world sees.</summary>
    public long Id { get; set; }

    /// <summary>The removed attachment's own identifier — what a phone holds the link under.</summary>
    public Guid AttachmentId { get; set; }

    /// <summary>The file the attachment hung, as it was when the row went.</summary>
    public Guid FileId { get; set; }

    /// <summary>The document that file belonged to, when it could still be found.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>The feature the attachment hung on, for a feature target.</summary>
    public Guid? FeatureId { get; set; }

    /// <summary>The kind of non-feature target, for a target addressed by the polymorphic pair.</summary>
    public AttachedEntityType? EntityType { get; set; }

    /// <summary>The non-feature target's identifier.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>When the row went — the database's clock, at the deleting statement.</summary>
    public DateTimeOffset RemovedAt { get; set; }
}
