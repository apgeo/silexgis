// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromArrayBuffer } from 'geotiff';
import { afterEach, describe, expect, it } from 'vitest';
import { definedProjection } from '../geo/formats/nationalGrids.ts';
import type { CogOptions } from '../map/cogScaling.ts';
import { writeCogFixture, type CogFixtureOptions } from './cogFixture.ts';
import {
  createCogTileSource,
  openCog,
  paintCogTile,
  planCogTile,
  readCogWindow,
  type OpenedCog,
} from './cogImagery3d.ts';
import { forgetDrawnTiles, levelResolution, tileExtent, toMercator, type RenderedSourceStatus } from './renderedTiles3d.ts';

// The GeoTIFFs here are written by the test itself (`cogFixture.ts`) with samples that name their
// own pixel — a column in one band, a row in another — so a painted tile says exactly which pixel
// of which level it was painted from, and the window arithmetic is checked against the file the
// real reader reads, not against a restatement of the arithmetic.

afterEach(() => forgetDrawnTiles());

const options = (overrides: Partial<CogOptions> = {}): CogOptions => ({
  bands: null,
  nodata: null,
  min: 0,
  max: 255,
  projection: 'EPSG:3857',
  projectionDefinition: null,
  ...overrides,
});

/** A web-mercator tile at level 11, the four level-13 tiles under it at full resolution. */
const TILE = { x: 1150, y: 730, level: 11 };
const [west, , , north] = tileExtent(TILE.x, TILE.y, TILE.level);

function mercatorFixture(overrides: Partial<CogFixtureOptions> = {}): ArrayBuffer {
  return writeCogFixture({
    width: 1024,
    height: 1024,
    tileSize: 256,
    bands: 2,
    origin: [west, north],
    pixelSize: [levelResolution(13, 256), levelResolution(13, 256)],
    epsg: 3857,
    overviews: 3,
    value: (band, column, row) => (band === 0 ? column % 256 : row % 256),
    ...overrides,
  });
}

async function opened(buffer: ArrayBuffer, cogOptions: CogOptions = options()): Promise<OpenedCog> {
  const result = await openCog(await fromArrayBuffer(buffer), cogOptions);
  if (typeof result === 'string') {
    throw new Error(result);
  }
  return result;
}

/** Paints one tile through the real reader and returns its pixels. */
async function painted(cog: OpenedCog, x: number, y: number, level: number): Promise<Uint8ClampedArray | string> {
  const plan = planCogTile(cog.geometry, x, y, level);
  if (typeof plan === 'string') {
    return plan;
  }
  const out = new Uint8ClampedArray(256 * 256 * 4);
  paintCogTile(plan, await readCogWindow(cog, plan, null, new AbortController().signal), cog.drawing, out);
  return out;
}

const pixel = (pixels: Uint8ClampedArray, px: number, py: number) => [...pixels.slice((py * 256 + px) * 4, (py * 256 + px) * 4 + 4)];

describe('opening a catalogue GeoTIFF', () => {
  it('finds its levels, full resolution first, and where it lies', async () => {
    const cog = await opened(mercatorFixture());

    expect(cog.geometry.levels).toEqual([
      { width: 1024, height: 1024 },
      { width: 512, height: 512 },
      { width: 256, height: 256 },
      { width: 128, height: 128 },
    ]);
    const [w, s, e, n] = cog.bounds;
    const [tileWest, tileSouth, tileEast, tileNorth] = tileExtent(TILE.x, TILE.y, TILE.level);
    expect(toMercator(w, s)[0]).toBeCloseTo(tileWest, 3);
    expect(toMercator(w, s)[1]).toBeCloseTo(tileSouth, 3);
    expect(toMercator(e, n)[0]).toBeCloseTo(tileEast, 3);
    expect(toMercator(e, n)[1]).toBeCloseTo(tileNorth, 3);
    // Its full resolution is level 13's: deeper than that the globe enlarges level 13.
    expect(cog.maximumLevel).toBe(13);
    expect(cog.shortPyramid).toBe(false);
  });

  it('says why a file cannot be drawn', async () => {
    expect(await openCog(await fromArrayBuffer(mercatorFixture()), options({ bands: [3] }))).toBe('cog.band_missing');
    expect(
      await openCog(await fromArrayBuffer(mercatorFixture()), options({ projection: 'EPSG:3844', projectionDefinition: null })),
    ).toBe('cog.projection_unusable');
  });

  it('stretches by the file’s own statistics and nodata when the entry states none', async () => {
    const cog = await opened(
      mercatorFixture({ bands: 1, bitsPerSample: 16, statistics: { min: 100, max: 900 }, nodata: 0 }),
      options({ min: null, max: null }),
    );
    expect(cog.drawing).toEqual({ bands: [1], min: 100, max: 900, nodata: 0, transparency: true });
  });

  it('notices a pyramid too short to be drawn from far out', async () => {
    const cog = await opened(mercatorFixture({ width: 2048, height: 2048, overviews: 0 }));
    expect(cog.shortPyramid).toBe(true);
  });
});

describe('the window one tile reads', () => {
  it('is the matching piece of full resolution where the globe is as detailed as the file', async () => {
    const cog = await opened(mercatorFixture());
    // The north-west level-13 tile under the fixture's level-11 tile is its first 256 columns.
    const plan = planCogTile(cog.geometry, TILE.x * 4, TILE.y * 4, 13);
    expect(plan).toMatchObject({ level: 0 });
    if (typeof plan === 'string') throw new Error(plan);
    expect(plan.window[0]).toBe(0);
    expect(plan.window[1]).toBe(0);
    expect(plan.window[2]).toBeGreaterThanOrEqual(256);
    expect(plan.window[2]).toBeLessThanOrEqual(258);

    const pixels = await painted(cog, TILE.x * 4 + 1, TILE.y * 4 + 2, 13);
    if (typeof pixels === 'string') throw new Error(pixels);
    // Column 256 + px and row 512 + py of the file, read as grey with alpha (two bands).
    expect(pixel(pixels, 0, 0)).toEqual([0, 0, 0, 0]); // column 256 → 0, row 512 → alpha 0
    expect(pixel(pixels, 10, 20)).toEqual([10, 10, 10, 20]);
    expect(pixel(pixels, 200, 100)).toEqual([200, 200, 200, 100]);
  });

  it('comes from the overview whose detail matches the tile, never a coarser one', async () => {
    const cog = await opened(mercatorFixture());
    expect(planCogTile(cog.geometry, TILE.x * 2, TILE.y * 2, 12)).toMatchObject({ level: 1 });
    expect(planCogTile(cog.geometry, TILE.x, TILE.y, 11)).toMatchObject({ level: 2 });
    // Further out than the coarsest overview: that overview, a small window of it.
    expect(planCogTile(cog.geometry, TILE.x >> 2, TILE.y >> 2, 9)).toMatchObject({ level: 3 });
    // Closer than full resolution: full resolution, a window smaller than the tile.
    const close = planCogTile(cog.geometry, TILE.x * 8, TILE.y * 8, 14);
    if (typeof close === 'string') throw new Error(close);
    expect(close.level).toBe(0);
    expect(close.window[2] - close.window[0]).toBeLessThanOrEqual(130);

    // A level-12 tile, painted from the half-size overview: each pixel is every second column.
    const pixels = await painted(cog, TILE.x * 2, TILE.y * 2, 12);
    if (typeof pixels === 'string') throw new Error(pixels);
    expect(pixel(pixels, 5, 7)).toEqual([10, 10, 10, 14]);
  });

  it('is nothing for a tile off the file', async () => {
    const cog = await opened(mercatorFixture());
    expect(planCogTile(cog.geometry, TILE.x + 3, TILE.y, 11)).toBe('outside');
  });

  it('is refused rather than reading a whole file for a far-out tile of a file with no pyramid', async () => {
    const cog = await opened(mercatorFixture({ width: 2048, height: 2048, overviews: 0 }));
    expect(planCogTile(cog.geometry, TILE.x, TILE.y, 11)).toBe('too_large');
    // Closer in it draws.
    expect(planCogTile(cog.geometry, TILE.x * 4, TILE.y * 4, 13)).toMatchObject({ level: 0 });
  });
});

describe('painting a tile', () => {
  it('stretches by the entry’s range and leaves nodata clear', async () => {
    const cog = await opened(
      mercatorFixture({ bands: 1, nodata: 0, value: (_band, column) => (column % 256 < 8 ? 0 : 100) }),
      options({ min: 0, max: 200 }),
    );
    const pixels = await painted(cog, TILE.x * 4, TILE.y * 4, 13);
    if (typeof pixels === 'string') throw new Error(pixels);
    expect(pixel(pixels, 3, 3)).toEqual([0, 0, 0, 0]);
    expect(pixel(pixels, 50, 3)).toEqual([127, 127, 127, 255]);
  });

  it('leaves clear what the file’s mask hides', async () => {
    const cog = await opened(
      mercatorFixture({ bands: 1, value: () => 60, mask: (column) => column % 256 >= 128 }),
    );
    expect(cog.masks.every(Boolean)).toBe(true);
    const pixels = await painted(cog, TILE.x * 4, TILE.y * 4, 13);
    if (typeof pixels === 'string') throw new Error(pixels);
    expect(pixel(pixels, 10, 10)[3]).toBe(0);
    expect(pixel(pixels, 200, 10)).toEqual([60, 60, 60, 255]);
  });

  it('places a file in a national grid by the definition the catalogue sent, not by its own keys', async () => {
    const stereo70 =
      '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
      '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs';
    // 256 by 256 pixels of 100 m from (450 000, 520 000): red names the column, green the row.
    const buffer = writeCogFixture({
      width: 256,
      height: 256,
      tileSize: 256,
      bands: 3,
      origin: [450000, 520000],
      pixelSize: [100, 100],
      epsg: 3844,
      overviews: 0,
      value: (band, column, row) => (band === 0 ? column : band === 1 ? row : 0),
    });
    const cog = await opened(buffer, options({ projection: 'EPSG:3844', projectionDefinition: stereo70 }));

    // The centre of pixel (column 100, row 50), on the ground, then in a level-14 tile.
    const [lon, lat] = definedProjection(stereo70).inverse(450000 + 100.5 * 100, 520000 - 50.5 * 100)!;
    const [mx, my] = toMercator(lon, lat);
    const level = 14;
    const size = levelResolution(level, 256) * 256;
    const x = Math.floor((mx + 20037508.342789244) / size);
    const y = Math.floor((20037508.342789244 - my) / size);
    const [tileWest, , , tileNorth] = tileExtent(x, y, level);
    const px = Math.floor(((mx - tileWest) / size) * 256);
    const py = Math.floor(((tileNorth - my) / size) * 256);

    const pixels = await painted(cog, x, y, level);
    if (typeof pixels === 'string') throw new Error(pixels);
    expect(pixel(pixels, px, py)).toEqual([100, 50, 0, 255]);
  });
});

describe('the scene’s tile source for a catalogue GeoTIFF', () => {
  const sourceFor = (buffer: ArrayBuffer | Error, cogOptions = options(), url = '/lidar.tif') => {
    const reports: RenderedSourceStatus[] = [];
    const tiles: Uint8ClampedArray[] = [];
    const source = createCogTileSource(url, cogOptions, (status) => reports.push(status), {
      openFile: () => (buffer instanceof Error ? Promise.reject(buffer) : fromArrayBuffer(buffer)),
      pool: null,
      toImage: (pixels) => {
        tiles.push(pixels);
        return {} as HTMLCanvasElement;
      },
    });
    return { source, reports, tiles };
  };

  it('opens with where the file is and how deep it goes, and says it is ready', async () => {
    const { source, reports } = sourceFor(mercatorFixture());
    const info = await source.open();
    expect(info.maximumLevel).toBe(13);
    expect(info.tileSize).toBe(256);
    expect(info.bounds).toHaveLength(4);
    expect(reports).toEqual([{ state: 'ready' }]);
  });

  it('says a file it cannot read is not drawn, and refuses to open', async () => {
    const { source, reports } = sourceFor(new Error('404'), options(), '/missing.tif');
    await expect(source.open()).rejects.toThrow();
    expect(reports).toEqual([{ state: 'unavailable', code: 'cog.unreadable' }]);
  });

  it('says a file with a short pyramid is drawn only close in', async () => {
    const { source, reports } = sourceFor(mercatorFixture({ width: 2048, height: 2048, overviews: 0 }), options(), '/flat.tif');
    await source.open();
    expect(reports).toEqual([{ state: 'limited', code: 'cog.short_pyramid' }]);
  });

  it('draws a few tiles at a time, says "later" past that, and draws a tile once', async () => {
    const { source, tiles } = sourceFor(mercatorFixture(), options(), '/busy.tif');
    await source.open();
    const asked = [0, 1, 2, 3, 4].map((k) => source.renderTile(TILE.x * 4 + (k % 4), TILE.y * 4 + 1 + (k >> 2), 13));
    expect(asked.slice(0, 4).every((promise) => promise !== undefined)).toBe(true);
    expect(asked[4]).toBeUndefined();
    // The same tile asked again while it is drawn is the same work.
    expect(source.renderTile(TILE.x * 4, TILE.y * 4 + 1, 13)).toBe(asked[0]);
    await Promise.all(asked.slice(0, 4));
    expect(tiles).toHaveLength(4);
    // Drawn once and kept: asked again, it is not painted again.
    await source.renderTile(TILE.x * 4, TILE.y * 4 + 1, 13);
    expect(tiles).toHaveLength(4);
  });

  it('paints a tile off the file clear rather than failing it', async () => {
    const { source, tiles } = sourceFor(mercatorFixture(), options(), '/edge.tif');
    await source.open();
    await source.renderTile(TILE.x * 4 + 9, TILE.y * 4, 13);
    expect(tiles[0]!.every((value) => value === 0)).toBe(true);
  });

  it('stops answering once destroyed', async () => {
    const { source } = sourceFor(mercatorFixture(), options(), '/gone.tif');
    await source.open();
    source.destroy();
    await expect(source.renderTile(TILE.x * 4, TILE.y * 4, 13)).rejects.toThrow();
  });
});
