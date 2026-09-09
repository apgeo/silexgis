// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import type { RegistryDistributionBin } from '../../api/hooks.ts';
import {
  distributionBars,
  hasDistribution,
  mergedBinCount,
  paretoTailSpan,
} from './registryDistribution.ts';

const bin = (
  lowerBound: number,
  upperBound: number,
  count: number,
  merged = false,
): RegistryDistributionBin => ({ lowerBound, upperBound, count, merged });

const labels = {
  bound: (value: number) => String(value),
  joined: (range: { from: string; to: string }) => `${range.from}-${range.to} joined`,
};

describe('the registry intervals a chart is handed', () => {
  it('are the ones the registry returned, in the order it returned them', () => {
    const bars = distributionBars([bin(0, 100, 4), bin(100, 200, 0), bin(200, 300, 9)], labels);

    expect(bars.map((bar) => bar.count)).toEqual([4, 0, 9]);
    expect(bars.map((bar) => bar.lowerBound)).toEqual([0, 100, 200]);
    expect(bars.map((bar) => bar.upperBound)).toEqual([100, 200, 300]);
  });

  // An interval holding too few caves to publish is joined to its neighbour rather than left out,
  // because leaving it out would itself announce that something is there. Joined, it is wider
  // than its neighbours — so a reader who is not told takes that width for a real feature of the
  // distribution. This is the assertion that stops a later tidy-up from making the labels uniform.
  it('name a joined interval as joined, and an ordinary one by its lower bound alone', () => {
    const bars = distributionBars([bin(0, 100, 7), bin(100, 400, 3, true)], labels);

    expect(bars[0].merged).toBe(false);
    expect(bars[0].label).toBe('0');
    expect(bars[1].merged).toBe(true);
    expect(bars[1].label).toBe('100-400 joined');
  });

  it('carry the flag through even when every interval was joined into one', () => {
    const bars = distributionBars([bin(0, 300, 3, true)], labels);

    expect(bars).toHaveLength(1);
    expect(bars[0].merged).toBe(true);
    expect(mergedBinCount([bin(0, 300, 3, true)])).toBe(1);
  });

  it('count no joined intervals when the registry joined none', () => {
    expect(mergedBinCount([bin(0, 100, 4), bin(100, 200, 6)])).toBe(0);
  });

  // No intervals at all is an answer and not an absence of one: nothing in the set records the
  // measurement, or everything recording it records the same value, so there is no range to cut
  // up. The counts beside it are still true, which is why the page distinguishes this from a
  // reply that has not arrived.
  it('tell an answer with no range to divide apart from one with intervals in it', () => {
    expect(hasDistribution([])).toBe(false);
    expect(hasDistribution([bin(0, 100, 1)])).toBe(true);
  });
});

/**
 * Which bars a fitted tail covers. The exponent is beside the chart and the bars are in it, and
 * without this the reader has a number for where the tail starts and no way to see which part of
 * the distribution it is a claim about.
 */
describe('where a fitted power-law tail sits among the drawn intervals', () => {
  const drawn = [bin(0, 100, 40), bin(100, 200, 12), bin(200, 300, 5), bin(300, 400, 2)];

  it('counts the intervals from the one the fitted bound falls inside', () => {
    expect(paretoTailSpan(drawn, 250)).toEqual({ from: 250, intervals: 2, total: 4 });
  });

  it('says the tail is the whole distribution when the bound sits below every interval', () => {
    expect(paretoTailSpan(drawn, 0)).toEqual({ from: 0, intervals: 4, total: 4 });
  });

  // Saying nothing is the answer here: a tail announced as starting where no bar is drawn sends
  // the reader looking along the axis for something that is not on it.
  it('says nothing where there is no fit, no interval, or no interval the bound reaches', () => {
    expect(paretoTailSpan(drawn, null)).toBeNull();
    expect(paretoTailSpan([], 250)).toBeNull();
    expect(paretoTailSpan(drawn, 400)).toBeNull();
  });
});
