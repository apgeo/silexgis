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
 * The three things a trip can be drawn as, and they are three because they mean three different
 * things. The shape a trip drew of itself is where the party worked; its meeting point is where
 * the party gathered, which is routinely a car park in a village; and a dot inherited from a cave
 * the trip names is neither — it is the cave's own surveyed entrance, standing in for a trip that
 * recorded no geometry at all.
 *
 * Drawing any two of them alike would put a car park where the reader read a cave, so each is
 * given both its own colour and its own outline: filled disc, hollow ring, square. Colour alone
 * is not enough — a reader who cannot separate teal from magenta still has to be able to tell a
 * worked cave from a rendezvous.
 */
export const tripPalette = {
  /** Where the trip worked: the shape it drew of itself. */
  sketch: '#146262',
  /** Wash under a sketch that is a line or an area rather than a single point. */
  sketchFill: 'rgba(20, 98, 98, 0.2)',
  /** Where its party met: a different place, drawn hollow so it cannot be mistaken for the first. */
  meeting: '#bc5090',
  /** The hollow ring's centre — near-opaque rather than clear, so the basemap does not read as fill. */
  meetingCentre: 'rgba(255, 255, 255, 0.9)',
  /**
   * A position the trip never stated, inherited from a cave it names. Deliberately unlike both of
   * the trip's own statements: it is an answer assembled from somewhere else, and a reader must
   * not take it for a coordinate the trip recorded.
   */
  derived: '#5a3fa0',
  stroke: '#ffffff',
} as const;

/** Where the feature-type symbol images are served from; the server sends the file name only. */
export function featureSymbolUrl(symbolFile: string): string {
  return `/feature_symbols/${encodeURIComponent(symbolFile)}`;
}
