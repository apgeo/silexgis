// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import {
  MaxChartHeight,
  MaxLabelLength,
  MinChartHeight,
  axisRoomFor,
  chartHeightForCategories,
  truncateLabel,
} from './tripInsights.ts';

/**
 * The numbers behind the drawing, asserted directly.
 *
 * A chart can only be checked loosely once it is rendered — that a label exists, that an axis was
 * drawn — so these rules are stated here where the assertions can be exact. The two ends of each
 * range are the cases that matter: a breakdown with four values and one with forty are both
 * ordinary, and it is the same function that has to leave neither unreadable.
 */
describe('how tall a breakdown chart is drawn', () => {
  it('never shrinks below the height an axis and a few bars need', () => {
    expect(chartHeightForCategories(0)).toBe(MinChartHeight);
    expect(chartHeightForCategories(1)).toBe(MinChartHeight);
  });

  it('grows with the number of categories in between', () => {
    const twelve = chartHeightForCategories(12);
    expect(twelve).toBeGreaterThan(chartHeightForCategories(4));
    expect(twelve).toBeLessThan(chartHeightForCategories(24));
  });

  it('stops growing rather than running off a screen', () => {
    expect(chartHeightForCategories(40)).toBeLessThanOrEqual(MaxChartHeight);
    expect(chartHeightForCategories(4000)).toBe(MaxChartHeight);
  });

  it('leaves a bar for every category up to the bound', () => {
    // Twelve bars in the height chosen for twelve is at least a dozen pixels each, which is the
    // difference between a chart and a smear. The clamp is what makes this worth asserting: it is
    // the case where the height stops following the count.
    expect(chartHeightForCategories(12) / 12).toBeGreaterThan(12);
  });
});

describe('how much room the labels are given', () => {
  it('cuts a long name and says it cut it', () => {
    const long = 'a'.repeat(MaxLabelLength + 20);
    expect(truncateLabel(long)).toHaveLength(MaxLabelLength);
    expect(truncateLabel(long).endsWith('…')).toBe(true);
  });

  it('leaves a name that fits exactly as it was written', () => {
    expect(truncateLabel('Padiș')).toBe('Padiș');
  });

  it('widens the left margin for longer names and clamps it at both ends', () => {
    const narrow = axisRoomFor(['N']);
    const wide = axisRoomFor(['Peștera de la Fața Muntelui']);
    expect(wide).toBeGreaterThan(narrow);
    expect(narrow).toBeGreaterThanOrEqual(72);
    // One absurd name must not push the bars off the chart, which is what the upper clamp is for.
    expect(axisRoomFor(['x'.repeat(400)])).toBeLessThanOrEqual(240);
  });

  it('is sized by the longest name and not by the first', () => {
    expect(axisRoomFor(['N', 'Peștera de la Fața Muntelui'])).toBe(
      axisRoomFor(['Peștera de la Fața Muntelui']),
    );
  });

  it('needs no room at all for no labels', () => {
    expect(axisRoomFor([])).toBe(72);
  });
});
