// SPDX-License-Identifier: AGPL-3.0-or-later

// "This is where two caves come closest" — measured on one page, drawn by every view that can
// draw it.
//
// The measurement is asked for from a cave's own page, which has no map on it. What it produces
// is a line between two points on the ground with depths of its own, and the question a caver is
// really asking — where would one dig to connect these two systems — is answered by seeing that
// line, not by reading its length. So the answer is announced here, and the flat map and the 3D
// scene each draw it while they are on screen.
//
// One line at a time, and the last one measured wins: the panel measures one pair at a time, and
// two shortest lines on a map with nothing to say which pair each belongs to would be worse than
// either alone. Clearing it is how the panel says the pair was unpicked or the page left.
//
// Announcing rather than reaching for a map is what keeps this safe. A detail page must not know
// which views exist, and a measurement made with no map open should reach nobody rather than
// leave a line behind on a view that is mounted later.

/** One end of the shortest line, in the stored system, at the altitude the line work carries. */
export interface ClosestApproachEnd {
  longitude: number;
  latitude: number;
  altitudeM: number;
}

/** The shortest line between two caves, and what to write beside it. */
export interface ClosestApproachLine {
  /** The cave the first end belongs to — needed to hang the line where that cave is drawn. */
  caveAId: string;
  /** The cave the second end belongs to. */
  caveBId: string;
  from: ClosestApproachEnd;
  to: ClosestApproachEnd;
  /**
   * The distance written beside the line, already worded and already in the reader's own number
   * formatting. Composed where there is a translator rather than here: a renderer has no business
   * knowing how a length is said in Romanian, and a second place that decided it would drift from
   * the figure the panel shows.
   */
  label: string;
}

let current: ClosestApproachLine | null = null;
const listeners = new Set<(line: ClosestApproachLine | null) => void>();

/** What is being drawn now, for a view that has only just been shown it. */
export function getClosestApproachLine(): ClosestApproachLine | null {
  return current;
}

/** Registers a view's redraw, and returns an unsubscribe function. */
export function onClosestApproachLineChanged(
  listener: (line: ClosestApproachLine | null) => void,
): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Announces a measured pair, or clears the one being shown. */
export function setClosestApproachLine(line: ClosestApproachLine | null): void {
  current = line;
  for (const listener of [...listeners]) {
    listener(current);
  }
}
