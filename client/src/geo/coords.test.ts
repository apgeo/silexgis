// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { clampLonLat, formatLonLat } from './coords.ts';

describe('coords', () => {
  it('formats north-east coordinates', () => {
    expect(formatLonLat(25.44721, 45.53127)).toBe('45.53127°N 25.44721°E');
  });

  it('formats south-west coordinates', () => {
    expect(formatLonLat(-70.1, -33.5, 1)).toBe('33.5°S 70.1°W');
  });

  it('clamps out-of-range values', () => {
    expect(clampLonLat(200, -95)).toEqual([180, -90]);
  });
});
