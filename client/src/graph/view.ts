// SPDX-License-Identifier: AGPL-3.0-or-later
import { connectedFromSeed, isEdgeEnabled, isNodeShown, noFilters } from './filters.ts';
import type { GraphModel } from './graphModel.ts';
import type { GraphEdge, GraphFilters, GraphNode, GraphSelection, HighlightState, NodeKey } from './types.ts';

/** 'hub' is a resource link of three or more members drawn as a node of its own; 'more' is a "+N more" stub. */
export type ViewNodeKind = 'node' | 'hub' | 'more';

export interface ViewMore {
  /** The expanded node the remaining items hang off. */
  nodeKey: NodeKey;
  category: string;
  remaining: number;
}

export interface ViewNode {
  key: NodeKey;
  kind: ViewNodeKind;
  type: string;
  /** Null for a "+N more" stub, which stands for items not loaded. */
  node: GraphNode | null;
  state: HighlightState;
  isSeed: boolean;
  restricted: boolean;
  expanded: boolean;
  more: ViewMore | null;
}

export interface ViewEdge {
  key: string;
  category: string;
  relation: string;
  relationName: string | null;
  inverseName: string | null;
  directed: boolean;
  source: NodeKey;
  target: NodeKey;
  anchorKind: string | null;
  /** Set when this single edge stands for a whole two-member link: the hub it replaced. */
  link: { key: NodeKey; shortCode: string | null } | null;
  /** 'selected' | 'neighbour' (touches the selection) | 'faded' | 'normal'. */
  state: HighlightState;
  isMore: boolean;
}

export interface GraphView {
  seedKey: NodeKey | null;
  nodes: ViewNode[];
  edges: ViewEdge[];
  /** The selection as it applies to this view: a selection the filters have hidden is no selection. */
  selection: GraphSelection;
  /** Nodes and edges that are real items and relations, not "+N more" stubs. */
  nodeCount: number;
  edgeCount: number;
}

export interface ViewOptions {
  /** Add one "+N more" stub per expanded visible node and category with items left to load. Off until the UI can page. */
  showMore?: boolean;
}

export const moreKey = (nodeKey: NodeKey, category: string): string => `more:${nodeKey}:${category}`;

export const moreEdgeKey = (nodeKey: NodeKey, category: string): string => `moreEdge:${nodeKey}:${category}`;

const HUB_TYPE = 'resLink';

/**
 * Turns the explored graph into what is drawn, for the filters and the selection as they stand.
 *
 * - A resource link with exactly two members is one edge between them (from the main member when
 *   the relation is directed), a link with more is a small hub node with an edge to each member.
 *   The decision is made on the link's whole membership, not on how many members survive the
 *   filters, so toggling a type never turns a hub into an edge and back.
 * - A node is shown when its type is on (the seed always) and a route of enabled edges between
 *   shown nodes leads to it from the seed.
 * - Highlight states are computed here and only painted by a renderer, so every renderer and the
 *   list view agree on what a selection means.
 */
export function toView(
  model: GraphModel,
  filters: GraphFilters = noFilters(),
  selection: GraphSelection = null,
  options: ViewOptions = {},
): GraphView {
  const seedKey = model.seedKey;
  if (seedKey === null) {
    return { seedKey, nodes: [], edges: [], selection: null, nodeCount: 0, edgeCount: 0 };
  }

  const { candidateEdges, collapsedHubs } = candidateEdgesOf(model, filters);
  const shownKeys = new Set<NodeKey>();
  for (const node of model.nodes.values()) {
    if (!collapsedHubs.has(node.key) && isNodeShown(filters, node, seedKey)) {
      shownKeys.add(node.key);
    }
  }
  const reached = connectedFromSeed(seedKey, shownKeys, candidateEdges);

  const nodes: ViewNode[] = [];
  for (const node of model.nodes.values()) {
    if (reached.has(node.key)) {
      nodes.push({
        key: node.key,
        kind: node.type === HUB_TYPE ? 'hub' : 'node',
        type: node.type,
        node,
        state: 'normal',
        isSeed: node.key === seedKey,
        restricted: node.restricted,
        expanded: model.isExpanded(node.key),
        more: null,
      });
    }
  }
  const edges: ViewEdge[] = candidateEdges.filter((e) => reached.has(e.source) && reached.has(e.target));

  const realNodes = nodes.length;
  const realEdges = edges.length;

  if (options.showMore) {
    for (const parent of [...nodes]) {
      for (const left of model.remaining(parent.key)) {
        if (filters.disabledCategories.has(left.category)) {
          continue;
        }
        const key = moreKey(parent.key, left.category);
        nodes.push({
          key,
          kind: 'more',
          type: 'more',
          node: null,
          state: 'normal',
          isSeed: false,
          restricted: false,
          expanded: false,
          more: { nodeKey: parent.key, category: left.category, remaining: left.remaining },
        });
        edges.push({
          // Nodes and edges share one id space in a drawing library, so the stub's edge has its own key.
          key: moreEdgeKey(parent.key, left.category),
          category: left.category,
          relation: '',
          relationName: null,
          inverseName: null,
          directed: false,
          source: parent.key,
          target: key,
          anchorKind: null,
          link: null,
          state: 'normal',
          isMore: true,
        });
      }
    }
  }

  const effective = applySelection(nodes, edges, selection);
  return { seedKey, nodes, edges, selection: effective, nodeCount: realNodes, edgeCount: realEdges };
}

/** Enabled edges after the two-member collapse, plus the hubs that collapse made redundant. */
function candidateEdgesOf(
  model: GraphModel,
  filters: GraphFilters,
): { candidateEdges: ViewEdge[]; collapsedHubs: Set<NodeKey> } {
  const byHub = new Map<NodeKey, GraphEdge[]>();
  const direct: GraphEdge[] = [];
  for (const edge of model.edges.values()) {
    if (edge.category === 'resLink' && model.nodes.get(edge.source)?.type === HUB_TYPE) {
      (byHub.get(edge.source) ?? byHub.set(edge.source, []).get(edge.source)!).push(edge);
    } else {
      direct.push(edge);
    }
  }

  const collapsedHubs = new Set<NodeKey>();
  const candidateEdges: ViewEdge[] = [];
  for (const edge of direct) {
    if (isEdgeEnabled(filters, edge)) {
      candidateEdges.push(plainEdge(edge));
    }
  }
  for (const [hubKey, members] of byHub) {
    const hub = model.nodes.get(hubKey)!;
    // The seed is always drawn, so a seed that is itself a two-member link stays a node.
    if (members.length === 2 && hubKey !== model.seedKey) {
      collapsedHubs.add(hubKey);
      if (isEdgeEnabled(filters, members[0])) {
        candidateEdges.push(collapsedEdge(hub, members));
      }
    } else {
      for (const edge of members) {
        if (isEdgeEnabled(filters, edge)) {
          candidateEdges.push(plainEdge(edge));
        }
      }
    }
  }
  return { candidateEdges, collapsedHubs };
}

const plainEdge = (e: GraphEdge): ViewEdge => ({
  key: e.key,
  category: e.category,
  relation: e.relation,
  relationName: e.relationName,
  inverseName: e.inverseName,
  directed: e.directed,
  source: e.source,
  target: e.target,
  anchorKind: e.anchorKind,
  link: null,
  state: 'normal',
  isMore: false,
});

/** One edge for a two-member link: from the main member when directed, else in the order the server listed them. */
function collapsedEdge(hub: GraphNode, [a, b]: GraphEdge[]): ViewEdge {
  const forward = a.directed && b.isMain === true && a.isMain !== true ? [b, a] : [a, b];
  const [from, to] = forward;
  return {
    key: hub.key,
    category: a.category,
    relation: a.relation,
    relationName: a.relationName,
    inverseName: a.inverseName,
    directed: a.directed,
    source: from.target,
    target: to.target,
    anchorKind: a.anchorKind ?? b.anchorKind,
    link: { key: hub.key, shortCode: hub.shortCode },
    state: 'normal',
    isMore: false,
  };
}

function applySelection(nodes: ViewNode[], edges: ViewEdge[], selection: GraphSelection): GraphSelection {
  if (selection === null) {
    return null;
  }
  if (selection.kind === 'node') {
    if (!nodes.some((n) => n.key === selection.key && n.kind !== 'more')) {
      return null;
    }
    const near = new Set<NodeKey>();
    for (const edge of edges) {
      if (edge.source === selection.key) {
        near.add(edge.target);
        edge.state = 'neighbour';
      } else if (edge.target === selection.key) {
        near.add(edge.source);
        edge.state = 'neighbour';
      } else {
        edge.state = 'faded';
      }
    }
    for (const node of nodes) {
      node.state = node.key === selection.key ? 'selected' : near.has(node.key) ? 'neighbour' : 'faded';
    }
    return selection;
  }
  const picked = edges.find((e) => e.key === selection.key && !e.isMore);
  if (picked === undefined) {
    return null;
  }
  for (const edge of edges) {
    edge.state = edge === picked ? 'selected' : 'faded';
  }
  for (const node of nodes) {
    node.state = node.key === picked.source || node.key === picked.target ? 'neighbour' : 'faded';
  }
  return selection;
}

export interface Neighbour {
  edge: ViewEdge;
  other: ViewNode;
  /** Which way the relation reads from the asked node: 'inverse' only for a directed edge entered at its target. */
  reading: 'forward' | 'inverse';
}

/** The visible relations of one node, for the list view and the details panel. */
export function neighboursOf(view: GraphView, key: NodeKey): Neighbour[] {
  const byKey = new Map(view.nodes.map((n) => [n.key, n]));
  const found: Neighbour[] = [];
  for (const edge of view.edges) {
    if (edge.isMore) {
      continue;
    }
    const outgoing = edge.source === key;
    if (!outgoing && edge.target !== key) {
      continue;
    }
    const other = byKey.get(outgoing ? edge.target : edge.source);
    if (other !== undefined) {
      found.push({ edge, other, reading: !outgoing && edge.directed ? 'inverse' : 'forward' });
    }
  }
  return found;
}
