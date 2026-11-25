// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { flybyBundleMarkerWindow, flybyBundleMarkersOf } from './flybyBundleExport.ts';

// What the view's half of a bundle works out by itself: where markers are looked for, and which of
// what a layer's route answers with can be one. Everything here is invented.

describe('the window markers are looked for in', () => {
  it('is the bounds of the caves the view draws, widened by half their larger side', () => {
    // Two caves, 0.04° of longitude apart at 45° north: a little over three kilometres.
    const [west, south, east, north] = flybyBundleMarkerWindow([
      [25, 45, 25.01, 45.005],
      [25.03, 45.002, 25.04, 45.004],
    ])!
      .split(',')
      .map(Number);
    const margin = (0.5 * (0.04 * 111_320 * Math.cos((45.0025 * Math.PI) / 180))) / 111_320;
    expect(south).toBeCloseTo(45 - margin, 5);
    expect(north).toBeCloseTo(45.005 + margin, 5);
    expect(east - 25.04).toBeCloseTo(25 - west, 6);
    expect(east - 25.04).toBeCloseTo(0.02, 4);
  });

  it('is at least a quarter of a kilometre past a small cave on every side', () => {
    const [west, south, east, north] = flybyBundleMarkerWindow([[25, 45, 25.0001, 45.0001]])!.split(',').map(Number);
    expect((45 - south) * 111_320).toBeCloseTo(250, 0);
    expect((north - 45.0001) * 111_320).toBeCloseTo(250, 0);
    expect((25 - west) * 111_320 * Math.cos((45 * Math.PI) / 180)).toBeCloseTo(250, 0);
    expect(east).toBeGreaterThan(25.0001);
  });

  it('is nowhere for a view that draws no cave', () => {
    expect(flybyBundleMarkerWindow([])).toBeUndefined();
  });
});

describe('what of a layer can be a marker', () => {
  const ground = (longitude: number, latitude: number) => 100 + longitude + latitude;
  const feature = (geometry: unknown, properties: Record<string, unknown>) => ({ type: 'Feature', geometry, properties });
  const point = (longitude: number, latitude: number) => ({ type: 'Point', coordinates: [longitude, latitude] });

  it('is each single point the reader is shown exactly, by its id, with the ground under it', () => {
    const collection = {
      type: 'FeatureCollection',
      features: [
        feature(point(1, 2), { id: 'a', caveId: 'c1' }),
        // Several in one, a place moved to the grid, a line, an area and a place withheld: none.
        feature(point(3, 4), { cluster: true, count: 5 }),
        feature(point(5, 6), { id: 'moved', approximate: true }),
        feature({ type: 'LineString', coordinates: [[1, 2], [3, 4]] }, { id: 'line' }),
        feature({ type: 'MultiPoint', coordinates: [[1, 2], [3, 4]] }, { id: 'several' }),
        feature(null, { id: 'withheld' }),
        feature(point(7, 8), { id: 'b', caveId: 'c2' }),
      ],
    };
    expect(flybyBundleMarkersOf(collection, ground, (properties) => properties.caveId === 'c2')).toEqual([
      { featureId: 'a', groundM: 103 },
      { featureId: 'b', groundM: 115, named: true },
    ]);
  });

  it('is nothing of an answer that is no collection', () => {
    expect(flybyBundleMarkersOf(null, ground)).toEqual([]);
    expect(flybyBundleMarkersOf({ type: 'FeatureCollection', features: [] }, ground)).toEqual([]);
  });
});
