// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DTileImage } from './scene3dEngine.ts';

// What every tile source this application draws itself shares, whatever it draws: where a tile of
// the web-mercator pyramid lies, how many tiles are being drawn at once, and the tiles already
// drawn this session. A source supplies only "draw this tile"; this module decides when.

/** Why a self-drawn layer draws less than it should, or nothing; the 3D layer list words each. */
export type RenderedSourceCode =
  | 'cog.unreadable'
  | 'cog.not_georeferenced'
  | 'cog.projection_unusable'
  | 'cog.band_missing'
  | 'cog.short_pyramid'
  | 'vector.unreadable'
  | 'vector.stand_in';

/**
 * How a self-drawn layer stands: drawing; drawing with a limit the viewer should know of; or not
 * drawing at all. Reported by the source as soon as it knows, and again if it changes.
 */
export type RenderedSourceStatus =
  | { state: 'ready' }
  | { state: 'limited'; code: RenderedSourceCode }
  | { state: 'unavailable'; code: RenderedSourceCode };

export type RenderedSourceReport = (status: RenderedSourceStatus) => void;

/** Half the side of the web-mercator square, in metres. */
export const MERCATOR_HALF_WORLD = 20037508.342789244;

/** Earth radius the web-mercator projection is defined on. */
const MERCATOR_RADIUS = 6378137;

/** The northern and southern edge of the web-mercator square, in degrees. */
export const MERCATOR_MAX_LATITUDE = 85.0511287798066;

/** Web-mercator metres of a longitude/latitude. */
export function toMercator(longitude: number, latitude: number): [number, number] {
  const clamped = Math.max(-MERCATOR_MAX_LATITUDE, Math.min(MERCATOR_MAX_LATITUDE, latitude));
  const x = (longitude * Math.PI * MERCATOR_RADIUS) / 180;
  const y = MERCATOR_RADIUS * Math.log(Math.tan(Math.PI / 4 + (clamped * Math.PI) / 360));
  return [x, y];
}

/** Longitude/latitude of web-mercator metres. */
export function fromMercator(x: number, y: number): [number, number] {
  const longitude = (x / MERCATOR_RADIUS) * (180 / Math.PI);
  const latitude = (2 * Math.atan(Math.exp(y / MERCATOR_RADIUS)) - Math.PI / 2) * (180 / Math.PI);
  return [longitude, latitude];
}

/** A tile's extent in web-mercator metres: west, south, east, north. Row 0 is the northernmost. */
export function tileExtent(x: number, y: number, level: number): [number, number, number, number] {
  const size = (2 * MERCATOR_HALF_WORLD) / 2 ** level;
  const west = -MERCATOR_HALF_WORLD + x * size;
  const north = MERCATOR_HALF_WORLD - y * size;
  return [west, north - size, west + size, north];
}

/** Metres per pixel of a level of the pyramid, at the equator. */
export function levelResolution(level: number, tileSize: number): number {
  return (2 * MERCATOR_HALF_WORLD) / (tileSize * 2 ** level);
}

/**
 * The shallowest level whose pixels are at least as fine as `metresPerPixel` (web-mercator
 * metres). Past it there is nothing new to draw, so the engine enlarges this one instead.
 */
export function levelForResolution(metresPerPixel: number, tileSize: number): number {
  if (!(metresPerPixel > 0)) {
    return 0;
  }
  const level = Math.ceil(Math.log2((2 * MERCATOR_HALF_WORLD) / (tileSize * metresPerPixel)) - 1e-9);
  return Math.max(0, Math.min(MAX_LEVEL, level));
}

/** Deeper than any source here has detail for: a quarter of a millimetre per pixel. */
const MAX_LEVEL = 24;

/** A canvas a tile is drawn into. */
export function createTileCanvas(size: number): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  return canvas;
}

/**
 * The tiles drawn this session, newest last, across every self-drawn source. One budget for all
 * of them because the memory is one memory: a 256-pixel tile is a quarter of a megabyte, and a
 * scene with a vector basemap and a GeoTIFF overlay should not hold twice what one would.
 */
const drawnTiles = new Map<string, Scene3DTileImage>();

/** About forty megabytes of 256-pixel tiles. */
export const DRAWN_TILE_BUDGET = 160;

function remember(key: string, image: Scene3DTileImage) {
  drawnTiles.delete(key);
  drawnTiles.set(key, image);
  while (drawnTiles.size > DRAWN_TILE_BUDGET) {
    const oldest = drawnTiles.keys().next().value as string;
    drawnTiles.delete(oldest);
  }
}

function recall(key: string): Scene3DTileImage | undefined {
  const image = drawnTiles.get(key);
  if (image) {
    // Touched, so the tiles in view are the last to be let go of.
    drawnTiles.delete(key);
    drawnTiles.set(key, image);
  }
  return image;
}

/** Forgets every drawn tile of one source, or of all of them; for tests and for a source that changed. */
export function forgetDrawnTiles(sourceKey?: string): void {
  if (sourceKey === undefined) {
    drawnTiles.clear();
    return;
  }
  for (const key of [...drawnTiles.keys()]) {
    if (key.startsWith(`${sourceKey}|`)) {
      drawnTiles.delete(key);
    }
  }
}

export interface TileQueueOptions {
  /**
   * Names what is drawn — the file, or the data and its style — so two layers drawing the same
   * thing share their tiles, and a tile drawn before the scene was rebuilt is not drawn again.
   */
  sourceKey: string;
  /** How many tiles may be drawn at once. Past it the engine is told to ask again later. */
  concurrency: number;
  /** Draws one tile; the signal is aborted when the source is destroyed. */
  draw: (x: number, y: number, level: number, signal: AbortSignal) => Promise<Scene3DTileImage>;
}

export interface TileQueue {
  request(x: number, y: number, level: number): Promise<Scene3DTileImage> | undefined;
  /** How many tiles are being drawn now. */
  inFlight(): number;
  destroy(): void;
}

/**
 * Bounds the tiles drawn at once and keeps the ones drawn.
 *
 * Bounded because a globe asks for every tile in view in the same frame — two hundred of them on a
 * tilted view — and each one here is real work on this page's own thread: range requests and
 * decoding for a GeoTIFF, a vector render for a style. Answering "busy" past the bound is what the
 * engine expects from a source that throttles (it asks again on a later frame, and keeps drawing
 * frames while any tile is outstanding), so the bound costs latency, not tiles.
 *
 * Nothing is cancelled because the camera moved: the engine keeps every tile it asked for and
 * draws the parents while the children arrive, so a tile asked for is a tile it will use. What is
 * cancelled is everything at once, when the layer goes — the engine has stopped listening then.
 */
export function createTileQueue(options: TileQueueOptions): TileQueue {
  const controller = new AbortController();
  const pending = new Map<string, Promise<Scene3DTileImage>>();
  return {
    request(x, y, level) {
      if (controller.signal.aborted) {
        return Promise.reject(new Error('the source was closed'));
      }
      const key = `${options.sourceKey}|${level}/${x}/${y}`;
      const drawn = recall(key);
      if (drawn) {
        return Promise.resolve(drawn);
      }
      const running = pending.get(key);
      if (running) {
        return running;
      }
      if (pending.size >= options.concurrency) {
        return undefined;
      }
      const promise = options.draw(x, y, level, controller.signal).then(
        (image) => {
          pending.delete(key);
          if (!controller.signal.aborted) {
            remember(key, image);
          }
          return image;
        },
        (error: unknown) => {
          pending.delete(key);
          throw error;
        },
      );
      pending.set(key, promise);
      return promise;
    },
    inFlight: () => pending.size,
    destroy() {
      controller.abort();
      pending.clear();
    },
  };
}
