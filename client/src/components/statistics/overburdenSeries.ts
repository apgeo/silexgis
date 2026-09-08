// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveOverburdenSample } from '../../api/hooks.ts';

/**
 * Turning a cave's overburden readings into something a chart can draw, without inventing any of
 * the numbers that are missing.
 *
 * <p>
 * <b>A reading with no ground height is a break in the curve, not a nought.</b> Where no prepared
 * elevation model covers the passage — or where the one that does has a hole in it — nobody knows
 * how much rock is overhead. Substituting nought draws the passage arriving at the surface, which
 * is plausible, alarming and entirely invented; joining the curve straight across the gap is the
 * same lie one step later, because the line then asserts a thickness at every distance it crosses.
 * So the arrays below carry `null` at those places and the chart leaves a hole.
 * </p>
 * <p>
 * Nothing here recomputes a figure the server already sent. The thinnest, thickest and mean
 * thicknesses, the covered count and the passage length are carried across as they arrived: a
 * number derived a second way eventually disagrees with the same number printed beside it.
 * </p>
 */

/** The three parallel arrays a curve-with-a-band chart is drawn from. */
export interface OverburdenSeries {
  /** Distance along the passage, metres, one entry per reading. */
  x: number[];
  /** Rock overhead there, metres; null where the ground could not be read. */
  overburdenM: (number | null)[];
  /**
   * The floor of the shaded rock: nought where there is a reading, so the shading runs from the
   * passage up to the thickness overhead, and null where there is not, so the shading stops with
   * the curve instead of sweeping along the axis through a gap.
   */
  base: (number | null)[];
}

/**
 * The readings in the shape the chart draws.
 *
 * The order is the order they arrived in, which the server states is increasing distance along.
 * Re-sorting here would hide a server that stopped doing that rather than show it.
 */
export function overburdenSeries(samples: readonly CaveOverburdenSample[]): OverburdenSeries {
  return {
    x: samples.map((s) => s.distanceAlongM),
    overburdenM: samples.map((s) => (s.outcome === 'sampled' ? s.overburdenM : null)),
    base: samples.map((s) => (s.outcome === 'sampled' && s.overburdenM !== null ? 0 : null)),
  };
}

/**
 * Why some of the passage has no thickness over it — which is a different problem in each case and
 * needs a different thing done about it.
 *
 * `outsideCoverage` means no elevation model reaches that part of the cave, and somebody has to
 * build or import one; `noData` means one reaches it and records nothing there, which is a hole in
 * the data somebody chose. `mixed` is both, and `none` is a profile with no gaps at all.
 */
export type OverburdenGapReason = 'none' | 'outsideCoverage' | 'noData' | 'mixed';

/** Which of the two ways a reading can fail actually happened along this profile. */
export function overburdenGapReason(samples: readonly CaveOverburdenSample[]): OverburdenGapReason {
  const outside = samples.some((s) => s.outcome === 'outsideCoverage');
  const hole = samples.some((s) => s.outcome === 'noData');

  if (outside && hole) return 'mixed';
  if (outside) return 'outsideCoverage';
  if (hole) return 'noData';
  return 'none';
}
