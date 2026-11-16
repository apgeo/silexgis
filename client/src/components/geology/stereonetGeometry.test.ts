// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import { contourLevels, densityBand, densityCells, NET_CENTRE, NET_RADIUS, netPoint } from './stereonetGeometry.ts';

describe('stereonet geometry', () => {
  it('puts north at the top and east at the right of the drawing', () => {
    expect(netPoint(0, 0)).toEqual({ x: NET_CENTRE, y: NET_CENTRE });
    expect(netPoint(0, 1)).toEqual({ x: NET_CENTRE, y: NET_CENTRE - NET_RADIUS });
    expect(netPoint(1, 0)).toEqual({ x: NET_CENTRE + NET_RADIUS, y: NET_CENTRE });
  });

  it('bands a Kamb density by its contour interval', () => {
    expect(densityBand(1.9, 2)).toBe(0);
    expect(densityBand(2, 2)).toBe(1);
    expect(densityBand(5.5, 2)).toBe(2);
    expect(densityBand(Number.NaN, 2)).toBe(0);
    expect(contourLevels(6.2, 2)).toEqual([2, 4, 6]);
    expect(contourLevels(1, 2)).toEqual([]);
  });

  it('draws a square only for nodes on the net and above the first level', () => {
    // A three-node grid: corners off the net (null), the centre dense, the edges sparse.
    const cells = densityCells({
      size: 3,
      contourIntervalSigma: 2,
      values: [null, 1, null, 1, 6, 1, null, 1, null],
    });
    expect(cells).toHaveLength(1);
    expect(cells[0]).toEqual({ x: NET_CENTRE - NET_RADIUS / 2, y: NET_CENTRE - NET_RADIUS / 2, size: NET_RADIUS, band: 3 });
    expect(densityCells(null)).toEqual([]);
  });
});
