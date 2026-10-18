// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  answeredCellMultiple,
  clampToWorld,
  coarserCellMultiple,
  densityCellCount,
  densityCellMetres,
  fittingCellMultiple,
  limitsFromRefusal,
  type LonLatBox,
} from './densityCells.ts';

/**
 * Choosing the cell size for a map window from the two numbers the server publishes. The limits
 * here are invented but shaped like an installation's: a 5 km protection grid and 5,000 cells.
 */
const limits = { minimumCellMetres: 5000, maxCells: 5000 };

/** A window of roughly what a 1000 px map shows at a web-mercator zoom, centred on 25°E 45.5°N. */
function windowAtZoom(zoom: number): LonLatBox {
  const degreesWide = (360 * 1000) / (256 * 2 ** zoom);
  const degreesHigh = degreesWide * 0.6;
  return clampToWorld([25 - degreesWide / 2, 45.5 - degreesHigh / 2, 25 + degreesWide / 2, 45.5 + degreesHigh / 2]);
}

describe('density cell stepping', () => {
  it('counts cells the way the server does, with each end of the window inside its own cell', () => {
    const cellDegrees = 5000 / 111_320;
    // Exactly ten cells wide and one high, anchored on the zero-based lattice.
    expect(densityCellCount([0, 0, cellDegrees * 9, 0], 5000)).toBe(10);
    // A window that is a point still falls in one cell.
    expect(densityCellCount([1, 1, 1, 1], 5000)).toBe(1);
  });

  it('stays at the floor while the window fits and doubles as the view widens', () => {
    const multiples = [12, 10, 8, 6, 4, 2].map((zoom) => fittingCellMultiple(windowAtZoom(zoom), limits));
    expect(multiples[0]).toBe(1);
    expect(multiples[1]).toBe(1);
    // Never finer as the window widens, and every step a power of two of the floor.
    for (let i = 1; i < multiples.length; i += 1) {
      expect(multiples[i]!).toBeGreaterThanOrEqual(multiples[i - 1]!);
      expect(Math.log2(multiples[i]!) % 1).toBe(0);
    }
    expect(multiples[multiples.length - 1]!).toBeGreaterThan(1);
  });

  it('chooses the finest multiple within the limit, and one step finer would exceed it', () => {
    for (const zoom of [3, 5, 7, 9]) {
      const box = windowAtZoom(zoom);
      const multiple = fittingCellMultiple(box, limits)!;
      expect(densityCellCount(box, densityCellMetres(limits.minimumCellMetres, multiple))).toBeLessThanOrEqual(
        limits.maxCells,
      );
      if (multiple > 1) {
        expect(
          densityCellCount(box, densityCellMetres(limits.minimumCellMetres, multiple / 2)),
        ).toBeGreaterThan(limits.maxCells);
      }
    }
  });

  it('gives up rather than asking for a cell wider than any it would step to', () => {
    // Twelve doublings of a 1 m floor is 4 km, which cannot cover the world in ten cells.
    expect(fittingCellMultiple([-180, -90, 180, 90], { minimumCellMetres: 1, maxCells: 10 })).toBeNull();
    expect(coarserCellMultiple(2 ** 12)).toBeNull();
    expect(coarserCellMultiple(4)).toBe(8);
  });

  it('stops at the largest cell the server states rather than asking past it', () => {
    // The whole world at a 5 km floor first fits in 5000 cells at 640 km, past a 500 km edge.
    const world = [-180, -90, 180, 90] as const;
    expect(fittingCellMultiple(world, { ...limits, maxCellMetres: 500_000 })).toBeNull();
    expect(fittingCellMultiple(world, { ...limits, maxCellMetres: 1_000_000 })).toBe(128);
    expect(fittingCellMultiple(world, limits)).toBe(128);
    expect(coarserCellMultiple(64, { ...limits, maxCellMetres: 500_000 })).toBeNull();
    expect(coarserCellMultiple(32, { ...limits, maxCellMetres: 500_000 })).toBe(64);
    expect(coarserCellMultiple(64, limits)).toBe(128);
  });

  it('reads the limits a too-many-cells refusal carries, and nothing else as limits', () => {
    expect(limitsFromRefusal({ code: 'density.too_many_cells', minimumCellMetres: 5000, maxCells: 5000 })).toEqual(
      limits,
    );
    expect(
      limitsFromRefusal({ code: 'density.too_many_cells', minimumCellMetres: 5000, maxCells: 5000, maxCellMetres: 500_000 }),
    ).toEqual({ ...limits, maxCellMetres: 500_000 });
    expect(limitsFromRefusal({ code: 'density.too_many_cells' })).toBeNull();
    expect(limitsFromRefusal({ minimumCellMetres: '5000', maxCells: 5000 })).toBeNull();
    expect(limitsFromRefusal(undefined)).toBeNull();
  });

  it('clamps a window wider than the world to what the server accepts', () => {
    expect(clampToWorld([-400, -95, 400, 95])).toEqual([-180, -90, 180, 90]);
  });
});

describe('the multiple an answer was taken at', () => {
  it('is one at the floor and the stepped power of two above it', () => {
    expect(answeredCellMultiple({ minimumCellMetres: 5000, maxCells: 5000, cellMetres: 5000 })).toBe(1);
    expect(answeredCellMultiple({ minimumCellMetres: 5000, maxCells: 5000, cellMetres: 40000 })).toBe(8);
  });
});
