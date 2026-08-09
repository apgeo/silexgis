// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Broad file categories used for storage layout and later media handling. Stored as
/// smallint and append-only: the values are part of the schema contract, so a new member
/// takes the next free number and nothing is ever renumbered or reused.
/// </summary>
public enum FileKind : short
{
    Image = 0,
    Document = 1,
    Survey = 2,
    Raster = 3,
    Vector = 4,
    Model = 5,
    Other = 6,
    Audio = 7,
    Video = 8,
}

/// <summary>
/// An uploaded file's metadata; bytes live in the file store (<see cref="IFileStore"/>),
/// addressed by <see cref="StoragePath"/> relative to the store root. Immutable after
/// upload — replacing content means uploading a new version of the document it belongs to.
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

    /// <summary>
    /// The document revision these bytes belong to. Every stored file has one: a photo is a
    /// document with a single version and a single page. Version-detail fields (number,
    /// uploader, document date, change note) live on the version, not here, so several files
    /// that make up one revision — the scan and its cloud-optimized rendition, a two-part
    /// survey — cannot disagree about them.
    /// </summary>
    public Guid DocumentVersionId { get; set; }

    public FileKind Kind { get; set; } = FileKind.Other;

    /// <summary>
    /// Capture location for a photo, read from EXIF GPS at upload (null when the image carries
    /// no GPS or is not an image). A geotagged photo's point IS location data: paths that emit
    /// it enforce the same cave-location protection as entrance coordinates.
    /// </summary>
    public Point? Geom { get; set; }

    /// <summary>
    /// How <see cref="Geom"/> came to be what it is. Recorded rather than inferred: a position
    /// somebody placed by hand must survive every later automatic pass over the bytes, and
    /// somebody about to drag a picture onto the map has to be told when the camera already
    /// recorded a fix they are about to replace. The point alone answers neither question.
    /// </summary>
    public PhotoPositionSource PositionSource { get; set; } = PhotoPositionSource.None;

    /// <summary>
    /// Capture altitude in metres, as the camera reported it. Kept beside the point rather than
    /// inside it: the column is 2-D by design, and this number is shown with the picture rather
    /// than measured against anything.
    /// </summary>
    public double? AltitudeMeters { get; set; }

    /// <summary>
    /// Which way the camera was pointing, in degrees clockwise from north. Worth more than it
    /// looks: an entrance photograph that says which way the lens faced is what turns "somewhere
    /// on this slope" into a hole somebody can walk back to.
    /// </summary>
    public double? DirectionDegrees { get; set; }

    /// <summary>
    /// Whether <see cref="DirectionDegrees"/> is measured from magnetic north rather than true.
    /// Phones record either, and in this part of the world the two differ by enough to matter
    /// when you are looking for a hole in a forest.
    /// </summary>
    public bool DirectionIsMagnetic { get; set; }

    /// <summary>
    /// The dilution of precision the fix reported: how much the satellite geometry multiplied
    /// whatever error the receiver had. Carried because a phone under a cliff is tens of metres
    /// out and nothing on screen would otherwise say so — a bad fix should look uncertain
    /// rather than authoritative.
    /// <para>
    /// Dimensionless, and stored that way. Turning it into metres means assuming a figure for
    /// the receiver's own error, and a number that reads as a measurement and is not is worse
    /// than the honest one the file states. <see cref="Geo.PositionConfidence"/> is what turns
    /// it into something a person can read.
    /// </para>
    /// </summary>
    public double? PositionDop { get; set; }

    /// <summary>
    /// Format-specific metadata (EXIF, dimensions, layer info, …) as jsonb.
    /// <para>
    /// For a photograph this holds what is shown beside the picture and nothing that is queried:
    /// camera, lens, orientation, exposure. Anything a listing filters or orders on has a column
    /// of its own instead — a jsonb bag is not indexed for that.
    /// </para>
    /// </summary>
    public string Metadata { get; set; } = "{}";

    /// <summary>
    /// Pages this file holds, once something has counted them. An image is one page by
    /// definition and gets its count at upload; paged formats are counted by text
    /// extraction, which is the only thing that reads them.
    /// </summary>
    public int? PageCount { get; set; }

    /// <summary>
    /// How far reading this file's text layer has got. Written only by extraction, so it says
    /// nothing about who uploaded the file or when — a file whose format holds no text rests
    /// at <see cref="TextExtractionState.NotApplicable"/> and is never queued.
    /// </summary>
    public TextExtractionState TextExtraction { get; set; } = TextExtractionState.NotApplicable;

    /// <summary>
    /// Why reading failed, when it did. Bounded free text meant for whoever has to explain a
    /// document that will not open; null in every other state.
    /// </summary>
    public string? TextExtractionError { get; set; }

    /// <summary>
    /// How far turning this file into a portable document — so its pages can be drawn — has
    /// got. Only ever left the resting state for a format that has no pagination of its own;
    /// a portable document already has pages and a picture has nothing to convert.
    /// </summary>
    public ConversionState Conversion { get; set; } = ConversionState.NotApplicable;

    /// <summary>
    /// The uploaded file this one was converted from, when it is a converted copy rather than
    /// something a person uploaded. Null for everything a person uploaded, which is almost
    /// every row.
    /// <para>
    /// The direction is deliberate: the copy points at the original, so the original is never
    /// touched to record that a copy exists. An upload is immutable, and a derived file is
    /// stored beside it exactly as a page of read-out text is stored beside it.
    /// </para>
    /// </summary>
    public Guid? ConvertedFromFileId { get; set; }

    /// <summary>Author embedded in the file's own metadata, not the account that uploaded it.</summary>
    public string? Author { get; set; }

    /// <summary>Software that produced the file, as the file itself reports it.</summary>
    public string? Producer { get; set; }

    /// <summary>Creation timestamp embedded in the file, distinct from the upload time.</summary>
    public DateTimeOffset? ContentCreatedAt { get; set; }

    /// <summary>Last-modified timestamp embedded in the file, distinct from the upload time.</summary>
    public DateTimeOffset? ContentModifiedAt { get; set; }

    /// <summary>Playing time of an audio or video file, in seconds.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Primary codec of an audio or video file, as the container names it.</summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Quarter-turns clockwise to apply when drawing this picture: 0, 1, 2 or 3.
    ///
    /// <para>
    /// Rotating a photograph does not rewrite it. The upload is immutable — its hash is what
    /// duplicate detection compares and what the archive means by "this is what we were given" —
    /// so a turn is recorded here and applied to every rendering instead. Nothing is copied,
    /// nothing is re-encoded, and turning a picture back leaves it byte-identical to what
    /// arrived.
    /// </para>
    /// <para>
    /// This is a correction somebody made, and is deliberately separate from the orientation tag
    /// the camera wrote: that one is already honoured when a rendering is drawn, and a picture
    /// needs turning precisely when the camera got it wrong or said nothing.
    /// </para>
    /// </summary>
    public int OrientationQuarterTurns { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
