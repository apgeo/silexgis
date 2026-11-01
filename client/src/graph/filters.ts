// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphEdge, GraphFilters, GraphNode, NodeKey } from './types.ts';

/**
 * Filters never go to the server: the explorer always asks for every category and hides locally,
 * so toggling one is instant and a toggled-off category costs nothing to switch back on. They are
 * kept as the set of things switched OFF, so a category or type the page has never heard of is on
 * by default instead of silently hidden.
 */
export const noFilters = (): GraphFilters => ({
  disabledCategories: new Set(),
  disabledRelations: new Set(),
  disabledTypes: new Set(),
});

/** Identifies one relation code inside its category; codes repeat across categories ("parent"). */
export const relationFilterKey = (category: string, relation: string): string => `${category}:${relation}`;

export const isFiltered = (f: GraphFilters): boolean =>
  f.disabledCategories.size > 0 || f.disabledRelations.size > 0 || f.disabledTypes.size > 0;

export const isCategoryEnabled = (f: GraphFilters, category: string): boolean => !f.disabledCategories.has(category);

export const isTypeEnabled = (f: GraphFilters, type: string): boolean => !f.disabledTypes.has(type);

export const isEdgeEnabled = (f: GraphFilters, edge: Pick<GraphEdge, 'category' | 'relation'>): boolean =>
  isCategoryEnabled(f, edge.category) && !f.disabledRelations.has(relationFilterKey(edge.category, edge.relation));

function toggled(set: ReadonlySet<string>, value: string, enabled?: boolean): Set<string> {
  const next = new Set(set);
  // "enabled" is the state asked for; leaving it out flips whatever is there.
  const wantOff = enabled === undefined ? !next.has(value) : !enabled;
  if (wantOff) {
    next.add(value);
  } else {
    next.delete(value);
  }
  return next;
}

export const toggleCategory = (f: GraphFilters, category: string, enabled?: boolean): GraphFilters => ({
  ...f,
  disabledCategories: toggled(f.disabledCategories, category, enabled),
});

export const toggleRelation = (f: GraphFilters, category: string, relation: string, enabled?: boolean): GraphFilters => ({
  ...f,
  disabledRelations: toggled(f.disabledRelations, relationFilterKey(category, relation), enabled),
});

export const toggleType = (f: GraphFilters, type: string, enabled?: boolean): GraphFilters => ({
  ...f,
  disabledTypes: toggled(f.disabledTypes, type, enabled),
});

/** "Only this category": every other category in `all` is switched off, this one on. Relation and type filters are kept. */
export const onlyCategory = (f: GraphFilters, category: string, all: Iterable<string>): GraphFilters => ({
  ...f,
  disabledCategories: new Set([...all].filter((c) => c !== category)),
});

export const onlyType = (f: GraphFilters, type: string, all: Iterable<string>): GraphFilters => ({
  ...f,
  disabledTypes: new Set([...all].filter((t) => t !== type)),
});

/** Whether a node of this type may be drawn: its type is on. The seed is exempt — the page is about it. */
export const isNodeShown = (f: GraphFilters, node: Pick<GraphNode, 'type' | 'key'>, seedKey: NodeKey | null): boolean =>
  node.key === seedKey || isTypeEnabled(f, node.type);

/**
 * The keys reachable from the seed, treating every edge as two-way and passing only through the
 * nodes in `shown`. A filter that cuts the only path to a branch therefore hides the whole branch,
 * not just the nodes of the switched-off kind: an island with no route back to the seed is not
 * something the viewer asked about, and it would suggest a connection the picture no longer shows.
 */
export function connectedFromSeed(
  seedKey: NodeKey,
  shown: ReadonlySet<NodeKey>,
  edges: Iterable<Pick<GraphEdge, 'source' | 'target'>>,
): Set<NodeKey> {
  const reached = new Set<NodeKey>();
  if (!shown.has(seedKey)) {
    return reached;
  }
  const adjacent = new Map<NodeKey, NodeKey[]>();
  for (const { source, target } of edges) {
    if (!shown.has(source) || !shown.has(target)) {
      continue;
    }
    (adjacent.get(source) ?? adjacent.set(source, []).get(source)!).push(target);
    (adjacent.get(target) ?? adjacent.set(target, []).get(target)!).push(source);
  }
  const queue: NodeKey[] = [seedKey];
  reached.add(seedKey);
  for (let head = 0; head < queue.length; head++) {
    for (const next of adjacent.get(queue[head]) ?? []) {
      if (!reached.has(next)) {
        reached.add(next);
        queue.push(next);
      }
    }
  }
  return reached;
}

export interface FilterFacets {
  /** Per category present in the model: how many edges, and the relation codes seen with their edge counts. */
  categories: { category: string; edges: number; relations: { relation: string; edges: number }[] }[];
  /** Node types present in the model, with counts. */
  types: { type: string; nodes: number }[];
}

/** What a filter panel can offer: only what the explored graph actually contains. */
export function facetsOf(nodes: Iterable<GraphNode>, edges: Iterable<GraphEdge>): FilterFacets {
  const byCategory = new Map<string, Map<string, number>>();
  for (const edge of edges) {
    const relations = byCategory.get(edge.category) ?? new Map<string, number>();
    relations.set(edge.relation, (relations.get(edge.relation) ?? 0) + 1);
    byCategory.set(edge.category, relations);
  }
  const byType = new Map<string, number>();
  for (const node of nodes) {
    byType.set(node.type, (byType.get(node.type) ?? 0) + 1);
  }
  return {
    categories: [...byCategory].map(([category, relations]) => ({
      category,
      edges: [...relations.values()].reduce((a, b) => a + b, 0),
      relations: [...relations].map(([relation, count]) => ({ relation, edges: count })).sort((a, b) => a.relation.localeCompare(b.relation)),
    })),
    types: [...byType].map(([type, count]) => ({ type, nodes: count })),
  };
}
