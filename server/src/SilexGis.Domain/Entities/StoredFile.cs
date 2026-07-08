// SPDX-License-Identifier: AGPL-3.0-or-later
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

    public FileKind Kind { get; set; } = FileKind.Other;

    /// <summary>Format-specific metadata (EXIF, dimensions, layer info, …) as jsonb.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
