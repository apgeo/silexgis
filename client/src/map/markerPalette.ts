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
 * The one reading on an overburden curve a reader pressed, marked where it was taken.
 *
 * A bright red is chosen against everything else drawn near it: the survey line under it is a dark
 * desaturated maroon and the answer-to-a-question blue is already spoken for, so the mark reads as
 * neither a passage nor a measurement between caves. It is transient — one point at a time, gone
 * when the reader presses elsewhere — which is why it is allowed to be the loudest thing on the
 * map for as long as it is there.
 */
export const overburdenHighlightPalette = {
  mark: '#f5222d',
  /** Casing under the mark and halo behind its label, so both stay legible over any basemap. */
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

/**
 * Where the people of a watch are drawn on a survey model.
 *
 * <b>Both are opaque, and that is a correctness requirement rather than a preference.</b> The
 * survey viewer hands a marker's colour straight to a point material, whose parser accepts an
 * `rgba()` string, discards the alpha with a console warning and keeps the three channels — so a
 * 25%-black "muted" colour arrives as solid black and a 25%-white one as solid white, the second of
 * which is the most prominent thing on a dark scene. On this surface the distinction between who is
 * still underground and who has come out is the one that matters, so it may not be expressed in an
 * alpha that never survives.
 *
 * <b>Chosen against the viewer's background, not the application's.</b> The scene behind these is
 * the viewer's own dark grey whatever theme the page is in, and the colours already on it are the
 * viewer's: red default stations, yellow junctions, white entrances, cyan linked stations. A caver
 * is neither a station nor a passage, and carries their name beside them.
 */
export const trackedCaverPalette = {
  /** Still underground: the application's teal, lifted to read on the viewer's dark scene. */
  underground: '#3ab5b5',
  /** Reported out — their marker is where they were, not where they are. Quiet, but not invisible. */
  out: '#9a9a9a',
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

/**
 * Station points pinned onto a scanned cave map.
 *
 * Drawn over paper — a scan is mostly white or sepia with dark ink — so the pin is the
 * application's own dark teal with a white stroke: a pinned station is a surveyed fact,
 * not an annotation, and it should read as kin to the entrance dot on the geo map. The
 * label halo is the near-opaque white the other over-ink labels already use, because a
 * station name printed over hatching is unreadable without one.
 */
export const rasterMapPalette = {
  station: '#146262',
  stroke: '#ffffff',
  labelHalo: 'rgba(255, 255, 255, 0.85)',
} as const;

/** Where the feature-type symbol images are served from; the server sends the file name only. */
export function featureSymbolUrl(symbolFile: string): string {
  return `/feature_symbols/${encodeURIComponent(symbolFile)}`;
}
