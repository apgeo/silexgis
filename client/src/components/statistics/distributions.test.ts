// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import { ccdf, fiveNumberSummary, histogram, logLogFit, powerLawExponent } from './distributions.ts';

describe('histogram', () => {
  it('puts every value in exactly one bin, the maximum included', () => {
    const bins = histogram([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 5);

    expect(bins).toHaveLength(5);
    // The point of the closed top bin: without it the 10 falls off the end.
    expect(bins.reduce((sum, b) => sum + b.count, 0)).toBe(11);
    expect(bins[4].count).toBe(3);
  });

  it('reports no bins for no values rather than one empty bin', () => {
    expect(histogram([], 10)).toEqual([]);
  });

  it('gives a single bin when every value is the same, instead of bins of zero width', () => {
    const bins = histogram([7, 7, 7], 4);

    expect(bins).toHaveLength(1);
    expect(bins[0]).toMatchObject({ from: 7, to: 7, count: 3 });
  });

  it('ignores values that are not finite', () => {
    const bins = histogram([1, 2, Number.NaN, Number.POSITIVE_INFINITY, 3], 2);

    expect(bins.reduce((sum, b) => sum + b.count, 0)).toBe(3);
  });
});

describe('ccdf', () => {
  it('ranks values from largest down', () => {
    const { points } = ccdf([5, 1, 3]);

    expect(points).toEqual([
      { x: 5, count: 1 },
      { x: 3, count: 2 },
      { x: 1, count: 3 },
    ]);
  });

  it('drops values with no position on a logarithmic axis, and says how many', () => {
    const { points, droppedNonPositive } = ccdf([10, 0, -4, 2]);

    expect(points.map((p) => p.x)).toEqual([10, 2]);
    expect(droppedNonPositive).toBe(2);
  });
});

describe('powerLawExponent', () => {
  it('recovers the exponent of a sample drawn from a known power law', () => {
    // Inverse-transform sampling of a continuous power law with alpha = 2.5 over x >= 1, from a
    // fixed grid rather than a random draw so the expectation is exact and the test never flakes.
    const alpha = 2.5;
    const values = Array.from({ length: 2000 }, (_, i) => {
      const u = (i + 0.5) / 2000;
      return Math.pow(1 - u, -1 / (alpha - 1));
    });

    const estimate = powerLawExponent(values, 1);

    expect(estimate).toBeCloseTo(alpha, 1);
  });

  it('declines to estimate from a single tail value', () => {
    expect(powerLawExponent([100], 10)).toBeNull();
  });
});

describe('fiveNumberSummary', () => {
  it('interpolates quartiles the way the database does', () => {
    const summary = fiveNumberSummary([1, 2, 3, 4]);

    expect(summary).toMatchObject({ min: 1, q1: 1.75, median: 2.5, q3: 3.25, max: 4, count: 4 });
  });

  it('is null for no values', () => {
    expect(fiveNumberSummary([])).toBeNull();
  });
});

describe('logLogFit', () => {
  it('recovers the slope of an exact power relation', () => {
    // y = 3 * x^2 is a straight line of slope 2 in log space.
    const pairs: Array<[number, number]> = [1, 2, 4, 8, 16].map((x) => [x, 3 * x * x]);

    const fit = logLogFit(pairs);

    expect(fit?.slope).toBeCloseTo(2, 10);
    expect(fit?.r2).toBeCloseTo(1, 10);
  });

  it('is null when fewer than two pairs can be transformed', () => {
    expect(logLogFit([[1, 0], [2, -1]])).toBeNull();
  });
});
