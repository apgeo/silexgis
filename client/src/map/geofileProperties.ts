// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where the server puts what it resolved about a row of an imported file, and how to read it back.
 *
 * <p>A module of its own, holding no OpenLayers and no DOM, because four otherwise-unrelated
 * things need it: the layer that draws these points, the tooltip that names one under the cursor,
 * the balloon that opens when one is clicked, and the 3D scene, which draws the same files as
 * lines and must not pull a flat-map library in to learn what colour they are. Any two of those
 * importing it from a third would make a cycle out of what is really one shared fact.</p>
 *
 * <p>The names are prefixed because the row's own columns are spread into the same object,
 * untouched, and a GPS export with a column literally called <code>label</code> is not a strange
 * file. The prefix is what keeps "what the file said" and "what we worked out" apart.</p>
 *
 * <p>Working it out happens on the SERVER, not here. Which of a source's columns is its name is a
 * domain rule — a GPX writes <code>name</code>, a shapefile abbreviates to ten characters, a
 * spreadsheet is whatever somebody typed — and it already has one home, shared with the import
 * review. A second copy in the browser would drift, and the way it would show is the map labelling
 * a waypoint by its comment while the review screen labels it by its name.</p>
 */
export const GEOFILE_LABEL_PROPERTY = 'silexgis:label';

/** @see GEOFILE_LABEL_PROPERTY */
export const GEOFILE_DESCRIPTION_PROPERTY = 'silexgis:description';

/** @see GEOFILE_LABEL_PROPERTY */
export const GEOFILE_ELEVATION_PROPERTY = 'silexgis:elevationM';

/** The name to show for an imported row, or nothing when the file gave it none. */
export function geofileLabel(props: Record<string, unknown>): string | undefined {
  const label = props[GEOFILE_LABEL_PROPERTY];
  return typeof label === 'string' && label.trim().length > 0 ? label.trim() : undefined;
}

/**
 * The prefix of the layer id an imported file is drawn under, in both views, followed by the
 * file's own id. It names the layer on the flat map and the batch in the scene; it is NOT the key
 * the workspace keeps a file's fade under — that is the bare file id, and anything reading or
 * writing a fade has to use the bare id or the two views stop sharing it.
 */
export const GEOFILE_LAYER_PREFIX = 'geofile:';

/** The layer id of one imported file, as both views name what they draw it in. */
export function geofileLayerId(geofileId: string): string {
  return GEOFILE_LAYER_PREFIX + geofileId;
}

/**
 * What an imported file's own style may override: the colours of its lines, areas and points.
 * The style is a free-form JSON column on the file; these are the keys the views read from it.
 */
export interface GeofileStyleOverrides {
  stroke?: string;
  fill?: string;
  point?: string;
}

/**
 * The colour a file's lines are drawn in when its style says nothing. Both views start from this
 * one value, so a track is the same blue on the globe as on the flat map.
 */
export const GEOFILE_DEFAULT_STROKE = '#2f54eb';

/** A file's style overrides, or nothing when the file carries no usable style. */
export function geofileStyleOverrides(geofile: { style: unknown }): GeofileStyleOverrides | null {
  const style = geofile.style;
  return typeof style === 'object' && style !== null ? (style as GeofileStyleOverrides) : null;
}

/**
 * The colour a file's lines are drawn in: its own stroke colour when the style names one, the
 * shared default otherwise. One rule for both views, so a file is recognisably the same file in
 * each.
 */
export function geofileStrokeColour(geofile: { style: unknown }): string {
  const stroke = geofileStyleOverrides(geofile)?.stroke;
  return typeof stroke === 'string' && stroke.length > 0 ? stroke : GEOFILE_DEFAULT_STROKE;
}
