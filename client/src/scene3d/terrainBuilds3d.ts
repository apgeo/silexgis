// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapConfig } from '../api/hooks.ts';
import type { Scene3DBounds, Scene3DPosition } from './scene3dEngine.ts';

// Which of an installation's baked elevation models the scene draws, and which of them covers a
// given place.
//
// An installation may hold several builds at once: a coarse one over the whole region and a finer
// one over the massif a group actually works in, baked later and to a deeper level. The server
// names one of them as the default and the scene draws that; everything here is about letting a
// viewer draw a different one on purpose, and about noticing when the camera has entered a finer
// build's area so the viewer can be told. Told, never switched for: swapping the ground discards
// every surface tile the globe holds and moves the vertical datum under every survey, which is
// not something to do to a viewer mid-pan without asking.
//
// Engine-free and React-free, so every decision is arithmetic over plain objects.

/** One build the server offers for the scene to draw, as the map configuration lists it. */
export type TerrainBuildChoice = MapConfig['terrainBuilds'][number];

/** One elevation source as the server describes it: the configured address or a build's own. */
export type TerrainSourceInfo = NonNullable<MapConfig['terrain']>;

/** One elevation source the scene may be asked to draw, whichever list it came from. */
export interface TerrainCandidate3D {
  url: string;
  attribution?: string;
  /** What to add to a surveyed altitude for it to meet this source's ground. */
  surveyHeightOffsetM: number;
  /** The checked build this source is, when it is one; null for an address configured by hand. */
  buildId: string | null;
}

/** The part of the map configuration the ground is decided from. */
export type TerrainConfig = Pick<MapConfig, 'terrain' | 'terrainFallback' | 'terrainBuilds'>;

/**
 * Which sources to try, in order, given what the server offers and what the viewer chose.
 *
 * Without a choice this is the server's own preference: what the installation configured, then
 * whatever whole pyramid it holds of its own — the second exists only when the configured address
 * and a checked build differ, which is the shape of a stale configuration line outliving the
 * setup it described, and falling back is what keeps the scene usable until somebody fixes it.
 *
 * With a choice there is exactly one candidate and no fallback at all. The viewer asked for that
 * build by name; drawing a different one when it cannot be read would show them ground they did
 * not ask for under a radio button that still names the one they did. The scene refuses instead
 * and says why, which is the same answer a configured source gets.
 *
 * A choice naming a build the server no longer lists — it was deleted while the scene was open —
 * is treated as no choice, rather than as a choice of nothing: the viewer still has a globe.
 */
export function terrainCandidates(
  config: TerrainConfig | undefined,
  chosenBuildId: string | undefined,
): TerrainCandidate3D[] {
  if (!config) {
    return [];
  }
  const chosen = chosenBuildId
    ? config.terrainBuilds.find((build) => build.id === chosenBuildId)
    : undefined;
  if (chosen) {
    return [candidateFromBuild(chosen)];
  }
  return [config.terrain, config.terrainFallback]
    .filter((source): source is TerrainSourceInfo => source !== null && source !== undefined)
    .map(candidateFromSource);
}

export function candidateFromSource(source: TerrainSourceInfo): TerrainCandidate3D {
  return {
    url: source.url,
    ...(source.attribution ? { attribution: source.attribution } : {}),
    surveyHeightOffsetM: source.surveyHeightOffsetM,
    buildId: source.buildId,
  };
}

export function candidateFromBuild(build: TerrainBuildChoice): TerrainCandidate3D {
  return {
    url: build.url,
    ...(build.attribution ? { attribution: build.attribution } : {}),
    surveyHeightOffsetM: build.surveyHeightOffsetM,
    buildId: build.id,
  };
}

/** A longitude/latitude box, in degrees. */
export interface ExtentBox {
  west: number;
  south: number;
  east: number;
  north: number;
}

type Ring = [number, number][];

/**
 * The outer ring of each polygon in a GeoJSON geometry, as [longitude, latitude] pairs.
 *
 * Holes are deliberately ignored: a build's extent is the rectangle somebody drew on a map, and
 * the one question asked of it — is this place inside — is answered by its outline. Read
 * defensively because the geometry arrives as untyped JSON, and a shape that is not a polygon is
 * simply an extent that contains nothing rather than an error.
 */
function outerRings(extent: unknown): Ring[] {
  if (typeof extent !== 'object' || extent === null) {
    return [];
  }
  const { type, coordinates } = extent as { type?: unknown; coordinates?: unknown };
  const polygons =
    type === 'Polygon' ? [coordinates] : type === 'MultiPolygon' ? coordinates : undefined;
  if (!Array.isArray(polygons)) {
    return [];
  }
  const rings: Ring[] = [];
  for (const polygon of polygons) {
    const ring = Array.isArray(polygon) ? polygon[0] : undefined;
    if (
      Array.isArray(ring) &&
      ring.length >= 3 &&
      ring.every(
        (position) =>
          Array.isArray(position) &&
          typeof position[0] === 'number' &&
          typeof position[1] === 'number' &&
          Number.isFinite(position[0]) &&
          Number.isFinite(position[1]),
      )
    ) {
      rings.push(ring.map((position) => [position[0], position[1]] as [number, number]));
    }
  }
  return rings;
}

/** The smallest box around an extent, or undefined when it holds no polygon. */
export function extentBox(extent: unknown): ExtentBox | undefined {
  const positions = outerRings(extent).flat();
  if (positions.length === 0) {
    return undefined;
  }
  const box: ExtentBox = {
    west: Number.POSITIVE_INFINITY,
    south: Number.POSITIVE_INFINITY,
    east: Number.NEGATIVE_INFINITY,
    north: Number.NEGATIVE_INFINITY,
  };
  for (const [longitude, latitude] of positions) {
    box.west = Math.min(box.west, longitude);
    box.east = Math.max(box.east, longitude);
    box.south = Math.min(box.south, latitude);
    box.north = Math.max(box.north, latitude);
  }
  return box;
}

/** Length of one degree of latitude, and of longitude at the equator, in kilometres. */
const KM_PER_DEGREE = 111.32;

/**
 * How big an extent is on the ground, as the width and height of its box in kilometres.
 *
 * Equirectangular arithmetic, with the width scaled by the cosine of the box's middle latitude:
 * accurate to well under a percent for the tens-of-kilometres boxes a build covers, and read by a
 * viewer choosing between "40 × 30 km" and "12 × 9 km", who needs no better.
 */
export function extentSizeKm(extent: unknown): { widthKm: number; heightKm: number } | undefined {
  const box = extentBox(extent);
  if (!box) {
    return undefined;
  }
  const midLatitude = ((box.south + box.north) / 2) * (Math.PI / 180);
  return {
    widthKm: (box.east - box.west) * KM_PER_DEGREE * Math.cos(midLatitude),
    heightKm: (box.north - box.south) * KM_PER_DEGREE,
  };
}

/**
 * Whether a place is inside an extent.
 *
 * The box is checked first because it answers almost every call — the camera is usually nowhere
 * near a build's edge — and the ring is only walked when the box says maybe. The walk is the
 * even-odd rule: a ray cast east from the point crosses the outline an odd number of times when
 * the point is inside. A point exactly on an edge may answer either way, which for an extent
 * drawn tens of kilometres wide is of no consequence.
 */
export function extentContains(extent: unknown, longitude: number, latitude: number): boolean {
  const box = extentBox(extent);
  if (
    !box ||
    longitude < box.west ||
    longitude > box.east ||
    latitude < box.south ||
    latitude > box.north
  ) {
    return false;
  }
  return outerRings(extent).some((ring) => ringContains(ring, longitude, latitude));
}

function ringContains(ring: Ring, longitude: number, latitude: number): boolean {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i, i += 1) {
    const [xi, yi] = ring[i];
    const [xj, yj] = ring[j];
    const crosses =
      yi > latitude !== yj > latitude &&
      longitude < ((xj - xi) * (latitude - yi)) / (yj - yi) + xi;
    if (crosses) {
      inside = !inside;
    }
  }
  return inside;
}

/**
 * The finest build covering a place that is finer than what is drawn, or undefined when there is
 * none worth offering.
 *
 * `drawnDepth` is the level of the build on the globe; undefined means the bare ellipsoid is
 * drawn, against which any build at all is an improvement. Ties between builds of one level go to
 * the first listed, which is the newest. `excluded` names builds not to offer whatever they cover:
 * the ones the viewer has already declined this session, and the one they already chose.
 */
export function finerBuildAt(
  builds: readonly TerrainBuildChoice[],
  longitude: number,
  latitude: number,
  drawnDepth: number | undefined,
  excluded: ReadonlySet<string> = new Set(),
): TerrainBuildChoice | undefined {
  let finest: TerrainBuildChoice | undefined;
  for (const build of builds) {
    if (excluded.has(build.id)) {
      continue;
    }
    if (drawnDepth !== undefined && build.requestedMaxDepth <= drawnDepth) {
      continue;
    }
    if (finest && build.requestedMaxDepth <= finest.requestedMaxDepth) {
      continue;
    }
    if (extentContains(build.extent, longitude, latitude)) {
      finest = build;
    }
  }
  return finest;
}

/** The middle of a longitude/latitude box, at the ellipsoid, or undefined when there is no box. */
export function boundsCentre(bounds: Scene3DBounds | undefined): Scene3DPosition | undefined {
  if (!bounds) {
    return undefined;
  }
  const [west, south, east, north] = bounds;
  return { longitude: (west + east) / 2, latitude: (south + north) / 2, height: 0 };
}
