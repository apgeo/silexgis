// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import View from 'ol/View';
import type VectorLayer from 'ol/layer/Vector';
import type VectorSource from 'ol/source/Vector';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const fetchEntranceFeatures = vi.fn();
const fetchMapFeatures = vi.fn();
vi.mock('../api/hooks/map.ts', () => ({
  fetchEntranceFeatures: (...args: unknown[]) => fetchEntranceFeatures(...args),
  fetchMapFeatures: (...args: unknown[]) => fetchMapFeatures(...args),
}));

const { addFilterLayer, filterLayerId, updateFilterLayer } = await import('./filterLayerList.ts');
const { detachFilterLayers, onFilterLayerRefusal, syncFilterLayers } = await import('./filterLayers.ts');
const { findOverlayLayer, getOverlayOrder } = await import('./mapContext.ts');

const first = '00000000-0000-4000-8000-000000000001';
const second = '00000000-0000-4000-8000-000000000002';

const point = (id: string) => ({
  type: 'FeatureCollection',
  features: [{ type: 'Feature', id, geometry: { type: 'Point', coordinates: [25, 45] }, properties: {} }],
});
const empty = { type: 'FeatureCollection', features: [] };

function fakeMap(): Map & { pan: () => Promise<void> } {
  const view = new View({ center: [2_800_000, 5_600_000], zoom: 10 });
  const moveEnd = new Set<() => void>();
  return {
    getSize: () => [800, 600],
    getView: () => view,
    on: vi.fn((type: string, handler: () => void) => type === 'moveend' && moveEnd.add(handler)),
    un: vi.fn((type: string, handler: () => void) => type === 'moveend' && moveEnd.delete(handler)),
    // A pan, past the loaders' debounce.
    pan: async () => {
      moveEnd.forEach((handler) => handler());
      await new Promise((resolve) => setTimeout(resolve, 300));
    },
  } as unknown as Map & { pan: () => Promise<void> };
}

const sourceOf = (savedFilterId: string) =>
  (findOverlayLayer(filterLayerId(savedFilterId)) as VectorLayer).getSource() as VectorSource;
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('the layers drawn from saved filters', () => {
  beforeEach(() => {
    fetchEntranceFeatures.mockReset().mockResolvedValue(point('e1'));
    fetchMapFeatures.mockReset().mockResolvedValue(empty);
  });
  afterEach(() => detachFilterLayers());

  it('asks both map routes with its own saved filter, and draws what they answer', async () => {
    const map = fakeMap();
    const list = addFilterLayer(addFilterLayer([], first), second);
    syncFilterLayers(map, list, new Set(), new globalThis.Map());
    await settle();
    expect(fetchEntranceFeatures).toHaveBeenCalledWith(expect.any(String), 10, { savedFilterId: first });
    expect(fetchMapFeatures).toHaveBeenCalledWith(expect.any(String), { savedFilterId: second });
    expect(sourceOf(first).getFeatures()).toHaveLength(1);
    expect(getOverlayOrder()).toEqual([filterLayerId(first), filterLayerId(second)]);
  });

  it('keeps an unreadable filter as an empty layer that asks nothing, beside a readable one that draws', async () => {
    const map = fakeMap();
    const list = addFilterLayer(addFilterLayer([], first), second);
    syncFilterLayers(map, list, new Set([second]), new globalThis.Map());
    await settle();
    // The readable layer draws: the empty one is empty because of its filter, not because nothing loads.
    expect(sourceOf(first).getFeatures()).toHaveLength(1);
    expect(sourceOf(second).getFeatures()).toHaveLength(0);
    expect(fetchEntranceFeatures.mock.calls.map((call) => call[2])).toEqual([{ savedFilterId: first }]);
    expect(findOverlayLayer(filterLayerId(second))).toBeDefined();
  });

  it('empties a layer whose filter the server refuses, and says which filter it was', async () => {
    const map = fakeMap();
    const refused: string[] = [];
    const stop = onFilterLayerRefusal((savedFilterId, code) => refused.push(`${savedFilterId}:${code}`));
    syncFilterLayers(map, addFilterLayer([], first), new Set(), new globalThis.Map());
    await settle();
    expect(sourceOf(first).getFeatures()).toHaveLength(1);

    fetchEntranceFeatures.mockRejectedValue({ code: 'saved_filter.not_found' });
    await map.pan();
    expect(sourceOf(first).getFeatures()).toHaveLength(0);
    expect(refused).toEqual([`${first}:saved_filter.not_found`]);
    stop();
  });

  it('keeps what it drew through an error that is not a refusal', async () => {
    const map = fakeMap();
    syncFilterLayers(map, addFilterLayer([], first), new Set(), new globalThis.Map());
    await settle();
    const refused: string[] = [];
    const stop = onFilterLayerRefusal((savedFilterId) => refused.push(savedFilterId));
    fetchMapFeatures.mockRejectedValue(new TypeError('network'));
    await map.pan();
    expect(fetchMapFeatures).toHaveBeenCalledTimes(2);
    expect(sourceOf(first).getFeatures()).toHaveLength(1);
    expect(refused).toEqual([]);
    stop();
  });

  it('follows the list: style, visibility and transparency, and removal', async () => {
    const map = fakeMap();
    let list = addFilterLayer([], first);
    syncFilterLayers(map, list, new Set(), new globalThis.Map());
    list = updateFilterLayer(list, first, { color: 'gold', symbol: 'diamond', visible: false });
    syncFilterLayers(map, list, new Set(), new globalThis.Map([[filterLayerId(first), 0.3]]));
    const layer = findOverlayLayer(filterLayerId(first))!;
    expect(layer.get('filterColor')).toBe('gold');
    expect(layer.get('filterSymbol')).toBe('diamond');
    expect(layer.getVisible()).toBe(false);
    expect(layer.getOpacity()).toBe(0.3);

    syncFilterLayers(map, [], new Set(), new globalThis.Map());
    expect(findOverlayLayer(filterLayerId(first))).toBeUndefined();
    expect(map.un).toHaveBeenCalled();
  });

  it('asks nothing while hidden', async () => {
    const map = fakeMap();
    const list = updateFilterLayer(addFilterLayer([], first), first, { visible: false });
    syncFilterLayers(map, list, new Set(), new globalThis.Map());
    await settle();
    expect(fetchEntranceFeatures).not.toHaveBeenCalled();
  });
});
