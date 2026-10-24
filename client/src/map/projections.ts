// SPDX-License-Identifier: AGPL-3.0-or-later
import proj4 from 'proj4';
import { get as getProjection, getTransformFromProjections } from 'ol/proj';
import { register } from 'ol/proj/proj4';
import { userManager } from '../auth/auth.tsx';

/*
 * The coordinate systems the map can place a raster in, beyond the two OpenLayers knows itself
 * (web mercator and plain longitude and latitude).
 *
 * A raster in any other system — a Romanian scan in Stereo 70 is the usual one — is drawn by
 * OpenLayers only once that system's definition is registered with it: without one it has no way
 * to turn the file's coordinates into the map's, and the picture cannot be placed at all. Two
 * routes bring a definition here, and neither asks anybody outside the installation:
 *
 * - a catalogue GeoTIFF carries its definition with the layer, resolved by the server from its
 *   own coordinate-system registry (`registerProjection`);
 * - an uploaded georeferenced map names its system only inside the file, so its code is looked up
 *   in this installation's registry once the file has been opened (`resolveProjection`).
 *   OpenLayers' own lookup is not used: it asks a public web service, which tells a third party
 *   what is being looked at and fails on an installation with no route out, and a lookup that
 *   fails inside it surfaces as an unhandled error rather than as a map that cannot be placed.
 */

const MAP_PROJECTION = 'EPSG:3857';

/** Registers a definition for a code, so OpenLayers can transform between it and the map's system. */
export function registerProjection(code: string, definition: string): void {
  if (!proj4.defs(code)) {
    proj4.defs(code, definition);
  }
  register(proj4);
}

/** Whether OpenLayers can already turn a system's coordinates into the map's. */
export function canPlace(code: string): boolean {
  // Asked by projection rather than by code: OpenLayers' lookup by code fails outright for a code
  // it has never been given.
  const projection = getProjection(code);
  return projection !== null && getTransformFromProjections(projection, getProjection(MAP_PROJECTION)!) !== null;
}

/**
 * Makes a system the map does not know yet placeable, from this installation's registry: true when
 * it can be placed now, false when the code is not an EPSG code or the registry has no definition
 * for it. Never rejects.
 */
export async function resolveProjection(
  code: string,
  lookup: (code: string) => Promise<string | null> = installationDefinition,
): Promise<boolean> {
  if (canPlace(code)) {
    return true;
  }
  const definition = await lookup(code);
  if (!definition) {
    return false;
  }
  try {
    registerProjection(code, definition);
  } catch {
    return false;
  }
  return canPlace(code);
}

/**
 * The definition of a code such as `EPSG:3844` from this installation's registry, with the
 * signed-in reader's credential; null when it is not an EPSG code, the registry does not know it,
 * or the request fails.
 */
export async function installationDefinition(code: string): Promise<string | null> {
  const match = /^EPSG:(\d+)$/i.exec(code.trim());
  if (!match) {
    return null;
  }
  try {
    const user = await userManager.getUser();
    const response = await fetch(`/api/v1/crs/${match[1]}.proj4`, {
      headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
    });
    return response.ok ? await response.text() : null;
  } catch {
    return null;
  }
}
