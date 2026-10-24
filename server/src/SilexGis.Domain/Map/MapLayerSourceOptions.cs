// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Map;

/// <summary>
/// How a Web Map Service draws one catalogue entry: the service is asked for one picture per map
/// tile, in the map's own projection or in plain longitude and latitude.
/// </summary>
/// <param name="Layers">The service's layer names, comma-separated, as its capabilities list them.</param>
/// <param name="Styles">The style names, comma-separated, one per layer; empty for each layer's default.</param>
/// <param name="Format">The picture format to ask for, e.g. <c>image/png</c> or <c>image/jpeg</c>.</param>
/// <param name="Version"><c>1.3.0</c> or <c>1.1.1</c>.</param>
/// <param name="Transparent">Whether to ask for a transparent background — what an overlay wants.</param>
/// <param name="Crs">
/// The coordinate system each tile is asked in: <c>EPSG:3857</c> (the map's own, and what every
/// tile then lines up with exactly) or <c>EPSG:4326</c> (for a service that offers nothing else).
/// </param>
public sealed record MapLayerWmsOptions(
    string Layers,
    string Styles,
    string Format,
    string Version,
    bool Transparent,
    string Crs);

/// <summary>
/// How a Web Map Tile Service draws one catalogue entry: ready-cut tiles on one of two well-known
/// grids, named here rather than read from the service's capabilities document.
/// </summary>
/// <remarks>
/// An explicit grid rather than a capabilities address, because it is the one form both the flat
/// map and the 3D scene can draw from: the 3D engine takes a layer, a matrix set and a tiling
/// scheme and reads no capabilities at all, so a capabilities address would be honoured in one
/// view and silently ignored in the other. The two grids are the two every public tile service
/// offers — the web-mercator pyramid (<c>GoogleMapsCompatible</c>) and the two-tile longitude and
/// latitude one (<c>WorldCRS84Quad</c>) — and a matrix set on either, whatever its own name, is
/// drawn correctly by naming its coordinate system.
/// </remarks>
/// <param name="Layer">The service's layer identifier.</param>
/// <param name="MatrixSet">The tile matrix set identifier, exactly as the service names it.</param>
/// <param name="Format">The tile format, e.g. <c>image/png</c>.</param>
/// <param name="Style">The style identifier; <c>default</c> where the service offers only one.</param>
/// <param name="Crs">
/// <c>EPSG:3857</c> for a web-mercator matrix set, <c>EPSG:4326</c> for a two-tile geographic one.
/// </param>
/// <param name="MatrixIdPrefix">
/// What the service puts before the zoom level in each matrix identifier — empty for <c>0</c>,
/// <c>1</c>, <c>2</c>…, or e.g. <c>EPSG:3857:</c> for <c>EPSG:3857:0</c>, <c>EPSG:3857:1</c>…
/// </param>
public sealed record MapLayerWmtsOptions(
    string Layer,
    string MatrixSet,
    string Format,
    string Style,
    string Crs,
    string MatrixIdPrefix);

/// <summary>What the catalogue knows about drawing each kind of map layer.</summary>
public static class MapLayerKinds
{
    /// <summary>The map's own projection, and the default for both services.</summary>
    public const string WebMercator = "EPSG:3857";

    /// <summary>Plain longitude and latitude.</summary>
    public const string Geographic = "EPSG:4326";

    /// <summary>The coordinate systems a WMS or WMTS entry may name.</summary>
    public static readonly IReadOnlyList<string> SupportedCrs = [WebMercator, Geographic];

    /// <summary>The WMS versions the map can speak.</summary>
    public static readonly IReadOnlyList<string> SupportedWmsVersions = ["1.3.0", "1.1.1"];

    /// <summary>
    /// Whether this version can draw the kind at all. Vector tiles and cloud-optimised GeoTIFFs
    /// have names in the stored vocabulary and nothing that draws them yet.
    /// </summary>
    public static bool IsDrawable(MapLayerKind kind) =>
        kind is MapLayerKind.Xyz or MapLayerKind.Wms or MapLayerKind.Wmts;

    /// <summary>
    /// The catalogue's word for a kind: what an operator writes in the file's <c>kind</c>
    /// attribute, and what a problem line names.
    /// </summary>
    public static string NameOf(MapLayerKind kind) => kind switch
    {
        MapLayerKind.Xyz => "xyz",
        MapLayerKind.Wms => "wms",
        MapLayerKind.Wmts => "wmts",
        MapLayerKind.Vector => "vector",
        MapLayerKind.Cog => "cog",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>The kind a catalogue word names, or null for a word that names none.</summary>
    public static MapLayerKind? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        null or "" or "xyz" => MapLayerKind.Xyz,
        "wms" => MapLayerKind.Wms,
        "wmts" => MapLayerKind.Wmts,
        "vector" => MapLayerKind.Vector,
        "cog" => MapLayerKind.Cog,
        _ => null,
    };
}

/// <summary>
/// The stored form of a layer's service options: the catalogue entry's typed options written as
/// JSON into the layer's options column, and read back into the same types.
/// </summary>
/// <remarks>
/// Typed at both ends so nothing between the catalogue file and the map ever handles an opaque
/// bag: a stored row whose options do not read back as its kind's type — edited by hand, or left
/// from before the column was used — reads as no options at all, and a service layer with none is
/// one nothing can draw, so it is not published.
/// </remarks>
public static class MapLayerOptionsJson
{
    // Every member must be present and none may be null: a stored text that only partly matches
    // the kind's type is not that kind's options, and reading it as them would publish a layer
    // with half its request missing.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <summary>The JSON to store for an entry, or null for a kind that has no options.</summary>
    public static string? Write(MapLayerEntry entry) => entry.Kind switch
    {
        MapLayerKind.Wms when entry.Wms is not null => JsonSerializer.Serialize(entry.Wms, Options),
        MapLayerKind.Wmts when entry.Wmts is not null => JsonSerializer.Serialize(entry.Wmts, Options),
        _ => null,
    };

    /// <summary>The WMS options stored for a layer of that kind, or null.</summary>
    public static MapLayerWmsOptions? Wms(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Wms ? Read<MapLayerWmsOptions>(stored) : null;

    /// <summary>The WMTS options stored for a layer of that kind, or null.</summary>
    public static MapLayerWmtsOptions? Wmts(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Wmts ? Read<MapLayerWmtsOptions>(stored) : null;

    /// <summary>
    /// Whether a stored layer can be drawn: a kind this version draws, carrying the options its
    /// kind needs.
    /// </summary>
    public static bool IsDrawable(MapLayerKind kind, string? stored) => kind switch
    {
        MapLayerKind.Xyz => true,
        MapLayerKind.Wms => Wms(kind, stored) is not null,
        MapLayerKind.Wmts => Wmts(kind, stored) is not null,
        _ => false,
    };

    /// <summary>
    /// Whether two stored texts hold the same options, whatever their spelling.
    /// </summary>
    /// <remarks>
    /// The column is jsonb, which hands back its own spelling of what was written — keys
    /// reordered, spaces added — so the text read back never equals the text the catalogue writes
    /// although the options are the same. Compared as text, every start would count every layer
    /// with options as changed and write it again.
    /// </remarks>
    public static bool SameOptions(string? stored, string? written)
    {
        if (stored is null || written is null)
        {
            return stored == written;
        }

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(written));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static T? Read<T>(string? stored)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(stored, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
