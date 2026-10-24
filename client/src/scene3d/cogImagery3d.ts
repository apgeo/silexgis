// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromUrl, Pool, type GeoTIFF, type GeoTIFFImage, type TypedArray } from 'geotiff';
import { definedProjection } from '../geo/formats/nationalGrids.ts';
import {
  cogDrawing,
  isNodata,
  scaledByte,
  writeBandColour,
  type CogDrawing,
  type CogFileFacts,
  type CogOptions,
} from '../map/cogScaling.ts';
import {
  createTileCanvas,
  createTileQueue,
  fromMercator,
  levelForResolution,
  MERCATOR_MAX_LATITUDE,
  tileExtent,
  toMercator,
  type RenderedSourceReport,
} from './renderedTiles3d.ts';
import type {
  Scene3DBounds,
  Scene3DTileImage,
  Scene3DTileSource,
  Scene3DTileSourceInfo,
} from './scene3dEngine.ts';

// A cloud-optimised GeoTIFF from the layer catalogue, drawn on the globe tile by tile.
//
// The engine has no GeoTIFF reader, and the georeferenced-map overlay's way round that — render the
// file once, flattened to one 2048-pixel picture — is fine for a survey sheet and useless for a
// hillshade covering a county, which would be a blur at any height worth looking from. So each
// tile the globe asks for is read from the file's own pyramid: the overview closest to the tile's
// detail, only the window under the tile, by HTTP range requests, and painted here.
//
// Painted by the rules the flat map hands OpenLayers (`map/cogScaling.ts`), so a file is stretched
// and made see-through alike in both views; placed by the coordinate system the catalogue names
// for it, never by the file's own keys, so the two views put it in the same place.

/** Side of a tile handed to the globe. */
const TILE_SIZE = 256;

/**
 * Each tile's ground is placed through a coarse grid of exactly projected points and interpolated
 * between them: within one tile the distortion between a national grid and web mercator is far
 * below a pixel, and projecting all sixty-five thousand pixels would make projection the cost of
 * drawing.
 */
const GRID_STEPS = 8;

/**
 * The most pixels one tile may read from one level of the file — four times the tile's own, in
 * each direction. A file whose pyramid stops short (or that has none) would otherwise read
 * hundreds of megabytes to paint a continent-scale tile; past this the tile is left empty and the
 * file draws only closer in, where its own levels answer.
 */
const MAX_WINDOW_PIXELS = (4 * TILE_SIZE) ** 2;

/** Tiles read and painted at once, per file. */
const CONCURRENT_TILES = 4;

/** Where the file's pixels are on the ground: to and from its coordinate system. */
export interface CogProjection {
  toFile(longitude: number, latitude: number): [number, number] | null;
  toLonLat(x: number, y: number): [number, number] | null;
}

/** The two systems every view knows, and anything else by the definition the catalogue sent. */
export function cogProjection(options: Pick<CogOptions, 'projection' | 'projectionDefinition'>): CogProjection | undefined {
  if (options.projection === 'EPSG:4326') {
    return { toFile: (lon, lat) => [lon, lat], toLonLat: (x, y) => [x, y] };
  }
  if (options.projection === 'EPSG:3857') {
    return { toFile: (lon, lat) => toMercator(lon, lat), toLonLat: (x, y) => fromMercator(x, y) };
  }
  if (!options.projectionDefinition) {
    return undefined;
  }
  try {
    const projection = definedProjection(options.projectionDefinition);
    return { toFile: projection.forward, toLonLat: projection.inverse };
  } catch {
    return undefined;
  }
}

/** The file's layout: its levels, full resolution first, and how pixels map to its coordinates. */
export interface CogGeometry {
  levels: { width: number; height: number }[];
  /** The top-left corner of pixel (0, 0), in the file's coordinate system. */
  origin: [number, number];
  /** Coordinate units per full-resolution pixel; the second is negative for a north-up file. */
  resolution: [number, number];
  projection: CogProjection;
}

/** What one tile reads: a window of one level, and where each pixel of the tile falls in it. */
export interface CogTilePlan {
  level: number;
  /** Columns and rows of that level: first column, first row, end column, end row (exclusive). */
  window: [number, number, number, number];
  /**
   * `(GRID_STEPS + 1)²` grid points across the tile, west to east then north to south, as column
   * and row of the level relative to the window; NaN where the point has no position in the file.
   */
  nodes: Float64Array;
  steps: number;
}

/**
 * Which level and which window of it one tile of the globe needs, or why it needs none: the tile
 * is off the file, or the file has no level coarse enough to paint it from a bounded read.
 */
export function planCogTile(
  geometry: CogGeometry,
  x: number,
  y: number,
  level: number,
  tileSize = TILE_SIZE,
  steps = GRID_STEPS,
): CogTilePlan | 'outside' | 'too_large' {
  const [west, south, east, north] = tileExtent(x, y, level);
  const count = steps + 1;
  const full = new Float64Array(count * count * 2);
  const [ox, oy] = geometry.origin;
  const [rx, ry] = geometry.resolution;
  let anyPlaced = false;
  for (let j = 0; j < count; j++) {
    for (let i = 0; i < count; i++) {
      const [lon, lat] = fromMercator(west + ((east - west) * i) / steps, north - ((north - south) * j) / steps);
      const placed = geometry.projection.toFile(lon, lat);
      const at = (j * count + i) * 2;
      if (placed) {
        full[at] = (placed[0] - ox) / rx;
        full[at + 1] = (placed[1] - oy) / ry;
        anyPlaced = true;
      } else {
        full[at] = Number.NaN;
        full[at + 1] = Number.NaN;
      }
    }
  }
  if (!anyPlaced) {
    return 'outside';
  }

  // How many full-resolution pixels one tile pixel spans, along the finer of the two directions:
  // the level is chosen so the tile is never painted from fewer pixels than it shows.
  let across = 0;
  let acrossCount = 0;
  let down = 0;
  let downCount = 0;
  for (let j = 0; j < count; j++) {
    for (let i = 0; i < count; i++) {
      const at = (j * count + i) * 2;
      if (i + 1 < count) {
        const step = Math.hypot(full[at + 2]! - full[at]!, full[at + 3]! - full[at + 1]!);
        if (Number.isFinite(step)) {
          across += step;
          acrossCount++;
        }
      }
      if (j + 1 < count) {
        const below = at + count * 2;
        const step = Math.hypot(full[below]! - full[at]!, full[below + 1]! - full[at + 1]!);
        if (Number.isFinite(step)) {
          down += step;
          downCount++;
        }
      }
    }
  }
  const pixelsPerStep = tileSize / steps;
  const density = Math.min(
    acrossCount ? across / acrossCount / pixelsPerStep : Infinity,
    downCount ? down / downCount / pixelsPerStep : Infinity,
  );

  const levels = geometry.levels;
  const fullWidth = levels[0]!.width;
  const fullHeight = levels[0]!.height;
  let chosen = 0;
  for (let index = 1; index < levels.length; index++) {
    if (fullWidth / levels[index]!.width <= density) {
      chosen = index;
    }
  }
  const { width, height } = levels[chosen]!;
  const scaleX = width / fullWidth;
  const scaleY = height / fullHeight;

  let minColumn = Infinity;
  let minRow = Infinity;
  let maxColumn = -Infinity;
  let maxRow = -Infinity;
  for (let at = 0; at < full.length; at += 2) {
    const column = full[at]! * scaleX;
    const row = full[at + 1]! * scaleY;
    if (Number.isFinite(column) && Number.isFinite(row)) {
      minColumn = Math.min(minColumn, column);
      maxColumn = Math.max(maxColumn, column);
      minRow = Math.min(minRow, row);
      maxRow = Math.max(maxRow, row);
    }
  }
  // One pixel of margin each way: between grid points a reprojected edge bows by a fraction of one.
  const window: [number, number, number, number] = [
    Math.max(0, Math.floor(minColumn) - 1),
    Math.max(0, Math.floor(minRow) - 1),
    Math.min(width, Math.ceil(maxColumn) + 1),
    Math.min(height, Math.ceil(maxRow) + 1),
  ];
  if (window[0] >= window[2] || window[1] >= window[3]) {
    return 'outside';
  }
  if ((window[2] - window[0]) * (window[3] - window[1]) > MAX_WINDOW_PIXELS) {
    return 'too_large';
  }

  const nodes = new Float64Array(full.length);
  for (let at = 0; at < full.length; at += 2) {
    nodes[at] = full[at]! * scaleX - window[0];
    nodes[at + 1] = full[at + 1]! * scaleY - window[1];
  }
  return { level: chosen, window, nodes, steps };
}

/** One window's samples, band by band, and its mask when the file has one. */
export interface CogWindowData {
  width: number;
  height: number;
  bands: ArrayLike<number>[];
  mask?: ArrayLike<number>;
}

/**
 * Paints one tile from the window its plan read: each tile pixel takes the window's pixel under
 * it (the nearest — the globe smooths a tile itself when it enlarges it), stretched and coloured
 * by the file's rules. Off the file, or see-through by its nodata or mask, a pixel is left clear.
 */
export function paintCogTile(
  plan: CogTilePlan,
  data: CogWindowData,
  drawing: CogDrawing,
  out: Uint8ClampedArray,
  tileSize = TILE_SIZE,
): void {
  const { nodes, steps } = plan;
  const count = steps + 1;
  const bandCount = drawing.bands.length;
  const bytes = new Array<number>(bandCount);
  for (let py = 0; py < tileSize; py++) {
    const v = ((py + 0.5) / tileSize) * steps;
    const j = Math.min(steps - 1, Math.floor(v));
    const fv = v - j;
    for (let px = 0; px < tileSize; px++) {
      const u = ((px + 0.5) / tileSize) * steps;
      const i = Math.min(steps - 1, Math.floor(u));
      const fu = u - i;
      const a = (j * count + i) * 2;
      const b = a + 2;
      const c = a + count * 2;
      const d = c + 2;
      const column = Math.floor(
        (1 - fv) * ((1 - fu) * nodes[a]! + fu * nodes[b]!) + fv * ((1 - fu) * nodes[c]! + fu * nodes[d]!),
      );
      const row = Math.floor(
        (1 - fv) * ((1 - fu) * nodes[a + 1]! + fu * nodes[b + 1]!) +
          fv * ((1 - fu) * nodes[c + 1]! + fu * nodes[d + 1]!),
      );
      const offset = (py * tileSize + px) * 4;
      // NaN fails every comparison, so a pixel between points with no position is left clear too.
      if (!(column >= 0 && row >= 0 && column < data.width && row < data.height)) {
        out[offset + 3] = 0;
        continue;
      }
      const sample = row * data.width + column;
      if (data.mask && !data.mask[sample]) {
        out[offset + 3] = 0;
        continue;
      }
      let allNodata = drawing.nodata !== undefined;
      for (let band = 0; band < bandCount; band++) {
        const value = data.bands[band]![sample]!;
        if (allNodata && !isNodata(value, drawing.nodata)) {
          allNodata = false;
        }
        bytes[band] = scaledByte(value, drawing.min, drawing.max);
      }
      if (allNodata) {
        out[offset + 3] = 0;
        continue;
      }
      writeBandColour(bytes, bandCount, out, offset);
    }
  }
}

/** A file opened and understood: how to read it, how to paint it, and where it is. */
export interface OpenedCog {
  geometry: CogGeometry;
  drawing: CogDrawing;
  /** The images, full resolution first, and the mask beside each when the file has masks. */
  images: GeoTIFFImage[];
  masks: (GeoTIFFImage | undefined)[];
  /** True when the file's photometric interpretation is converted to RGB on reading. */
  rgb: boolean;
  bounds: Scene3DBounds;
  maximumLevel: number;
  /** True when the file has no levels coarse enough to be drawn from far out. */
  shortPyramid: boolean;
}

/** Why a catalogue GeoTIFF cannot be drawn in the scene; the 3D layer list words each. */
export type CogProblem =
  | 'cog.unreadable'
  | 'cog.not_georeferenced'
  | 'cog.projection_unusable'
  | 'cog.band_missing';

/** TIFF photometric interpretations OpenLayers converts to RGB when reading ("auto"). */
const CONVERTED_TO_RGB = new Set([5, 6, 8, 9]);

function isMask(image: GeoTIFFImage): boolean {
  const subfile = Number(image.fileDirectory.getValue('NewSubfileType') ?? 0);
  return (subfile & 4) === 4;
}

function finiteNumber(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : typeof value === 'string' ? Number.parseFloat(value) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : undefined;
}

/**
 * The lon/lat box the file covers: its outline walked in the file's own system and projected,
 * because a national grid's rectangle is not a rectangle in degrees.
 */
function lonLatBounds(geometry: CogGeometry): Scene3DBounds | undefined {
  const { width, height } = geometry.levels[0]!;
  const [ox, oy] = geometry.origin;
  const [rx, ry] = geometry.resolution;
  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;
  const along = 16;
  for (let k = 0; k <= along; k++) {
    const t = k / along;
    for (const [column, row] of [
      [t * width, 0],
      [t * width, height],
      [0, t * height],
      [width, t * height],
    ] as const) {
      const lonLat = geometry.projection.toLonLat(ox + column * rx, oy + row * ry);
      if (lonLat) {
        west = Math.min(west, lonLat[0]);
        east = Math.max(east, lonLat[0]);
        south = Math.min(south, lonLat[1]);
        north = Math.max(north, lonLat[1]);
      }
    }
  }
  if (!(west < east && south < north)) {
    return undefined;
  }
  return [
    Math.max(-180, west),
    Math.max(-MERCATOR_MAX_LATITUDE, south),
    Math.min(180, east),
    Math.min(MERCATOR_MAX_LATITUDE, north),
  ];
}

/** The deepest level of the pyramid with detail in it: where one tile pixel is one file pixel. */
function deepestLevel(geometry: CogGeometry): number {
  const { width, height } = geometry.levels[0]!;
  const [ox, oy] = geometry.origin;
  const [rx, ry] = geometry.resolution;
  const column = width / 2;
  const row = height / 2;
  const centre = geometry.projection.toLonLat(ox + column * rx, oy + row * ry);
  const beside = geometry.projection.toLonLat(ox + (column + 1) * rx, oy + (row + 1) * ry);
  if (!centre || !beside) {
    return 0;
  }
  const [cx, cy] = toMercator(centre[0], centre[1]);
  const [bx, by] = toMercator(beside[0], beside[1]);
  // The diagonal step, back to one side.
  return levelForResolution(Math.hypot(bx - cx, by - cy) / Math.SQRT2, TILE_SIZE);
}

/** Reads a file's header and decides how it is drawn, or says why it cannot be. */
export async function openCog(tiff: GeoTIFF, options: CogOptions): Promise<OpenedCog | CogProblem> {
  const projection = cogProjection(options);
  if (!projection) {
    return 'cog.projection_unusable';
  }
  const count = await tiff.getImageCount();
  const all = await Promise.all(Array.from({ length: count }, (_, index) => tiff.getImage(index)));
  const images = all.filter((image) => !isMask(image)).sort((a, b) => b.getWidth() - a.getWidth());
  const maskImages = all.filter(isMask);
  const full = images[0];
  if (!full) {
    return 'cog.unreadable';
  }

  let origin: number[];
  let resolution: number[];
  try {
    origin = full.getOrigin();
    resolution = full.getResolution();
  } catch {
    return 'cog.not_georeferenced';
  }
  if (!origin.every(Number.isFinite) || !resolution.every(Number.isFinite) || !resolution[0] || !resolution[1]) {
    return 'cog.not_georeferenced';
  }

  const samplesPerPixel = full.getSamplesPerPixel();
  const photometric = Number(full.fileDirectory.getValue('PhotometricInterpretation'));
  const rgb = samplesPerPixel === 3 && CONVERTED_TO_RGB.has(photometric);
  const metadata = await full.getGDALMetadata(0);
  const facts: CogFileFacts = {
    samplesPerPixel: rgb ? 3 : samplesPerPixel,
    sampleFormat: rgb ? 1 : full.getSampleFormat(0),
    bitsPerSample: rgb ? 8 : full.getBitsPerSample(0),
    statistics: {
      min: finiteNumber(metadata?.STATISTICS_MINIMUM),
      max: finiteNumber(metadata?.STATISTICS_MAXIMUM),
    },
    nodata: full.getGDALNoData(),
    hasMask: maskImages.length > 0,
  };
  const drawing = cogDrawing(options, facts);
  if (typeof drawing === 'string') {
    return drawing;
  }

  const geometry: CogGeometry = {
    levels: images.map((image) => ({ width: image.getWidth(), height: image.getHeight() })),
    origin: [origin[0]!, origin[1]!],
    resolution: [resolution[0]!, resolution[1]!],
    projection,
  };
  const bounds = lonLatBounds(geometry);
  if (!bounds) {
    return 'cog.projection_unusable';
  }
  // A mask is matched to its image by size, not by position in the file: writers order them
  // differently, and a mask paired with the wrong level would cut holes in the wrong places.
  const masks = images.map((image) =>
    maskImages.find((mask) => mask.getWidth() === image.getWidth() && mask.getHeight() === image.getHeight()),
  );
  const coarsest = geometry.levels[geometry.levels.length - 1]!;
  return {
    geometry,
    drawing,
    images,
    masks,
    rgb,
    bounds,
    maximumLevel: deepestLevel(geometry),
    shortPyramid: coarsest.width * coarsest.height > MAX_WINDOW_PIXELS,
  };
}

/** Reads one plan's window: the drawn bands (or RGB), and the mask when there is one. */
export async function readCogWindow(
  cog: OpenedCog,
  plan: CogTilePlan,
  pool: Pool | null,
  signal: AbortSignal,
): Promise<CogWindowData> {
  const image = cog.images[plan.level]!;
  const mask = cog.masks[plan.level];
  const window = plan.window;
  const width = window[2] - window[0];
  const height = window[3] - window[1];
  const read = cog.rgb
    ? image.readRGB({ window, interleave: false, pool, signal, enableAlpha: false })
    : image.readRasters({ window, samples: cog.drawing.bands.map((band) => band - 1), interleave: false, pool, signal });
  const [rasters, maskRasters] = await Promise.all([
    read,
    mask ? mask.readRasters({ window, samples: [0], interleave: false, pool, signal }) : undefined,
  ]);
  const bands = rasters as unknown as TypedArray[];
  return {
    width,
    height,
    bands: cog.rgb ? cog.drawing.bands.map((band) => bands[band - 1]!) : bands,
    mask: maskRasters ? (maskRasters as unknown as TypedArray[])[0] : undefined,
  };
}

let sharedPool: Pool | null | undefined;

/**
 * Decoding runs in workers, as the flat map's does, so a tile of compressed data does not stall
 * the page. One pool for every file in the scene; none where workers do not exist.
 */
function decoderPool(): Pool | null {
  if (sharedPool === undefined) {
    const processors = typeof navigator !== 'undefined' ? navigator.hardwareConcurrency || 2 : 2;
    sharedPool = typeof Worker === 'undefined' ? null : new Pool(Math.max(1, Math.min(4, processors - 1)));
  }
  return sharedPool;
}

/** Writes painted pixels into a tile canvas. */
function canvasOf(pixels: Uint8ClampedArray, size: number): HTMLCanvasElement {
  const canvas = createTileCanvas(size);
  const context = canvas.getContext('2d');
  if (context && typeof context.createImageData === 'function') {
    const image = context.createImageData(size, size);
    image.data.set(pixels);
    context.putImageData(image, 0, 0);
  }
  return canvas;
}

export interface CogTileSourceDependencies {
  /** Opens the file; range requests in the application, an in-memory file in a test. */
  openFile?: (url: string, signal: AbortSignal) => Promise<GeoTIFF>;
  pool?: Pool | null;
  /** Turns painted pixels into what the engine takes; a canvas, unless a test looks at the pixels. */
  toImage?: (pixels: Uint8ClampedArray, size: number) => Scene3DTileImage;
}

/**
 * The scene's tile source for one catalogue GeoTIFF. It reports how it stands — readable, drawn
 * only close in, or not drawable and why — so the layer list can say so instead of the globe
 * showing a layer that is on and draws nothing.
 */
export function createCogTileSource(
  url: string,
  options: CogOptions,
  report: RenderedSourceReport,
  dependencies: CogTileSourceDependencies = {},
): Scene3DTileSource {
  const controller = new AbortController();
  const openFile = dependencies.openFile ?? ((address, signal) => fromUrl(address, {}, signal));
  let opened: OpenedCog | undefined;

  const queue = createTileQueue({
    sourceKey: `cog:${url}:${JSON.stringify(options)}`,
    concurrency: CONCURRENT_TILES,
    async draw(x, y, level, signal) {
      const pixels = new Uint8ClampedArray(TILE_SIZE * TILE_SIZE * 4);
      if (opened) {
        const plan = planCogTile(opened.geometry, x, y, level);
        if (typeof plan !== 'string') {
          const data = await readCogWindow(opened, plan, dependencies.pool ?? decoderPool(), signal);
          paintCogTile(plan, data, opened.drawing, pixels);
        }
      }
      return (dependencies.toImage ?? canvasOf)(pixels, TILE_SIZE);
    },
  });

  return {
    async open(): Promise<Scene3DTileSourceInfo> {
      let result: OpenedCog | CogProblem;
      try {
        result = await openCog(await openFile(url, controller.signal), options);
      } catch {
        result = 'cog.unreadable';
      }
      if (controller.signal.aborted) {
        throw new Error('the source was closed');
      }
      if (typeof result === 'string') {
        report({ state: 'unavailable', code: result });
        throw new Error(result);
      }
      opened = result;
      report(result.shortPyramid ? { state: 'limited', code: 'cog.short_pyramid' } : { state: 'ready' });
      return { bounds: result.bounds, maximumLevel: result.maximumLevel, tileSize: TILE_SIZE };
    },
    renderTile: (x, y, level) => queue.request(x, y, level),
    destroy() {
      controller.abort();
      queue.destroy();
    },
  };
}
