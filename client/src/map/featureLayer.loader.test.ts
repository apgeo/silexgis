// SPDX-License-Identifier: AGPL-3.0-or-later
import Collection from 'ol/Collection';
import type Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import { Modify } from 'ol/interaction';
import { ModifyEvent } from 'ol/interaction/Modify';
import { fromLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchMapFeatures } from '../api/hooks.ts';
import { surfaceFeaturesChanged } from '../workspace/surfaceFeatureRefresh.ts';
import { attachSurfaceFeatureLoader, getSurfaceFeatureSource } from './featureLayer.ts';
import { MapEditController } from './mapEdit.ts';

vi.mock('../api/hooks.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/hooks.ts')>()),
  fetchMapFeatures: vi.fn(),
  fetchFeatureVersion: vi.fn(async () => '"1"'),
}));

/** The server's answer for one load: saved point features with the given ids at [lon, lat]. */
function answer(...rows: [string, number, number][]) {
  return {
    type: 'FeatureCollection',
    features: rows.map(([id, lon, lat]) => ({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: { id, featureTypeId: 1 },
    })),
  } as never;
}

function sized(): Map {
  const map = new Map({ view: new View({ center: fromLonLat([24, 45]), zoom: 10 }) });
  map.setSize([400, 300]);
  return map;
}

/** Waits for the loader's request and its answer to land. */
async function settle(): Promise<void> {
  await vi.waitFor(() => expect(vi.mocked(fetchMapFeatures)).toHaveBeenCalled());
  await new Promise((resolve) => setTimeout(resolve, 0));
}

function byId(id: string): Feature[] {
  return getSurfaceFeatureSource()
    .getFeatures()
    .filter((f) => f.get('id') === id);
}

/** Reshapes a saved feature the way a Modify drag does: start, move, end. */
function reshape(map: Map, feature: Feature, to: [number, number]): void {
  const modify = map.getInteractions().getArray().find((i) => i instanceof Modify) as Modify;
  const features = new Collection([feature]);
  modify.dispatchEvent(new ModifyEvent('modifystart', features, {} as never));
  feature.setGeometry(new Point(fromLonLat(to)));
  modify.dispatchEvent(new ModifyEvent('modifyend', features, {} as never));
}

let detach: (() => void) | undefined;

beforeEach(() => {
  vi.mocked(fetchMapFeatures).mockReset();
});

afterEach(() => {
  detach?.();
  detach = undefined;
  getSurfaceFeatureSource().clear();
});

describe('surface feature loader and pending edits', () => {
  it('keeps an unsaved drawing on the map when the layer reloads', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();

    controller.placePointAt([24.01, 45.01], 1);
    const [drawn] = controller.getPendingEdits().created;
    vi.mocked(fetchMapFeatures).mockClear();
    surfaceFeaturesChanged();
    await settle();

    expect(getSurfaceFeatureSource().getFeatures()).toContain(drawn.feature);
    expect(byId('a')).toHaveLength(1);
    expect(controller.getPendingEdits().created).toHaveLength(1);
  });

  it('keeps a reshaped saved feature in its edited shape, not the server copy, across a reload', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45], ['b', 24.02, 45.02]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    controller.setMode('modify');
    const [saved] = byId('a');
    reshape(map, saved, [24.05, 45.05]);
    expect(controller.getPendingEdits().modified.has('a')).toBe(true);

    vi.mocked(fetchMapFeatures).mockClear();
    surfaceFeaturesChanged();
    await settle();

    expect(byId('a')).toEqual([saved]);
    expect((saved.getGeometry() as Point).getCoordinates()).toEqual(fromLonLat([24.05, 45.05]));
    expect(byId('b')).toHaveLength(1);
  });

  it('tells the drawing tools about every feature it takes off, so none of them holds a ghost', async () => {
    const map = sized();
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45], ['b', 24.02, 45.02]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    const removed: string[] = [];
    getSurfaceFeatureSource().on('removefeature', (e) => removed.push(e.feature!.get('id') as string));

    vi.mocked(fetchMapFeatures).mockClear();
    surfaceFeaturesChanged();
    await settle();

    expect(removed.sort()).toEqual(['a', 'b']);
  });

  it('lets a discard take drawings off the map and the next load restore the saved shape', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    controller.placePointAt([24.01, 45.01], 1);
    const [drawn] = controller.getPendingEdits().created;
    controller.setMode('modify');
    reshape(map, byId('a')[0], [24.05, 45.05]);

    controller.reset();
    vi.mocked(fetchMapFeatures).mockClear();
    surfaceFeaturesChanged();
    await settle();

    expect(getSurfaceFeatureSource().getFeatures()).not.toContain(drawn.feature);
    const [restored] = byId('a');
    expect((restored.getGeometry() as Point).getCoordinates()).toEqual(fromLonLat([24, 45]));
    expect(byId('a')).toHaveLength(1);
  });

  it('leaves no drawing and no edited shape behind a controller that was disposed without a save', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    controller.placePointAt([24.01, 45.01], 1);
    const [drawn] = controller.getPendingEdits().created;
    controller.setMode('modify');
    reshape(map, byId('a')[0], [24.05, 45.05]);

    // Leaving the map: the page's clean-up disposes the controller and detaches the loader.
    controller.dispose();
    detach();
    // Coming back: a new controller and a new loader over the same source.
    const next = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockClear();
    detach = attachSurfaceFeatureLoader(map);
    await settle();

    expect(getSurfaceFeatureSource().getFeatures()).not.toContain(drawn.feature);
    const [restored] = byId('a');
    expect((restored.getGeometry() as Point).getCoordinates()).toEqual(fromLonLat([24, 45]));
    expect(byId('a')).toHaveLength(1);
    expect(next.getPendingEdits().created).toHaveLength(0);
    expect(next.getPendingEdits().modified.size).toBe(0);
  });

  it('never has Redo record a shape on a feature a reload has taken off the map', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    controller.setMode('modify');
    reshape(map, byId('a')[0], [24.05, 45.05]);
    controller.undo();
    expect(controller.getPendingEdits().modified.size).toBe(0);

    // Nothing is pending any more, so a pan replaces the feature with the server's copy.
    vi.mocked(fetchMapFeatures).mockClear();
    surfaceFeaturesChanged();
    await settle();
    let canRedo = true;
    controller.subscribe((s) => (canRedo = s.canRedo));
    controller.redo();

    // The step belonged to an object the map no longer shows: nothing is pending, the map shows
    // the stored shape, and Redo has nothing left to offer.
    expect(controller.getPendingEdits().modified.has('a')).toBe(false);
    const [shown] = byId('a');
    expect((shown.getGeometry() as Point).getCoordinates()).toEqual(fromLonLat([24, 45]));
    expect(canRedo).toBe(false);
  });

  it('still redoes an undone reshape while no reload has replaced the feature', async () => {
    const map = sized();
    const controller = new MapEditController(map);
    vi.mocked(fetchMapFeatures).mockResolvedValue(answer(['a', 24, 45]));
    detach = attachSurfaceFeatureLoader(map);
    await settle();
    controller.setMode('modify');
    const [saved] = byId('a');
    reshape(map, saved, [24.05, 45.05]);
    controller.undo();
    controller.redo();

    expect(controller.getPendingEdits().modified.get('a')).toEqual(
      MapEditController.toGeoJsonGeometry(saved.getGeometry()!),
    );
    expect((saved.getGeometry() as Point).getCoordinates()).toEqual(fromLonLat([24.05, 45.05]));
  });
});
