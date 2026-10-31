// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  TERRAIN_PROFILE_MAX_VERTICES,
  getTerrainProfileSource,
  showTerrainProfileLine,
  thinVertices,
} from './terrainProfileTool.ts';

describe('thinVertices', () => {
  it('leaves a short line alone', () => {
    expect(thinVertices([1, 2, 3])).toEqual([1, 2, 3]);
  });

  it('thins a long line to the request limit and keeps both ends', () => {
    const points = Array.from({ length: 1000 }, (_, i) => i);
    const thinned = thinVertices(points);
    expect(thinned).toHaveLength(TERRAIN_PROFILE_MAX_VERTICES);
    expect(thinned[0]).toBe(0);
    expect(thinned.at(-1)).toBe(999);
    expect(new Set(thinned).size).toBe(thinned.length);
  });
});

describe('showTerrainProfileLine', () => {
  it('draws one line and clears it again', () => {
    showTerrainProfileLine([
      { longitude: 25, latitude: 46 },
      { longitude: 25.1, latitude: 46 },
    ]);
    expect(getTerrainProfileSource().getFeatures()).toHaveLength(1);
    showTerrainProfileLine(null);
    expect(getTerrainProfileSource().getFeatures()).toHaveLength(0);
  });
});
