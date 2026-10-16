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
    expect(new Set(full.map((layer) => layer.color)).size).toBe(MAX_FILTER_LAYERS);
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
