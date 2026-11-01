// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  connectedFromSeed,
  facetsOf,
  isCategoryEnabled,
  isEdgeEnabled,
  isFiltered,
  isNodeShown,
  noFilters,
  onlyCategory,
  onlyType,
  relationFilterKey,
  toggleCategory,
  toggleRelation,
  toggleType,
} from './filters.ts';
import { GRAPH_CATEGORIES } from './types.ts';

describe('filter state', () => {
  it('starts with everything on, including values it has never heard of', () => {
    const f = noFilters();
    expect(isFiltered(f)).toBe(false);
    expect(isCategoryEnabled(f, 'brandNewCategory')).toBe(true);
    expect(isNodeShown(f, { key: 'x:1', type: 'brandNewType' }, 'feature:s')).toBe(true);
  });

  it('toggles without touching the previous state object', () => {
    const f = noFilters();
    const off = toggleCategory(f, 'tag');
    expect(isCategoryEnabled(off, 'tag')).toBe(false);
    expect(isCategoryEnabled(f, 'tag')).toBe(true);
    expect(isCategoryEnabled(toggleCategory(off, 'tag'), 'tag')).toBe(true);
    expect(isCategoryEnabled(toggleCategory(f, 'tag', false), 'tag')).toBe(false);
    expect(isCategoryEnabled(toggleCategory(off, 'tag', false), 'tag')).toBe(false);
    expect(isCategoryEnabled(toggleCategory(off, 'tag', true), 'tag')).toBe(true);
  });

  it('scopes a relation code to its category', () => {
    const f = toggleRelation(noFilters(), 'cabinet', 'parent');
    expect(isEdgeEnabled(f, { category: 'cabinet', relation: 'parent' })).toBe(false);
    expect(isEdgeEnabled(f, { category: 'hierarchy', relation: 'parent' })).toBe(true);
    expect(isEdgeEnabled(f, { category: 'cabinet', relation: 'filed' })).toBe(true);
    expect(relationFilterKey('cabinet', 'parent')).toBe('cabinet:parent');
  });

  it('a switched-off category disables all its edges whatever their relation', () => {
    const f = toggleCategory(noFilters(), 'attachment');
    expect(isEdgeEnabled(f, { category: 'attachment', relation: 'plan' })).toBe(false);
  });

  it('"only this category" switches every other one off and keeps type filters', () => {
    const f = onlyCategory(toggleType(noFilters(), 'tag'), 'hierarchy', GRAPH_CATEGORIES);
    expect(GRAPH_CATEGORIES.filter((c) => isCategoryEnabled(f, c))).toEqual(['hierarchy']);
    expect(f.disabledTypes.has('tag')).toBe(true);
    const types = onlyType(noFilters(), 'caver', ['feature', 'caver', 'tag']);
    expect([...types.disabledTypes].sort()).toEqual(['feature', 'tag']);
  });

  it('never hides the seed, whatever its type filter says', () => {
    const f = toggleType(noFilters(), 'feature');
    expect(isNodeShown(f, { key: 'feature:s', type: 'feature' }, 'feature:s')).toBe(true);
    expect(isNodeShown(f, { key: 'feature:o', type: 'feature' }, 'feature:s')).toBe(false);
  });
});

describe('connectedFromSeed', () => {
  const edges = [
    { source: 's', target: 'a' },
    { source: 'b', target: 'a' },
    { source: 'c', target: 'd' },
  ];

  it('walks edges both ways and stops at nodes that are not shown', () => {
    expect([...connectedFromSeed('s', new Set(['s', 'a', 'b', 'c', 'd']), edges)].sort()).toEqual(['a', 'b', 's']);
    expect([...connectedFromSeed('s', new Set(['s', 'b', 'c', 'd']), edges)]).toEqual(['s']);
  });

  it('is empty when the seed itself is not shown', () => {
    expect(connectedFromSeed('s', new Set(['a']), edges).size).toBe(0);
  });

  it('survives a cycle', () => {
    const ring = [{ source: 's', target: 'a' }, { source: 'a', target: 'b' }, { source: 'b', target: 's' }];
    expect(connectedFromSeed('s', new Set(['s', 'a', 'b']), ring).size).toBe(3);
  });
});

describe('facetsOf', () => {
  it('lists only categories, relation codes and types the graph contains, with counts', () => {
    const base = { directed: true, relationName: null, inverseName: null, isMain: null, anchorKind: null };
    const facets = facetsOf(
      [
        { key: 'a', type: 'feature' },
        { key: 'b', type: 'feature' },
        { key: 'c', type: 'tag' },
      ] as never,
      [
        { ...base, key: '1', category: 'cabinet', relation: 'parent', source: 'a', target: 'b' },
        { ...base, key: '2', category: 'cabinet', relation: 'filed', source: 'a', target: 'c' },
        { ...base, key: '3', category: 'cabinet', relation: 'filed', source: 'b', target: 'c' },
      ],
    );
    expect(facets.categories).toEqual([{ category: 'cabinet', edges: 3, relations: [{ relation: 'filed', edges: 2 }, { relation: 'parent', edges: 1 }] }]);
    expect(facets.types).toEqual([{ type: 'feature', nodes: 2 }, { type: 'tag', nodes: 1 }]);
  });
});
