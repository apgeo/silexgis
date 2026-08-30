// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The arithmetic behind the distribution charts, deliberately kept out of the chart components.
 *
 * <p>
 * Two reasons, and the second is the one that matters. A chart component tested through a rendered
 * chart can only be asserted on loosely — element counts and labels — so any real numeric claim
 * would go untested. And keeping the numbers here is what makes the drawing library replaceable:
 * everything below is plain arrays in and plain arrays out, so swapping how a histogram is drawn
 * cannot change what it says.
 * </p>
 */

/** One bar of a histogram: the half-open interval [from, to) and how many values fell in it. */
export interface Bin {
  from: number;
  to: number;
  count: number;
}

/**
 * Equal-width bins over the values' own range.
 *
 * An empty input gives no bins rather than one empty bin, and a single distinct value gives one
 * bin — a range of zero width cannot be divided, and pretending otherwise produces bins whose
 * bounds are all the same number.
 */
export function histogram(values: number[], binCount: number): Bin[] {
  const finite = values.filter((v) => Number.isFinite(v));
  if (finite.length === 0 || binCount < 1) return [];

  const min = Math.min(...finite);
  const max = Math.max(...finite);
  if (min === max) return [{ from: min, to: min, count: finite.length }];

  const width = (max - min) / binCount;
  const bins: Bin[] = Array.from({ length: binCount }, (_, i) => ({
    from: min + i * width,
    to: min + (i + 1) * width,
    count: 0,
  }));

  for (const v of finite) {
    // The last bin is closed at the top, so the maximum lands in it rather than in a bin past the
    // end. Without this the largest value in every dataset is silently dropped.
    const index = Math.min(binCount - 1, Math.floor((v - min) / width));
    bins[index].count += 1;
  }
  return bins;
}

/** A point on a complementary cumulative distribution: how many values are >= x. */
export interface CcdfPoint {
  x: number;
  count: number;
}

/**
 * The complementary cumulative distribution, as the rank-size plot the karst literature reads:
 * values descending, each paired with its rank.
 *
 * <p>
 * Non-positive values are dropped, and that is a real decision rather than defensive tidying: this
 * is drawn on logarithmic axes, where zero has no position at all. Dropping them silently would
 * misstate the distribution, so the count of what was dropped is returned alongside for the caller
 * to show.
 * </p>
 */
export function ccdf(values: number[]): { points: CcdfPoint[]; droppedNonPositive: number } {
  const usable = values.filter((v) => Number.isFinite(v) && v > 0);
  const dropped = values.filter((v) => Number.isFinite(v)).length - usable.length;
  const sorted = [...usable].sort((a, b) => b - a);
  return {
    points: sorted.map((x, i) => ({ x, count: i + 1 })),
    droppedNonPositive: dropped,
  };
}

/**
 * The maximum-likelihood exponent of a power law fitted to the tail at or above `xMin`, by the
 * closed form for continuous data.
 *
 * <p>
 * Fitting a straight line to a log-log histogram is the usual approach and it is substantially
 * biased; the estimator below is the standard correction. `null` when fewer than two values reach
 * the tail, because an exponent from one point is not an estimate.
 * </p>
 */
export function powerLawExponent(values: number[], xMin: number): number | null {
  const tail = values.filter((v) => Number.isFinite(v) && v >= xMin && v > 0);
  if (tail.length < 2 || xMin <= 0) return null;

  const sum = tail.reduce((acc, v) => acc + Math.log(v / xMin), 0);
  if (sum <= 0) return null;
  return 1 + tail.length / sum;
}

/** The five numbers a box plot draws, in the order the drawing library expects them. */
export interface FiveNumber {
  min: number;
  q1: number;
  median: number;
  q3: number;
  max: number;
  count: number;
}

/**
 * The five-number summary, with quartiles by linear interpolation between order statistics — the
 * same convention the database's own percentile function uses, so a figure computed in the browser
 * and the same figure computed server-side agree.
 */
export function fiveNumberSummary(values: number[]): FiveNumber | null {
  const sorted = values.filter((v) => Number.isFinite(v)).sort((a, b) => a - b);
  if (sorted.length === 0) return null;

  const at = (p: number) => {
    const position = p * (sorted.length - 1);
    const lower = Math.floor(position);
    const upper = Math.ceil(position);
    if (lower === upper) return sorted[lower];
    return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
  };

  return {
    min: sorted[0],
    q1: at(0.25),
    median: at(0.5),
    q3: at(0.75),
    max: sorted[sorted.length - 1],
    count: sorted.length,
  };
}

/**
 * Ordinary least squares on log-transformed pairs, which is a straight line on a log-log plot.
 *
 * Returns the slope and intercept in log space plus the coefficient of determination, or `null`
 * when fewer than two pairs survive the transform. Pairs with a non-positive ordinate are dropped
 * for the same reason as in {@link ccdf}: they have no position on a logarithmic axis.
 */
export function logLogFit(pairs: Array<[number, number]>): { slope: number; intercept: number; r2: number } | null {
  const usable = pairs.filter(([x, y]) => Number.isFinite(x) && Number.isFinite(y) && x > 0 && y > 0);
  if (usable.length < 2) return null;

  const xs = usable.map(([x]) => Math.log(x));
  const ys = usable.map(([, y]) => Math.log(y));
  const meanX = xs.reduce((a, b) => a + b, 0) / xs.length;
  const meanY = ys.reduce((a, b) => a + b, 0) / ys.length;

  let sxy = 0;
  let sxx = 0;
  let syy = 0;
  for (let i = 0; i < xs.length; i += 1) {
    const dx = xs[i] - meanX;
    const dy = ys[i] - meanY;
    sxy += dx * dy;
    sxx += dx * dx;
    syy += dy * dy;
  }
  if (sxx === 0) return null;

  const slope = sxy / sxx;
  return {
    slope,
    intercept: meanY - slope * meanX,
    r2: syy === 0 ? 1 : (sxy * sxy) / (sxx * syy),
  };
}
