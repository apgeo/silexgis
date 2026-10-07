// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How the plate behind a marker's label is coloured.
 *
 * `derived` is the viewer's own: it works the plate out from the scene's background, which over a
 * black scene is a light grey plate at 60 % opacity with black writing on it. `dark` is a near-black
 * plate with white writing.
 */
export type MarkerLabelPlate = 'derived' | 'dark';

/**
 * The dark plate, in the names the viewer's theme uses.
 *
 * On the derived plate the first line of each label, drawn in the marker's own colour, is orange or
 * pink or teal on mid grey — hard to read in a small picture — and with the plate turned off the
 * writing stays black, so the names under a team's heading vanish into a black scene. A dark plate
 * with white writing reads either way: the coloured first lines stand out on it, and without it
 * white writing stands out on black.
 */
const DARK_MARKER_LABEL_THEME = {
  liveMarkers: {
    labelBackground: '#141414',
    labelText: '#ffffff',
    labelBackgroundOpacity: 0.8,
  },
} as const;

/**
 * The theme a viewer is built with for a plate, or undefined for the viewer's own — so a viewer
 * that asks for nothing is built with exactly the options it always was.
 *
 * <b>A theme is read when a viewer is made and never again</b>, so this is chosen before the viewer
 * is built; nothing can switch the plate of one that is already showing a model.
 */
export function markerLabelTheme(plate: MarkerLabelPlate): Record<string, unknown> | undefined {
  return plate === 'dark' ? DARK_MARKER_LABEL_THEME : undefined;
}
