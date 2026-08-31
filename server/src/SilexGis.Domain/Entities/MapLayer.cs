// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Admin-configured base/overlay layer catalog entry.</summary>
public class MapLayer : ITimestamped
{
    public long Id { get; set; }

    public required string Name { get; set; }

    public MapLayerKind LayerKind { get; set; } = MapLayerKind.Xyz;

    public required string UrlTemplate { get; set; }

    /// <summary>Layer-kind specific options (jsonb), passed through to OpenLayers.</summary>
    public string? Options { get; set; }

    public string? Attribution { get; set; }

    /// <summary>
    /// The heading this layer is listed under, or null for the ungrouped top of the list.
    /// </summary>
    /// <remarks>
    /// Presentation, and deliberately so: a catalogue of forty tile sources does not fit the left
    /// panel, and the only thing that makes it navigable is somebody having said which ones belong
    /// together. Deciding that per installation is the point — a group mapping Romanian karst wants
    /// its topographic sources at the top, and nothing here can know that.
    /// </remarks>
    public string? GroupName { get; set; }

    /// <summary>
    /// Zoom range the source actually holds tiles for. Outside it the layer is not drawn.
    /// </summary>
    /// <remarks>
    /// This is not a nicety. A tile server asked for a zoom it does not have answers 404 for every
    /// tile in the viewport at once, and what that looks like is a map that goes blank when you
    /// zoom in — which reads as the application breaking rather than as the source ending. Both
    /// ends are stated because both happen: OpenTopoMap stops at 17, and some overlays start well
    /// above 0.
    /// </remarks>
    public int MinZoom { get; set; }

    /// <inheritdoc cref="MinZoom"/>
    public int MaxZoom { get; set; } = 19;

    /// <summary>
    /// Which configured access key this source's URL needs, or null when it needs none.
    /// </summary>
    /// <remarks>
    /// A name rather than the key itself, and the key is never stored here. The value is looked up
    /// at publication time and substituted into <see cref="UrlTemplate"/>, so a catalogue file can
    /// be committed, shared and read by anybody while the key stays in the installation's own
    /// environment. A source naming a key nobody configured is published disabled rather than
    /// published broken: a tile URL with a literal placeholder in it answers 401 for every tile
    /// and looks exactly like a source that is down.
    /// </remarks>
    public string? ApiKeyName { get; set; }

    public bool IsBase { get; set; }

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
