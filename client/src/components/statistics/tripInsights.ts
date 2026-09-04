// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The arithmetic behind the trip insight charts, kept out of the components that draw them.
 *
 * <p>
 * A chart rendered into a test can only be asserted on loosely — that an element exists, that a
 * label reads a certain way — so a number worked out inside a component is a number nothing
 * checks. Everything here is plain values in and plain values out, so the rules that decide how
 * tall a chart is and how much room its labels need are stated once and tested directly.
 * </p>
 */

/** Shortest a bar chart is ever drawn: below this the axis and the bars fight for the same pixels. */
export const MinChartHeight = 200;

/**
 * Tallest a bar chart is ever drawn. A breakdown hands back at most a few dozen values, and a
 * chart taller than a screen is one nobody can compare the ends of — past this the bars thin out
 * rather than the page growing.
 */
export const MaxChartHeight = 620;

/** Room one category needs to stay readable, and the axis, legend and margins around them all. */
const RowHeight = 24;
const ChartChrome = 96;

/**
 * How tall a horizontal bar chart of this many categories is drawn.
 *
 * Scaled rather than fixed because the two ends of the range are both real: a breakdown by
 * lifecycle state has four bars and a fixed tall chart wastes most of a screen on white space,
 * while a breakdown by person has forty and a fixed short one stacks them into an unreadable
 * smear. Clamped at both ends because neither growth is worth following forever.
 */
export function chartHeightForCategories(count: number): number {
  const wanted = count * RowHeight + ChartChrome;
  return Math.min(MaxChartHeight, Math.max(MinChartHeight, wanted));
}

/** Longest a category label is drawn before it is cut, so one long name cannot eat the chart. */
export const MaxLabelLength = 28;

/** A label short enough to leave room for the bars, with an ellipsis where it was cut. */
export function truncateLabel(label: string, max = MaxLabelLength): string {
  return label.length <= max ? label : `${label.slice(0, max - 1)}…`;
}

/**
 * How much room the category axis of a horizontal bar chart needs on the left, in pixels.
 *
 * Measured from the labels rather than left to the library: version 6 deprecated the option that
 * fits a grid around its labels and warns on the console for every chart that uses it, so the
 * margin is ours to size. A width per character is an approximation, which is why it is clamped:
 * too small clips a name and too large leaves the bars nowhere to go.
 */
export function axisRoomFor(labels: readonly string[]): number {
  const longest = labels.reduce((widest, label) => Math.max(widest, truncateLabel(label).length), 0);
  return Math.min(240, Math.max(72, longest * 7 + 18));
}
