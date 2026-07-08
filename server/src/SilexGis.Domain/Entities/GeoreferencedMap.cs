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

    public Guid? CaveId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
