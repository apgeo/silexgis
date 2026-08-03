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

const {
  attachCaveData3d,
  CENTERLINE_SOURCE_ID,
  ENTRANCE_SOURCE_ID,
  SURFACE_FEATURE_LINE_SOURCE_ID,
  SURFACE_FEATURE_SOURCE_ID,
} = await import('./caveData3d.ts');
const { setMapTagFilter } = await import('../map/mapFilters.ts');
type CaveData3DEngine = import('./caveData3d.ts').CaveData3DEngine;
type Scene3DBounds = import('./scene3dEngine.ts').Scene3DBounds;
type Scene3DCameraState = import('./scene3dEngine.ts').Scene3DCameraState;
type Scene3DVectorSource<T> = import('./scene3dEngine.ts').Scene3DVectorSource<T>;
type Scene3DCutawayFootprint = import('./scene3dEngine.ts').Scene3DCutawayFootprint;
type Scene3DSurfaceMode = import('./scene3dEngine.ts').Scene3DSurfaceMode;

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

/** Two caves eighty kilometres apart, the way a regional view answers. */
function twoCaveCollection() {
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
        properties: { id: 'line-1', caveId: 'cave-1', hasZ: true, topAltitudeM: 700 },
      },
      {
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [26.44, 45.53, 900],
            [26.441, 45.53, 880],
          ],
        },
        properties: { id: 'line-2', caveId: 'cave-2', hasZ: true, topAltitudeM: 900 },
      },
    ],
    withheldCount: 0,
    detail: true,
    flatCount: 0,
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
  opacity = 1;
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
  setOpacity(opacity: number) {
    this.opacity = opacity;
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

  /** What the loader last told the scene about the ground and how deep a viewer may go. */
  footprint: Scene3DCutawayFootprint | undefined;
  footprintCalls = 0;
  cameraFloor: number | undefined;
  surfaceMode: Scene3DSurfaceMode = 'overlay';

  setCameraFloorHeight(height: number) {
    this.cameraFloor = height;
  }
  setSurfaceMode(mode: Scene3DSurfaceMode) {
    this.surfaceMode = mode;
  }
  getSurfaceState() {
    return {
      requested: this.surfaceMode,
      effective: this.surfaceMode,
      cutawayAvailable: true,
      hasFootprint: this.footprint !== undefined,
    };
  }
  onSurfaceStateChanged() {
    return () => {};
  }
  setCutawayFootprint(footprint: Scene3DCutawayFootprint | undefined) {
    this.footprint = footprint;
    this.footprintCalls += 1;
  }

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

  it('hands the ground and the descent limit back when it is detached', async () => {
    // The scene is shared and outlives this loader, so a hole cut around a survey that is no
    // longer drawn would stay cut, over ground with nothing under it.
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.footprint).toBeDefined());

    handle.detach();

    expect(engine.footprint).toBeUndefined();
    expect(engine.cameraFloor).toBe(-2000);
  });
});

describe('the ground the survey is under', () => {
  it('works the opening and the descent limit out from the survey it drew', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);

    await vi.waitFor(() => expect(engine.footprint).toBeDefined());

    // The fixture cave hangs from its own top: the drawn depths run to 10 m below it, so the
    // excavation floor is under that and the camera may go under the floor.
    expect(engine.footprint!.ring.length).toBeGreaterThan(3);
    expect(engine.footprint!.floorHeight).toBe(-210);
    expect(engine.cameraFloor).toBe(-2000);
    handle.detach();
  });

  it('cuts around the cave the view is centred on, not around every cave it holds', async () => {
    // An opening is sized to the survey it has to reveal, so one drawn around every cave a
    // regional view happens to hold is not a cutaway of a cave at all: it is an ellipse as wide as
    // the region, which takes the basemap off the screen from horizon to horizon and leaves a few
    // threads of survey on a flat floor. And because the angle a viewer has to look from is worked
    // out from that same outline, an opening that wide also reports that it is legible from
    // anywhere, so the mode never hands the view back either.
    responses.centerlines = () => Promise.resolve(twoCaveCollection());
    const engine = new FakeEngine();
    engine.bounds = [25.39, 45.49, 25.49, 45.57];
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.footprint).toBeDefined());

    const longitudes = engine.footprint!.ring.map((position) => position.longitude);
    expect(Math.min(...longitudes)).toBeGreaterThan(25.4);
    expect(Math.max(...longitudes)).toBeLessThan(25.5);
    handle.detach();
  });

  it('follows the viewer to the cave they moved to', async () => {
    responses.centerlines = () => Promise.resolve(twoCaveCollection());
    const engine = new FakeEngine();
    engine.bounds = [25.39, 45.49, 25.49, 45.57];
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.footprint).toBeDefined());

    engine.bounds = [26.39, 45.49, 26.49, 45.57];
    handle.reload();

    await vi.waitFor(() => {
      const longitudes = engine.footprint!.ring.map((position) => position.longitude);
      expect(Math.min(...longitudes)).toBeGreaterThan(26.4);
    });
    handle.detach();
  });

  it('has no opening to offer when the view holds no survey', async () => {
    const engine = new FakeEngine();
    responses.centerlines = () =>
      Promise.resolve({ type: 'FeatureCollection', features: [], withheldCount: 0, detail: false, flatCount: 0 });
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.footprintCalls).toBe(1));

    expect(engine.footprint).toBeUndefined();
    handle.detach();
  });
});

describe('layers a viewer turns off and fades', () => {
  it('stops fetching a layer that is turned off, because the requests are the cost of it', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, false);
    handle.reload();
    await vi.waitFor(() => expect(calls.entrances).toHaveLength(2));

    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.visible).toBe(false);
    expect(calls.centerlines).toHaveLength(1);
    handle.detach();
  });

  it('catches up on the view it missed when the layer comes back', async () => {
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));
    handle.setLayerVisible(CENTERLINE_SOURCE_ID, false);

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, true);

    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(2));
    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.visible).toBe(true);
    handle.detach();
  });

  it('stops explaining a layer nobody is looking at', async () => {
    // The notices are about what the survey layer could not show. Over a view the viewer emptied
    // themselves, they would be telling them the wrong thing.
    const engine = new FakeEngine();
    responses.centerlines = () => Promise.resolve(centerlineCollection({ withheldCount: 7 }));
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(handle.getState().withheldCount).toBe(7));

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, false);

    expect(handle.getState().withheldCount).toBe(0);
    handle.detach();
  });

  it('hands the ground back when the survey layer is turned off, and takes it again when it returns', async () => {
    // An excavation around a survey nobody is drawing is an opening with nothing in it. Worse, a
    // layer that is off is not fetched either, so nothing would ever move the opening or take it
    // away: it would stay cut over the last cave the viewer looked at however far they travelled
    // from it, while the control still said the cutaway was showing them a cave.
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.footprint).toBeDefined());

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, false);

    expect(engine.footprint).toBeUndefined();

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, true);

    await vi.waitFor(() => expect(engine.footprint).toBeDefined());
    handle.detach();
  });

  it('ignores an answer that lands after the layer was turned off', async () => {
    // Turning the layer off clears the notices on purpose, and no load starts, so a request that
    // was already in the air would sail past the staleness check and put them back over a view the
    // viewer has just emptied — where they would stay, because every later load stops before it
    // publishes anything while the layer is off.
    let release: ((value: unknown) => void) | undefined;
    responses.centerlines = () =>
      new Promise((resolve) => {
        release = resolve;
      });
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(calls.centerlines).toHaveLength(1));

    handle.setLayerVisible(CENTERLINE_SOURCE_ID, false);
    release?.(centerlineCollection({ withheldCount: 7, flatCount: 2 }));
    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));

    expect(handle.getState().withheldCount).toBe(0);
    expect(handle.getState().flatCount).toBe(0);
    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.replaceCount).toBe(0);
    // And the ground the hide handed back stays handed back.
    expect(engine.footprint).toBeUndefined();
    handle.detach();
  });

  it('fades both halves of a layer that is drawn as two batches', async () => {
    // A surface feature is a symbol and the outline of the same thing; fading half of one would
    // be nonsense.
    const engine = new FakeEngine();
    const handle = attachCaveData3d(engine);
    await vi.waitFor(() => expect(engine.sources.size).toBe(4));

    handle.setLayerOpacity(SURFACE_FEATURE_SOURCE_ID, 0.4);

    expect(engine.sources.get(SURFACE_FEATURE_SOURCE_ID)!.opacity).toBe(0.4);
    expect(engine.sources.get(SURFACE_FEATURE_LINE_SOURCE_ID)!.opacity).toBe(0.4);
    // And nothing else moved.
    expect(engine.sources.get(CENTERLINE_SOURCE_ID)!.opacity).toBe(1);
    handle.detach();
  });
});
