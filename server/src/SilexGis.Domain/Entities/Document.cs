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

    /// <summary>
    /// Which kind of document this is, naming the row that carries the metadata schema
    /// <see cref="Metadata"/> is validated against. Null means untyped: the document
    /// accepts any metadata object, which is what every upload starts as.
    /// </summary>
    public long? DocumentTypeId { get; set; }

    /// <summary>
    /// Typed, user-set metadata for this document's kind, as jsonb. Facts read out of the
    /// file itself — page count, author, embedded dates, media duration — are columns on
    /// the file rather than entries here, because those are queried and sorted on.
    /// </summary>
    public string Metadata { get; set; } = "{}";

    /// <summary>
    /// The document type's metadata-schema version this row's <see cref="Metadata"/> was
    /// last validated against. Null when the type carries no schema, or when the document
    /// has no type. A row behind its type's current version stays valid as written — it is
    /// re-checked against the version stamped here, and against the current one only when
    /// the metadata itself is rewritten.
    /// </summary>
    public int? MetadataSchemaVersion { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
