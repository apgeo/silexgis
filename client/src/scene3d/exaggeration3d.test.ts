// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  clampExaggeration,
  exaggerationInForce,
  formatExaggeration,
  MAX_EXAGGERATION,
  MIN_EXAGGERATION,
  readExaggeration,
} from './exaggeration3d.ts';

describe('the vertical exaggeration setting', () => {
  it('runs from half the true height to five times it', () => {
    expect(MIN_EXAGGERATION).toBe(0.5);
    expect(MAX_EXAGGERATION).toBe(5);
  });

  it('clamps a factor into the range and refuses what is not a number', () => {
    expect(clampExaggeration(2)).toBe(2);
    expect(clampExaggeration(2.37)).toBe(2.37);
    expect(clampExaggeration(9)).toBe(5);
    expect(clampExaggeration(0)).toBe(0.5);
    expect(clampExaggeration(-3)).toBe(0.5);
    expect(clampExaggeration(Number.NaN)).toBeUndefined();
    expect(clampExaggeration(Number.POSITIVE_INFINITY)).toBeUndefined();
  });

  it('reads a stored or shared factor back to the step a person chooses in, and nothing else', () => {
    expect(readExaggeration(2)).toBe(2);
    expect(readExaggeration(1.25)).toBe(1.3);
    expect(readExaggeration(0.1 * 13)).toBe(1.3);
    expect(readExaggeration(40)).toBe(5);
    expect(readExaggeration('2')).toBeUndefined();
    expect(readExaggeration(null)).toBeUndefined();
    expect(readExaggeration(undefined)).toBeUndefined();
    expect(readExaggeration(Number.NaN)).toBeUndefined();
  });

  it('draws at what a saved view brought into the session, else at the browser’s own, else true height', () => {
    expect(exaggerationInForce(undefined, undefined)).toBe(1);
    expect(exaggerationInForce(undefined, 2)).toBe(2);
    expect(exaggerationInForce(3, 2)).toBe(3);
    // A view saved at true height is true height in this session, whatever the browser prefers.
    expect(exaggerationInForce(1, 2)).toBe(1);
    // Something unreadable stands for nothing, and the next answer down is used.
    expect(exaggerationInForce(Number.NaN, 2)).toBe(2);
  });

  it('writes a factor the way a person reads one, in their own digits', () => {
    expect(formatExaggeration(2, 'en')).toBe('2');
    expect(formatExaggeration(0.5, 'en')).toBe('0.5');
    expect(formatExaggeration(1.5, 'ro')).toBe('1,5');
    expect(formatExaggeration(2.04, 'en')).toBe('2');
  });
});
