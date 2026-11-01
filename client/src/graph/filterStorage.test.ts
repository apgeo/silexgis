// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { filtersToPref, prefToFilters, sanitizeGraphFilterPref } from './filterStorage.ts';
import { noFilters, toggleCategory, toggleRelation, toggleType } from './filters.ts';
import { useExplorerStore } from './explorerStore.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

afterEach(() => {
  useExplorerStore.getState().resetFilters();
  useUiPrefsStore.getState().releaseAccount();
});

describe('filter persistence', () => {
  it('round-trips all three kinds of switch', () => {
    const f = toggleType(toggleRelation(toggleCategory(noFilters(), 'tag'), 'hierarchy', 'parent'), 'document');
    const back = prefToFilters(sanitizeGraphFilterPref(JSON.parse(JSON.stringify(filtersToPref(f)))));
    expect([...back.disabledCategories]).toEqual(['tag']);
    expect([...back.disabledRelations]).toEqual(['hierarchy:parent']);
    expect([...back.disabledTypes]).toEqual(['document']);
  });

  it('falls back to nothing filtered on garbage or odd shapes', () => {
    expect(sanitizeGraphFilterPref('x').categories).toEqual([]);
    expect(sanitizeGraphFilterPref(42).types).toEqual([]);
    expect(sanitizeGraphFilterPref({ categories: ['a', 7, null], types: 'x' })).toEqual({
      categories: ['a'],
      relations: [],
      types: [],
    });
  });

  it('writes the store\'s filters to the account preferences when they change', () => {
    useExplorerStore.getState().toggleCategory('survey', false);
    expect(useUiPrefsStore.getState().graphFilters.categories).toEqual(['survey']);
    useExplorerStore.getState().resetFilters();
    expect(useUiPrefsStore.getState().graphFilters.categories).toEqual([]);
  });

  it('follows the account preferences when another account takes over', () => {
    useExplorerStore.getState().toggleCategory('tag', false);
    useUiPrefsStore.getState().releaseAccount();
    expect(useExplorerStore.getState().filters.disabledCategories.size).toBe(0);
    useUiPrefsStore.getState().setGraphFilters({ categories: ['hierarchy'], relations: [], types: [] });
    expect(useExplorerStore.getState().filters.disabledCategories.has('hierarchy')).toBe(true);
  });
});
