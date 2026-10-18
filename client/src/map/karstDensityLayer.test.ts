// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { Polygon } from 'ol/geom';
import { toLonLat } from 'ol/proj';
import { ApiError } from '../api/client.ts';
import type { DensityCell, DensityGrid, DensityQuery } from '../api/hooks.ts';
import { fetchSteppedDensity, TOO_MANY_CELLS } from './densityCells.ts';
import { bandOf, densityBandMaxima, densityCellFeatures } from './karstDensityLayer.ts';

// Invented cells and grids: the shapes the density route answers with, none of it real.

function cell(over: Partial<DensityCell> = {}): DensityCell {
  return {
    cellX: 0,
    cellY: 0,
    west: 25,
    south: 45,
    east: 25.05,
    north: 45.05,
    count: 1,
    areaKm2: 25,
    densityPerKm2: 0.04,
    studyAreaFraction: null,
    kernelDensityPerKm2: 0.03,
    hotSpotZ: 0,
    ...over,
  };
}

function grid(cellMetres: number, cells: DensityCell[] = [cell()]): DensityGrid {
  return {
    cellMetres,
    minimumCellMetres: 5000,
    maxCells: 5000,
    maxCellMetres: 500_000,
    protectionGridMetres: 5000,
    bandwidthMetres: cellMetres * 2,
    minimumBandwidthMetres: cellMetres,
    studyAreaId: null,
    studyAreaKm2: null,
    featureCount: cells.reduce((sum, c) => sum + c.count, 0),
    cellCount: cells.length,
    cells,
    autocorrelation: {
      cellCount: cells.length,
      neighbourPairCount: 0,
      index: null,
      expectedIndex: null,
      zScore: null,
      pValue: null,
      pattern: 'undetermined',
      significanceZ: 1.96,
    },
  };
}

function tooManyCells(withLimits: boolean): ApiError {
  return new ApiError(400, TOO_MANY_CELLS, 'too many', {
    code: TOO_MANY_CELLS,
    ...(withLimits ? { minimumCellMetres: 5000, maxCells: 5000 } : {}),
  });
}

describe('density cell polygons', () => {
  it('draws one polygon per cell from the bounds the server sent', () => {
    const features = densityCellFeatures([cell(), cell({ west: 25.05, east: 25.1, densityPerKm2: 0.2 })], 'density', 1.96);
    expect(features).toHaveLength(2);
    const ring = (features[0]!.getGeometry() as Polygon).getCoordinates()[0]!.map((c) => toLonLat(c));
    expect(ring[0]![0]).toBeCloseTo(25, 9);
    expect(ring[0]![1]).toBeCloseTo(45, 9);
    expect(ring[2]![0]).toBeCloseTo(25.05, 9);
    expect(ring[2]![1]).toBeCloseTo(45.05, 9);
    // The denser cell is in the top band.
    expect(features[1]!.get('fill')).not.toBe(features[0]!.get('fill'));
  });

  it('draws nothing for an empty cell, and only significant cells for hot spots', () => {
    const cells = [
      cell({ count: 0, densityPerKm2: 0, kernelDensityPerKm2: 0, hotSpotZ: 0 }),
      cell({ hotSpotZ: 2.5 }),
      cell({ hotSpotZ: -2.1 }),
      cell({ hotSpotZ: 1.0 }),
      cell({ hotSpotZ: null }),
    ];
    expect(densityCellFeatures(cells, 'density', 1.96)).toHaveLength(4);
    const spots = densityCellFeatures(cells, 'hotSpot', 1.96);
    expect(spots).toHaveLength(2);
    expect(spots[0]!.get('fill')).not.toBe(spots[1]!.get('fill'));
    // The threshold is the one the server states, not a constant of this client.
    expect(densityCellFeatures(cells, 'hotSpot', 3)).toHaveLength(0);
  });

  it('bands each reading from zero to the largest value in the answer', () => {
    const cells = [cell({ densityPerKm2: 1 }), cell({ densityPerKm2: 0.1 })];
    const maxima = densityBandMaxima(cells, 'density');
    expect(maxima).toHaveLength(5);
    expect(maxima[4]).toBe(1);
    expect(bandOf(1, maxima)).toBe(4);
    expect(bandOf(0.1, maxima)).toBe(0);
    expect(bandOf(0, maxima)).toBeNull();
    expect(densityBandMaxima(cells, 'hotSpot')).toEqual([]);
  });
});

describe('asking for a window at a cell that fits', () => {
  const wide: [number, number, number, number] = [10, 40, 40, 50];

  it('asks with no cell first, then at the multiple the refusal makes possible', async () => {
    const asked: DensityQuery[] = [];
    const fetchGrid = vi.fn(async (query: DensityQuery) => {
      asked.push(query);
      if (query.cellMetres === undefined) throw tooManyCells(true);
      return grid(query.cellMetres);
    });
    const answer = await fetchSteppedDensity(wide, null, fetchGrid);
    expect(asked[0]!.cellMetres).toBeUndefined();
    expect(asked).toHaveLength(2);
    expect(asked[1]!.cellMetres! % 5000).toBe(0);
    expect(asked[1]!.cellMetres!).toBeGreaterThan(5000);
    expect(answer.kind).toBe('grid');
    // The answer states all three limits, the largest cell among them.
    expect(answer.limits).toEqual({ minimumCellMetres: 5000, maxCells: 5000, maxCellMetres: 500_000 });
  });

  it('chooses the cell before asking once the limits are known', async () => {
    const fetchGrid = vi.fn(async (query: DensityQuery) => grid(query.cellMetres!));
    await fetchSteppedDensity(wide, { minimumCellMetres: 5000, maxCells: 5000 }, fetchGrid);
    expect(fetchGrid).toHaveBeenCalledTimes(1);
    expect(fetchGrid.mock.calls[0]![0].cellMetres).toBeGreaterThan(5000);
  });

  it('steps one doubling coarser when the server still finds the window too large', async () => {
    const asked: number[] = [];
    const fetchGrid = vi.fn(async (query: DensityQuery) => {
      asked.push(query.cellMetres!);
      if (asked.length === 1) throw tooManyCells(true);
      return grid(query.cellMetres!);
    });
    await fetchSteppedDensity(wide, { minimumCellMetres: 5000, maxCells: 5000 }, fetchGrid);
    expect(asked[1]).toBe(asked[0]! * 2);
  });

  it('reports any other refusal as itself, not as an empty grid', async () => {
    const fetchGrid = vi.fn(async () => {
      throw new ApiError(400, 'density.cell_below_protection_grid', 'too fine', {});
    });
    const answer = await fetchSteppedDensity(wide, null, fetchGrid);
    expect(answer).toEqual({ kind: 'refused', code: 'density.cell_below_protection_grid', limits: null });
  });

  it('says the window is too wide when a refusal states no limits to step by', async () => {
    const fetchGrid = vi.fn(async () => {
      throw tooManyCells(false);
    });
    const answer = await fetchSteppedDensity(wide, null, fetchGrid);
    expect(answer.kind).toBe('refused');
    expect(fetchGrid).toHaveBeenCalledTimes(1);
  });

  it('lets a failure to reach the server through, so the row can say the grid failed', async () => {
    const fetchGrid = vi.fn(async () => {
      throw new TypeError('network');
    });
    await expect(fetchSteppedDensity(wide, null, fetchGrid)).rejects.toThrow('network');
  });
});
