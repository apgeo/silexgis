// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { clampLonLat, formatLonLat, worldBbox } from './coords.ts';

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

  it('passes a window already on the globe through unchanged', () => {
    expect(worldBbox([25.1, 45.2, 25.3, 45.4])).toBe('25.10000,45.20000,25.30000,45.40000');
  });

  it('cuts a window wider than the world down to the world', () => {
    expect(worldBbox([-300, -85, 300, 85])).toBe('-180.00000,-85.00000,180.00000,85.00000');
  });

  it('brings a window on a copy of the world back onto the world it shows', () => {
    expect(worldBbox([385.1, 45.2, 385.3, 45.4])).toBe('25.10000,45.20000,25.30000,45.40000');
  });

  it('cuts a window crossing the antimeridian at the edge rather than turning it round', () => {
    expect(worldBbox([170, 0, 190, 10])).toBe('170.00000,0.00000,180.00000,10.00000');
  });
});
