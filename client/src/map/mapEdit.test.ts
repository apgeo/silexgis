// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type Point from 'ol/geom/Point';
import { toLonLat } from 'ol/proj';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getSurfaceFeatureSource } from './featureLayer.ts';
import { MapEditController, type EditState } from './mapEdit.ts';

// placePointAt/requestPlacement never touch the OL map (no interactions are
// attached), so a bare object stands in for it.
const fakeMap = {} as Map;

afterEach(() => {
  getSurfaceFeatureSource().clear();
});

describe('MapEditController.placePointAt', () => {
  it('creates a pending point feature at the exact coordinate through the undo pipeline', () => {
    const controller = new MapEditController(fakeMap);
    const drawEnded = vi.fn();
    controller.onDrawEnd = drawEnded;
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.placePointAt([25.5, 45.25], 7);

    const { created } = controller.getPendingEdits();
    expect(created).toHaveLength(1);
    expect(created[0].geometryType).toBe('Point');
    expect(created[0].feature.get('featureTypeId')).toBe(7);
    expect(created[0].feature.get('pendingNew')).toBe(true);
    const [lon, lat] = toLonLat((created[0].feature.getGeometry() as Point).getCoordinates());
    expect(lon).toBeCloseTo(25.5, 5);
    expect(lat).toBeCloseTo(45.25, 5);
    expect(getSurfaceFeatureSource().getFeatures()).toContain(created[0].feature);
    expect(drawEnded).toHaveBeenCalledWith(created[0].feature);
    expect(state?.dirty).toBe(1);
    expect(state?.canUndo).toBe(true);

    controller.undo();
    expect(controller.getPendingEdits().created).toHaveLength(0);
    expect(getSurfaceFeatureSource().getFeatures()).toHaveLength(0);
    expect(state?.dirty).toBe(0);

    controller.redo();
    expect(controller.getPendingEdits().created).toHaveLength(1);
    expect(state?.dirty).toBe(1);
  });
});

describe('MapEditController.requestPlacement', () => {
  it('reports the coordinate through onPointPlaced and disarms', () => {
    const controller = new MapEditController(fakeMap);
    const placed = vi.fn();
    controller.onPointPlaced = placed;
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.requestPlacement('add-cave', [22.1, 46.9]);

    expect(placed).toHaveBeenCalledWith('add-cave', [22.1, 46.9]);
    expect(state?.mode).toBe('none');
  });
});
