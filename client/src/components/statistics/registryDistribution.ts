// SPDX-License-Identifier: AGPL-3.0-or-later
import type { RegistryDistributionBin } from '../../api/hooks.ts';

/**
 * Turning the registry's own intervals into something a bar chart can draw, without deciding
 * anything about them on the way.
 *
 * <p>
 * The arithmetic behind these intervals happened once, on the server, over the caves this reader
 * may read. Nothing here re-bins, re-counts or re-fits: a second definition of an interval is how
 * a screen ends up disagreeing with the file taken from it. Plain objects in, plain objects out,
 * so the one decision that is made here — how a joined interval is named — can be asserted
 * without a chart.
 * </p>
 */

/** One interval as a chart wants it: what it covers, how many caves, and what it is called. */
export interface DistributionBar {
  lowerBound: number;
  upperBound: number;
  count: number;
  /** True when the registry joined several requested intervals into this one. */
  merged: boolean;
  /** The axis label. Wider for a joined interval, because a joined interval is wider. */
  label: string;
}

/** The words a label is built from, resolved by the caller so nothing here translates. */
export interface DistributionBarLabels {
  /** A bound written the way the reader's locale writes numbers. */
  bound: (value: number) => string;
  /**
   * The name of an interval that is several joined together. It reads as joined because it is:
   * the registry joins intervals holding too few caves to publish rather than dropping them, and
   * a dropped interval would itself announce that something rare is in it. Drawn as an ordinary
   * bar, a joined one misleads the other way — a reader takes a wide bar for a real feature of
   * the distribution instead of for the registry declining to be more precise.
   */
  joined: (range: { from: string; to: string }) => string;
}

/** The registry's intervals, ready to draw, in the order the registry returned them. */
export function distributionBars(
  bins: readonly RegistryDistributionBin[],
  labels: DistributionBarLabels,
): DistributionBar[] {
  return bins.map((bin) => {
    const from = labels.bound(bin.lowerBound);
    const to = labels.bound(bin.upperBound);
    return {
      lowerBound: bin.lowerBound,
      upperBound: bin.upperBound,
      count: bin.count,
      merged: bin.merged,
      label: bin.merged ? labels.joined({ from, to }) : from,
    };
  });
}

/** How many of the drawn intervals are several joined together. Zero when none are. */
export function mergedBinCount(bins: readonly RegistryDistributionBin[]): number {
  return bins.reduce((total, bin) => total + (bin.merged ? 1 : 0), 0);
}

/**
 * Whether the answer has a distribution in it at all.
 *
 * No intervals is a real answer and not a missing one: it is what the registry says when nothing
 * carries the measurement, or when everything carrying it records the same value, so there is no
 * range to divide. The counts beside it are still true and still worth showing.
 */
export function hasDistribution(bins: readonly RegistryDistributionBin[]): boolean {
  return bins.length > 0;
}

/** Which of the drawn intervals the fitted tail covers, and where it starts. */
export interface ParetoTailSpan {
  /** The lower bound the tail was fitted from, as the registry gave it. */
  from: number;
  /** How many of the drawn intervals lie in the tail, counted from the right. */
  intervals: number;
  /** How many intervals are drawn in all, so the share is readable without counting bars. */
  total: number;
}

/**
 * Where a fitted power-law tail begins among the intervals on screen.
 *
 * <p>
 * The exponent and its lower bound are fitted on the server, over the values themselves rather
 * than over these intervals. Nothing is refitted here and no curve is produced: turning an
 * exponent into a shape drawn over the bars would mean deciding how many caves it implies per
 * interval, which is a second definition of the distribution — and the intervals are not even of
 * equal width once any of them are joined. What is worked out is only which of the drawn
 * intervals the tail covers, so the figures tabulated beside the chart can be tied to the bars a
 * reader is looking at instead of standing on their own.
 * </p>
 * <p>
 * Null when nothing was fitted, when there are no intervals, or when the bound falls outside
 * them: a tail said to start where nothing is drawn would send the reader looking for a bar.
 * </p>
 */
export function paretoTailSpan(
  bins: readonly RegistryDistributionBin[],
  lowerBound: number | null,
): ParetoTailSpan | null {
  if (lowerBound === null || bins.length === 0) {
    return null;
  }
  // The first interval the bound falls in, or that lies wholly above it — a bound below every
  // interval means the tail is the whole distribution, which is a true and worth-saying answer.
  const first = bins.findIndex((bin) => bin.upperBound > lowerBound);
  if (first < 0) {
    return null;
  }
  return { from: lowerBound, intervals: bins.length - first, total: bins.length };
}
