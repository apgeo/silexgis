// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SelectorScope } from './selection.ts';
import type { FilterAnchor, FilterDocument, FilterNode, FilterValue, SpatialAnchorValue } from './types.ts';

/**
 * Spatial conditions as the editor writes them: a radius and an anchor for `within`, one outline
 * for `inside`, and the anchor nearest-first is measured from.
 *
 * The limits mirror the server's so a value the server would refuse is caught while it is being
 * typed; the server still decides, and its refusal is what a row shows when the two disagree.
 * Whether a row's position may be used at all — an object the reader may not place exactly, a
 * saved filter they cannot read — only the server can say, and it says so in one refusal for all
 * of them.
 */

/** The smallest radius a condition may ask about, in metres. */
export const MIN_RADIUS_METERS = 1;

/**
 * The largest radius the server ships with, in metres — used only until the vocabulary arrives.
 * The installation can set another, and publishes it as `limits.maxRadiusMeters`.
 */
export const DEFAULT_MAX_RADIUS_METERS = 200_000;

/** Positions over all rings of one outline, and rings in it. */
export const MAX_POLYGON_POSITIONS = 2_000;
export const MAX_POLYGON_RINGS = 50;

/** The kinds of anchor a `within` row can measure from, in the order the editor offers them. */
export type WithinAnchorKind = 'point' | 'object' | 'savedFilter';

/** The kinds of outline an `inside` row can test against. */
export type InsideOutlineKind = 'area' | 'polygon';

export function isSpatialAnchor(value: FilterValue | undefined): value is SpatialAnchorValue {
  return value !== undefined
    && (value.type === 'point' || value.type === 'polygon' || value.type === 'object' || value.type === 'savedFilter');
}

/** A radius the server would accept, under the installation's ceiling. */
export function isRadius(value: number | null | undefined, max: number = DEFAULT_MAX_RADIUS_METERS): value is number {
  return typeof value === 'number' && Number.isFinite(value)
    && value >= MIN_RADIUS_METERS && value <= max;
}

/** A longitude and a latitude on the globe. */
export function isOnGlobe(longitude: number | null | undefined, latitude: number | null | undefined): boolean {
  return typeof longitude === 'number' && typeof latitude === 'number'
    && Number.isFinite(longitude) && Number.isFinite(latitude)
    && longitude >= -180 && longitude <= 180 && latitude >= -90 && latitude <= 90;
}

/**
 * Whether a spatial row holds everything its operator needs. A radius not yet typed is held as an
 * empty number in its place and an anchor not yet chosen is simply absent, so the count alone does
 * not say the row is finished. A radius past the ceiling is written, not unfinished: the server
 * refuses it and names its limit on the row.
 */
export function isSpatialComplete(op: string, values: FilterValue[]): boolean {
  if (op === 'within') {
    const [radius, anchor] = values;
    return values.length === 2
      && radius?.type === 'number' && isRadius(radius.value, Number.POSITIVE_INFINITY)
      && (anchor?.type === 'point' || anchor?.type === 'object' || anchor?.type === 'savedFilter');
  }
  if (op === 'inside') {
    const [outline] = values;
    return values.length === 1 && (outline?.type === 'polygon' || outline?.type === 'object');
  }
  return false;
}

/**
 * The outline in a pasted GeoJSON text, as a polygon value's rings — or null with the reason it
 * could not be read. Takes a Polygon geometry, a Feature holding one, or a FeatureCollection of
 * exactly one such Feature: what a drawing tool or a GIS export of one shape gives. Anything that
 * would need a decision about which shape was meant — several features, a multipolygon — is
 * refused rather than guessed at.
 */
export function parsePolygon(text: string): { coordinates: number[][][] } | { problem: PolygonProblem } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { problem: 'notJson' };
  }

  const geometry = polygonGeometry(parsed);
  if (geometry === null) {
    return { problem: 'notPolygon' };
  }

  const rings = geometry.coordinates;
  if (!Array.isArray(rings) || rings.length === 0 || rings.length > MAX_POLYGON_RINGS) {
    return { problem: 'notPolygon' };
  }

  let positions = 0;
  const out: number[][][] = [];
  for (const ring of rings) {
    if (!Array.isArray(ring) || ring.length < 4) {
      return { problem: 'notPolygon' };
    }
    positions += ring.length;
    const read: number[][] = [];
    for (const position of ring) {
      if (!Array.isArray(position) || position.length < 2 || !isOnGlobe(position[0], position[1])) {
        return { problem: 'offGlobe' };
      }
      // A third ordinate (a height) is dropped: an outline here is drawn on the ground.
      read.push([position[0] as number, position[1] as number]);
    }
    const first = read[0];
    const last = read[read.length - 1];
    if (first[0] !== last[0] || first[1] !== last[1]) {
      return { problem: 'notClosed' };
    }
    out.push(read);
  }
  if (positions > MAX_POLYGON_POSITIONS) {
    return { problem: 'tooLarge' };
  }
  return { coordinates: out };
}

export type PolygonProblem = 'notJson' | 'notPolygon' | 'offGlobe' | 'notClosed' | 'tooLarge';

function polygonGeometry(value: unknown): { coordinates: unknown } | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const v = value as { type?: unknown; coordinates?: unknown; geometry?: unknown; features?: unknown };
  if (v.type === 'Polygon') {
    return { coordinates: v.coordinates };
  }
  if (v.type === 'Feature') {
    return polygonGeometry(v.geometry);
  }
  if (v.type === 'FeatureCollection' && Array.isArray(v.features) && v.features.length === 1) {
    return polygonGeometry(v.features[0]);
  }
  return null;
}

/** A polygon value written back as the GeoJSON geometry it was read from, for showing it again. */
export function polygonText(coordinates: number[][][]): string {
  return JSON.stringify({ type: 'Polygon', coordinates });
}

/**
 * Whether the document carries a position of its own anywhere — a point or an outline in a
 * condition, or an anchor for nearest-first. Such a document can be run and put in a link, but not
 * saved: a stored position is a statement about a place that outlives the question and is handed
 * to everyone the filter is shared with. The server refuses it; this lets a control say so before
 * asking.
 */
export function holdsPosition(document: FilterDocument): boolean {
  return document.anchor != null
    || document.scope.some((s) => s.where !== null && anyValue(s.where, (v) => v.type === 'point' || v.type === 'polygon'));
}

/** The document as it may be stored: the request's own anchor taken off. Conditions are untouched. */
export function withoutAnchor(document: FilterDocument): FilterDocument {
  if (document.anchor === undefined) {
    return document;
  }
  const { anchor: _anchor, ...rest } = document;
  return rest;
}

export function withAnchor(document: FilterDocument, anchor: FilterAnchor | null): FilterDocument {
  return anchor === null ? withoutAnchor(document) : { ...document, anchor };
}

/** Whether any condition under this node measures from something the server has to look up. */
export function namesAnchor(node: FilterNode): boolean {
  return anyValue(node, (v) => v.type === 'object' || v.type === 'savedFilter');
}

function anyValue(node: FilterNode, test: (value: FilterValue) => boolean): boolean {
  switch (node.node) {
    case 'condition':
      return node.values.some(test);
    case 'not':
      return anyValue(node.of, test);
    default:
      return node.of.some((child) => anyValue(child, test));
  }
}

/**
 * The one world an object anchor or an area is picked from, whichever world the row filters: a
 * feature is the only row whose position the server's placement gate decides. A photograph within
 * 500 m of a cave is measured from the cave.
 */
export const ANCHOR_WORLD = 'feature';

/** What an object anchor or an area is picked from — always features. */
export function anchorScopes(purpose: 'object' | 'area'): SelectorScope[] {
  const world = ANCHOR_WORLD;
  if (purpose === 'area') {
    return [
      { id: 'areas', labelKey: 'filters.editor.spatial.areas', world, where: { field: 'category', value: 'area' } },
      { id: 'features', labelKey: 'filters.scopes.features', world, where: null },
    ];
  }
  return [
    { id: 'caves', labelKey: 'filters.scopes.caves', world, where: { field: 'kind', value: 'cave' } },
    { id: 'entrances', labelKey: 'filters.scopes.entrances', world, where: { field: 'kind', value: 'caveEntrance' } },
    { id: 'features', labelKey: 'filters.scopes.features', world, where: null },
  ];
}
