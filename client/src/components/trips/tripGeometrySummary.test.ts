// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { formatPosition } from './tripGeometrySummary.ts';

const cardinals = { north: 'N', south: 'S', east: 'E', west: 'W' };

describe('a position as a printed report writes it', () => {
  it('never lets a line break part a figure from its direction letter', () => {
    const text = formatPosition([25.44127, 45.53802], cardinals);
    expect(text).toBe('45.53802° N, 25.44127° E');
    // The one breakable space is the one between the two axes.
    expect(text.match(/ /g)).toHaveLength(1);
  });
});
