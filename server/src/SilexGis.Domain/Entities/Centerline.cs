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
/// Centerline subtype row (shared PK with its <see cref="Feature"/>, which owns the name
/// and the 3D MultiLineString geometry — the cave's physical shape as a spatial object).
/// A centerline is a child of its cave in the containment hierarchy; multiple survey
/// generations may coexist and exactly one per cave is the default — the shape used for
/// map display and cross-feature spatial queries. Centerlines trace the cave's exact
/// position underground, so under location protection they are withheld entirely.
/// </summary>
public class Centerline : IAuditable
{
    /// <summary>Equals the feature id (shared primary key).</summary>
    public Guid Id { get; set; }

    public Feature Feature { get; set; } = null!;

    /// <summary>
    /// The owning cave (feature id; FK to the cave subtype row). Structural truth for the
    /// exactly-one-cave constraint; the write service mirrors it as the primary
    /// containment edge, and the verifier checks the two agree.
    /// </summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>The survey model this centerline was derived from, when known.</summary>
    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// The cave's current shape: exactly one centerline per cave holds this flag
    /// (partial unique index on the primary parent edge maintained by the write service).
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 2D display skeleton: the traverse network with splays removed and the surviving shots
    /// sewn into long polylines, built once at upload. Null when the reduction found nothing
    /// to remove, in which case the feature geometry is already its own skeleton. This is
    /// what the map overlay draws at overview zooms — a survey export is mostly splays, and
    /// drawing them costs one canvas path each.
    /// </summary>
    public MultiLineString? Skeleton { get; set; }

    /// <summary>
    /// Line components in the feature geometry. Stored because the map endpoint decides
    /// whether a centerline is too heavy to serve before it reads any geometry, and counting
    /// components on a multi-megabyte geometry means fetching all of it out of TOAST storage.
    /// </summary>
    public int PathCount { get; set; }

    /// <summary>Line components in <see cref="Skeleton"/>; null when there is no skeleton.</summary>
    public int? SkeletonPathCount { get; set; }

    /// <summary>
    /// Geodesic length in meters of the passage this centerline traces, computed server-side when
    /// the line work arrives.
    ///
    /// <para>
    /// Wall shots are not passage and are not in it. A centerline drawn by hand or imported as a
    /// track has none to leave out, so its length is simply the length of its geometry; one read
    /// out of a survey file leaves out the legs that file flagged as splays, which in a whole-system
    /// export are the large majority of them. So this means the same thing whatever the centerline
    /// came from — and for a read survey it is deliberately not the length of
    /// <see cref="Feature"/>'s geometry, which also holds every wall shot the surveyor measured.
    /// </para>
    /// </summary>
    public decimal? LengthM { get; set; }

    public CenterlineSource Source { get; set; } = CenterlineSource.Uploaded;

    public string AuditId => Id.ToString();
}
