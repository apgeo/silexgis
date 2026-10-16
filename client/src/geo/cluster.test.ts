// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { clusterCellBbox, clusterCellOf } from './cluster.ts';

describe('cluster cells (named by the server)', () => {
  it('reads the four bounds a cluster carries', () => {
    expect(clusterCellOf([25.3125, 45.703125, 25.400390625, 45.791015625])).toEqual([
      25.3125, 45.703125, 25.400390625, 45.791015625,
    ]);
  });

  it('refuses anything that is not a real cell', () => {
    expect(clusterCellOf(undefined)).toBeUndefined();
    expect(clusterCellOf([1, 2, 3])).toBeUndefined();
    expect(clusterCellOf([1, 2, 'x', 4])).toBeUndefined();
    expect(clusterCellOf([3, 2, 1, 4])).toBeUndefined();
    expect(clusterCellOf([1, 4, 3, 2])).toBeUndefined();
  });

  it('asks for exactly the cell, with no margin', () => {
    expect(clusterCellBbox([25.3125, 45.703125, 25.400390625, 45.791015625])).toBe(
      '25.3125,45.703125,25.400390625,45.791015625',
    );
  });
});
