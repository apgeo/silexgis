// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// An uploaded vector dataset (GPX, KML, GeoJSON, zipped shapefile, WKT/WKB). The raw
/// upload stays in the file store; a server-side import job materializes its rows into
/// <see cref="GeofileFeature"/> so the data becomes bbox-queryable and exportable.
/// </summary>
public class Geofile : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid FileId { get; set; }

    public GeofileFormat Format { get; set; }

    /// <summary>Source SRID when known; imported geometries are always stored in 4326.</summary>
    public int? Srid { get; set; }

    public GeofileImportStatus ImportStatus { get; set; } = GeofileImportStatus.Uploaded;

    /// <summary>Human-readable reason when <see cref="ImportStatus"/> is Failed.</summary>
    public string? ImportError { get; set; }

    public int FeatureCount { get; set; }

    /// <summary>Envelope of all imported features (4326); null until imported.</summary>
    public Polygon? Bbox { get; set; }

    /// <summary>Optional client style overrides (jsonb): stroke, fill, marker, ….</summary>
    public string? Style { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One vector row imported from a geofile. Derived, re-importable data — bigint key,
/// access control inherited from the owning geofile.
/// </summary>
public class GeofileFeature
{
    public long Id { get; set; }

    public Guid GeofileId { get; set; }

    public required Geometry Geom { get; set; }

    /// <summary>Source attributes as a JSON object (jsonb).</summary>
    public string Properties { get; set; } = "{}";
}
