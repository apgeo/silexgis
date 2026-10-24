// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

/// <summary>
/// How a cloud-optimised GeoTIFF draws one catalogue entry: one raster file — an orthophoto, a
/// scanned geological map, a LiDAR hillshade — read piece by piece with HTTP range requests, at the
/// level of detail the view needs, from any web server that answers them.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate system is named in the entry and its definition travels with it, rather than
/// each view reading it out of the file: the flat map and the 3D scene must place the picture
/// alike, a page with no account has nowhere to look a definition up, and a file in a system a
/// view does not know would otherwise be drawn in the wrong place with nothing saying so. The
/// definition is this installation's own answer for the code, resolved offline when the catalogue
/// is read, and an entry whose code has none is refused there.
/// </para>
/// <para>
/// Samples are scaled to the screen across one value range, the same for every band drawn:
/// <paramref name="Min"/> to <paramref name="Max"/> when the entry gives them, otherwise the
/// file's own statistics, otherwise its data type's range — which is what a 16-bit elevation or
/// hillshade file needs the entry to say, and an 8-bit photograph does not.
/// </para>
/// </remarks>
/// <param name="Bands">
/// The file's bands to draw, numbered from 1, in drawing order: one band is drawn as grey, two as
/// grey and transparency, three as red, green and blue, four as those and transparency. Null to
/// draw the file's bands as it stores them.
/// </param>
/// <param name="Nodata">
/// A sample value drawn transparent, in place of whatever the file declares; null for the file's own.
/// </param>
/// <param name="Min">The sample value drawn as the low end of the range; null, with <paramref name="Max"/>, for the default.</param>
/// <param name="Max">The sample value drawn as the high end of the range; null, with <paramref name="Min"/>, for the default.</param>
/// <param name="Projection">
/// The coordinate system the file is in, written <c>EPSG:</c> and its code — what both views place
/// it by, whatever the file's own header says.
/// </param>
/// <param name="ProjectionDefinition">
/// The PROJ.4 definition of <paramref name="Projection"/>, for a view to register before it reads
/// the file; null for <c>EPSG:3857</c> and <c>EPSG:4326</c>, which every view knows already.
/// </param>
public sealed record MapLayerCogOptions(
    IReadOnlyList<int>? Bands,
    double? Nodata,
    double? Min,
    double? Max,
    string Projection,
    string? ProjectionDefinition);

/// <summary>The form a vector entry's shapes arrive in.</summary>
public enum MapLayerVectorFormat
{
    /// <summary>
    /// Mapbox vector tiles: a z/x/y template, or the address of the service's TileJSON document
    /// when the tiles' own addresses change with each build.
    /// </summary>
    Mvt = 0,

    /// <summary>One PMTiles archive, its tiles read out of it with HTTP range requests.</summary>
    Pmtiles = 1,

    /// <summary>One GeoJSON file holding every shape.</summary>
    Geojson = 2,
}

/// <summary>
/// How a vector entry draws: shapes with attributes rather than finished pictures, drawn by the
/// browser in a look of the installation's choosing.
/// </summary>
/// <remarks>
/// The entry's url is the data and the style is only the look, kept apart on purpose: one tile set
/// is drawn in several styles (light, dark, muted under cave markers), and one style serves every
/// build of a self-hosted tile set, so neither may stand in for the other.
/// </remarks>
/// <param name="Format">The form the entry's url delivers its shapes in.</param>
/// <param name="Style">
/// The address of a MapLibre style document whose layers draw the shapes — colours, widths, labels,
/// and the fonts and icons it names; null to draw them in the map's plain default look.
/// </param>
/// <param name="StyleSource">
/// Which source of <paramref name="Style"/> the entry's data stands for: the style's layers drawing
/// that source are the ones used. Null for the style's first vector source.
/// </param>
/// <param name="LabelField">
/// The shapes' property holding a name in a given language, with <c>{lang}</c> where the reader's
/// language code goes — <c>name:{lang}</c> for the OpenMapTiles and Protomaps schemas. The style's
/// name labels then read that property, falling back to the plain <c>name</c>. Null to keep the
/// style's labels as it writes them.
/// </param>
/// <param name="Scene3dStandIn">
/// The name of the picture source the 3D scene drapes in this entry's place, so that both views
/// list the same backgrounds while the scene draws no vector style; null for none. Published only
/// while that source is itself published.
/// </param>
public sealed record MapLayerVectorOptions(
    MapLayerVectorFormat Format,
    string? Style,
    string? StyleSource,
    string? LabelField,
    string? Scene3dStandIn);

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

    /// <summary>Where the reader's language code goes in a vector entry's label field.</summary>
    public const string LanguagePlaceholder = "{lang}";

    /// <summary>
    /// The last zoom a vector entry's tiles are taken to hold when it does not say: where
    /// OpenMapTiles-schema tiles stop. Asked past its last level a tile service answers nothing
    /// for the whole view, while a view drawing vector tiles past it enlarges the last level and
    /// stays sharp — so a value too low costs nothing and one too high blanks the map.
    /// </summary>
    public const int VectorDefaultMaxZoom = 14;

    /// <summary>Whether a view needs no definition to draw in this coordinate system.</summary>
    public static bool IsBuiltInProjection(string code) => code is WebMercator or Geographic;

    /// <summary>
    /// Whether a source of this kind can be drawn by the 3D scene in a vector entry's place: one it
    /// drapes as pictures, on the same side of the list — a basemap for a basemap, an overlay for
    /// an overlay.
    /// </summary>
    public static bool CanStandIn3d(MapLayerKind standIn, bool standInIsBase, bool entryIsBase) =>
        (standIn is MapLayerKind.Xyz or MapLayerKind.Wms or MapLayerKind.Wmts) && standInIsBase == entryIsBase;

    /// <summary>The catalogue's word for a vector format, as an entry's <c>format</c> attribute writes it.</summary>
    public static string NameOf(MapLayerVectorFormat format) => format switch
    {
        MapLayerVectorFormat.Mvt => "mvt",
        MapLayerVectorFormat.Pmtiles => "pmtiles",
        MapLayerVectorFormat.Geojson => "geojson",
        _ => format.ToString().ToLowerInvariant(),
    };

    /// <summary>The vector format a catalogue word names, or null for a word that names none.</summary>
    public static MapLayerVectorFormat? ParseVectorFormat(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "mvt" => MapLayerVectorFormat.Mvt,
        "pmtiles" => MapLayerVectorFormat.Pmtiles,
        "geojson" => MapLayerVectorFormat.Geojson,
        _ => null,
    };

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
    // A vector format is stored by its word, never by its number, and a number is not read as
    // one: the column outlives any renumbering of the enum.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The JSON to store for an entry, or null for a kind that has no options.</summary>
    public static string? Write(MapLayerEntry entry) => entry.Kind switch
    {
        MapLayerKind.Wms when entry.Wms is not null => JsonSerializer.Serialize(entry.Wms, Options),
        MapLayerKind.Wmts when entry.Wmts is not null => JsonSerializer.Serialize(entry.Wmts, Options),
        MapLayerKind.Cog when entry.Cog is not null => JsonSerializer.Serialize(entry.Cog, Options),
        MapLayerKind.Vector when entry.Vector is not null => JsonSerializer.Serialize(entry.Vector, Options),
        _ => null,
    };

    /// <summary>The WMS options stored for a layer of that kind, or null.</summary>
    public static MapLayerWmsOptions? Wms(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Wms ? Read<MapLayerWmsOptions>(stored) : null;

    /// <summary>The WMTS options stored for a layer of that kind, or null.</summary>
    public static MapLayerWmtsOptions? Wmts(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Wmts ? Read<MapLayerWmtsOptions>(stored) : null;

    /// <summary>
    /// The cloud-optimised GeoTIFF options stored for a layer of that kind, or null — also for a
    /// stored text of the right shape whose values the catalogue would never have written, since a
    /// file drawn in a coordinate system nobody defined is a picture in the wrong place.
    /// </summary>
    public static MapLayerCogOptions? Cog(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Cog && Read<MapLayerCogOptions>(stored) is { } options && IsSound(options) ? options : null;

    /// <summary>The vector options stored for a layer of that kind, or null.</summary>
    public static MapLayerVectorOptions? Vector(MapLayerKind kind, string? stored) =>
        kind == MapLayerKind.Vector && Read<MapLayerVectorOptions>(stored) is { } options && Enum.IsDefined(options.Format)
            ? options
            : null;

    /// <summary>
    /// Whether a stored layer can be drawn: a kind this version draws, carrying the options its
    /// kind needs.
    /// </summary>
    public static bool IsDrawable(MapLayerKind kind, string? stored) => kind switch
    {
        MapLayerKind.Xyz => true,
        MapLayerKind.Wms => Wms(kind, stored) is not null,
        MapLayerKind.Wmts => Wmts(kind, stored) is not null,
        MapLayerKind.Cog => Cog(kind, stored) is not null,
        MapLayerKind.Vector => Vector(kind, stored) is not null,
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

    /// <summary>
    /// Whether stored GeoTIFF options are ones the catalogue could have written: a projection
    /// named as a code and, unless it is one every view knows, carrying its definition; one to
    /// four bands counted from 1; and finite numbers, the range's ends together and in order.
    /// </summary>
    private static bool IsSound(MapLayerCogOptions options) =>
        MapLayerCatalog.EpsgCode(options.Projection) is { } code
        && options.Projection == MapLayerCatalog.ProjectionName(code)
        && (MapLayerKinds.IsBuiltInProjection(options.Projection) || !string.IsNullOrWhiteSpace(options.ProjectionDefinition))
        && options.Bands is null or { Count: >= 1 and <= MapLayerCatalog.MaxCogBands }
        && (options.Bands ?? []).All(band => band >= 1)
        && Finite(options.Nodata) && Finite(options.Min) && Finite(options.Max)
        && (options.Min is null) == (options.Max is null)
        && !(options.Min >= options.Max);

    private static bool Finite(double? value) => value is null || double.IsFinite(value.Value);

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
