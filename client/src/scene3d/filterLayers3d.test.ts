// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../api/hooks.ts', () => ({
  fetchEntranceFeatures: (...args: unknown[]) => {
    calls.entrances.push(args);
    return responses.entrances();
  },
  fetchMapFeatures: (...args: unknown[]) => {
    calls.features.push(args);
    return responses.features();
  },
}));

const { attachFilterLayers3d } = await import('./filterLayers3d.ts');
const { selectionFromPick } = await import('./selection3d.ts');
const { filterLayerPalette } = await import('../map/markerPalette.ts');
type FilterLayers3DEngine = import('./filterLayers3d.ts').FilterLayers3DEngine;
type FilterLayerSpec = import('../map/filterLayerList.ts').FilterLayerSpec;
type Scene3DMarker = import('./scene3dEngine.ts').Scene3DMarker;

const LAYER = '00000000-0000-4000-8000-000000000001';

const calls = { entrances: [] as unknown[][], features: [] as unknown[][] };
const responses = {
  entrances: (): Promise<unknown> =>
    Promise.resolve({
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [25.4, 45.5] },
          properties: { id: 'entrance-1', caveId: 'cave-1', name: 'Mouth', approximate: true },
        },
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [25.0, 45.0] },
          properties: { cluster: true, count: 7, cell: [24.9, 44.9, 25.1, 45.1] },
        },
      ],
    }),
  features: (): Promise<unknown> =>
    Promise.resolve({
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [25.2, 45.2] },
          properties: { id: 'feature-1', name: 'Sinkhole' },
        },
        {
          type: 'Feature',
          geometry: { type: 'LineString', coordinates: [[25.2, 45.2], [25.3, 45.3]] },
          properties: { id: 'feature-2' },
        },
      ],
    }),
};

interface FakeSource<T> {
  id: string;
  items: readonly T[];
  visible: boolean;
  opacity: number;
  removed: boolean;
}

function fakeEngine() {
  const markers: FakeSource<Scene3DMarker>[] = [];
  const lines: FakeSource<unknown>[] = [];
  let viewListener: (() => void) | undefined;
  const source = <T,>(list: FakeSource<T>[], id: string) => {
    const state: FakeSource<T> = { id, items: [], visible: true, opacity: 1, removed: false };
    list.push(state);
    return {
      replace: (items: readonly T[]) => {
        state.items = items;
      },
      clear: () => {
        state.items = [];
      },
      setVisible: (visible: boolean) => {
        state.visible = visible;
      },
      setOpacity: (opacity: number) => {
        state.opacity = opacity;
      },
      remove: () => {
        state.removed = true;
      },
    };
  };
  const engine = {
    getVisibleBounds: () => [25, 45, 26, 46],
    getPseudoZoom: () => 12,
    onViewChanged: (listener: () => void) => {
      viewListener = listener;
      return () => {
        viewListener = undefined;
      };
    },
    createMarkerSource: (id: string) => source(markers, id),
    createPolylineSource: (id: string) => source(lines, id),
  } as unknown as FilterLayers3DEngine;
  return { engine, markers, lines, moved: () => viewListener?.() };
}

const spec = (overrides: Partial<FilterLayerSpec> = {}): FilterLayerSpec => ({
  savedFilterId: LAYER,
  color: 'magenta',
  symbol: 'triangle',
  visible: true,
  ...overrides,
});

const settle = () => vi.waitFor(() => expect(calls.entrances.length).toBeGreaterThan(0));

describe('the layers drawn from saved filters, in the scene', () => {
  beforeEach(() => {
    calls.entrances = [];
    calls.features = [];
  });
  afterEach(() => vi.useRealTimers());

  it("asks the map's routes with the layer's filter and draws in its colour and symbol", async () => {
    const { engine, markers, lines } = fakeEngine();
    const handle = attachFilterLayers3d(engine);
    handle.setLayers([spec()], new Set(), new Map());
    await settle();
    await vi.waitFor(() => expect(markers[0].items.length).toBe(3));

    expect(calls.entrances[0][2]).toEqual({ savedFilterId: LAYER });
    expect(calls.features[0][1]).toEqual({ savedFilterId: LAYER });
    expect(markers[0].id).toBe(`filter:${LAYER}`);
    // The marker image is an SVG of the layer's own colour, a triangle.
    const image = decodeURIComponent(markers[0].items[0].image);
    expect(image).toContain(filterLayerPalette.magenta);
    expect(image).toContain('<polygon');
    expect(lines[0].items).toHaveLength(1);
    handle.detach();
    expect(markers[0].removed).toBe(true);
  });

  it('selects through the main payloads, and a cluster lists the members of its own filter', async () => {
    const { engine, markers } = fakeEngine();
    const handle = attachFilterLayers3d(engine);
    handle.setLayers([spec()], new Set(), new Map());
    await vi.waitFor(() => expect(markers[0].items.length).toBe(3));

    const selections = markers[0].items.map((item) => selectionFromPick({ id: item.id }));
    expect(selections).toContainEqual({ kind: 'entrance', entranceId: 'entrance-1', caveId: 'cave-1' });
    expect(selections).toContainEqual({ kind: 'feature', featureId: 'feature-1' });
    expect(selections).toContainEqual(
      expect.objectContaining({ kind: 'cluster', count: 7, savedFilterId: LAYER }),
    );
    handle.detach();
  });

  it('asks nothing for a hidden or unreadable layer, and follows the transparency', async () => {
    const { engine, markers } = fakeEngine();
    const handle = attachFilterLayers3d(engine);
    handle.setLayers([spec({ visible: false })], new Set(), new Map([[`filter:${LAYER}`, 0.4]]));
    expect(markers[0].visible).toBe(false);
    expect(markers[0].opacity).toBe(0.4);
    handle.setLayers([spec()], new Set([LAYER]), new Map());
    await Promise.resolve();
    expect(calls.entrances).toHaveLength(0);
    expect(markers[0].items).toHaveLength(0);

    // Readable again: it loads.
    handle.setLayers([spec()], new Set(), new Map());
    await settle();
    // Removed from the list: its sources leave the scene.
    handle.setLayers([], new Set(), new Map());
    expect(markers[0].removed).toBe(true);
    handle.detach();
  });

  it('reloads when the camera settles', async () => {
    vi.useFakeTimers();
    const { engine, moved } = fakeEngine();
    const handle = attachFilterLayers3d(engine);
    handle.setLayers([spec()], new Set(), new Map());
    const before = calls.entrances.length;
    moved();
    vi.advanceTimersByTime(300);
    expect(calls.entrances.length).toBe(before + 1);
    handle.detach();
  });
});
