// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where a drawn map sheet lies, as the placement editor builds it before the server checks it.
 *
 * A placement is a list of control points, each tying a point of the picture — `u`, `v` as
 * fractions of its width and height, measured from the top-left corner — to a point on the
 * ground in the placement's coordinate system. The editor only ever speaks WGS 84 here, so `x`
 * is a longitude and `y` a latitude. Nothing in this file knows about the map library or the
 * screen: the editor converts to and from the map's projection at its own boundary.
 */

export type LonLat = [number, number];

export type CornerId = 'nw' | 'ne' | 'se' | 'sw';

/** The corners in the order they go round the sheet, which is also the order they are sent in. */
export const CORNER_IDS: readonly CornerId[] = ['nw', 'ne', 'se', 'sw'];

/** Where each corner is on the picture. */
export const CORNER_FRACTIONS: Readonly<Record<CornerId, readonly [number, number]>> = {
  nw: [0, 0],
  ne: [1, 0],
  se: [1, 1],
  sw: [0, 1],
};

export type Corners = Record<CornerId, LonLat>;

/** The transformations the corner mode offers: a free quadrilateral, or one kept a parallelogram. */
export type CornerTransformation = 'homography' | 'affine';

export type PlacementTransformation =
  | 'similarity'
  | 'affine'
  | 'homography'
  | 'polynomial2'
  | 'polynomial3'
  | 'tps';

export interface ControlPoint {
  u: number;
  v: number;
  x: number;
  y: number;
  label: string | null;
}

/** The body the placement routes take, in the shape the server reads. */
export interface PlacementBody {
  version: number;
  transformation: string;
  crs: string;
  gcps: ControlPoint[];
  mask: number[][] | null;
}

export const PLACEMENT_CRS = 'EPSG:4326';

/** Every transformation the server can fit, in the order the editor offers them. */
export const TRANSFORMATIONS: readonly PlacementTransformation[] = [
  'similarity',
  'affine',
  'homography',
  'polynomial2',
  'polynomial3',
  'tps',
];

/**
 * A coordinate system named the way a document names its own — by its whole definition rather
 * than by a code. Such points can be shown and sent back as they are, but not typed in or
 * converted here.
 */
export function isWktCrs(crs: string): boolean {
  return crs.trim().toUpperCase().startsWith('WKT:');
}

/** The fewest points the server accepts for each transformation. */
export function minimumPoints(transformation: PlacementTransformation): number {
  switch (transformation) {
    case 'similarity':
      return 2;
    case 'affine':
      return 3;
    case 'homography':
      return 4;
    case 'polynomial2':
      return 6;
    case 'polynomial3':
      return 10;
    case 'tps':
      return 3;
  }
}

/**
 * The first corners of a sheet that has none yet: centred on the view, 60 % of its width, with
 * the picture's own proportions — shrunk to fit when a tall sheet would run off the top and
 * bottom. Works in the view's own planar coordinates (the map's projection), because a
 * proportion measured in degrees would be stretched by the latitude.
 */
export function initialCorners(
  viewExtent: readonly [number, number, number, number],
  aspect: number,
): Record<CornerId, [number, number]> {
  const [minX, minY, maxX, maxY] = viewExtent;
  const viewWidth = maxX - minX;
  const viewHeight = maxY - minY;
  const ratio = Number.isFinite(aspect) && aspect > 0 ? aspect : 1;
  let width = viewWidth * 0.6;
  let height = width / ratio;
  if (height > viewHeight * 0.8) {
    height = viewHeight * 0.8;
    width = height * ratio;
  }
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;
  const left = cx - width / 2;
  const right = cx + width / 2;
  const top = cy + height / 2;
  const bottom = cy - height / 2;
  return { nw: [left, top], ne: [right, top], se: [right, bottom], sw: [left, bottom] };
}

/** The picture's proportions, width over height; square when the drawing has not said. */
export function sheetAspect(width: number | null | undefined, height: number | null | undefined): number {
  return width && height && width > 0 && height > 0 ? width / height : 1;
}

const near = (a: number, b: number) => Math.abs(a - b) < 1e-9;

/**
 * The corners of a placement that was made of exactly the four picture corners in WGS 84 — the
 * shape this editor saves — so reopening it shows the corners where they were left. Null for
 * anything else (a placement embedded in the document, or one made from other points), which
 * the editor then starts afresh rather than misreading.
 */
export function cornersOfPlacement(placement: PlacementBody | null | undefined): Corners | null {
  if (!placement || placement.crs !== PLACEMENT_CRS || placement.gcps.length !== 4) {
    return null;
  }
  const found: Partial<Corners> = {};
  for (const point of placement.gcps) {
    const corner = CORNER_IDS.find(
      (id) => near(CORNER_FRACTIONS[id][0], point.u) && near(CORNER_FRACTIONS[id][1], point.v),
    );
    if (corner === undefined || found[corner] !== undefined) {
      return null;
    }
    found[corner] = [point.x, point.y];
  }
  return found as Corners;
}

/** The transformation a saved corner placement used, when it is one the corner mode offers. */
export function cornerTransformationOf(placement: PlacementBody | null | undefined): CornerTransformation {
  return placement?.transformation === 'affine' ? 'affine' : 'homography';
}

/** Four corners as the request the placement routes take. */
export function cornersToPlacement(corners: Corners, transformation: CornerTransformation): PlacementBody {
  return {
    version: 1,
    transformation,
    crs: PLACEMENT_CRS,
    gcps: CORNER_IDS.map((id) => ({
      u: CORNER_FRACTIONS[id][0],
      v: CORNER_FRACTIONS[id][1],
      x: corners[id][0],
      y: corners[id][1],
      label: id.toUpperCase(),
    })),
    mask: null,
  };
}

/**
 * Whether four corners, taken in order round the sheet, make a quadrilateral the sheet can be
 * stretched over: convex, not folded over itself, not flattened. A request for anything else
 * would only come back refused, and while corners are typed one at a time the shape passes
 * through such states on its way to the one meant — so they are not asked about at all.
 */
export function isUsableQuadrilateral(corners: Corners): boolean {
  const ring = CORNER_IDS.map((id) => corners[id]);
  if (ring.some(([x, y]) => !Number.isFinite(x) || !Number.isFinite(y))) {
    return false;
  }
  let sign = 0;
  for (let i = 0; i < 4; i++) {
    const [ax, ay] = ring[i];
    const [bx, by] = ring[(i + 1) % 4];
    const [cx, cy] = ring[(i + 2) % 4];
    const cross = (bx - ax) * (cy - by) - (by - ay) * (cx - bx);
    if (Math.abs(cross) < 1e-18) {
      return false;
    }
    const s = Math.sign(cross);
    if (sign === 0) {
      sign = s;
    } else if (s !== sign) {
      return false;
    }
  }
  return true;
}

/**
 * One control point being made: the spot on the picture is chosen first, and the spot on the
 * ground follows, so either half may still be missing.
 */
export interface PointPair {
  u: number;
  v: number;
  x: number | null;
  y: number | null;
  /** What the point is called, when anybody named it. */
  label?: string | null;
}

/** The pairs that have both halves. */
export function completePairs(pairs: readonly PointPair[]): ControlPoint[] {
  return pairs.flatMap((pair, index) =>
    pair.x !== null && pair.y !== null && Number.isFinite(pair.x) && Number.isFinite(pair.y)
      ? [{ u: pair.u, v: pair.v, x: pair.x, y: pair.y, label: pair.label?.trim() || String(index + 1) }]
      : [],
  );
}

/**
 * Matched point pairs as a placement request, or null while there are too few complete pairs
 * for the transformation to be worked out.
 */
export function pairsToPlacement(
  pairs: readonly PointPair[],
  transformation: PlacementTransformation = 'similarity',
  options: { crs?: string; mask?: readonly (readonly [number, number])[] | null } = {},
): PlacementBody | null {
  const gcps = completePairs(pairs);
  if (gcps.length < minimumPoints(transformation)) {
    return null;
  }
  const mask = options.mask && options.mask.length >= 3 ? options.mask.map(([u, v]) => [u, v]) : null;
  return { version: 1, transformation, crs: options.crs ?? PLACEMENT_CRS, gcps, mask };
}

/** The pairs of a saved placement, whatever its points and system, for the control-point table. */
export function pairsOfPlacement(placement: PlacementBody | null | undefined): PointPair[] {
  return (placement?.gcps ?? []).map((p) => ({ u: p.u, v: p.v, x: p.x, y: p.y, label: p.label ?? null }));
}

/** A placement's outline as picture fractions, or null when it has none worth drawing. */
export function maskOf(placement: PlacementBody | null | undefined): [number, number][] | null {
  const ring = placement?.mask;
  if (!ring || ring.length < 3) {
    return null;
  }
  return ring.map((point) => [point[0], point[1]] as [number, number]);
}

/**
 * Each pair's residual from a preview answer. The answer numbers the points it was sent, and only
 * complete pairs are sent, so its indexes are mapped back to the table's rows here.
 */
export function residualsByPair(
  pairs: readonly PointPair[],
  residuals: readonly { index: number; metres: number }[],
): Map<number, number> {
  const sent = pairs.flatMap((pair, index) =>
    pair.x !== null && pair.y !== null && Number.isFinite(pair.x) && Number.isFinite(pair.y) ? [index] : [],
  );
  const byPair = new Map<number, number>();
  for (const { index, metres } of residuals) {
    const row = sent[index];
    if (row !== undefined) {
      byPair.set(row, metres);
    }
  }
  return byPair;
}

/**
 * The rows whose residual is more than three times the fit's root mean square: the points most
 * likely clicked in the wrong place. Nothing is singled out while the fit is exact or unmeasured.
 */
export function outlierRows(byPair: ReadonlyMap<number, number>, rms: number | null): Set<number> {
  const out = new Set<number>();
  if (rms === null || !(rms > 0)) {
    return out;
  }
  for (const [row, metres] of byPair) {
    if (metres > 3 * rms) {
      out.add(row);
    }
  }
  return out;
}

/** The file an exported placement is saved as: the map's name, made safe for a file system. */
export function annotationFileName(name: string): string {
  const safe = [...name.normalize('NFC')]
    // Characters below a space are no part of a file name anywhere.
    .map((char) => (char.charCodeAt(0) < 32 ? '_' : char))
    .join('')
    .replace(/[\\/:*?"<>|]+/g, '_')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/^\.+/, '');
  return `${safe || 'map'}.georef.json`;
}

/** A distance in metres as the footer prints it: finer for a close fit, whole metres for a poor one. */
export function formatMetres(metres: number): string {
  if (metres < 1) {
    return metres.toFixed(2);
  }
  if (metres < 100) {
    return metres.toFixed(1);
  }
  return Math.round(metres).toString();
}

/** The options a static image source needs to draw a preview answer where it belongs. */
export function previewImageOptions(preview: { image: string; extent3857: readonly number[] }): {
  url: string;
  imageExtent: [number, number, number, number];
  projection: 'EPSG:3857';
} | null {
  const [minX, minY, maxX, maxY] = preview.extent3857;
  if (
    preview.extent3857.length !== 4 ||
    ![minX, minY, maxX, maxY].every(Number.isFinite) ||
    maxX <= minX ||
    maxY <= minY
  ) {
    return null;
  }
  return { url: preview.image, imageExtent: [minX, minY, maxX, maxY], projection: 'EPSG:3857' };
}

/** A coordinate system control points may be typed in, as the server lists them. */
export interface TypedSystem {
  crs: string;
  name: string;
  geographic: boolean;
  working: boolean;
}

export const WGS84_SYSTEM: TypedSystem = { crs: PLACEMENT_CRS, name: 'WGS 84', geographic: true, working: false };

/**
 * The systems to offer, in the order offered: WGS 84 first (it is always there, whatever the
 * server listed), then the installation's working system, then the rest as listed, each once.
 */
export function orderSystems(listed: readonly TypedSystem[] | undefined): TypedSystem[] {
  const all = listed ?? [];
  const wgs = all.find((s) => s.crs === PLACEMENT_CRS) ?? WGS84_SYSTEM;
  const rest = all.filter((s) => s.crs !== PLACEMENT_CRS);
  const seen = new Set<string>([PLACEMENT_CRS]);
  const ordered = [wgs];
  for (const system of [...rest.filter((s) => s.working), ...rest.filter((s) => !s.working)]) {
    if (!seen.has(system.crs)) {
      seen.add(system.crs);
      ordered.push(system);
    }
  }
  return ordered;
}

/** A station pinned on the sheet, with where its survey puts it, in degrees. */
export interface PinnedStation {
  label: string;
  u: number;
  v: number;
  x: number;
  y: number;
}

/**
 * Pinned stations as control-point rows, their ground halves moved into the system the table is
 * typed in. A station the conversion cannot place is left out rather than added half-empty; one
 * already in the table (the same spot on the sheet) is not added twice.
 */
export function stationPairs(
  stations: readonly PinnedStation[],
  existing: readonly PointPair[],
  convert: (lon: number, lat: number) => [number, number] | null,
): PointPair[] {
  const taken = new Set(existing.map((p) => `${p.u.toFixed(6)},${p.v.toFixed(6)}`));
  return stations.flatMap((station) => {
    const key = `${station.u.toFixed(6)},${station.v.toFixed(6)}`;
    const at = convert(station.x, station.y);
    if (taken.has(key) || at === null) {
      return [];
    }
    taken.add(key);
    return [{ u: station.u, v: station.v, x: at[0], y: at[1], label: station.label }];
  });
}

/**
 * The transformation to start from for a number of points: a gentle bend once there are enough
 * points to support one, an affine fit for a few, and the choice left alone below that.
 */
export function suggestedTransformation(points: number): PlacementTransformation | null {
  if (points >= 6) {
    return 'polynomial2';
  }
  return points >= 3 ? 'affine' : null;
}
