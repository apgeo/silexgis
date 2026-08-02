// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The stable identity of a piece of content, independent of the bytes that currently
/// represent it. Four levels hang off it:
/// <code>
/// Document           title, owner/visibility trio, timestamps, audit
/// └── DocumentVersion    ordered; exactly one is current, the rest are superseded
///     └── StoredFile         one or more physical files per version
///         └── DocumentPage       ordered page rows carrying extracted text
/// </code>
/// Every stored file belongs to a version, so a photo is simply a document with one
/// version and one page. Anything that must survive a re-upload — links, cabinets,
/// comments — anchors here rather than on a file id, because file ids change with every
/// new version while this one never does.
/// </summary>
public class Document : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Human-readable name; defaults to the first upload's file name.</summary>
    public required string Title { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
