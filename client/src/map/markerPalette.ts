// SPDX-License-Identifier: AGPL-3.0-or-later

// The colours the data overlays are drawn in, in one place because more than one renderer draws
// them now. The flat map strokes them onto a canvas and the 3D scene bakes them into marker
// images; the two are not required to be pixel-identical, but a viewer moving between them has to
// recognise the same cave as the same cave, and that stops being true the moment one of the two
// files quietly picks a different teal.
//
// Deliberately free of any rendering library so both sides can import it.

export const entrancePalette = {
  /** An entrance whose coordinates are the surveyed ones. */
  point: '#146262',
  /** An entrance shown snapped to the protection grid rather than where it really is. */
  approximate: '#d46b08',
  /** A server-side aggregation of several entrances, labelled with how many. */
  cluster: '#0f4c4c',
  stroke: '#ffffff',
} as const;

export const surfaceFeaturePalette = {
  /** Fallback dot for a feature type with no symbol file of its own. */
  point: '#7a5c1e',
  line: '#8c4a2f',
  fill: 'rgba(140, 74, 47, 0.15)',
  stroke: '#ffffff',
  highlight: '#1677ff',
  highlightHalo: 'rgba(22, 119, 255, 0.25)',
} as const;

export const centerlinePalette = {
  line: '#7a1f1f',
  /** Light casing stroked under the line where it is worth a second pass over the geometry. */
  casing: 'rgba(255, 255, 255, 0.7)',
} as const;

/**
 * Where two caves come closest. Deliberately unlike the survey colours it is drawn among: it is
 * not a thing that was surveyed, it is an answer to a question somebody asked, and it should not
 * be mistaken for a passage.
 */
export const closestApproachPalette = {
  line: '#0958d9',
  /** Casing under the line and halo behind its label, so both stay legible over any basemap. */
  casing: 'rgba(255, 255, 255, 0.85)',
} as const;

/**
 * Photographs held by a photo library this installation does not own — one hue per library.
 *
 * Deliberately clear of every hue already on this map: the entrance teal, the approximate-entrance
 * orange, the surface-feature browns, the centerline maroon, the closest-approach blue, and the
 * purple the in-house photo pin is drawn in (which lives inline in that layer rather than here).
 * Two pins for the same photograph is a real state — one library may hold what the other does not,
 * which is the comparison this exists for — so the two libraries have to be told apart at a glance
 * and neither may be mistaken for a photograph this installation holds itself.
 */
export const libraryPhotoPalette = {
  immich: '#c41d7f',
  photoprism: '#237804',
  stroke: '#ffffff',
} as const;

/** Where the feature-type symbol images are served from; the server sends the file name only. */
export function featureSymbolUrl(symbolFile: string): string {
  return `/feature_symbols/${encodeURIComponent(symbolFile)}`;
}
