// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Only the imperative fetcher is used — the loader lives outside React, the way the flat map's
// file layers do, so there is no query cache and no provider to stand up.
vi.mock('../api/hooks.ts', () => ({
  fetchGeofileFeatureCollection: (...args: unknown[]) => {
    calls.push(args);
    return respond(args[0] as string);
  },
}));

const { attachGeofileTracks3d, geofileTrackPolylines } = await import('./geofileTracks3d.ts');
const { pickPayload } = await import('./selection3d.ts');
const { GEOFILE_DEFAULT_STROKE } = await import('../map/geofileProperties.ts');
type GeofileTracks3DEngine = import('./geofileTracks3d.ts').GeofileTracks3DEngine;
type GeofileTrack3DFile = import('./geofileTracks3d.ts').GeofileTrack3DFile;
type Scene3DBounds = import('./scene3dEngine.ts').Scene3DBounds;
type Scene3DPolyline = import('./scene3dEngine.ts').Scene3DPolyline;
type Scene3DVectorSource<T> = import('./scene3dEngine.ts').Scene3DVectorSource<T>;

let calls: unknown[][] = [];
let respond: (geofileId: string) => Promise<unknown> = () => Promise.resolve(aHike());

/** A GPS track with recorded altitudes, the ordinary case. */
function aHike(extras: Record<string, unknown> = {}) {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 1100],
            [25.441, 45.531, 1150],
            [25.442, 45.532, 1120],
          ],
        },
        properties: { id: 'row-1', 'silexgis:label': 'Ridge walk' },
      },
    ],
    ...extras,
  };
}

/** A track written by a receiver that recorded no altitude at all. */
function aPlanOnly() {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [25.44, 45.53],
            [25.441, 45.531],
          ],
        },
        properties: { id: 'row-2' },
      },
    ],
  };
}

/** A track whose altitude field was written, but filled with nothing. */
function aZeroedTrack() {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: {
          type: 'MultiLineString',
          coordinates: [
            [
              [25.44, 45.53, 0],
              [25.441, 45.531, 0],
            ],
            [
              [25.442, 45.532, 0],
              [25.443, 45.533, 0],
            ],
          ],
        },
        properties: { id: 'row-3' },
      },
    ],
  };
}

function aFile(overrides: Partial<GeofileTrack3DFile> = {}): GeofileTrack3DFile {
  return { id: 'file-1', name: 'Ridge walk.gpx', style: null, importStatus: 'imported', ...overrides };
}

const withRelief = { absolute: true, offsetM: 0 };

/** A source that records what it was told to hold, standing in for a batch in the scene. */
class FakeSource implements Scene3DVectorSource<Scene3DPolyline> {
  items: readonly Scene3DPolyline[] = [];
  opacity = 1;
  visible = true;
  removed = false;
  replaceCount = 0;

  replace(items: readonly Scene3DPolyline[]) {
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
class FakeEngine implements GeofileTracks3DEngine {
  readonly sources = new Map<string, FakeSource>();
  readonly viewListeners = new Set<() => void>();
  bounds: Scene3DBounds | undefined = [25.0, 45.4, 25.6, 45.8];

  createPolylineSource(id: string) {
    const source = new FakeSource();
    this.sources.set(id, source);
    return source;
  }
  getVisibleBounds() {
    return this.bounds;
  }
  onViewChanged(listener: () => void) {
    this.viewListeners.add(listener);
    return () => this.viewListeners.delete(listener);
  }
  /** Stands in for the viewer letting go of the camera. */
  settle() {
    for (const listener of [...this.viewListeners]) listener();
  }
}

beforeEach(() => {
  calls = [];
  respond = () => Promise.resolve(aHike());
});

afterEach(() => {
  vi.useRealTimers();
});

describe('geofileTrackPolylines', () => {
  it('lays a track with no altitudes on the ground, under relief as on the bare globe', () => {
    // The degraded case, and the one that must be visible: positions with no third ordinate read
    // as height zero, and drawn as heights they would sit at sea level — under real relief, a
    // hillside below the caves the track walks past, where nothing would ever be seen of it.
    for (const placement of [withRelief, { absolute: false, offsetM: 0 }]) {
      const lines = geofileTrackPolylines(aPlanOnly(), 'file-1', '#2f54eb', placement);

      expect(lines).toHaveLength(1);
      expect(lines[0].clampToGround).toBe(true);
      expect(lines[0].positions.map((position) => position.height)).toEqual([0, 0]);
    }
  });

  it('treats altitudes that are zero everywhere as no altitudes at all', () => {
    // A receiver that writes the field and fills it with nothing is not reporting sea level.
    const lines = geofileTrackPolylines(aZeroedTrack(), 'file-1', '#2f54eb', withRelief);

    expect(lines).toHaveLength(2);
    expect(lines.every((line) => line.clampToGround === true)).toBe(true);
  });

  it('draws a track with altitudes at them, through the same placement the surveys use', () => {
    const lines = geofileTrackPolylines(aHike(), 'file-1', '#2f54eb', { absolute: true, offsetM: 43 });

    expect(lines).toHaveLength(1);
    expect(lines[0].clampToGround).toBeUndefined();
    // The ground's own correction and nothing else: there is no top of a cave to hang from.
    expect(lines[0].positions.map((position) => position.height)).toEqual([1143, 1193, 1163]);
  });

  it('lays a track with altitudes on the bare globe, where every surface thing sits', () => {
    // No hillside to place an altitude against: at 1100 m the track would float a kilometre over
    // the smooth sphere, above the entrance markers it walks past, and hung from its own top it
    // would dip below the surface as if a path over a ridge went underground.
    const lines = geofileTrackPolylines(aHike(), 'file-1', '#2f54eb');

    expect(lines[0].clampToGround).toBe(true);
    expect(lines[0].positions.map((position) => position.height)).toEqual([0, 0, 0]);
  });

  it('draws every line in the colour it was given, at the width the surveys are drawn at', () => {
    const lines = geofileTrackPolylines(aHike(), 'file-1', '#c41d7f', withRelief);

    expect(lines[0].color).toBe('#c41d7f');
    expect(lines[0].widthPixels).toBe(2);
  });

  it('attaches a payload that selects nothing, so a click on a track is a click on nothing', () => {
    const lines = geofileTrackPolylines(aHike(), 'file-1', '#2f54eb', withRelief);

    expect(lines[0].id).toEqual({ kind: 'geofile-track', geofileId: 'file-1', featureId: 'row-1' });
    expect(pickPayload({ id: lines[0].id })).toBeUndefined();
  });

  it('skips the waypoints of a file, which are not lines', () => {
    const lines = geofileTrackPolylines(
      {
        type: 'FeatureCollection',
        features: [
          {
            type: 'Feature',
            geometry: { type: 'Point', coordinates: [25.44, 45.53, 1100] },
            properties: { id: 'row-9' },
          },
        ],
      },
      'file-1',
      '#2f54eb',
      withRelief,
    );

    expect(lines).toEqual([]);
  });
});

describe('attachGeofileTracks3d', () => {
  it('draws nothing until a file is chosen, then fetches that file for the box in view', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    expect(calls).toEqual([]);
    expect(engine.sources.size).toBe(0);

    handle.setFiles([aFile()], new Set(['file-1']));
    await vi.waitFor(() => expect(calls).toHaveLength(1));

    // The same request the flat map makes for the same file: its id and the view's box.
    expect(calls[0]).toEqual(['file-1', '25.00000,45.40000,25.60000,45.80000']);
    const source = engine.sources.get('geofile:file-1')!;
    await vi.waitFor(() => expect(source.items).toHaveLength(1));
    expect(source.items[0].color).toBe(GEOFILE_DEFAULT_STROKE);
    handle.detach();
  });

  it('names each source the way the flat map names the same file\'s layer', () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);

    handle.setFiles([aFile()], new Set(['file-1']));

    expect([...engine.sources.keys()]).toEqual(['geofile:file-1']);
    handle.detach();
  });

  it('draws a file in its own stroke colour when its style names one', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);

    handle.setFiles([aFile({ style: { stroke: '#c41d7f' } })], new Set(['file-1']));
    const source = engine.sources.get('geofile:file-1')!;
    await vi.waitFor(() => expect(source.items).toHaveLength(1));

    expect(source.items[0].color).toBe('#c41d7f');
    handle.detach();
  });

  it('leaves a file that is listed but not chosen, and one still importing, out of the scene', () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);

    handle.setFiles(
      [aFile(), aFile({ id: 'file-2', importStatus: 'importing' })],
      new Set(['file-2']),
    );

    expect(engine.sources.size).toBe(0);
    expect(calls).toEqual([]);
    handle.detach();
  });

  it('takes a file out of the scene when it is turned off, and drops its late answer', async () => {
    let release: ((value: unknown) => void) | undefined;
    respond = () =>
      new Promise((resolve) => {
        release = resolve;
      });
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile()], new Set(['file-1']));
    await vi.waitFor(() => expect(release).toBeDefined());
    const source = engine.sources.get('geofile:file-1')!;

    handle.setFiles([aFile()], new Set());
    expect(source.removed).toBe(true);

    release!(aHike());
    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));
    expect(source.replaceCount).toBe(0);
    handle.detach();
  });

  it('applies a fade set before the file was shown, as well as one set after', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);

    handle.setOpacity('file-1', 0.25);
    handle.setFiles([aFile()], new Set(['file-1']));
    const source = engine.sources.get('geofile:file-1')!;
    expect(source.opacity).toBe(0.25);

    handle.setOpacity('file-1', 0.5);
    expect(source.opacity).toBe(0.5);
    handle.detach();
  });

  it('waits for the camera to come to rest before refetching every chosen file', async () => {
    vi.useFakeTimers();
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile(), aFile({ id: 'file-2' })], new Set(['file-1', 'file-2']));
    await vi.advanceTimersByTimeAsync(0);
    expect(calls).toHaveLength(2);

    // A drag raises this repeatedly; only the last one should turn into requests.
    engine.settle();
    await vi.advanceTimersByTimeAsync(100);
    engine.settle();
    await vi.advanceTimersByTimeAsync(100);
    expect(calls).toHaveLength(2);

    await vi.advanceTimersByTimeAsync(250);
    expect(calls).toHaveLength(4);
    expect(calls.slice(2).map((call) => call[0]).sort()).toEqual(['file-1', 'file-2']);
    handle.detach();
  });

  it('throws away an answer a newer request has already overtaken', async () => {
    let releaseFirst: ((value: unknown) => void) | undefined;
    respond = () =>
      calls.length === 1
        ? new Promise((resolve) => {
            releaseFirst = resolve;
          })
        : Promise.resolve(aHike());
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile()], new Set(['file-1']));
    await vi.waitFor(() => expect(calls).toHaveLength(1));
    const source = engine.sources.get('geofile:file-1')!;

    handle.reload();
    await vi.waitFor(() => expect(source.replaceCount).toBe(1));

    // The stale answer arrives late and must not overwrite the newer one.
    releaseFirst?.(aPlanOnly());
    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));
    expect(source.replaceCount).toBe(1);
    // The newer answer is the three-point hike, not the two-point plan the stale one carried.
    expect(source.items[0].positions).toHaveLength(3);
    handle.detach();
  });

  it('keeps what is drawn when a request fails, rather than blanking the track', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile()], new Set(['file-1']));
    const source = engine.sources.get('geofile:file-1')!;
    await vi.waitFor(() => expect(source.items).toHaveLength(1));

    respond = () => Promise.reject(new Error('network'));
    handle.reload();
    await vi.waitFor(() => expect(calls).toHaveLength(2));
    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));

    expect(source.items).toHaveLength(1);
    expect(source.replaceCount).toBe(1);
    handle.detach();
  });

  it('moves the tracks onto the hillside when the ground gains relief, without fetching again', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile()], new Set(['file-1']));
    const source = engine.sources.get('geofile:file-1')!;
    await vi.waitFor(() => expect(source.items).toHaveLength(1));
    expect(source.items[0].clampToGround).toBe(true);

    handle.setAltitudePlacement({ absolute: true, offsetM: 10 });

    expect(calls).toHaveLength(1);
    expect(source.items[0].clampToGround).toBeUndefined();
    expect(source.items[0].positions.map((position) => position.height)).toEqual([1110, 1160, 1130]);
    handle.detach();
  });

  it('redraws a file whose colour changed in the catalogue, without fetching it again', async () => {
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile()], new Set(['file-1']));
    const source = engine.sources.get('geofile:file-1')!;
    await vi.waitFor(() => expect(source.items).toHaveLength(1));

    handle.setFiles([aFile({ style: { stroke: '#c41d7f' } })], new Set(['file-1']));

    expect(calls).toHaveLength(1);
    expect(source.items[0].color).toBe('#c41d7f');
    // The same file handed over again, unchanged, is not a reason to touch the scene.
    handle.setFiles([aFile({ style: { stroke: '#c41d7f' } })], new Set(['file-1']));
    expect(source.replaceCount).toBe(2);
    handle.detach();
  });

  it('reports loading while any file\'s request is in the air', async () => {
    let release: ((value: unknown) => void) | undefined;
    respond = () =>
      new Promise((resolve) => {
        release = resolve;
      });
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    const seen: boolean[] = [];
    handle.subscribe((state) => seen.push(state.loading));

    handle.setFiles([aFile()], new Set(['file-1']));
    expect(handle.getState().loading).toBe(true);

    release!(aHike());
    await vi.waitFor(() => expect(handle.getState().loading).toBe(false));
    expect(seen).toEqual([true, false]);
    handle.detach();
  });

  it('asks nothing while the camera is not looking at the globe', () => {
    const engine = new FakeEngine();
    engine.bounds = undefined;
    const handle = attachGeofileTracks3d(engine);

    handle.setFiles([aFile()], new Set(['file-1']));

    expect(calls).toEqual([]);
    expect(handle.getState().loading).toBe(false);
    handle.detach();
  });

  it('takes every file out of the scene and stops listening when detached', async () => {
    vi.useFakeTimers();
    const engine = new FakeEngine();
    const handle = attachGeofileTracks3d(engine);
    handle.setFiles([aFile(), aFile({ id: 'file-2' })], new Set(['file-1', 'file-2']));
    await vi.advanceTimersByTimeAsync(0);

    handle.detach();

    expect([...engine.sources.values()].every((source) => source.removed)).toBe(true);
    expect(engine.viewListeners.size).toBe(0);
    engine.settle();
    await vi.advanceTimersByTimeAsync(500);
    expect(calls).toHaveLength(2);
  });
});
