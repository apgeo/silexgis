// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TripLogInfo } from '../../api/hooks.ts';

/** A trip's sketch, said in words and numbers rather than drawn. */
export interface GeometrySummary {
  /** The stored GeoJSON type — Point, LineString, Polygon and their Multi- forms. */
  type: string;
  /** How many positions the shape is made of. */
  positions: number;
  /** Longitude then latitude, the mean of every position in the shape. */
  center: [number, number];
}

/**
 * Every [longitude, latitude] pair inside an arbitrarily nested GeoJSON coordinates value.
 *
 * A position is recognised by being an array whose first two entries are numbers, which is what
 * separates it from the rings and parts wrapped around it — the nesting depth differs per
 * geometry type and there is nothing else in the value to tell them apart. A third entry
 * (altitude) is allowed and ignored: the sketch is drawn on a map and says nothing about depth.
 */
function collect(value: unknown, into: [number, number][]): void {
  if (!Array.isArray(value)) {
    return;
  }
  if (typeof value[0] === 'number' && typeof value[1] === 'number') {
    into.push([value[0], value[1]]);
    return;
  }
  for (const entry of value) {
    collect(entry, into);
  }
}

/**
 * What a trip's sketch amounts to, for a reader who has no map in front of them.
 *
 * This exists for paper. A map is drawn on a canvas while somebody looks at it, and a printer is
 * handed the page rather than the canvas, so a printed report that relied on the map would carry
 * at best whatever happened to have been rendered and at worst a blank rectangle. Writing the
 * shape down instead discloses nothing the screen did not: a trip's own geometry is exact for
 * everyone who may read the trip, unlike a cave's, which is why it may be printed as it stands.
 *
 * Returns null for a shape with no positions at all, which the caller draws as no sketch rather
 * than as an empty one.
 */
export function tripGeometrySummary(
  geom: TripLogInfo['geom'] | null | undefined,
): GeometrySummary | null {
  if (!geom || typeof geom.type !== 'string') {
    return null;
  }
  const found: [number, number][] = [];
  collect(geom.coordinates, found);
  if (found.length === 0) {
    return null;
  }
  return {
    type: geom.type,
    positions: found.length,
    center: [
      found.reduce((sum, position) => sum + position[0], 0) / found.length,
      found.reduce((sum, position) => sum + position[1], 0) / found.length,
    ],
  };
}

/**
 * The wording key for a stored GeoJSON type, in the vocabulary the sketch editor already offers —
 * a reader who drew a point should read "point" and not "MultiPoint". A type this does not know
 * has no wording of its own and is shown as it is stored.
 */
export function shapeLabelKey(type: string): string | null {
  switch (type.replace(/^Multi/, '')) {
    case 'Point':
      return 'trips.drawPoint';
    case 'LineString':
      return 'trips.drawLine';
    case 'Polygon':
      return 'trips.drawArea';
    default:
      return null;
  }
}

/** The four bearings, worded by whoever is reading — east is not "E" in every language. */
export interface Cardinals {
  north: string;
  south: string;
  east: string;
  west: string;
}

/**
 * A position as a printed report writes it: five decimal places, which is about a metre and is
 * as much as a sketch drawn with a mouse ever claims.
 */
export function formatPosition([lon, lat]: [number, number], cardinals: Cardinals): string {
  const ns = lat >= 0 ? cardinals.north : cardinals.south;
  const ew = lon >= 0 ? cardinals.east : cardinals.west;
  return `${Math.abs(lat).toFixed(5)}° ${ns}, ${Math.abs(lon).toFixed(5)}° ${ew}`;
}
