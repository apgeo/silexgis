// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Admin-configured base/overlay layer catalog entry (02-data-model.md §4).</summary>
public class MapLayer : ITimestamped
{
    public long Id { get; set; }

    public required string Name { get; set; }

    public MapLayerKind LayerKind { get; set; } = MapLayerKind.Xyz;

    public required string UrlTemplate { get; set; }

    /// <summary>Layer-kind specific options (jsonb), passed through to OpenLayers.</summary>
    public string? Options { get; set; }

    public string? Attribution { get; set; }

    public bool IsBase { get; set; }

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
