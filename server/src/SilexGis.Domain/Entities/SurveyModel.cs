// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>Survey file format of a 3D cave model. Stored as smallint.</summary>
public enum SurveyModelFormat : short
{
    /// <summary>Therion Loch export (.lox).</summary>
    Lox = 0,

    /// <summary>Survex image file (.3d).</summary>
    Survex3d = 1,

    /// <summary>Triangle mesh of the passage walls, as exported by every survey toolchain in use.</summary>
    Stl = 2,
}

/// <summary>
/// How far an uploaded model has got towards being drawable.
///
/// <para>
/// Only wall meshes have anything to do here: line-plot formats are handed to the viewer as
/// uploaded, so they are ready the moment they land.
/// </para>
/// </summary>
public enum SurveyModelStatus : short
{
    /// <summary>Drawable now.</summary>
    Ready = 0,

    /// <summary>Waiting for conversion.</summary>
    Pending = 1,

    /// <summary>Being converted.</summary>
    Processing = 2,

    /// <summary>Conversion failed; <see cref="SurveyModel.ProcessingError"/> says why.</summary>
    Failed = 3,
}

/// <summary>
/// A 3D cave survey model (Therion .lox / Survex .3d) rendered client-side by the
/// embedded viewer. Belongs to exactly one cave and inherits its access control — no own
/// RLS columns. The file carries absolute georeferenced coordinates, so for
/// location-protected caves the whole record (and its file URL) is withheld from callers
/// without the exact-location permission.
/// </summary>
public class SurveyModel : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning cave's feature id (FK to the cave subtype row).</summary>
    public Guid CaveFeatureId { get; set; }

    public required string Name { get; set; }

    public Guid FileId { get; set; }

    public SurveyModelFormat Format { get; set; }

    public string? Description { get; set; }

    public DateOnly? SurveyedAt { get; set; }

    public SurveyModelStatus Status { get; set; } = SurveyModelStatus.Ready;

    /// <summary>Why conversion failed, in words worth showing whoever uploaded the file.</summary>
    public string? ProcessingError { get; set; }

    /// <summary>
    /// The drawable model produced from <see cref="FileId"/>, once there is one.
    ///
    /// <para>
    /// A second file rather than a replacement: uploads are immutable, and the original is what a
    /// re-conversion starts from when the anchor turns out to have been declared wrongly.
    /// </para>
    /// </summary>
    public Guid? ConvertedFileId { get; set; }

    /// <summary>
    /// Where the converted model's own zero point sits, as a WGS 84 point.
    ///
    /// <para>
    /// This is the cave's position by another name, so it is location data and travels only on the
    /// paths that already withhold this whole record from callers without exact-location access.
    /// The converted file itself holds nothing but offsets from here, which is what lets it be
    /// delivered at all.
    /// </para>
    /// </summary>
    public Point? Anchor { get; set; }

    /// <summary>The altitude the model's zero plane sits at, in metres.</summary>
    public double? AnchorHeightM { get; set; }

    /// <summary>
    /// The coordinate system the uploaded file's own coordinates were declared to be in, or null
    /// for plain metres about a fixed point. Kept so a re-conversion can repeat what was done.
    /// </summary>
    public int? SourceEpsg { get; set; }

    /// <summary>
    /// The turn, in degrees, already applied to the converted model to bring the source grid's
    /// north onto true north. Recorded for the audit trail — it must not be applied again by
    /// anything drawing the model, which is why it is not published.
    /// </summary>
    public double? AppliedRotationDeg { get; set; }

    /// <summary>Triangles in the converted model, after the ones carrying no surface were dropped.</summary>
    public int? TriangleCount { get; set; }

    /// <summary>
    /// Set when the uploaded file's coordinates were already too large for the 32-bit floats it
    /// stores them in — so the survey's precision was lost by the exporter, before upload, and
    /// nothing here can recover it. Worth telling the uploader, who can re-export about a local
    /// origin and get it back.
    /// </summary>
    public bool SourcePrecisionLost { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Survey models surface in their cave's timeline (features audit as "Feature").
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}
