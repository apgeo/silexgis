// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where the server puts what it resolved about a row of an imported file, and how to read it back.
 *
 * <p>A module of its own, holding no OpenLayers and no DOM, because three otherwise-unrelated
 * things need it: the layer that draws these points, the tooltip that names one under the cursor,
 * and the balloon that opens when one is clicked. Any two of those importing it from the third
 * would make a cycle out of what is really one shared fact.</p>
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
