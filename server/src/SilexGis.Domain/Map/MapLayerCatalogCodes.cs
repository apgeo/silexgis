// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Map;

/// <summary>
/// The stable codes of what reading a catalogue file can report: one per kind of problem, so a
/// line in the start-up log can be searched for, counted and tested by what it is rather than by
/// how its sentence happens to be worded today.
/// </summary>
/// <remarks>
/// The three reasons a source loses the mark that lets a document copy it are not repeated here:
/// they are <see cref="MapLayerDocumentRules"/>' own codes, which the administrator's switch
/// answers with too, so one obstacle has one name wherever it is met.
/// </remarks>
public static class MapLayerCatalogCodes
{
    /// <summary>The file is not XML at all; nothing in it was read.</summary>
    public const string NotXml = "map_layer.catalog_not_xml";

    /// <summary>An entry has no name, so nothing can refer to it; skipped.</summary>
    public const string NoName = "map_layer.no_name";

    /// <summary>An entry has no url; skipped.</summary>
    public const string NoUrl = "map_layer.no_url";

    /// <summary>An entry names an access key and its url has nowhere to put it; skipped.</summary>
    public const string KeyWithoutPlaceholder = "map_layer.key_without_placeholder";

    /// <summary>An entry's url wants an access key and the entry names none; skipped.</summary>
    public const string PlaceholderWithoutKey = "map_layer.placeholder_without_key";

    /// <summary>An entry's <c>kind</c> names no kind of map layer; skipped.</summary>
    public const string UnknownKind = "map_layer.unknown_kind";

    /// <summary>An address holds a placeholder that neither map fills; skipped.</summary>
    public const string StrayPlaceholder = "map_layer.stray_placeholder";

    /// <summary>An entry's zoom range is the wrong way round; skipped.</summary>
    public const string ZoomRangeInverted = "map_layer.zoom_range_inverted";

    /// <summary>Two entries share a name; the later one is kept.</summary>
    public const string DeclaredTwice = "map_layer.declared_twice";

    /// <summary>
    /// A source stored from an earlier start is switched off, because every entry of its name is
    /// now refused.
    /// </summary>
    public const string SwitchedOffAsRefused = "map_layer.switched_off_as_refused";

    /// <summary>A WMS entry names no layers; skipped.</summary>
    public const string WmsNoLayers = "map_layer.wms_no_layers";

    /// <summary>A WMS entry asks for a version the map does not speak; skipped.</summary>
    public const string WmsVersionUnsupported = "map_layer.wms_version_unsupported";

    /// <summary>A WMTS entry lacks its layer or its matrix set; skipped.</summary>
    public const string WmtsIncomplete = "map_layer.wmts_incomplete";

    /// <summary>A WMTS REST template names the matrix but not both its row and column; skipped.</summary>
    public const string WmtsTemplateIncomplete = "map_layer.wmts_template_incomplete";

    /// <summary>A service names a coordinate system its grid cannot be drawn on; skipped.</summary>
    public const string CrsUnsupported = "map_layer.crs_unsupported";

    /// <summary>A cloud-optimised GeoTIFF entry does not say which coordinate system it is in; skipped.</summary>
    public const string CogProjectionMissing = "map_layer.cog_projection_missing";

    /// <summary>
    /// A cloud-optimised GeoTIFF entry names a coordinate system this installation has no
    /// definition for, or names it in a form that is not <c>EPSG:</c> and a code; skipped.
    /// </summary>
    public const string CogProjectionUnknown = "map_layer.cog_projection_unknown";

    /// <summary>A cloud-optimised GeoTIFF entry's band list is not one to four band numbers; skipped.</summary>
    public const string CogBandsInvalid = "map_layer.cog_bands_invalid";

    /// <summary>A cloud-optimised GeoTIFF entry's nodata, min or max is not a finite number; skipped.</summary>
    public const string CogNumberInvalid = "map_layer.cog_number_invalid";

    /// <summary>
    /// A cloud-optimised GeoTIFF entry's value range has one end without the other, or its low
    /// end is not below its high end; skipped.
    /// </summary>
    public const string CogRangeInvalid = "map_layer.cog_range_invalid";

    /// <summary>A vector entry names no format, or one that is none of the three; skipped.</summary>
    public const string VectorFormatInvalid = "map_layer.vector_format_invalid";

    /// <summary>A vector tile template names only some of <c>{z}</c>, <c>{x}</c> and <c>{y}</c>; skipped.</summary>
    public const string VectorTemplateIncomplete = "map_layer.vector_template_incomplete";

    /// <summary>A vector entry names a style source or a label field and no style; skipped.</summary>
    public const string VectorStyleRequired = "map_layer.vector_style_required";

    /// <summary>A vector entry's label field is not one property name; skipped.</summary>
    public const string VectorLabelFieldInvalid = "map_layer.vector_label_field_invalid";

    /// <summary>
    /// A vector entry's 3D stand-in names no entry the 3D scene could draw in its place; the
    /// entry is kept, without a stand-in.
    /// </summary>
    public const string StandInUnusable = "map_layer.stand_in_unusable";
}
