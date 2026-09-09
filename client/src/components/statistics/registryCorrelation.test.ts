// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import { correlationLine } from './registryCorrelation.ts';
import type { RegistryCorrelation } from '../../api/hooks.ts';

function fit(overrides: Partial<RegistryCorrelation> = {}): RegistryCorrelation {
  return {
    x: 'surveyedLength',
    y: 'depth',
    count: 40,
    slope: 0.5,
    intercept: 1,
    rSquared: 0.6,
    correlation: 0.77,
    logarithmic: true,
    basis: 'Counted over the caves you may read.',
    ...overrides,
  };
}

describe('the line a fitted relationship describes', () => {
  it('runs between the ends of the horizontal measurement, read back out of the logarithms', () => {
    const drawn = correlationLine(fit(), { state: 'answered', minimum: 10, maximum: 1000 });

    expect(drawn.absence).toBeNull();
    const [left, right] = drawn.line!;
    expect(left[0]).toBe(10);
    expect(right[0]).toBe(1000);
    // exp(1) * x^0.5 — the exponential form, not the straight line the parameters look like.
    expect(left[1]).toBeCloseTo(Math.exp(1) * Math.sqrt(10), 6);
    expect(right[1]).toBeCloseTo(Math.exp(1) * Math.sqrt(1000), 6);
  });

  it('is a straight line in the measurements own units when the fit was not over logarithms', () => {
    const drawn = correlationLine(fit({ logarithmic: false, slope: 2, intercept: -3 }), {
      state: 'answered',
      minimum: 0,
      maximum: 10,
    });

    expect(drawn.line).toEqual([
      [0, -3],
      [10, 17],
    ]);
  });

  // Each of these is a different state of the registry, so each gets its own word. A single blank
  // chart for all four would say the same thing about caves that are in four different situations.
  it('says no pair recorded both measurements rather than drawing a flat line', () => {
    const drawn = correlationLine(
      fit({ count: 0, slope: null, intercept: null, rSquared: null, correlation: null }),
      { state: 'answered', minimum: 10, maximum: 1000 },
    );

    expect(drawn.line).toBeNull();
    expect(drawn.absence).toBe('noPairs');
  });

  it('says a single pair is not a relationship, and draws nothing through it', () => {
    const drawn = correlationLine(
      fit({ count: 1, slope: null, intercept: null, rSquared: null, correlation: null }),
      { state: 'answered', minimum: 10, maximum: 1000 },
    );

    expect(drawn.line).toBeNull();
    expect(drawn.absence).toBe('notFitted');
  });

  it('draws nothing where the horizontal measurement covers no range', () => {
    expect(correlationLine(fit(), { state: 'answered', minimum: 40, maximum: 40 }).absence).toBe('noRange');
    expect(correlationLine(fit(), { state: 'answered', minimum: null, maximum: null }).absence).toBe('noRange');
  });

  it('draws nothing on logarithmic axes that would have to start at zero', () => {
    expect(correlationLine(fit(), { state: 'answered', minimum: 0, maximum: 900 }).absence).toBe('noPositiveRange');
    // The same range is perfectly drawable when the fit was not over logarithms.
    expect(correlationLine(fit({ logarithmic: false }), { state: 'answered', minimum: 0, maximum: 900 }).absence)
      .toBeNull();
  });

  // The range comes from a second question, which answers later than the fit and can fail on its
  // own. Both of those states used to arrive here as a pair of nulls and were reported as "this
  // measurement covers no range" — a statement about the registry made out of a request that had
  // not come back, and on the slower of the two routes that is what a reader saw on every load.
  it('does not call a range it has not been told about a range of zero', () => {
    const pending = correlationLine(fit(), { state: 'pending' });

    expect(pending.line).toBeNull();
    expect(pending.absence).toBe('rangeUnknown');
  });

  it('says the range could not be read when the question asking for it failed', () => {
    const failed = correlationLine(fit(), { state: 'unavailable' });

    expect(failed.line).toBeNull();
    expect(failed.absence).toBe('rangeUnavailable');
  });

  // What the fit itself says is known without the range at all, and is the more useful sentence.
  it('still says there were no pairs when the range is unknown as well', () => {
    const drawn = correlationLine(
      fit({ count: 0, slope: null, intercept: null, rSquared: null, correlation: null }),
      { state: 'pending' },
    );

    expect(drawn.absence).toBe('noPairs');
  });

  // Defensive: the page shows nothing at all until an answer arrives, so this branch is never
  // what a reader sees — it exists so that a caller who forgets that cannot draw an empty line.
  it('draws nothing when there is no answer yet', () => {
    expect(correlationLine(undefined, { state: 'answered', minimum: 10, maximum: 1000 }).line).toBeNull();
  });
});
