// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it } from 'vitest';
import { count, wireAnswer, wireEdge, wireNode } from './graphFixtures.ts';
import { getExplorerModel, selectIsFiltered, selectView, useExplorerStore } from './explorerStore.ts';

const S = wireNode('feature', 's', 'Cave S');
const a = wireNode('document', 'a', 'A');
const b = wireNode('tripLog', 'b', 'B');

const first = () => wireAnswer(S, [{ nodes: [a], edges: [wireEdge('attachment', 'plan', S.key, a.key)] }, { nodes: [b], edges: [wireEdge('tripParticipant', 'leader', b.key, S.key)] }], [count('attachment', 1), count('tripParticipant', 1, 4)]);

const state = () => useExplorerStore.getState();

beforeEach(() => {
  state().start({ type: 'feature', id: 's' });
  state().resetFilters();
  state().select(null);
});

describe('explorer store', () => {
  it('holds the model outside its state and bumps a version when the model changes', () => {
    const v0 = state().version;
    state().merge(first());
    expect(state().version).toBe(v0 + 1);
    expect(state().lastExpanded).toBe('feature:s');
    expect(getExplorerModel().nodes.size).toBe(3);
    expect(JSON.stringify(state())).not.toContain('Cave S');
  });

  it('start replaces the model and clears selection and search but keeps the filters', () => {
    state().merge(first());
    state().toggleCategory('tag');
    state().select({ kind: 'node', key: a.key });
    state().setSearchText('x');
    state().start({ type: 'feature', id: 'other' });
    expect(getExplorerModel().size).toBe(0);
    expect(state().seed).toEqual({ type: 'feature', id: 'other' });
    expect(state().selection).toBeNull();
    expect(state().searchText).toBe('');
    expect(state().filters.disabledCategories.has('tag')).toBe(true);
    expect(state().lastExpanded).toBeNull();
  });

  it('derives the view from model, filters and selection, and returns the same object while none changed', () => {
    state().merge(first());
    const v1 = selectView(state());
    expect(selectView(state())).toBe(v1);
    expect(v1.nodeCount).toBe(3);

    state().toggleCategory('tripParticipant');
    const v2 = selectView(state());
    expect(v2).not.toBe(v1);
    expect(v2.nodeCount).toBe(2);
    expect(selectIsFiltered(state())).toBe(true);

    state().select({ kind: 'node', key: a.key });
    expect(selectView(state()).nodes.find((n) => n.key === a.key)?.state).toBe('selected');
    expect(selectView(state(), { showMore: true })).not.toBe(selectView(state()));
  });

  it('never re-reads a stale view after start', () => {
    state().merge(first());
    selectView(state());
    state().start({ type: 'feature', id: 's' });
    expect(selectView(state()).nodes).toEqual([]);
  });

  it('removes a node and drops a selection that pointed at it; the seed cannot be removed', () => {
    state().merge(first());
    state().select({ kind: 'node', key: b.key });
    state().removeNode(b.key);
    expect(state().selection).toBeNull();
    expect(getExplorerModel().nodes.has(b.key)).toBe(false);
    const v = state().version;
    state().removeNode(S.key);
    expect(state().version).toBe(v);
  });

  it('collapses back to the seed hop and clears the selection', () => {
    state().merge(first());
    const c = wireNode('feature', 'c', 'C');
    state().merge(wireAnswer(a, [{ nodes: [c], edges: [wireEdge('hierarchy', 'parent', c.key, a.key)] }], [count('hierarchy', 1)]));
    state().select({ kind: 'node', key: c.key });
    state().collapseToSeed();
    expect(getExplorerModel().nodes.has(c.key)).toBe(false);
    expect(state().selection).toBeNull();
  });

  it('keeps layout, renderer and filter actions simple and independent', () => {
    state().setLayout('radial');
    state().toggleType('caver');
    state().toggleRelation('cabinet', 'parent');
    state().onlyCategory('tag', ['tag', 'hierarchy']);
    expect(state().layout).toBe('radial');
    expect(state().filters.disabledTypes.has('caver')).toBe(true);
    expect(state().filters.disabledRelations.has('cabinet:parent')).toBe(true);
    expect([...state().filters.disabledCategories]).toEqual(['hierarchy']);
    state().resetFilters();
    expect(selectIsFiltered(state())).toBe(false);
    state().setLayout('force');
  });

  it('rebuild swaps the model for fresh answers and keeps selection and filters', () => {
    state().merge(first());
    state().select({ kind: 'node', key: a.key });
    state().toggleCategory('tag');
    const v = state().version;
    const fresh = wireAnswer(S, [{ nodes: [a], edges: [wireEdge('attachment', 'plan', S.key, a.key)] }], [count('attachment', 1)]);
    state().rebuild([{ answer: fresh }]);
    expect(state().version).toBe(v + 1);
    expect(getExplorerModel().nodes.size).toBe(2);
    expect(state().selection).toEqual({ kind: 'node', key: a.key });
    expect(state().filters.disabledCategories.has('tag')).toBe(true);
  });
});
