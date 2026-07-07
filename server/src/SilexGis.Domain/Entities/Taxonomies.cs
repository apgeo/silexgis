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

/// <summary>Surface-feature taxonomy with symbology.</summary>
public class FeatureType : TaxonomyBase
{
    public GeometryKind GeometryKind { get; set; } = GeometryKind.Point;

    /// <summary>File name inside the bundled symbol set (e.g. "sinkhole.png").</summary>
    public string? SymbolFile { get; set; }

    /// <summary>Optional OpenLayers style overrides (jsonb).</summary>
    public string? Style { get; set; }

    /// <summary>Optional JSON schema for typed feature properties (jsonb).</summary>
    public string? PropertiesSchema { get; set; }
}
