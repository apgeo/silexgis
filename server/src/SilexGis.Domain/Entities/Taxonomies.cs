// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Admin-editable lookup taxonomies. bigint identity keys — exchanged
/// across installations by natural key <c>Code</c>.
/// </summary>
public abstract class TaxonomyBase : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

public class CaveType : TaxonomyBase;

public class EntranceType : TaxonomyBase;

public class RockType : TaxonomyBase;

/// <summary>
/// Data-level feature-kind registry: every generic feature names its kind here (subtyped
/// kinds — caves, entrances, centerlines — keep their own taxonomies instead). Adding a
/// feature kind is a row, not DDL.
/// </summary>
public class FeatureType : TaxonomyBase
{
    public FeatureCategory Category { get; set; } = FeatureCategory.Surface;

    /// <summary>OGC geometry classes rows of this kind may carry (incl. Multi* variants, opt-in).</summary>
    public GeometryClass[] AcceptedGeometryClasses { get; set; } = [GeometryClass.Point];

    /// <summary>
    /// Display of protected rows for callers without exact view. Security-bearing:
    /// admin-only edits, audited; seeded values contract-tested.
    /// </summary>
    public ProtectedDisplay ProtectedDisplay { get; set; } = ProtectedDisplay.SnapPoint;

    /// <summary>Kinds that only make sense inside a parent (e.g. a cave sector) refuse rootless rows.</summary>
    public bool RequiresParent { get; set; }

    /// <summary>File name inside the bundled symbol set (e.g. "sinkhole.png").</summary>
    public string? SymbolFile { get; set; }

    /// <summary>Optional OpenLayers style overrides (jsonb).</summary>
    public string? Style { get; set; }

    /// <summary>Optional JSON schema for typed feature properties (jsonb).</summary>
    public string? PropertiesSchema { get; set; }

    /// <summary>
    /// Bumped whenever <see cref="PropertiesSchema"/> changes; feature rows stamp the
    /// version they validated against and are revalidated lazily on their next edit.
    /// </summary>
    public int PropertiesSchemaVersion { get; set; } = 1;
}
