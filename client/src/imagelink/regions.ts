// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Regions drawn on a picture: what they are, how they are stored, and how they become
 * something that can be drawn on screen or clicked.
 *
 * <b>The frame, because everything here depends on it.</b> A region is measured in fractions of
 * the picture as it is drawn — 0 to 1 across its width and height, origin at its top-left, after
 * whatever quarter-turn correction somebody recorded against it, which is the only form anyone
 * ever sees it in.
 *
 * Fractions rather than pixels is not a matter of taste. A picture is served at whatever size the
 * viewer is entitled to and the screen can use, and the full-size rendering is bounded, so the
 * bytes a browser measures are usually not the bytes that were uploaded. Pixel coordinates would
 * mean a different place depending on which rendering they were drawn on, and nothing in a stored
 * region says which that was. Fractions are the same number on every rendering of the same
 * picture, and they are what this client can measure without being told the original's dimensions
 * — which it is not: the file it is given carries a name, a size and a type, and no width.
 *
 * Nothing here touches the DOM, so all of it is arithmetic a test can drive.
 */

/** A region as it is stored, in fractions of the drawn picture. */
export type ImageRegion =
  | { shape: 'point'; x: number; y: number }
  | { shape: 'rect'; x: number; y: number; w: number; h: number }
  | { shape: 'circle'; cx: number; cy: number; r: number }
  | { shape: 'polygon'; points: [number, number][] };

export type ImageRegionShape = ImageRegion['shape'];

/** The four shapes, in the order they are offered. */
export const IMAGE_REGION_SHAPES: ImageRegionShape[] = ['rect', 'circle', 'polygon', 'point'];

/** The size a picture is being drawn at, in screen pixels. */
export interface DrawnSize {
  width: number;
  height: number;
}

/**
 * A region converted to the pixels of one drawing of the picture. Everything that has to touch
 * the screen — drawing, hit-testing, placing a label — works on this rather than on fractions,
 * so the conversion happens exactly once and the awkward part of it lives in one place.
 */
export type PixelRegion =
  | { shape: 'point'; x: number; y: number }
  | { shape: 'rect'; x: number; y: number; w: number; h: number }
  | { shape: 'circle'; cx: number; cy: number; r: number }
  | { shape: 'polygon'; points: [number, number][] };

/** What a point drawn on the picture is worth on screen, in pixels, at this size. */
export const POINT_RADIUS_PX = 7;

function isFraction(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1;
}

function isFractionPair(value: unknown): value is [number, number] {
  return Array.isArray(value) && value.length === 2 && value.every(isFraction);
}

/**
 * The region a stored anchor payload describes, or null when it describes none.
 *
 * Defensive throughout, and deliberately so: a payload is free-form JSON that may have been
 * written by a newer server or by a client that got the frame wrong, and the consequence of
 * trusting one is a shape drawn somewhere arbitrary over somebody's photograph rather than an
 * error anybody sees. Anything that does not read as a whole, in-frame region reads as no region
 * at all, and the caller shows the member without one.
 */
export function readRegion(anchor: unknown): ImageRegion | null {
  if (typeof anchor !== 'object' || anchor === null) {
    return null;
  }

  const payload = anchor as Record<string, unknown>;
  switch (payload.shape) {
    case 'point':
      return isFraction(payload.x) && isFraction(payload.y)
        ? { shape: 'point', x: payload.x, y: payload.y }
        : null;
    case 'rect':
      // Extents are checked for being more than nothing as well as in frame: a zero-size
      // rectangle draws as a hairline somebody would read as a shape that was meant.
      return isFraction(payload.x) && isFraction(payload.y)
        && isFraction(payload.w) && isFraction(payload.h)
        && payload.w > 0 && payload.h > 0
        ? { shape: 'rect', x: payload.x, y: payload.y, w: payload.w, h: payload.h }
        : null;
    case 'circle':
      return isFraction(payload.cx) && isFraction(payload.cy) && isFraction(payload.r)
        && payload.r > 0
        ? { shape: 'circle', cx: payload.cx, cy: payload.cy, r: payload.r }
        : null;
    case 'polygon': {
      const points = payload.points;
      return Array.isArray(points) && points.length >= 3 && points.every(isFractionPair)
        ? { shape: 'polygon', points: points.map(([x, y]) => [x, y] as [number, number]) }
        : null;
    }
    default:
      return null;
  }
}

/**
 * The stored payload for a region. The server reads exactly these fields, and reads nothing
 * else, so this is where the wire shape is decided.
 */
export function regionAnchor(region: ImageRegion): Record<string, unknown> {
  return { ...region };
}

/**
 * A region in the pixels of one drawing of the picture.
 *
 * <b>A circle's radius is a fraction of the picture's width, on both axes.</b> The normalised
 * frame is not square — one unit across is a different number of pixels from one unit down on
 * any picture that is not — so a radius applied in that frame would draw as an ellipse, and
 * somebody who dragged out a circle would get back something that is not the shape they drew.
 * Taking the radius from the width alone keeps a circle a circle on screen, which is the whole
 * reason the shape is offered separately from a rectangle.
 */
export function toPixels(region: ImageRegion, size: DrawnSize): PixelRegion {
  const { width, height } = size;
  switch (region.shape) {
    case 'point':
      return { shape: 'point', x: region.x * width, y: region.y * height };
    case 'rect':
      return {
        shape: 'rect',
        x: region.x * width,
        y: region.y * height,
        w: region.w * width,
        h: region.h * height,
      };
    case 'circle':
      return {
        shape: 'circle',
        cx: region.cx * width,
        cy: region.cy * height,
        r: region.r * width,
      };
    case 'polygon':
      return {
        shape: 'polygon',
        points: region.points.map(([x, y]) => [x * width, y * height] as [number, number]),
      };
  }
}

/** The smallest box on screen that holds the region — where a label goes, and what a frame fits. */
export function pixelBounds(region: PixelRegion): { x: number; y: number; w: number; h: number } {
  switch (region.shape) {
    case 'point':
      return {
        x: region.x - POINT_RADIUS_PX,
        y: region.y - POINT_RADIUS_PX,
        w: POINT_RADIUS_PX * 2,
        h: POINT_RADIUS_PX * 2,
      };
    case 'rect':
      return { x: region.x, y: region.y, w: region.w, h: region.h };
    case 'circle':
      return {
        x: region.cx - region.r,
        y: region.cy - region.r,
        w: region.r * 2,
        h: region.r * 2,
      };
    case 'polygon': {
      const xs = region.points.map(([x]) => x);
      const ys = region.points.map(([, y]) => y);
      const minX = Math.min(...xs);
      const minY = Math.min(...ys);
      return { x: minX, y: minY, w: Math.max(...xs) - minX, h: Math.max(...ys) - minY };
    }
  }
}

/**
 * Whether a click at these pixels landed on the region.
 *
 * A point and a polygon outline are both thin things to hit, so both are given the same slack a
 * point is drawn with. Without it a polygon somebody drew is only clickable in its middle, and a
 * point is only clickable on the exact pixel it names.
 */
export function hitTest(region: PixelRegion, x: number, y: number): boolean {
  switch (region.shape) {
    case 'point':
      return Math.hypot(x - region.x, y - region.y) <= POINT_RADIUS_PX;
    case 'rect':
      return x >= region.x && x <= region.x + region.w
        && y >= region.y && y <= region.y + region.h;
    case 'circle':
      return Math.hypot(x - region.cx, y - region.cy) <= region.r;
    case 'polygon':
      return insidePolygon(region.points, x, y);
  }
}

/**
 * The even-odd rule: count the edges a ray to the right crosses. Odd means inside.
 *
 * Written out rather than reached for, because the shapes involved are small and the alternative
 * is a dependency for twelve lines of arithmetic. The comparison on the y span is deliberately
 * half-open — an edge counts at its lower end and not at its upper — so that a ray passing
 * exactly through a shared vertex crosses it once rather than twice or not at all.
 */
function insidePolygon(points: [number, number][], x: number, y: number): boolean {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const [xi, yi] = points[i];
    const [xj, yj] = points[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) {
      inside = !inside;
    }
  }
  return inside;
}

/** Brings a fraction back inside the picture, so a drag that left it cannot store a region outside. */
export function clampFraction(value: number): number {
  return Math.min(1, Math.max(0, value));
}

/**
 * The region a drag from one corner to another describes, or null when it describes nothing.
 *
 * Null rather than a degenerate shape is the point: a click that does not move is a click, and
 * returning a rectangle of no width from it would store something invisible that the reader can
 * never select or delete. The caller decides what a click means — for the shapes drawn by
 * dragging it means "nothing yet".
 */
export function regionFromDrag(
  shape: 'rect' | 'circle',
  from: { x: number; y: number },
  to: { x: number; y: number },
  size: DrawnSize,
): ImageRegion | null {
  const x1 = clampFraction(from.x);
  const y1 = clampFraction(from.y);
  const x2 = clampFraction(to.x);
  const y2 = clampFraction(to.y);

  if (shape === 'rect') {
    const w = Math.abs(x2 - x1);
    const h = Math.abs(y2 - y1);
    return w > 0 && h > 0
      ? { shape: 'rect', x: Math.min(x1, x2), y: Math.min(y1, y2), w, h }
      : null;
  }

  // The radius is in widths, so the vertical leg is converted into one before the two are
  // combined — otherwise dragging down a tall picture yields a far larger circle than dragging
  // the same distance across it.
  const aspect = size.height / size.width;
  const r = Math.hypot(x2 - x1, (y2 - y1) * aspect);
  if (r <= 0) {
    return null;
  }

  // A circle whose centre sits near an edge would otherwise be storable with a radius that
  // reaches outside the picture, which the server refuses as a coordinate out of frame.
  return { shape: 'circle', cx: x1, cy: y1, r: Math.min(r, 1) };
}

/**
 * The region a run of clicked points describes, or null while there are too few of them.
 *
 * Three is the server's floor and it is the honest one: two points are a line, and a line is not
 * in the vocabulary a region can be stored in.
 */
export function polygonFrom(points: { x: number; y: number }[]): ImageRegion | null {
  return points.length >= 3
    ? {
        shape: 'polygon',
        points: points.map((p) => [clampFraction(p.x), clampFraction(p.y)] as [number, number]),
      }
    : null;
}
