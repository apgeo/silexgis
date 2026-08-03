// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Only the imperative fetchers are used here — the loader lives outside React on purpose, the
// same way the flat map's overlays do, so there is no query cache and no provider to stand up.
vi.mock('../api/hooks.ts', () => ({
  fetchCenterlineFeatures: (...args: unknown[]) => {
    calls.centerlines.push(args);
    return responses.centerlines();
  },
  fetchEntranceFeatures: (...args: unknown[]) => {
    calls.entrances.push(args);
    return responses.entrances();
  },
  fetchMapFeatures: (...args: unknown[]) => {
    calls.features.push(args);
    return responses.features();
  },
}));

const { attachCaveData3d, CENTERLINE_SOURCE_ID, ENTRANCE_SOURCE_ID } = await import('./caveData3d.ts');
const { setMapTagFilter } = await import('../map/mapFilters.ts');
type CaveData3DEngine = import('./caveData3d.ts').CaveData3DEngine;
type Scene3DBounds = import('./scene3dEngine.ts').Scene3DBounds;
type Scene3DCameraState = import('./scene3dEngine.ts').Scene3DCameraState;
type Scene3DVectorSource<T> = import('./scene3dEngine.ts').Scene3DVectorSource<T>;

const calls = {
  centerlines: [] as unknown[][],
  entrances: [] as unknown[][],
  features: [] as unknown[][],
};

function centerlineCollection(extras: Record<string, unknown> = {}) {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 700],
            [25.441, 45.53, 690],
          ],
        },
        properties: { id: 'line-1', caveId: 'cave-1', hasZ: true },
      },
    ],
    withheldCount: 0,
    detail: true,
    flatCount: 0,
    ...extras,
  };
}

function entranceCollection() {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [25.4472, 45.5312] },
        properties: { id: 'entrance-1', caveId: 'cave-1', approximate: false },
      },
    ],
  };
}

const responses = {
  centerlines: () => Promise.resolve(centerlineCollection()),
  entrances: () => Promise.resolve(entranceCollection()),
  features: () => Promise.resolve({ type: 'FeatureCollection', features: [] }),
};

/** A source that records what it was told to hold, standing in for a batch in the scene. */
class FakeSource<TItem> implements Scene3DVectorSource<TItem> {
  items: readonly TItem[] = [];
  visible = true;
  removed = false;
  replaceCount = 0;

  replace(items: readonly TItem[]) {
    this.items = items;
    this.replaceCount += 1;
  }
  clear() {
    this.items = [];
  }
  setVisible(visible: boolean) {
    this.visible = visible;
  }
  remove() {
    this.removed = true;
  }
}

/** A plain object standing in for the scene: no engine, no graphics context, no React. */
class FakeEngine implements CaveData3DEngine {
  readonly sources = new Map<string, FakeSource<unknown>>();
  readonly viewListeners = new Set<() => void>();
  bounds: Scene3DBounds | undefined = [25.0, 45.4, 25.6, 45.8];
  pseudoZoom = 14.4;

  createMarkerSource(id: string) {
    return this.source(id);
  }
  createPolylineSource(id: string) {
    return this.source(id);
  }
  getVisibleBounds() {
    return this.bounds;
  }
  getPseudoZoom() {
    return this.pseudoZoom;
  }
  onViewChanged(listener: () => void) {
    this.viewListeners.add(listener);
    return () => this.viewListeners.delete(listener);
  }
  /** Stands in for the viewer letting go of the camera. */
  settle() {
    for (const listener of [...this.viewListeners]) listener();
  }

  getCamera(): Scene3DCameraState {
    return { longitude: 25.3, latitude: 45.6, height: 5000, heading: 0, pitch: -90, roll: 0 };
  }
  setCamera() {}
  flyToZoom() {}
  fitBounds() {}

  private source(id: string) {
    const source = new FakeSource<never>();
    this.sources.set(id, source as FakeSource<unknown>);
    return source;
  }
}

beforeEach(() => {
  calls.centerlines = [];
  calls.entrances = [];
  calls.features = [];
  responses.centerlines = () => Promise.resolve(centerlineCollection());
  responses.entrances = () => Promise.resolve(entranceCollection());
  responses.features = () => Promise.resolve({ type: 'FeatureCollection', features: [] });
  setMapTagFilter(null);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('attachCaveData3d', () => {
  it('fills the view straight away instead of waiting for the camera to move', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));

    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.items).toHaveLength(1);
    expect(engine.sources.get(ENTRANCE_SOURCE_ID)!.items).toHaveLength(1);
    handle.detach();
  });

  it('asks every endpoint about the same patch of ground at the same zoom', async () => {
    // Four requests derived from four separately sampled cameras would let the overlays disagree
    // about which ground is being shown, and would open a cluster at a zoom it was never summed at.
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.features).toHaveLength(1));

    const [centerlineBbox, centerlineZoom] = calls.centerlines[0];
    const [entranceBbox, entranceZoom] = calls.entrances[0];
    expect(entranceBbox).toBe(centerlineBbox);
    expect(entranceZoom).toBe(centerlineZoom);
    expect(calls.features[0][0]).toBe(centerlineBbox);
    // Rounded, the way the flat map rounds its own view's zoom before sending it.
    expect(centerlineZoom).toBe(14);
    handle.detach();
  });

  it('opts into surveyed altitudes, which is the only reason to draw a survey in three dimensions', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));

    expect(calls.centerlines[0][4]).toBe(true);
    handle.detach();
  });

  it('waits for the camera to come to rest before refetching', async () => {
    vi.useFakeTimers();
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.advanceTimersByTimeAsync(0);
    expect(calls.centerlines).toHaveLength(1);

    // A drag raises this repeatedly; only the last one should turn into a request.
    engine.settle();
    await vi.advanceTimersByTimeAsync(100);
    engine.settle();
    await vi.advanceTimersByTimeAsync(100);
    engine.settle();
    expect(calls.centerlines).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(250);
    expect(calls.centerlines).toHaveLength(2);
    handle.detach();
  });

  it('throws away an answer a newer request has already overtaken', async () => {
    let releaseFirst: ((value: unknown) => void) | undefined;
    responses.centerlines = () =>
      calls.centerlines.length === 1
        ? new Promise((resolve) => {
            releaseFirst = resolve;
          })
        : Promise.resolve(centerlineCollection());

    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));

    handle.reload();
    await vi.waitFor(() => expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.replaceCount).toBe(1));

    // The stale answer arrives late and must not overwrite the newer one.
    releaseFirst?.(centerlineCollection({ withheldCount: 99 }));
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(2));
    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.replaceCount).toBe(1);
    expect(handle.getState().withheldCount).toBe(0);
    handle.detach();
  });

  it('keeps what is drawn when a request fails, rather than blanking the view', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));
    const source = engine.sources.get(CENTERLINE_SOURCE_ID)!;

    responses.centerlines = () => Promise.reject(new Error('network'));
    handle.reload();
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(2));

    expect(source.items).toHaveLength(1);
    expect(source.replaceCount).toBe(1);
    handle.detach();
  });

  it('reports what the server held back and what it could only send flat', async () => {
    responses.centerlines = () =>
      Promise.resolve(centerlineCollection({ withheldCount: 3, flatCount: 2, detail: false }));
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    const seen: unknown[] = [];
    handle.subscribe((state) => seen.push(state));

    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));

    expect(handle.getState()).toEqual({
      withheldCount: 3,
      detail: false,
      flatCount: 2,
      loading: false,
    });
    // Loading is announced as well as finished, so chrome can say the view is still filling in.
    expect(seen.length).toBeGreaterThan(1);
    handle.detach();
  });

  it('applies the installation\'s limits and reloads, but only when they actually changed', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));
    expect(calls.centerlines[0][2]).toBe(18);
    expect(calls.centerlines[0][3]).toBe(25000);

    handle.setLimits({ detailZoom: 16, maxPaths: 40000 });
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(2));
    expect(calls.centerlines[1][2]).toBe(16);
    expect(calls.centerlines[1][3]).toBe(40000);

    handle.setLimits({ detailZoom: 16, maxPaths: 40000 });
    expect(calls.centerlines).toHaveLength(2);
    handle.detach();
  });

  it('carries the map-wide tag filter, so a filtered view means the same thing in both views', async () => {
    setMapTagFilter('winter-2026');
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.entrances).toHaveLength(1));

    expect(calls.entrances[0][2]).toBe('winter-2026');
    expect(calls.features[0][1]).toEqual({ tag: 'winter-2026' });
    handle.detach();
  });

  it('puts the markers into the scene after the lines they stand on', async () => {
    // An entrance sits at the head of the survey line that starts there, so the two are drawn on
    // top of each other and hit tested together. The marker is the thing a viewer is aiming at,
    // and adding it last is what keeps it in front.
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.sources.size).toBe(4));

    const order = [...engine.sources.keys()];
    expect(order.indexOf(ENTRANCE_SOURCE_ID)).toBeGreaterThan(order.indexOf(CENTERLINE_SOURCE_ID));
    handle.detach();
  });

  it('asks about nothing when the camera is not looking at the globe', async () => {
    const engine = new FakeEngine();
    engine.bounds = undefined;
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.sources.size).toBe(4));

    expect(calls.centerlines).toHaveLength(0);
    handle.detach();
  });

  it('takes every batch out of the scene and stops loading when it is detached', async () => {
    vi.useFakeTimers();
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.advanceTimersByTimeAsync(0);

    handle.detach();
    engine.settle();
    await vi.advanceTimersByTimeAsync(1000);

    expect([...engine.sources.values()].every((source) => source.removed)).toBe(true);
    expect(engine.viewListeners.size).toBe(0);
    expect(calls.centerlines).toHaveLength(1);
  });
});
