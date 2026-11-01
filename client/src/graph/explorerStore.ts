// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import type { GraphNeighbourhood } from '../api/hooks/types.ts';
import {
  isFiltered,
  noFilters,
  onlyCategory,
  onlyType,
  toggleCategory,
  toggleRelation,
  toggleType,
} from './filters.ts';
import { GraphModel, type MergeResult } from './graphModel.ts';
import type { LayoutKind } from './renderer.ts';
import { DEFAULT_RENDERER, type RendererKind } from './rendererRegistry.ts';
import type { AnswerRequest, GraphFilters, GraphSelection, NodeKey } from './types.ts';
import { toView, type GraphView, type ViewOptions } from './view.ts';

/**
 * The explored graph lives here, outside React state: a model of hundreds of nodes does not
 * belong in a state tree that is compared and copied, and a drawing library reads it directly.
 * Components learn that it changed from `version` and re-read through `selectView`.
 */
let model = new GraphModel();

export const getExplorerModel = (): GraphModel => model;

/**
 * Counts the times the whole model was replaced or emptied (a new seed, a refresh, a collapse). An
 * answer that was asked for before such a change describes a graph that no longer exists and must
 * not be merged into the new one; `version` cannot serve for that because every merge bumps it.
 */
let modelEpoch = 0;
export const getModelEpoch = (): number => modelEpoch;

export interface ExplorerSeed {
  type: string;
  id: string;
}

export interface ExplorerState {
  /** Bumped on every change to the model; the only thing a component subscribes to for it. */
  version: number;
  seed: ExplorerSeed | null;
  filters: GraphFilters;
  selection: GraphSelection;
  layout: LayoutKind;
  rendererKind: RendererKind;
  /** The in-graph search text (the matches are derived from the view, not stored). */
  searchText: string;
  /** The node whose answer was merged last: new nodes are placed around it. Null after a reset. */
  lastExpanded: NodeKey | null;

  /** Starts a new exploration around `seed`: the model is emptied, the selection cleared, the filters kept. */
  start: (seed: ExplorerSeed) => void;
  merge: (answer: GraphNeighbourhood, request?: AnswerRequest) => MergeResult;
  /**
   * Swaps in a model rebuilt from fresh answers (a refresh). Selection, filters and layout stay; a
   * selected node that no longer exists drops out of the view by itself.
   */
  rebuild: (answers: { answer: GraphNeighbourhood; request?: AnswerRequest }[]) => void;
  removeNode: (key: NodeKey) => void;
  collapseToSeed: () => void;

  select: (selection: GraphSelection) => void;
  setFilters: (filters: GraphFilters) => void;
  toggleCategory: (category: string, enabled?: boolean) => void;
  toggleRelation: (category: string, relation: string, enabled?: boolean) => void;
  toggleType: (type: string, enabled?: boolean) => void;
  onlyCategory: (category: string, all: Iterable<string>) => void;
  onlyType: (type: string, all: Iterable<string>) => void;
  resetFilters: () => void;

  setLayout: (layout: LayoutKind) => void;
  setRendererKind: (kind: RendererKind) => void;
  setSearchText: (text: string) => void;
}

export const useExplorerStore = create<ExplorerState>()((set, get) => ({
  version: 0,
  seed: null,
  filters: noFilters(),
  selection: null,
  layout: 'force',
  rendererKind: DEFAULT_RENDERER,
  searchText: '',
  lastExpanded: null,

  start: (seed) => {
    modelEpoch++;
    model = new GraphModel();
    set((s) => ({ version: s.version + 1, seed, selection: null, searchText: '', lastExpanded: null }));
  },

  merge: (answer, request) => {
    const result = model.merge(answer, request);
    set((s) => ({ version: s.version + 1, lastExpanded: answer.seed.key }));
    return result;
  },

  rebuild: (answers) => {
    modelEpoch++;
    const next = new GraphModel();
    for (const { answer, request } of answers) {
      next.merge(answer, request);
    }
    model = next;
    set((s) => ({ version: s.version + 1 }));
  },

  removeNode: (key) => {
    if (model.removeNode(key)) {
      const { selection } = get();
      set((s) => ({
        version: s.version + 1,
        selection: selection?.kind === 'node' && selection.key === key ? null : selection,
        lastExpanded: null,
      }));
    }
  },

  collapseToSeed: () => {
    modelEpoch++;
    model.collapseToSeed();
    set((s) => ({ version: s.version + 1, selection: null, lastExpanded: null }));
  },

  select: (selection) => set({ selection }),
  setFilters: (filters) => set({ filters }),
  toggleCategory: (category, enabled) => set((s) => ({ filters: toggleCategory(s.filters, category, enabled) })),
  toggleRelation: (category, relation, enabled) =>
    set((s) => ({ filters: toggleRelation(s.filters, category, relation, enabled) })),
  toggleType: (type, enabled) => set((s) => ({ filters: toggleType(s.filters, type, enabled) })),
  onlyCategory: (category, all) => set((s) => ({ filters: onlyCategory(s.filters, category, all) })),
  onlyType: (type, all) => set((s) => ({ filters: onlyType(s.filters, type, all) })),
  resetFilters: () => set({ filters: noFilters() }),

  setLayout: (layout) => set({ layout }),
  setRendererKind: (rendererKind) => set({ rendererKind }),
  setSearchText: (searchText) => set({ searchText }),
}));

export const selectIsFiltered = (s: ExplorerState): boolean => isFiltered(s.filters);

// The view is derived data. One slot is enough: there is one explorer on screen, and a component
// re-rendering for an unrelated reason must get the same object back so effects keyed on it stay quiet.
let cache: { key: unknown[]; view: GraphView } | null = null;

/**
 * The current view for the store's state. Call it inside `useMemo`/an effect keyed on the state
 * it reads, or directly: it returns the same object while nothing it depends on has changed.
 */
export function selectView(
  state: Pick<ExplorerState, 'version' | 'filters' | 'selection'>,
  options: ViewOptions = {},
): GraphView {
  const key = [model, state.version, state.filters, state.selection, options.showMore ?? false];
  if (cache !== null && cache.key.every((part, i) => Object.is(part, key[i]))) {
    return cache.view;
  }
  const view = toView(model, state.filters, state.selection, options);
  cache = { key, view };
  return view;
}
