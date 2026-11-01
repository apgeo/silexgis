// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphCategoryCountWire, GraphEdgeWire, GraphNodeWire } from '../api/hooks/types.ts';

/** A node's identity: "<type>:<id>" for a readable item, "restricted:<memberId>" for a placeholder. */
export type NodeKey = string;

/**
 * The server sends types and categories as plain text and may grow new ones before the client
 * learns of them. The closed lists below are what the client draws specially; anything else is
 * carried as text and drawn with a generic look, so a newer server never breaks an older page.
 */
export const GRAPH_NODE_TYPES = [
  'feature',
  'document',
  'tripLog',
  'caver',
  'cavingGroup',
  'mapView',
  'surveyModel',
  'geofile',
  'cabinet',
  'expedition',
  'tag',
  'resLink',
  'file',
] as const;
export type GraphNodeType = (typeof GRAPH_NODE_TYPES)[number];

export const GRAPH_CATEGORIES = [
  'resLink',
  'featureLink',
  'hierarchy',
  'attachment',
  'tripParticipant',
  'expedition',
  'cabinet',
  'tag',
  'groupMembership',
  'survey',
] as const;
export type GraphCategory = (typeof GRAPH_CATEGORIES)[number];

/** The seed kinds the server accepts: everything but a bare stored file. */
export const GRAPH_SEED_TYPES: readonly GraphNodeType[] = GRAPH_NODE_TYPES.filter((type) => type !== 'file');

export const isKnownNodeType = (type: string): type is GraphNodeType =>
  (GRAPH_NODE_TYPES as readonly string[]).includes(type);

export const isKnownCategory = (category: string): category is GraphCategory =>
  (GRAPH_CATEGORIES as readonly string[]).includes(category);

/** A node as the model keeps it: the wire shape with the type and ids left as the server wrote them. */
export interface GraphNode {
  key: NodeKey;
  type: string;
  /** Null for a restricted placeholder, which names no item. */
  id: string | null;
  title: string | null;
  subtitle: string | null;
  kind: string | null;
  restricted: boolean;
  expandable: boolean;
  route: string | null;
  /** Set on a resource-link hub only. */
  shortCode: string | null;
  /** Set on a resource-link hub only: how many members the link has for this viewer. */
  memberCount: number | null;
}

export interface GraphEdge {
  key: string;
  category: string;
  /** The relation's code; for a resource link the code of its relation type ("unspecified" when none). */
  relation: string;
  /** Spelled-out names, present for a custom relation type only. */
  relationName: string | null;
  inverseName: string | null;
  directed: boolean;
  /** Resource-link member edges only: whether this member is the link's main one. */
  isMain: boolean | null;
  source: NodeKey;
  target: NodeKey;
  anchorKind: string | null;
}

export interface CategoryCount {
  category: string;
  shown: number;
  total: number;
}

export const nodeFromWire = (n: GraphNodeWire): GraphNode => ({
  key: n.key,
  type: n.type,
  id: n.id,
  title: n.title,
  subtitle: n.subtitle,
  kind: n.kind,
  restricted: n.restricted,
  expandable: n.expandable,
  route: n.route,
  shortCode: n.shortCode,
  memberCount: n.memberCount,
});

export const edgeFromWire = (e: GraphEdgeWire): GraphEdge => ({
  key: e.key,
  category: e.category,
  relation: e.relation,
  relationName: e.relationName,
  inverseName: e.inverseName,
  directed: e.directed,
  isMain: e.isMain,
  source: e.source,
  target: e.target,
  anchorKind: e.anchorKind,
});

export const countFromWire = (c: GraphCategoryCountWire): CategoryCount => ({
  category: c.category,
  shown: c.shown,
  total: c.total,
});

/** What one request to the neighbourhood route asks: the whole first hop, or one category's next page. */
export interface NeighbourhoodRequest {
  type: string;
  id: string;
  category?: string;
  offset?: number;
}

/** The request an answer was made for; the model needs it to read `counts` of a paged answer. */
export type AnswerRequest = Pick<NeighbourhoodRequest, 'category' | 'offset'>;

/** The state a renderer paints an element in. The model decides it; a renderer never infers it. */
export type HighlightState = 'selected' | 'neighbour' | 'faded' | 'normal';

/** What the user has picked: a node, an edge, or nothing. */
export type GraphSelection = { kind: 'node'; key: NodeKey } | { kind: 'edge'; key: string } | null;

/** Which relation categories, relation codes and node types are switched off. Everything starts on. */
export interface GraphFilters {
  disabledCategories: ReadonlySet<string>;
  /** Entries are `relationFilterKey(category, relation)`. */
  disabledRelations: ReadonlySet<string>;
  disabledTypes: ReadonlySet<string>;
}
