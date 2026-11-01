// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphView } from './view.ts';

export interface LegendContent {
  /** Item types with at least one node on screen, in the order they first appear. */
  types: string[];
  /** Relation categories with at least one edge on screen, in the order they first appear. */
  categories: string[];
  /** Whether any "+N more" stub is drawn. */
  hasMore: boolean;
  /** Whether any restricted placeholder (an item the viewer may not see) is drawn. */
  hasRestricted: boolean;
}

/**
 * What the legend explains: only the kinds of item and relation that are on screen now, so the key
 * stays as short as the picture and never describes something the viewer cannot find in it.
 */
export function legendOf(view: GraphView): LegendContent {
  const types = new Set<string>();
  const categories = new Set<string>();
  let hasMore = false;
  let hasRestricted = false;
  for (const node of view.nodes) {
    if (node.kind === 'more') {
      hasMore = true;
    } else {
      if (node.restricted) {
        hasRestricted = true;
      }
      types.add(node.type);
    }
  }
  for (const edge of view.edges) {
    if (!edge.isMore) {
      categories.add(edge.category);
    }
  }
  return { types: [...types], categories: [...categories], hasMore, hasRestricted };
}
