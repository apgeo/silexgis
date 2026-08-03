// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DBounds, Scene3DPosition } from './scene3dEngine.ts';

// Reading the map endpoints' GeoJSON into scene positions.
//
// The flat map hands whole responses to its rendering library's own reader; the scene has no such
// reader, and hand-rolling one is the point rather than a shortcut — the alternative builds one
// scene object per line component of every multi-part geometry, which for a survey that arrives
// as tens of thousands of one-shot components is tens of thousands of objects where a single
// batched collection was wanted.
//
// Everything here tolerates shapes the endpoints can genuinely produce and refuses to guess about
// anything else:
//
//   * A geometry may be absent. A protected feature the viewer may not locate exactly is served
//     as a readable row with no geometry at all, which is a row to skip, not an error.
//   * A multi-part geometry may arrive as its single-part form. Clipping a survey to the viewport
//     leaves whatever survived, and one surviving component comes back as a plain line rather than
//     as a collection of one.
//   * A position may or may not carry a third ordinate, within one response. Altitudes are opted
//     into per request and answered per row, so a response can mix surveyed depths with rows that
//     could only be served flat.
//
// The generated contract types the properties bag as empty and the coordinates as an opaque JSON
// value, because the server declares them as free-form dictionaries. Reading them therefore goes
// through the narrowing below rather than through a cast at each call site.

/** As much of a GeoJSON feature as any of the map endpoints guarantees. */
export interface GeoJsonFeatureLike {
  geometry?: { type?: string; coordinates?: unknown } | null;
  properties?: unknown;
}

/** The features of a collection, or nothing if it is not shaped like one. */
export function featuresOf(collection: unknown): GeoJsonFeatureLike[] {
  const features = (collection as { features?: unknown } | null | undefined)?.features;
  return Array.isArray(features) ? (features as GeoJsonFeatureLike[]) : [];
}

/** A feature's properties as a plain bag; an absent or non-object one reads as empty. */
export function propertiesOf(feature: GeoJsonFeatureLike): Record<string, unknown> {
  const { properties } = feature;
  return typeof properties === 'object' && properties !== null
    ? (properties as Record<string, unknown>)
    : {};
}

/** A property that must be a non-empty string to be usable as an identifier. */
export function stringProperty(
  properties: Record<string, unknown>,
  name: string,
): string | undefined {
  const value = properties[name];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/**
 * Every point a feature stands at: one for a plain point, several for a multi-point (which is how
 * an imported multi-part geodata row arrives, and the flat map draws its symbol at each of them).
 */
export function pointPositions(feature: GeoJsonFeatureLike): Scene3DPosition[] {
  const geometry = feature.geometry;
  if (!geometry) {
    return [];
  }
  if (geometry.type === 'Point') {
    const position = toPosition(geometry.coordinates);
    return position ? [position] : [];
  }
  if (geometry.type === 'MultiPoint') {
    return positionList(geometry.coordinates);
  }
  return [];
}

/**
 * Every line a feature draws as: the components of a line or multi-line, and the rings of a
 * polygon or multi-polygon. A polygon has no fill in the scene, so its rings are its outline.
 * Components with fewer than two usable positions are dropped — there is no line through one
 * point, and a survey export can carry a shot whose two stations coincide.
 */
export function lineStrings(feature: GeoJsonFeatureLike): Scene3DPosition[][] {
  const geometry = feature.geometry;
  if (!geometry) {
    return [];
  }
  const coordinates = geometry.coordinates;
  switch (geometry.type) {
    case 'LineString':
      return drawable([positionList(coordinates)]);
    case 'MultiLineString':
    case 'Polygon':
      return drawable(nestedList(coordinates).map(positionList));
    case 'MultiPolygon':
      return drawable(nestedList(coordinates).flatMap((polygon) => nestedList(polygon).map(positionList)));
    default:
      return [];
  }
}

/**
 * The longitude/latitude box a bare GeoJSON geometry occupies, or undefined when it has no
 * positions this reader understands — an absent geometry, or a shape it does not read.
 *
 * Altitude is deliberately not part of it. This exists so a "show me this feature" action can
 * frame the geometry, and a camera is framed by the ground it has to cover; a metre-scale vertical
 * extent would tell it nothing it can use.
 */
export function geoJsonBounds(geometry: unknown): Scene3DBounds | undefined {
  const feature: GeoJsonFeatureLike = {
    geometry: (geometry ?? undefined) as GeoJsonFeatureLike['geometry'],
  };
  const positions = [...pointPositions(feature), ...lineStrings(feature).flat()];
  if (positions.length === 0) {
    return undefined;
  }
  let west = Number.POSITIVE_INFINITY;
  let south = Number.POSITIVE_INFINITY;
  let east = Number.NEGATIVE_INFINITY;
  let north = Number.NEGATIVE_INFINITY;
  for (const position of positions) {
    west = Math.min(west, position.longitude);
    east = Math.max(east, position.longitude);
    south = Math.min(south, position.latitude);
    north = Math.max(north, position.latitude);
  }
  return [west, south, east, north];
}

/**
 * One position. The third ordinate, when there is one, is the surveyed altitude in metres.
 *
 * They are passed through exactly as they arrive. They are heights above the sea-level datum the
 * survey was recorded against, which is neither the globe's ellipsoid nor any surface drawn in
 * the scene, so they are not usable as they stand — but reconciling them is the business of
 * whichever overlay knows what surface its geometry is being drawn against, and doing it here
 * would put that decision in a shared reader that has no idea which overlay it is serving.
 *
 * A position with no altitude is placed at zero, which is the globe's own surface. That is the
 * honest rendering of "this row could not be served with depths" and the response says how many
 * rows it applies to, so the difference can be reported rather than silently drawn as sea level.
 */
export function toPosition(coordinates: unknown): Scene3DPosition | undefined {
  if (!Array.isArray(coordinates) || coordinates.length < 2) {
    return undefined;
  }
  const [longitude, latitude, height] = coordinates as unknown[];
  if (!isFiniteNumber(longitude) || !isFiniteNumber(latitude)) {
    return undefined;
  }
  return { longitude, latitude, height: isFiniteNumber(height) ? height : 0 };
}

function positionList(coordinates: unknown): Scene3DPosition[] {
  if (!Array.isArray(coordinates)) {
    return [];
  }
  const positions: Scene3DPosition[] = [];
  for (const entry of coordinates) {
    const position = toPosition(entry);
    if (position) {
      positions.push(position);
    }
  }
  return positions;
}

function nestedList(coordinates: unknown): unknown[] {
  return Array.isArray(coordinates) ? coordinates : [];
}

function drawable(lines: Scene3DPosition[][]): Scene3DPosition[][] {
  return lines.filter((line) => line.length >= 2);
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}
