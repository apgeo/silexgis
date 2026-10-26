// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What a raster map shows of its cave. Stored as smallint; append only, never renumber.
/// </summary>
public enum RasterMapType : short
{
    /// <summary>A plan: the cave seen from above.</summary>
    PlanView = 0,

    /// <summary>A profile projected onto a vertical plane.</summary>
    ProjectedProfile = 1,

    /// <summary>A profile unrolled along the passages.</summary>
    ExtendedProfile = 2,

    /// <summary>Anything else a caver draws or scans — a sketch, a detail.</summary>
    Other = 3,
}

/// <summary>
/// A picture of a cave a phone carries — a scanned plan, a drawn profile — and the places pinned on
/// it by pixel.
/// </summary>
/// <remarks>
/// <para>
/// The picture is a document like any other, made from the bytes the phone sent, and it is also
/// attached to the cave with the map role, so the browser lists it among the cave's maps without a
/// screen of its own. The map row adds what an attachment cannot say: which kind of drawing it is,
/// which part of the cave it covers, and where it sits among the cave's maps on the phone.
/// </para>
/// <para>
/// A map holds no position and has no georeference. It is as visible as its cave is: the rasters
/// stream carries it to a reader exactly when the feature stream hands that reader its cave, and who
/// may change it is whoever may change the cave. Its identifier is the phone's own, adopted verbatim
/// the first time it arrives.
/// </para>
/// </remarks>
public class RasterMap : ITimestamped, IAuditable
{
    /// <summary>The phone's identifier for the map, adopted verbatim.</summary>
    public Guid Id { get; set; }

    /// <summary>The cave the map is of.</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>The part of the cave the map covers, when it covers one part; null for the whole.</summary>
    public Guid? CaveAreaFeatureId { get; set; }

    public required string Title { get; set; }

    public RasterMapType MapType { get; set; }

    /// <summary>Where the map sits among the cave's maps, as the phone orders them.</summary>
    public int SortOrder { get; set; }

    /// <summary>The picture: the document made from the bytes the phone staged for it.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The account whose upload brought the map.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// When the phone believes it last wrote the map, by its own clock. Provenance only and never
    /// compared: a phone's clock is not this server's.
    /// </summary>
    public DateTimeOffset? ClientUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public List<RasterPin> Pins { get; set; } = [];

    public bool IsDeleted => DeletedAt is not null;

    public string AuditId => Id.ToString();
}

/// <summary>One place pinned on a raster map, at a pixel of the picture.</summary>
/// <remarks>
/// <para>
/// A pin is a pixel and never a position. It still tells where a place is — on a picture that this
/// application may one day draw on the ground, a pixel is a coordinate — so a pin is given to a
/// reader exactly when its place is, and no route shows a pin of a place its reader may not be
/// given. At most one live pin per place on a map.
/// </para>
/// </remarks>
public class RasterPin : ITimestamped, IAuditable, IAuditChild
{
    /// <summary>The phone's identifier for the pin, adopted verbatim.</summary>
    public Guid Id { get; set; }

    public Guid RasterMapId { get; set; }

    public RasterMap? RasterMap { get; set; }

    /// <summary>The place pinned.</summary>
    public Guid PlaceFeatureId { get; set; }

    /// <summary>Pixels from the left edge of the picture.</summary>
    public int X { get; set; }

    /// <summary>Pixels from the top edge of the picture.</summary>
    public int Y { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(RasterMap);

    public string? RootEntityId => RasterMapId.ToString();
}
