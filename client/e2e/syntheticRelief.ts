// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Page } from '@playwright/test';

// Relief for a browser spec, made up on the spot: one smooth hill wherever the spec wants it, in
// the elevation format the 3D scene reads, served to the page by intercepting its requests.
//
// A spec about heights needs ground that has some. The scratch stack's globe is the bare ellipsoid,
// where every height is zero and stretching it shows nothing; a baked pyramid of real terrain is
// half a gigabyte that only some machines hold. This is a few hundred lines of arithmetic instead,
// the same on every machine, and the hill's height at any point is known to the metre — which is
// what lets a spec say where the ground under something should be.
//
// The tiles are quantized-mesh 1.0 on the geographic tiling, every level offered everywhere, each
// one a regular grid sampled from the hill. No vertex normals are sent, so the ground is not shaded:
// relief shows as the shape of the ground and of the map draped on it, not as light and shadow.

/** A hill: its top, how high it is there, and how far out it has fallen to a third of that. */
export interface SyntheticHill {
  longitude: number;
  latitude: number;
  heightM: number;
  radiusM: number;
}

/** The address the configuration hands the scene, under which the tiles below are served. */
export const SYNTHETIC_RELIEF_URL = '/e2e-synthetic-relief/';

/** Deepest level offered: tiles a few hundred metres across, finer than any spec looks. */
const MAX_LEVEL = 16;
/** Samples along each side of a tile. */
const GRID = 33;
const QUANTIZED_MAX = 32767;

const WGS84_A = 6378137;
const WGS84_F = 1 / 298.257223563;
const WGS84_B = WGS84_A * (1 - WGS84_F);
const WGS84_E2 = WGS84_F * (2 - WGS84_F);

/** Height of the hill at a point, in metres above the ellipsoid; zero far from it. */
export function hillHeight(hill: SyntheticHill, longitude: number, latitude: number): number {
  const east = (longitude - hill.longitude) * 111_320 * Math.cos((hill.latitude * Math.PI) / 180);
  const north = (latitude - hill.latitude) * 110_540;
  return hill.heightM * Math.exp(-(east * east + north * north) / (hill.radiusM * hill.radiusM));
}

/**
 * Has the page's 3D scene draw its ground from the hill: the map configuration it reads is
 * answered with this relief as the installation's elevation model, and the tiles are served from
 * here. Call before the page loads the scene.
 */
export async function serveSyntheticRelief(page: Page, hill: SyntheticHill): Promise<void> {
  await page.route('**/api/v1/map/config', async (route) => {
    const response = await route.fetch();
    const config = (await response.json()) as Record<string, unknown>;
    await route.fulfill({
      response,
      json: {
        ...config,
        terrain: {
          url: SYNTHETIC_RELIEF_URL,
          attribution: 'Invented relief for a test',
          surveyHeightOffsetM: 0,
          origin: 'configured',
          buildId: null,
        },
        terrainFallback: null,
        terrainBuilds: [],
      },
    });
  });

  await page.route(`**${SYNTHETIC_RELIEF_URL}**`, async (route) => {
    const path = new URL(route.request().url()).pathname.slice(SYNTHETIC_RELIEF_URL.length);
    if (path === 'layer.json') {
      await route.fulfill({ json: layerJson() });
      return;
    }
    const match = /^(\d+)\/(\d+)\/(\d+)\.terrain$/.exec(path);
    if (!match) {
      await route.fulfill({ status: 404 });
      return;
    }
    const [level, x, y] = match.slice(1).map(Number);
    await route.fulfill({
      status: 200,
      contentType: 'application/vnd.quantized-mesh',
      body: Buffer.from(encodeTile(hill, level, x, y)),
    });
  });
}

function layerJson() {
  const available = [];
  for (let level = 0; level <= MAX_LEVEL; level += 1) {
    available.push([{ startX: 0, endX: 2 ** (level + 1) - 1, startY: 0, endY: 2 ** level - 1 }]);
  }
  return {
    tilejson: '2.1.0',
    name: 'Synthetic relief',
    format: 'quantized-mesh-1.0',
    version: '1.0.0',
    scheme: 'tms',
    projection: 'EPSG:4326',
    bounds: [-180, -90, 180, 90],
    tiles: ['{z}/{x}/{y}.terrain?v={version}'],
    available,
  };
}

interface Vec3 {
  x: number;
  y: number;
  z: number;
}

function toEarthFixed(longitude: number, latitude: number, height: number): Vec3 {
  const lon = (longitude * Math.PI) / 180;
  const lat = (latitude * Math.PI) / 180;
  const n = WGS84_A / Math.sqrt(1 - WGS84_E2 * Math.sin(lat) ** 2);
  return {
    x: (n + height) * Math.cos(lat) * Math.cos(lon),
    y: (n + height) * Math.cos(lat) * Math.sin(lon),
    z: (n * (1 - WGS84_E2) + height) * Math.sin(lat),
  };
}

const length = (v: Vec3) => Math.hypot(v.x, v.y, v.z);
const scaled = (v: Vec3): Vec3 => ({ x: v.x / WGS84_A, y: v.y / WGS84_A, z: v.z / WGS84_B });

/**
 * The point a renderer tests against the horizon to decide that none of a tile can be seen: in the
 * ellipsoid's scaled space, along the direction of the tile's centre, far enough out that it is
 * above the horizon wherever any vertex is. The renderer's own construction; a tile so large the
 * construction has no answer gets a point far out instead, which only ever costs a tile drawn that
 * could have been skipped.
 */
function horizonOcclusionPoint(center: Vec3, vertices: Vec3[]): Vec3 {
  const direction = scaled(center);
  const directionLength = length(direction);
  const d = { x: direction.x / directionLength, y: direction.y / directionLength, z: direction.z / directionLength };
  let magnitude = 0;
  for (const vertex of vertices) {
    const p = scaled(vertex);
    const pLength = Math.max(1, length(p));
    const unit = { x: p.x / length(p), y: p.y / length(p), z: p.z / length(p) };
    const cosAlpha = unit.x * d.x + unit.y * d.y + unit.z * d.z;
    const cross = {
      x: unit.y * d.z - unit.z * d.y,
      y: unit.z * d.x - unit.x * d.z,
      z: unit.x * d.y - unit.y * d.x,
    };
    const sinAlpha = length(cross);
    const cosBeta = 1 / pLength;
    const sinBeta = Math.sqrt(pLength * pLength - 1) * cosBeta;
    const denominator = cosAlpha * cosBeta - sinAlpha * sinBeta;
    magnitude = denominator > 1e-6 ? Math.max(magnitude, 1 / denominator) : Number.POSITIVE_INFINITY;
  }
  const out = Number.isFinite(magnitude) ? magnitude : 1000;
  return { x: d.x * out, y: d.y * out, z: d.z * out };
}

const zigZag = (value: number) => ((value << 1) ^ (value >> 31)) & 0xffff;

/** One tile of the geographic tiling, numbered from the south as the tiling's TMS scheme counts. */
function encodeTile(hill: SyntheticHill, level: number, x: number, y: number): Uint8Array {
  const size = 180 / 2 ** level;
  const west = -180 + x * size;
  const south = -90 + y * size;

  // The grid, south-west corner first, rows running east.
  const samples: { u: number; v: number; height: number; longitude: number; latitude: number }[] = [];
  for (let row = 0; row < GRID; row += 1) {
    for (let column = 0; column < GRID; column += 1) {
      const longitude = west + (size * column) / (GRID - 1);
      const latitude = south + (size * row) / (GRID - 1);
      samples.push({
        u: Math.round((QUANTIZED_MAX * column) / (GRID - 1)),
        v: Math.round((QUANTIZED_MAX * row) / (GRID - 1)),
        height: hillHeight(hill, longitude, latitude),
        longitude,
        latitude,
      });
    }
  }
  const triangles: number[] = [];
  for (let row = 0; row < GRID - 1; row += 1) {
    for (let column = 0; column < GRID - 1; column += 1) {
      const sw = row * GRID + column;
      const se = sw + 1;
      const nw = sw + GRID;
      const ne = nw + 1;
      // Counter-clockwise seen from above, as the format wants.
      triangles.push(sw, se, ne, sw, ne, nw);
    }
  }

  // The format numbers vertices in the order the triangles first use them, which is what lets its
  // index list be written as small differences from the highest number seen so far.
  const renumbered = new Map<number, number>();
  for (const index of triangles) {
    if (!renumbered.has(index)) {
      renumbered.set(index, renumbered.size);
    }
  }
  const vertices = new Array<(typeof samples)[number]>(samples.length);
  for (const [old, next] of renumbered) {
    vertices[next] = samples[old];
  }
  const indices = triangles.map((index) => renumbered.get(index)!);

  let minimumHeight = Math.min(...vertices.map((vertex) => vertex.height));
  let maximumHeight = Math.max(...vertices.map((vertex) => vertex.height));
  if (maximumHeight - minimumHeight < 1) {
    maximumHeight = minimumHeight + 1;
  }
  const earthFixed = vertices.map((vertex) => toEarthFixed(vertex.longitude, vertex.latitude, vertex.height));
  const center = toEarthFixed(west + size / 2, south + size / 2, (minimumHeight + maximumHeight) / 2);
  const centroid = earthFixed.reduce(
    (sum, point) => ({ x: sum.x + point.x / earthFixed.length, y: sum.y + point.y / earthFixed.length, z: sum.z + point.z / earthFixed.length }),
    { x: 0, y: 0, z: 0 },
  );
  const radius = Math.max(
    ...earthFixed.map((point) => Math.hypot(point.x - centroid.x, point.y - centroid.y, point.z - centroid.z)),
  );
  const occlusion = horizonOcclusionPoint(center, earthFixed);

  const vertexCount = vertices.length;
  const edges = {
    west: vertices.flatMap((vertex, index) => (vertex.u === 0 ? [index] : [])),
    south: vertices.flatMap((vertex, index) => (vertex.v === 0 ? [index] : [])),
    east: vertices.flatMap((vertex, index) => (vertex.u === QUANTIZED_MAX ? [index] : [])),
    north: vertices.flatMap((vertex, index) => (vertex.v === QUANTIZED_MAX ? [index] : [])),
  };
  const bytes =
    88 + 4 + vertexCount * 6 + 4 + indices.length * 2 +
    Object.values(edges).reduce((sum, edge) => sum + 4 + edge.length * 2, 0);
  const view = new DataView(new ArrayBuffer(bytes));
  let at = 0;
  const f64 = (value: number) => {
    view.setFloat64(at, value, true);
    at += 8;
  };
  const f32 = (value: number) => {
    view.setFloat32(at, value, true);
    at += 4;
  };
  const u32 = (value: number) => {
    view.setUint32(at, value, true);
    at += 4;
  };
  const u16 = (value: number) => {
    view.setUint16(at, value, true);
    at += 2;
  };

  f64(center.x);
  f64(center.y);
  f64(center.z);
  f32(minimumHeight);
  f32(maximumHeight);
  f64(centroid.x);
  f64(centroid.y);
  f64(centroid.z);
  f64(radius);
  f64(occlusion.x);
  f64(occlusion.y);
  f64(occlusion.z);

  u32(vertexCount);
  const quantizedHeights = vertices.map((vertex) =>
    Math.round(((vertex.height - minimumHeight) / (maximumHeight - minimumHeight)) * QUANTIZED_MAX),
  );
  for (const values of [vertices.map((vertex) => vertex.u), vertices.map((vertex) => vertex.v), quantizedHeights]) {
    let previous = 0;
    for (const value of values) {
      u16(zigZag(value - previous));
      previous = value;
    }
  }

  // Sixteen-bit indices, which the vertex count allows; the vertex data ends on an even byte.
  u32(indices.length / 3);
  let highest = 0;
  for (const index of indices) {
    u16(highest - index);
    if (index === highest) {
      highest += 1;
    }
  }
  for (const edge of [edges.west, edges.south, edges.east, edges.north]) {
    u32(edge.length);
    for (const index of edge) {
      u16(index);
    }
  }
  return new Uint8Array(view.buffer);
}
