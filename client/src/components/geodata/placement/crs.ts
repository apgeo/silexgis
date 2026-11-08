// SPDX-License-Identifier: AGPL-3.0-or-later
import { transform } from 'ol/proj';
import { isWktCrs, type PointPair } from '../../../geodata/placement/placement.ts';
import { canPlace, resolveProjection } from '../../../map/projections.ts';

/*
 * Conversions between longitude and latitude and the systems control points are typed in. Which
 * systems are offered is the server's answer; their definitions come from this installation's own
 * registry the first time one is used.
 */

/** Longitude and latitude themselves, which need no conversion at all. */
export const inDegrees = (crs: string) => crs === 'EPSG:4326';

/** Makes a system usable for conversion; false when the installation cannot define it. */
export async function prepareCrs(crs: string): Promise<boolean> {
  if (inDegrees(crs)) {
    return true;
  }
  return isWktCrs(crs) ? false : resolveProjection(crs);
}

/** A point in a system as longitude and latitude, or null where it cannot be converted. */
export function toLonLat(x: number, y: number, crs: string): [number, number] | null {
  if (inDegrees(crs)) {
    return [x, y];
  }
  if (isWktCrs(crs) || !canPlace(crs)) {
    return null;
  }
  const [lon, lat] = transform([x, y], crs, 'EPSG:4326');
  return Number.isFinite(lon) && Number.isFinite(lat) ? [lon, lat] : null;
}

/** Longitude and latitude in a system, or null where it cannot be converted. */
export function fromLonLat(lon: number, lat: number, crs: string): [number, number] | null {
  if (inDegrees(crs)) {
    return [lon, lat];
  }
  if (isWktCrs(crs) || !canPlace(crs)) {
    return null;
  }
  const [x, y] = transform([lon, lat], 'EPSG:4326', crs);
  return Number.isFinite(x) && Number.isFinite(y) ? [x, y] : null;
}

/** The ground halves of every pair moved from one system to another; a point that cannot be moved is emptied. */
export function convertPairs(pairs: readonly PointPair[], from: string, to: string): PointPair[] {
  return pairs.map((pair) => {
    if (pair.x === null || pair.y === null) {
      return pair;
    }
    const lonLat = toLonLat(pair.x, pair.y, from);
    const next = lonLat ? fromLonLat(lonLat[0], lonLat[1], to) : null;
    return { ...pair, x: next ? next[0] : null, y: next ? next[1] : null };
  });
}
