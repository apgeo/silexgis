// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>Broad categories of georeferenced raster maps. Stored as smallint.</summary>
public enum MapKind : short
{
    Geological = 0,
    Topographic = 1,
    Tourist = 2,
    CaveMap = 3,
    Other = 4,
}

/// <summary>Server-side raster preparation state. Stored as smallint.</summary>
public enum RasterStatus : short
{
    Uploaded = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,

    /// <summary>
    /// A drawing with no place on the map yet: the sheet was drawn (a portable document's page), but
    /// it carries no georeferencing and nobody has placed it. Listed, never drawn on the map.
    /// </summary>
    Unplaced = 4,
}

/// <summary>Where a sheet's current placement came from. Stored as smallint.</summary>
public enum PlacementSource : short
{
    /// <summary>The georeferencing the portable document carried itself.</summary>
    Embedded = 0,

    /// <summary>Control points somebody set by hand, which outrank the document's own.</summary>
    Manual = 1,
}

/// <summary>
/// A georeferenced raster overlay (geological/topographic/tourist/cave map). The upload
/// is normalized to a Cloud-Optimized GeoTIFF by a background job so the client can
/// stream tiles via HTTP range requests; <see cref="FileId"/> points at the COG.
/// Cave-linked maps inherit the cave's location protection: they are omitted entirely
/// for callers without the exact-location permission.
/// </summary>
public class GeoreferencedMap : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    public MapKind MapKind { get; set; } = MapKind.Other;

    /// <summary>The COG file once processing succeeds (the original upload before that).</summary>
    public Guid FileId { get; set; }

    public RasterStatus Status { get; set; } = RasterStatus.Uploaded;

    public string? ProcessingError { get; set; }

    /// <summary>Raster footprint (4326); null until processed.</summary>
    public Polygon? Bbox { get; set; }

    public int? MinZoom { get; set; }

    public int? MaxZoom { get; set; }

    public string? Attribution { get; set; }

    /// <summary>Initial layer opacity, 0–1.</summary>
    public decimal DefaultOpacity { get; set; } = 0.8m;

    /// <summary>Optional owning cave (feature id, FK to the cave subtype row) for georeferenced cave maps.</summary>
    public Guid? CaveFeatureId { get; set; }

    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// The uploaded portable document this map is drawn from, when it is a sheet rather than a
    /// GeoTIFF; null for a GeoTIFF upload. Kept apart from <see cref="FileId"/>, which names what
    /// the map is drawn from now (the source until the COG exists, the COG after).
    /// </summary>
    public Guid? SourceFileId { get; set; }

    /// <summary>The page of the source that was drawn, from 1.</summary>
    public int? SourcePage { get; set; }

    /// <summary>How many pages the source has, as the drawing job read it.</summary>
    public int? SourcePageCount { get; set; }

    /// <summary>The optional-content layer names of the drawn page, as a JSON array; [] when none.</summary>
    public string? SourceLayers { get; set; }

    /// <summary>The layer names chosen for drawing, as a JSON array; null for all of them.</summary>
    public string? RenderLayers { get; set; }

    /// <summary>The resolution the page was drawn at, in dots per inch.</summary>
    public decimal? RenderDpi { get; set; }

    public int? RenderWidth { get; set; }

    public int? RenderHeight { get; set; }

    /// <summary>What the drawing library complained about while still drawing the page whole.</summary>
    public string? RenderWarnings { get; set; }

    /// <summary>The drawn page with no place, kept so a new placement is a warp, not a redraw.</summary>
    public Guid? MasterFileId { get; set; }

    /// <summary>A small picture of the drawn page for the placement editor.</summary>
    public Guid? PreviewFileId { get; set; }

    /// <summary>The placement, as <see cref="Geo.MapPlacement"/> JSON; null when the sheet has none.</summary>
    public string? Placement { get; set; }

    public PlacementSource? PlacementSource { get; set; }

    /// <summary>
    /// The placement the document carried itself, kept when a hand placement overrides it so that
    /// removing the hand placement can put the document's own back without drawing it again.
    /// </summary>
    public string? EmbeddedPlacement { get; set; }

    /// <summary>The root-mean-square misfit of the control points, in metres on the ground.</summary>
    public decimal? PlacementRmsMetres { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
