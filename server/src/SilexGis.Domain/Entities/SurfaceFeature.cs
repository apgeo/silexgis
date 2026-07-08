// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A karst surface feature (sinkhole, pit, fracture line, lake, …). Geometry class varies
/// per feature type (point/line/polygon); free-form typed properties live in jsonb.
/// Optionally associated with a cave (e.g. a sinkhole above a known cave).
/// </summary>
public class SurfaceFeature : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string? Name { get; set; }

    public long FeatureTypeId { get; set; }

    public required Geometry Geom { get; set; }

    public string? Description { get; set; }

    /// <summary>JSON object with type-specific properties (jsonb).</summary>
    public string Properties { get; set; } = "{}";

    public Guid? CaveId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
