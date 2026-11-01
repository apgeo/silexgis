// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphNeighbourhood } from '../api/hooks/types.ts';
import {
  countFromWire,
  edgeFromWire,
  nodeFromWire,
  type AnswerRequest,
  type GraphEdge,
  type GraphNode,
  type NeighbourhoodRequest,
  type NodeKey,
} from './types.ts';

/** How much of one category's neighbours of one node the model holds, and how many there are for this viewer. */
export interface CategoryProgress {
  /** Top-level items taken so far: links for a resource link, edges for every other category. */
  loaded: number;
  total: number;
}

export interface Remaining {
  category: string;
  remaining: number;
  loaded: number;
  total: number;
}

export interface MergeResult {
  addedNodes: NodeKey[];
  addedEdges: string[];
}

interface MergedAnswer {
  answer: GraphNeighbourhood;
  request: AnswerRequest;
}

/**
 * The graph explored so far: every answer the server gave, merged by key. It knows nothing of
 * filters, selection or drawing — `toView` derives those — and nothing of React, so it can be
 * held outside component state and re-read when the store's version counter moves.
 *
 * One node per item however many relations reach it, which is the server's key rule; merging two
 * answers is therefore just a union by key. A restricted placeholder is keyed by its member row, so
 * two placeholders never merge: the graph shows no path through something the viewer cannot read.
 */
export class GraphModel {
  private seed: NodeKey | null = null;
  private readonly nodeMap = new Map<NodeKey, GraphNode>();
  private readonly edgeMap = new Map<string, GraphEdge>();
  /** Per expanded node, per category. A node is expanded once it has been the seed of an answer. */
  private readonly progressMap = new Map<NodeKey, Map<string, CategoryProgress>>();
  /** Kept so the model can be rebuilt down to the seed's own hop. */
  private answers: MergedAnswer[] = [];

  get seedKey(): NodeKey | null {
    return this.seed;
  }

  get nodes(): ReadonlyMap<NodeKey, GraphNode> {
    return this.nodeMap;
  }

  get edges(): ReadonlyMap<string, GraphEdge> {
    return this.edgeMap;
  }

  get size(): number {
    return this.nodeMap.size;
  }

  /**
   * Adds one answer. The first answer ever merged names the seed. Counts are recorded for the
   * answer's own seed node, so asking about a neighbour later grows that node's progress, not the
   * original seed's.
   */
  merge(answer: GraphNeighbourhood, request: AnswerRequest = {}): MergeResult {
    this.answers.push({ answer, request });
    return this.absorb(answer, request);
  }

  private absorb(answer: GraphNeighbourhood, request: AnswerRequest): MergeResult {
    const result: MergeResult = { addedNodes: [], addedEdges: [] };
    this.seed ??= answer.seed.key;

    for (const wire of answer.nodes) {
      const incoming = nodeFromWire(wire);
      const existing = this.nodeMap.get(incoming.key);
      if (existing === undefined) {
        this.nodeMap.set(incoming.key, incoming);
        result.addedNodes.push(incoming.key);
      } else {
        this.nodeMap.set(incoming.key, enrich(existing, incoming));
      }
    }
    for (const wire of answer.edges) {
      const edge = edgeFromWire(wire);
      if (!this.edgeMap.has(edge.key)) {
        this.edgeMap.set(edge.key, edge);
        result.addedEdges.push(edge.key);
      }
    }

    const progress = this.progressMap.get(answer.seed.key) ?? new Map<string, CategoryProgress>();
    const offset = request.category === undefined ? 0 : (request.offset ?? 0);
    for (const wire of answer.counts) {
      const count = countFromWire(wire);
      const previous = progress.get(count.category);
      progress.set(count.category, {
        // A page that starts at `offset` has taken everything before it as well.
        loaded: Math.max(previous?.loaded ?? 0, offset + count.shown),
        total: count.total,
      });
    }
    this.progressMap.set(answer.seed.key, progress);
    return result;
  }

  /**
   * The requests that built this model, in order. Asking them all again rebuilds it from fresh
   * answers (a refresh), and the order matters: the first one names the seed.
   */
  answeredRequests(): NeighbourhoodRequest[] {
    return this.answers.map(({ answer, request }) => ({
      type: answer.seed.type,
      id: answer.seed.id ?? '',
      ...(request.category === undefined ? {} : { category: request.category, offset: request.offset ?? 0 }),
    }));
  }

  isExpanded(key: NodeKey): boolean {
    return this.progressMap.has(key);
  }

  /** Every expanded node, in the order they were first asked about. */
  expandedKeys(): NodeKey[] {
    return [...this.progressMap.keys()];
  }

  progress(key: NodeKey): ReadonlyMap<string, CategoryProgress> {
    return this.progressMap.get(key) ?? new Map();
  }

  /** What is left to page on, per category, for an expanded node; categories with nothing left are left out. */
  remaining(key: NodeKey): Remaining[] {
    return [...this.progress(key)]
      .map(([category, p]) => ({ category, remaining: Math.max(0, p.total - p.loaded), loaded: p.loaded, total: p.total }))
      .filter((r) => r.remaining > 0);
  }

  /**
   * The request that would take the node further, or null when nothing can be asked: an
   * unknown node, a placeholder, a hub, a file, or (with a category) a category that is used up.
   * Without a category it is the whole first hop, and null once that has been taken.
   */
  requestFor(key: NodeKey, category?: string): NeighbourhoodRequest | null {
    const node = this.nodeMap.get(key);
    if (node === undefined || node.restricted || !node.expandable || node.id === null) {
      return null;
    }
    if (category === undefined) {
      return this.isExpanded(key) ? null : { type: node.type, id: node.id };
    }
    const p = this.progress(key).get(category);
    if (p === undefined || p.loaded >= p.total) {
      return null;
    }
    return { type: node.type, id: node.id, category, offset: p.loaded };
  }

  /** Forgets one node and its edges. Nodes it was the only way to reach drop out of the view by connectivity. */
  removeNode(key: NodeKey): boolean {
    if (key === this.seed || !this.nodeMap.delete(key)) {
      return false;
    }
    for (const [edgeKey, edge] of this.edgeMap) {
      if (edge.source === key || edge.target === key) {
        this.edgeMap.delete(edgeKey);
      }
    }
    this.progressMap.delete(key);
    return true;
  }

  /** Back to the seed's own first hop (and any further pages of it); everything learned about other nodes goes. */
  collapseToSeed(): void {
    const own = this.answers.filter((a) => a.answer.seed.key === this.seed);
    this.nodeMap.clear();
    this.edgeMap.clear();
    this.progressMap.clear();
    this.answers = [];
    for (const { answer, request } of own) {
      this.merge(answer, request);
    }
  }
}

/** A later answer may know more about a node than the first one did (a title that arrived late); it never erases. */
function enrich(existing: GraphNode, incoming: GraphNode): GraphNode {
  return {
    ...existing,
    title: incoming.title ?? existing.title,
    subtitle: incoming.subtitle ?? existing.subtitle,
    kind: incoming.kind ?? existing.kind,
    route: incoming.route ?? existing.route,
    shortCode: incoming.shortCode ?? existing.shortCode,
    memberCount: incoming.memberCount ?? existing.memberCount,
    expandable: incoming.expandable,
  };
}
