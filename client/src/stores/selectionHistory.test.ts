// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it } from 'vitest';
import { useWorkspaceStore } from './workspaceStore.ts';

const feature = (id: string) => ({ kind: 'feature' as const, featureId: id });

/**
 * Back and forward through what has been selected.
 *
 * Recorded inside the store's own setter rather than by whoever calls it, so the map, the bus, the
 * in-view list and a deep link all leave the same trail — a history that only some callers wrote
 * to would skip exactly the steps somebody wants to retrace.
 */
describe('selection history', () => {
  beforeEach(() => {
    useWorkspaceStore.setState({ selection: null, selectionHistory: [], selectionCursor: -1 });
  });

  it('records every selection and walks back through them', () => {
    const store = useWorkspaceStore.getState();
    store.setSelection(feature('a'));
    store.setSelection(feature('b'));
    store.setSelection(feature('c'));

    expect(useWorkspaceStore.getState().canGoBackSelection()).toBe(true);
    useWorkspaceStore.getState().goBackSelection();
    expect(useWorkspaceStore.getState().selection).toEqual(feature('b'));

    useWorkspaceStore.getState().goBackSelection();
    expect(useWorkspaceStore.getState().selection).toEqual(feature('a'));
    expect(useWorkspaceStore.getState().canGoBackSelection()).toBe(false);
  });

  it('goes forward again, and stops at the end', () => {
    const store = useWorkspaceStore.getState();
    store.setSelection(feature('a'));
    store.setSelection(feature('b'));
    useWorkspaceStore.getState().goBackSelection();

    useWorkspaceStore.getState().goForwardSelection();
    expect(useWorkspaceStore.getState().selection).toEqual(feature('b'));
    expect(useWorkspaceStore.getState().canGoForwardSelection()).toBe(false);
  });

  it('drops the forward tail when something new is selected after going back', () => {
    const store = useWorkspaceStore.getState();
    store.setSelection(feature('a'));
    store.setSelection(feature('b'));
    useWorkspaceStore.getState().goBackSelection();
    useWorkspaceStore.getState().setSelection(feature('c'));

    expect(useWorkspaceStore.getState().canGoForwardSelection()).toBe(false);
    useWorkspaceStore.getState().goBackSelection();
    expect(useWorkspaceStore.getState().selection).toEqual(feature('a'));
  });

  it('does not record deselecting, so Escape then Back returns to what was selected', () => {
    const store = useWorkspaceStore.getState();
    store.setSelection(feature('a'));
    store.setSelection(feature('b'));
    useWorkspaceStore.getState().setSelection(null);

    expect(useWorkspaceStore.getState().selection).toBeNull();
    useWorkspaceStore.getState().goBackSelection();
    expect(useWorkspaceStore.getState().selection).toEqual(feature('a'));
  });

  it('does not record selecting the same thing twice', () => {
    const store = useWorkspaceStore.getState();
    store.setSelection(feature('a'));
    store.setSelection(feature('a'));

    expect(useWorkspaceStore.getState().selectionHistory).toHaveLength(1);
  });
});

describe('multi-selection', () => {
  beforeEach(() => useWorkspaceStore.setState({ selectionSet: [] }));

  it('gathers and un-gathers the same object', () => {
    const store = useWorkspaceStore.getState();
    store.toggleInSelectionSet({ kind: 'feature', id: 'a' });
    store.toggleInSelectionSet({ kind: 'feature', id: 'b' });
    expect(useWorkspaceStore.getState().selectionSet).toHaveLength(2);

    useWorkspaceStore.getState().toggleInSelectionSet({ kind: 'feature', id: 'a' });
    expect(useWorkspaceStore.getState().selectionSet).toEqual([{ kind: 'feature', id: 'b' }]);
  });

  it('is bounded, because every bulk action writes per object', () => {
    const many = Array.from({ length: 400 }, (_, i) => ({ kind: 'feature' as const, id: `f${i}` }));
    useWorkspaceStore.getState().setSelectionSet(many);

    expect(useWorkspaceStore.getState().selectionSet.length).toBeLessThanOrEqual(200);
  });
});
