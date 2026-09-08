// SPDX-License-Identifier: AGPL-3.0-or-later

// "This is the place on the passage that reading came from" — picked on a chart, drawn by every
// view that can draw it.
//
// The overburden curve is read on a cave's page, which has no map on it. A point on that curve is
// a distance along the passage and a thickness of rock, and neither of those tells a reader where
// in the cave they are standing. Pressing the point announces the place here, and the flat map and
// the 3D scene each mark it while they are on screen.
//
// One place at a time, and the last one pressed wins: the curve is one cave's, the question is
// "where is this dip", and two marks with nothing to say which point each belongs to would be
// worse than either alone. Clearing it is how the panel says the point was unpicked, the reader
// pressed empty chart, or the page was left.
//
// Announcing rather than reaching for a map is what keeps this safe. A detail page must not know
// which views exist, and a point pressed with no map open should reach nobody rather than leave a
// mark behind on a view that is mounted later.

/** One reading's place on the passage, and what to write beside it. */
export interface OverburdenHighlight {
  /** The cave the reading belongs to — needed to hang the mark where that cave is drawn. */
  caveId: string;
  longitude: number;
  latitude: number;
  /** The passage's own altitude there, metres, in the survey's vertical datum. */
  altitudeM: number;
  /**
   * The figure written beside the mark, already worded and already in the reader's own number
   * formatting. Composed where there is a translator rather than here: a renderer has no business
   * knowing how a thickness of rock is said in Romanian, and a second place that decided it would
   * drift from the figure the panel shows.
   */
  label: string;
}

let current: OverburdenHighlight | null = null;
const listeners = new Set<(highlight: OverburdenHighlight | null) => void>();

/** What is being marked now, for a view that has only just been shown it. */
export function getOverburdenHighlight(): OverburdenHighlight | null {
  return current;
}

/** Registers a view's redraw, and returns an unsubscribe function. */
export function onOverburdenHighlightChanged(
  listener: (highlight: OverburdenHighlight | null) => void,
): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Announces a pressed reading, or clears the one being marked. */
export function setOverburdenHighlight(highlight: OverburdenHighlight | null): void {
  current = highlight;
  for (const listener of [...listeners]) {
    listener(current);
  }
}
