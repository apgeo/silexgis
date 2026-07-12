// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { clusterCellBbox, clusterCellDegrees } from './cluster.ts';

describe('cluster cell math (mirrors the server grid)', () => {
  it('computes ~64px cells on the 256px tile pyramid', () => {
    expect(clusterCellDegrees(0)).toBeCloseTo(90);
    expect(clusterCellDegrees(8)).toBeCloseTo(360 / 256 / 4);
    // Cell halves with every zoom step.
    expect(clusterCellDegrees(9)).toBeCloseTo(clusterCellDegrees(8) / 2);
  });

  it('builds a west,south,east,north bbox with one cell of margin per side', () => {
    const cell = clusterCellDegrees(10);
    const [west, south, east, north] = clusterCellBbox(25.3, 45.7, 10).split(',').map(Number);
    expect(west).toBeCloseTo(25.3 - cell, 5);
    expect(south).toBeCloseTo(45.7 - cell, 5);
    expect(east).toBeCloseTo(25.3 + cell, 5);
    expect(north).toBeCloseTo(45.7 + cell, 5);
    expect(west).toBeLessThan(east);
    expect(south).toBeLessThan(north);
  });
});
