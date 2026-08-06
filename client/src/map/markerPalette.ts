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

/** Where the feature-type symbol images are served from; the server sends the file name only. */
export function featureSymbolUrl(symbolFile: string): string {
  return `/feature_symbols/${encodeURIComponent(symbolFile)}`;
}
