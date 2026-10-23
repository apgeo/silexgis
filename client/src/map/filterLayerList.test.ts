// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { SavedFilter } from '../api/hooks/savedFilters.ts';
import {
  addFilterLayer,
  FILTER_LAYER_COLORS,
  filterLayerCandidates,
  filterLayerId,
  filterLayerSavedIdOf,
  filterLayerStatus,
  MAX_FILTER_LAYERS,
  nextFilterLayerColor,
  nextFilterLayerSymbol,
  nextFreeFilterLayerColor,
  nextFreeFilterLayerSymbol,
  readFilterLayers,
  removeFilterLayer,
  updateFilterLayer,
  type FilterLayerSpec,
} from './filterLayerList.ts';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

function savedFilter(n: number, overrides: Partial<SavedFilter> = {}): SavedFilter {
  return {
    id: id(n),
    name: `Filter ${n}`,
    description: null,
    document: { version: 1, scope: [], sort: 'updated', descending: true },
    state: 'ready',
    cavingGroupId: null,
    visibility: 'private',
    mine: true,
    createdAt: '2026-10-16T00:00:00Z',
    updatedAt: '2026-10-16T00:00:00Z',
    ...overrides,
  } as SavedFilter;
}

describe('the filter layer list', () => {
  it('gives each new layer the first colour nobody uses and the symbols in turn, switched on', () => {
    let list: FilterLayerSpec[] = [];
    list = addFilterLayer(list, id(1));
    list = addFilterLayer(list, id(2));
    expect(list).toEqual([
      { savedFilterId: id(1), color: FILTER_LAYER_COLORS[0], symbol: 'circle', visible: true },
      { savedFilterId: id(2), color: FILTER_LAYER_COLORS[1], symbol: 'square', visible: true },
    ]);
    // A colour freed by a removal is the next one handed out.
    list = addFilterLayer(removeFilterLayer(list, id(1)), id(3));
    expect(list.map((layer) => layer.color)).toEqual([FILTER_LAYER_COLORS[1], FILTER_LAYER_COLORS[0]]);
  });

  it('adds a filter once, refuses what cannot name a saved filter, and stops at the cap', () => {
    const once = addFilterLayer([], id(1));
    expect(addFilterLayer(once, id(1).toUpperCase())).toEqual(once);
    expect(addFilterLayer([], 'not-an-id')).toEqual([]);
    let full: FilterLayerSpec[] = [];
    for (let n = 1; n <= MAX_FILTER_LAYERS + 2; n++) {
      full = addFilterLayer(full, id(n));
    }
    expect(full).toHaveLength(MAX_FILTER_LAYERS);
    expect(MAX_FILTER_LAYERS).toBe(12);
    // Every colour in use, and no two layers drawing with both the same colour and the same symbol.
    expect(new Set(full.map((layer) => layer.color)).size).toBe(FILTER_LAYER_COLORS.length);
    expect(new Set(full.map((layer) => `${layer.color}|${layer.symbol}`)).size).toBe(MAX_FILTER_LAYERS);
  });

  it('keeps the first six as they always were, then repeats colours with another symbol', () => {
    let list: FilterLayerSpec[] = [];
    for (let n = 1; n <= 7; n++) {
      list = addFilterLayer(list, id(n));
    }
    expect(list.slice(0, 6).map((layer) => layer.color)).toEqual(FILTER_LAYER_COLORS);
    expect(list.slice(0, 6).map((layer) => layer.symbol)).toEqual([
      'circle', 'square', 'triangle', 'diamond', 'circle', 'square',
    ]);
    expect(list[6].color).toBe(FILTER_LAYER_COLORS[0]);
    expect(list[6].symbol).not.toBe(list[0].symbol);
  });

  it('steps a row to the next style no other layer has, and stays put when there is none', () => {
    let list: FilterLayerSpec[] = [];
    for (let n = 1; n <= 2; n++) {
      list = addFilterLayer(list, id(n));
    }
    // Layer 2 is a magenta square; with layer 1 made a violet square, stepping layer 1's colour
    // passes over magenta, which would make it layer 2's twin.
    list = updateFilterLayer(list, id(1), { symbol: 'square' });
    expect(list[1]).toMatchObject({ color: FILTER_LAYER_COLORS[1], symbol: 'square' });
    expect(nextFreeFilterLayerColor(list, list[0])).toBe(FILTER_LAYER_COLORS[2]);
    expect(nextFreeFilterLayerSymbol(list, list[0])).toBe('triangle');

    // Every other colour taken in this symbol: the colour button has nowhere to go.
    const crowded: FilterLayerSpec[] = FILTER_LAYER_COLORS.map((color, n) => ({
      savedFilterId: id(n + 1),
      color,
      symbol: 'circle',
      visible: true,
    }));
    expect(nextFreeFilterLayerColor(crowded, crowded[0])).toBe(crowded[0].color);
  });

  it('changes one layer and leaves the others alone', () => {
    const list = addFilterLayer(addFilterLayer([], id(1)), id(2));
    const changed = updateFilterLayer(list, id(2), { visible: false, symbol: 'diamond' });
    expect(changed[0]).toEqual(list[0]);
    expect(changed[1]).toEqual({ ...list[1], visible: false, symbol: 'diamond' });
  });

  it('steps colour and symbol round and back to the start', () => {
    let color = FILTER_LAYER_COLORS[0];
    let symbol = nextFilterLayerSymbol('circle');
    for (let i = 0; i < FILTER_LAYER_COLORS.length; i++) {
      color = nextFilterLayerColor(color);
    }
    for (let i = 0; i < 3; i++) {
      symbol = nextFilterLayerSymbol(symbol);
    }
    expect(color).toBe(FILTER_LAYER_COLORS[0]);
    expect(symbol).toBe('circle');
  });

  it('names its layers by the saved filter, and reads only its own ids back', () => {
    expect(filterLayerSavedIdOf(filterLayerId(id(4)))).toBe(id(4));
    expect(filterLayerSavedIdOf('entrances')).toBeNull();
    expect(filterLayerSavedIdOf('filter:not-an-id')).toBeNull();
    expect(filterLayerSavedIdOf(undefined)).toBeNull();
  });

  it('reads a stored list defensively', () => {
    expect(readFilterLayers(undefined)).toEqual([]);
    expect(readFilterLayers({ savedFilterId: id(1) })).toEqual([]);
    const many = Array.from({ length: MAX_FILTER_LAYERS + 3 }, (_, n) => ({ savedFilterId: id(n + 1) }));
    expect(readFilterLayers(many)).toHaveLength(MAX_FILTER_LAYERS);
    // A saved view of six from before the cap was raised reads back unchanged.
    let six: FilterLayerSpec[] = [];
    for (let n = 1; n <= 6; n++) {
      six = addFilterLayer(six, id(n));
    }
    expect(readFilterLayers(JSON.parse(JSON.stringify(six)))).toEqual(six);
    // Two stored entries with one style: the second is given a free one.
    const twins = readFilterLayers([
      { savedFilterId: id(1), color: 'violet', symbol: 'circle' },
      { savedFilterId: id(2), color: 'violet', symbol: 'circle' },
    ]);
    expect(`${twins[1].color}|${twins[1].symbol}`).not.toBe('violet|circle');
  });
});

describe('what a filter layer row says about its filter', () => {
  const spec = addFilterLayer([], id(1))[0];

  it('is ready while the filter is in the list and readable', () => {
    expect(filterLayerStatus(spec, [savedFilter(1)], new Set())).toBe('ready');
  });

  it('is unreadable when the filter is gone, unreadable, has no document, or was refused', () => {
    // The ready case beside each, so a status that never says ready cannot pass.
    expect(filterLayerStatus(spec, [savedFilter(1)], new Set())).toBe('ready');
    expect(filterLayerStatus(spec, [savedFilter(2)], new Set())).toBe('unreadable');
    expect(filterLayerStatus(spec, [savedFilter(1, { state: 'unreadable' })], new Set())).toBe('unreadable');
    expect(filterLayerStatus(spec, [savedFilter(1, { document: null })], new Set())).toBe('unreadable');
    expect(filterLayerStatus(spec, [savedFilter(1)], new Set([id(1)]))).toBe('unreadable');
  });

  it('waits for the list rather than calling a filter unreadable before it has arrived', () => {
    expect(filterLayerStatus(spec, undefined, new Set())).toBe('loading');
  });

  it('offers only readable filters that have no layer yet', () => {
    const offered = filterLayerCandidates(
      [savedFilter(1), savedFilter(2), savedFilter(3, { state: 'unreadable' }), savedFilter(4, { document: null })],
      [spec],
    );
    expect(offered.map((filter) => filter.id)).toEqual([id(2)]);
    expect(filterLayerCandidates(undefined, [])).toEqual([]);
  });
});
