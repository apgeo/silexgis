// SPDX-License-Identifier: AGPL-3.0-or-later
import type { WorkArea } from '../api/hooks.ts';

/**
 * Levelling, colouring and extents for work areas.
 *
 * Free of OpenLayers and of React on purpose: the overview map, the dashboard board and the link
 * that zooms the main map to an area all need the same answers, and only one of the three has a
 * map to ask. Kept pure, the rules can be asserted directly rather than through a rendered canvas.
 */

/**
 * The areas at the top of the tree.
 *
 * An area whose parent is not in the set counts as top-level, and that is the point rather than an
 * oversight: the server states a parent only when the caller may read it too, so an area nested
 * under something they cannot see must still appear somewhere. Dropped instead, it would vanish
 * from the overview entirely — visible to the server, invisible to its reader.
 */
export function topLevel(areas: readonly WorkArea[]): WorkArea[] {
  const present = new Set(areas.map((a) => a.id));
  return areas.filter((a) => a.parentId === null || !present.has(a.parentId));
}

/** The areas directly inside one, which is the next level down to offer. */
export function childrenOf(areas: readonly WorkArea[], parentId: string): WorkArea[] {
  return areas.filter((a) => a.parentId === parentId);
}

/**
 * The chain from the top down to one area, itself last — what a reader has opened, in order.
 *
 * Walks upward and stops on a repeat, so a cycle in the hierarchy returns a short path rather than
 * hanging the page. The write service refuses cycles and the integrity verifier re-checks them, so
 * this is not expected to fire; it is here because the alternative failure is a frozen browser.
 */
export function pathTo(areas: readonly WorkArea[], id: string): WorkArea[] {
  const byId = new Map(areas.map((a) => [a.id, a]));
  const path: WorkArea[] = [];
  const seen = new Set<string>();
  let current = byId.get(id);
  while (current && !seen.has(current.id)) {
    seen.add(current.id);
    path.unshift(current);
    current = current.parentId === null ? undefined : byId.get(current.parentId);
  }
  return path;
}

/**
 * The colours areas are told apart by on the overview.
 *
 * Chosen to stay distinguishable from each other rather than to match the data overlays: these
 * fills sit under everything else on that view and are read as "which area am I looking at", not
 * as "what kind of thing is this". Assigned by position in the level being drawn, so the same
 * area keeps its colour for as long as the level does.
 */
export const workAreaPalette = [
  '#146262', '#8c4a2f', '#4b5d8c', '#7a5c1e', '#5c3a6e',
  '#2f6b3d', '#8c2f4a', '#2f6b7a', '#6e5c2f', '#4a2f6b',
] as const;

export function colourFor(index: number): string {
  return workAreaPalette[index % workAreaPalette.length];
}

/** West, south, east, north — the convention the rest of this application writes a bbox in. */
export type Extent = [number, number, number, number];

/**
 * The bounding box of a GeoJSON geometry, or null when it has no coordinates to bound.
 *
 * Walks the coordinate nesting rather than switching on the geometry type: a work area is drawn as
 * a polygon today and the server accepts multipolygons, and the recursion covers both — and every
 * other shape — without a case that has to be remembered when one is added.
 */
export function extentOf(geometry: WorkArea['geometry']): Extent | null {
  if (!geometry) {
    return null;
  }
  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;

  const visit = (node: unknown): void => {
    if (!Array.isArray(node)) {
      return;
    }
    // A coordinate is a pair of numbers; anything else is a list of coordinates or of rings.
    if (typeof node[0] === 'number' && typeof node[1] === 'number') {
      const [lon, lat] = node as number[];
      west = Math.min(west, lon);
      east = Math.max(east, lon);
      south = Math.min(south, lat);
      north = Math.max(north, lat);
      return;
    }
    for (const child of node) {
      visit(child);
    }
  };
  visit(geometry.coordinates as unknown);

  return Number.isFinite(west) && Number.isFinite(south) ? [west, south, east, north] : null;
}

/** The extent covering several areas, for framing a whole level at once. */
export function extentOfAll(areas: readonly WorkArea[]): Extent | null {
  const boxes = areas.map((a) => extentOf(a.geometry)).filter((b): b is Extent => b !== null);
  if (boxes.length === 0) {
    return null;
  }
  return [
    Math.min(...boxes.map((b) => b[0])),
    Math.min(...boxes.map((b) => b[1])),
    Math.max(...boxes.map((b) => b[2])),
    Math.max(...boxes.map((b) => b[3])),
  ];
}
