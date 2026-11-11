// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  defaultPointDetailLevel,
  POINT_DETAIL_BY_LEVEL,
  POINT_DETAIL_LEVELS,
  pointCloudDetailInForce,
} from './pointCloudDetail.ts';

describe('point detail levels', () => {
  it('orders the three levels from the least asked of the machine to the most', () => {
    const [low, normal, high] = POINT_DETAIL_LEVELS.map((level) => POINT_DETAIL_BY_LEVEL[level]);
    // A larger screen-space error means coarser tiles are good enough, so low is the largest.
    expect(low.maximumScreenSpaceError).toBeGreaterThan(normal.maximumScreenSpaceError);
    expect(normal.maximumScreenSpaceError).toBeGreaterThan(high.maximumScreenSpaceError);
    expect(low.cacheBytes).toBeLessThan(normal.cacheBytes);
    expect(normal.cacheBytes).toBeLessThan(high.cacheBytes);
  });

  it('keeps normal at the engine’s own defaults, so choosing nothing on a workstation changes nothing', () => {
    expect(POINT_DETAIL_BY_LEVEL.normal).toEqual({ maximumScreenSpaceError: 16, cacheBytes: 512 * 1024 * 1024 });
  });

  it('gives a device with a coarse pointer the low level by default, and a mouse the normal one', () => {
    expect(defaultPointDetailLevel(true)).toBe('low');
    expect(defaultPointDetailLevel(false)).toBe('normal');
    expect(pointCloudDetailInForce(undefined, true)).toBe(POINT_DETAIL_BY_LEVEL.low);
    expect(pointCloudDetailInForce(undefined, false)).toBe(POINT_DETAIL_BY_LEVEL.normal);
  });

  it('lets a chosen level override the device default either way', () => {
    expect(pointCloudDetailInForce('high', true)).toBe(POINT_DETAIL_BY_LEVEL.high);
    expect(pointCloudDetailInForce('low', false)).toBe(POINT_DETAIL_BY_LEVEL.low);
  });
});
