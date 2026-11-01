// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphCategoryCountWire, GraphEdgeWire, GraphNeighbourhood, GraphNodeWire } from '../api/hooks/types.ts';

/**
 * Invented neighbourhood answers for the graph's unit tests: small, readable, and shaped exactly like
 * the server's (a hub carries a short code and a member count and no title; a placeholder has no id
 * and no title and is keyed by its member row). Never built from a database.
 */

export function wireNode(type: string, id: string, title: string | null, extra: Partial<GraphNodeWire> = {}): GraphNodeWire {
  return {
    key: `${type}:${id}`,
    type,
    id,
    title,
    subtitle: null,
    kind: null,
    restricted: false,
    expandable: type !== 'resLink' && type !== 'file',
    route: null,
    shortCode: null,
    memberCount: null,
    ...extra,
  };
}

export const wireHub = (id: string, members: number): GraphNodeWire =>
  wireNode('resLink', id, null, { shortCode: `L${id}`, memberCount: members, expandable: false });

export const wirePlaceholder = (memberId: string, type = 'feature'): GraphNodeWire => ({
  key: `restricted:${memberId}`,
  type,
  id: null,
  title: null,
  subtitle: null,
  kind: null,
  restricted: true,
  expandable: false,
  route: null,
  shortCode: null,
  memberCount: null,
});

export function wireEdge(
  category: string,
  relation: string,
  source: string,
  target: string,
  extra: Partial<GraphEdgeWire> = {},
): GraphEdgeWire {
  return {
    key: `${category}:${relation}:${source}>${target}`,
    category,
    relation,
    relationName: null,
    inverseName: null,
    directed: true,
    isMain: null,
    source,
    target,
    anchorKind: null,
    ...extra,
  };
}

/** One resource link as the server sends it: a hub, and an edge from the hub to each member. */
export function wireLink(
  linkId: string,
  relation: string,
  directed: boolean,
  members: { node: GraphNodeWire; isMain?: boolean }[],
): { nodes: GraphNodeWire[]; edges: GraphEdgeWire[] } {
  const hub = wireHub(linkId, members.length);
  return {
    nodes: [hub, ...members.map((m) => m.node)],
    edges: members.map((m) =>
      wireEdge('resLink', relation, hub.key, m.node.key, {
        key: `resLink:${linkId}:${m.node.key}`,
        directed,
        isMain: m.isMain ?? false,
      }),
    ),
  };
}

export function wireAnswer(
  seed: GraphNodeWire,
  parts: { nodes?: GraphNodeWire[]; edges?: GraphEdgeWire[] }[],
  counts: GraphCategoryCountWire[],
): GraphNeighbourhood {
  const nodes = new Map<string, GraphNodeWire>([[seed.key, seed]]);
  for (const part of parts) {
    for (const node of part.nodes ?? []) {
      nodes.set(node.key, node);
    }
  }
  return { seed, nodes: [...nodes.values()], edges: parts.flatMap((p) => p.edges ?? []), counts };
}

export const count = (category: string, shown: number, total = shown): GraphCategoryCountWire => ({ category, shown, total });
