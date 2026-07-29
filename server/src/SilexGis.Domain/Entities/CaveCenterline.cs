// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>How a centerline came to exist. Stored as smallint.</summary>
public enum CenterlineSource : short
{
    /// <summary>Uploaded by a user as GeoJSON/GPX.</summary>
    Uploaded = 0,

    /// <summary>Extracted from a survey model file (future processing job).</summary>
    Extracted = 1,
}

/// <summary>
/// A cave's surveyed centerline projected for display over surface maps. Belongs to one
/// cave and inherits its access control — no own RLS columns. Centerlines trace the
/// cave's exact position underground, so for location-protected caves they are withheld
/// entirely from callers without the exact-location permission.
/// </summary>
public class CaveCenterline : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaveId { get; set; }

    /// <summary>The survey model this centerline was derived from, when known.</summary>
    public Guid? SurveyModelId { get; set; }

    public required string Name { get; set; }

    /// <summary>3D centerline (Z = altitude in meters; 0 when the source had none).</summary>
    public required MultiLineString Geom { get; set; }

    /// <summary>
    /// 2D display skeleton: the traverse network with splays removed and the surviving shots
    /// sewn into long polylines, built once at upload by <c>CenterlineSkeleton</c>. Null when
    /// the rule found nothing to reduce, in which case <see cref="Geom"/> is already its own
    /// skeleton. This is what the map overlay draws at overview zooms — a survey export is
    /// mostly splays, and drawing them costs one canvas path each.
    /// </summary>
    public MultiLineString? Skeleton { get; set; }

    /// <summary>
    /// Line components in <see cref="Geom"/>. Stored because the map endpoint decides whether a
    /// centerline is too heavy to serve before it reads any geometry, and counting components on
    /// a multi-megabyte geometry means fetching all of it out of TOAST storage.
    /// </summary>
    public int PathCount { get; set; }

    /// <summary>Line components in <see cref="Skeleton"/>; null when there is no skeleton.</summary>
    public int? SkeletonPathCount { get; set; }

    /// <summary>Geodesic length in meters, computed server-side at upload.</summary>
    public decimal? LengthM { get; set; }

    public CenterlineSource Source { get; set; } = CenterlineSource.Uploaded;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Centerlines surface in their cave's timeline.
    public string RootEntityType => nameof(Cave);

    public string RootEntityId => CaveId.ToString();
}
