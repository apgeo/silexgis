// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Schema-level discriminator of the feature supertype. Closed set known to code — one
/// member per subtype table plus <see cref="Generic"/> for data-driven kinds. Stored as
/// smallint; append only, never renumber.
/// </summary>
public enum FeatureKind : short
{
    /// <summary>Data-driven kind: the row's <c>FeatureTypeId</c> names it; attributes live in jsonb.</summary>
    Generic = 0,

    Cave = 1,

    CaveEntrance = 2,

    /// <summary>A cave's surveyed centerline — the cave's physical shape as a spatial object.</summary>
    Centerline = 3,
}

/// <summary>
/// The supertype row every physical geospatial feature has (class-table inheritance).
/// Owns identity, kind, the map payload (name/geometry), access control, location
/// protection, the containment-hierarchy cache and lifecycle — so the hot map path reads
/// one table with no joins. Rich kinds keep a shared-PK subtype row (<see cref="Cave"/>,
/// <see cref="CaveEntrance"/>, <see cref="Centerline"/>) holding only type-specific
/// attributes; generic kinds are fully described by <see cref="FeatureTypeId"/> +
/// <see cref="Properties"/>.
///
/// Derived columns (<see cref="AncestorIds"/>, <see cref="IsProtectedEffective"/>, and
/// subtree <see cref="DeletedAt"/> stamping) are maintained exclusively by the feature
/// write service and re-checked by the integrity verifier job — never written by
/// endpoint code directly.
/// </summary>
public class Feature : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public FeatureKind Kind { get; set; }

    /// <summary>Data-level kind; required for <see cref="FeatureKind.Generic"/> rows, null for subtyped kinds.</summary>
    public long? FeatureTypeId { get; set; }

    /// <summary>
    /// Denormalized from the kind/feature type by the write service, so map layers and
    /// partial indexes can separate point-ish content from large area polygons.
    /// </summary>
    public FeatureCategory Category { get; set; } = FeatureCategory.Surface;

    public string? Name { get; set; }

    /// <summary>
    /// The feature's geometry (SRID 4326; Z admitted where the source has it). Geometry
    /// class varies per kind/type; nullable because grouping features (systems, areas
    /// without a drawn boundary yet) may have none.
    /// </summary>
    public Geometry? Geom { get; set; }

    public string? Description { get; set; }

    /// <summary>JSON object with kind-specific properties (jsonb). Also present on subtyped kinds for open-ended extras.</summary>
    public string Properties { get; set; } = "{}";

    /// <summary>
    /// The <c>feature_types.properties_schema_version</c> this row's properties were last
    /// validated against. Null when the kind has no schema. Rows behind the kind's current
    /// version stay valid-as-written and are revalidated on their next edit.
    /// </summary>
    public int? PropertiesSchemaVersion { get; set; }

    /// <summary>
    /// Marks a protection root: exact coordinates of this feature and every containment
    /// descendant require the ViewExactLocation permission (evaluated against every
    /// protected root above a row — most-restrictive wins).
    /// </summary>
    public bool LocationProtected { get; set; }

    /// <summary>Derived: any ancestor (including self) has <see cref="LocationProtected"/>.</summary>
    public bool IsProtectedEffective { get; set; }

    /// <summary>
    /// Derived: this feature's id plus every ancestor id over all paths of the containment
    /// DAG. The flat context visibility/protection/cascade queries splice as
    /// <c>= ANY(ancestor_ids)</c> — read paths never recurse.
    /// </summary>
    public Guid[] AncestorIds { get; set; } = [];

    // Access control (RLS-ready columns)
    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete, stamped over the whole containment subtree. Purge is an explicit admin job.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    // Shared-PK subtype rows (at most one, matching Kind — enforced by composite FK + CHECK).
    public Cave? Cave { get; set; }

    public CaveEntrance? Entrance { get; set; }

    public Centerline? Centerline { get; set; }

    public string AuditId => Id.ToString();
}
