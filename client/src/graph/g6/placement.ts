// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Point } from '../renderer.ts';
import type { NodeKey } from '../types.ts';

/**
 * Where nodes that have just joined the picture go, so that the ones already there never move.
 *
 * A full layout re-computes every position, which is what a user does not want when expanding one
 * node: the picture they were reading rearranges under them. Instead each new node is put near a
 * neighbour that is already placed — the node just expanded when it is one of them — on the side
 * facing away from the rest of the graph, so the newcomers fan outwards rather than landing on top
 * of what is there. This module is plain geometry and knows no drawing library.
 */

export interface PlacementInput {
  /** The nodes to place, in a stable order (the order decides who gets which slot). */
  added: readonly NodeKey[];
  /** Every edge of the view, either end may be an added node. */
  edges: readonly { source: NodeKey; target: NodeKey }[];
  /** Nodes that already have a position; these are never returned and never moved. */
  positions: ReadonlyMap<NodeKey, Point>;
  /** The node the user just expanded; a new node connected to it is placed around it. */
  anchorKey?: NodeKey;
  /** Where to start when nothing is placed yet. */
  center: Point;
}

/** Distance from a parent to its first ring of children. */
export const RING_RADIUS = 110;
/** Two nodes closer than this are considered to overlap. */
export const MIN_GAP = 34;

const TAU = Math.PI * 2;

const dist = (a: Point, b: Point): number => Math.hypot(a.x - b.x, a.y - b.y);

function centroid(points: Iterable<Point>, fallback: Point): Point {
  let n = 0;
  let x = 0;
  let y = 0;
  for (const p of points) {
    n += 1;
    x += p.x;
    y += p.y;
  }
  return n === 0 ? fallback : { x: x / n, y: y / n };
}

/** Pushes a candidate outwards from `from` until it stands clear of every placed node (bounded tries). */
function clearOf(candidate: Point, from: Point, placed: Iterable<Point>): Point {
  const points = [...placed];
  let current = candidate;
  const angle = Math.atan2(candidate.y - from.y, candidate.x - from.x);
  for (let attempt = 0; attempt < 6; attempt += 1) {
    if (!points.some((p) => dist(p, current) < MIN_GAP)) {
      return current;
    }
    const radius = Math.hypot(candidate.x - from.x, candidate.y - from.y) + (attempt + 1) * MIN_GAP;
    current = { x: from.x + Math.cos(angle) * radius, y: from.y + Math.sin(angle) * radius };
  }
  return current;
}

/** The ring slots for `count` children of `parent`, fanning away from `heart` (the centre of what is already drawn). */
function fan(parent: Point, heart: Point, count: number): Point[] {
  const away = dist(parent, heart) < 1 ? null : Math.atan2(parent.y - heart.y, parent.x - heart.x);
  // With no "outside" to face (the parent is the middle of the picture) use the whole circle, starting
  // at the top; otherwise a wedge that widens with the number of children, never wider than 3/4 of a turn.
  const span = away === null ? TAU : Math.min(TAU * 0.75, 0.7 * count);
  const start = away === null ? -Math.PI / 2 : away - span / 2;
  const slots: Point[] = [];
  for (let i = 0; i < count; i += 1) {
    const angle = away === null ? start + (TAU * i) / count : start + (span * (i + 0.5)) / count;
    // A crowded ring staggers its radius so neighbouring labels do not sit on one another.
    const radius = RING_RADIUS + (count > 8 && i % 2 === 1 ? MIN_GAP * 1.2 : 0);
    slots.push({ x: parent.x + Math.cos(angle) * radius, y: parent.y + Math.sin(angle) * radius });
  }
  return slots;
}

/** Positions for every key in `added`; nodes with a position already are not in the answer. */
export function placeNewNodes(input: PlacementInput): Map<NodeKey, Point> {
  const placed = new Map(input.positions);
  const result = new Map<NodeKey, Point>();
  const pending = input.added.filter((key) => !placed.has(key));
  const neighbours = new Map<NodeKey, NodeKey[]>();
  for (const edge of input.edges) {
    (neighbours.get(edge.source) ?? neighbours.set(edge.source, []).get(edge.source)!).push(edge.target);
    (neighbours.get(edge.target) ?? neighbours.set(edge.target, []).get(edge.target)!).push(edge.source);
  }
  const heart = centroid(placed.values(), input.center);

  const remaining = new Set(pending);
  while (remaining.size > 0) {
    // Group the pending nodes by the placed neighbour they will hang from; the anchor wins when it is one.
    const groups = new Map<NodeKey, NodeKey[]>();
    for (const key of pending) {
      if (!remaining.has(key)) continue;
      const around = neighbours.get(key) ?? [];
      const parent =
        input.anchorKey !== undefined && around.includes(input.anchorKey) && placed.has(input.anchorKey)
          ? input.anchorKey
          : around.find((n) => placed.has(n));
      if (parent !== undefined) {
        (groups.get(parent) ?? groups.set(parent, []).get(parent)!).push(key);
      }
    }
    if (groups.size === 0) break;
    for (const [parentKey, children] of groups) {
      const parent = placed.get(parentKey)!;
      const slots = fan(parent, heart, children.length);
      children.forEach((child, i) => {
        const at = clearOf(slots[i], parent, placed.values());
        placed.set(child, at);
        result.set(child, at);
        remaining.delete(child);
      });
    }
  }

  // Nodes no placed node leads to (a filter just revealed an island, say): a ring round the centre of the picture.
  const strays = pending.filter((key) => remaining.has(key));
  strays.forEach((key, i) => {
    const angle = -Math.PI / 2 + (TAU * i) / Math.max(strays.length, 1);
    const radius = RING_RADIUS * 2 + (i % 2) * MIN_GAP;
    const at = clearOf({ x: heart.x + Math.cos(angle) * radius, y: heart.y + Math.sin(angle) * radius }, heart, placed.values());
    placed.set(key, at);
    result.set(key, at);
  });
  return result;
}
