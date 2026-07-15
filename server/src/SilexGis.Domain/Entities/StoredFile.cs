// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>Broad file categories used for storage layout and later media handling.</summary>
public enum FileKind : short
{
    Image = 0,
    Document = 1,
    Survey = 2,
    Raster = 3,
    Vector = 4,
    Model = 5,
    Other = 6,
}

/// <summary>
/// An uploaded file's metadata; bytes live in the file store (<see cref="IFileStore"/>),
/// addressed by <see cref="StoragePath"/> relative to the store root. Immutable after
/// upload — replacing content means uploading a new file.
/// </summary>
public class StoredFile : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Relative path inside the file store root; never contains user input.</summary>
    public required string StoragePath { get; set; }

    public required string OriginalName { get; set; }

    public required string MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Lower-case hex SHA-256 of the content.</summary>
    public required string Sha256 { get; set; }

    public Guid? UploadedBy { get; set; }

    /// <summary>
    /// Stable document identity across versions: the id of the first version in the chain.
    /// A file's whole version chain is <c>WHERE version_group_id = @group</c>; the head is the
    /// row with the highest <see cref="VersionNumber"/>. Content stays immutable per row.
    /// </summary>
    public Guid VersionGroupId { get; set; }

    /// <summary>1-based position in the version chain; unique within a group.</summary>
    public int VersionNumber { get; set; } = 1;

    public FileKind Kind { get; set; } = FileKind.Other;

    /// <summary>
    /// User-set calendar date the document/photo is *from* (distinct from <see cref="CreatedAt"/>,
    /// the upload time). Prefilled client-side from EXIF where present; copied to new versions.
    /// </summary>
    public DateOnly? DocumentDate { get; set; }

    /// <summary>
    /// Capture location for a photo, read from EXIF GPS at upload (null when the image carries
    /// no GPS or is not an image). A geotagged photo's point IS location data: paths that emit
    /// it enforce the same cave-location protection as entrance coordinates.
    /// </summary>
    public Point? Geom { get; set; }

    /// <summary>Format-specific metadata (EXIF, dimensions, layer info, …) as jsonb.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
