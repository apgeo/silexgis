// SPDX-License-Identifier: AGPL-3.0-or-later

// Writes small cloud-optimised GeoTIFFs for tests: tiled, uncompressed, with halving overviews,
// geo-referenced by origin and pixel size, optionally with a nodata value, GDAL statistics and a
// transparency mask — the parts of the format the 3D scene's reader depends on. Generated rather
// than kept as binary fixtures so a test states the picture it reads (every sample comes from a
// function the test passes) and so no tool outside the repository is needed to make one.
//
// Used by the unit tests and by the browser specs, which serve the bytes themselves. Nothing in
// the application imports it.

export interface CogFixtureOptions {
  /** Full-resolution size in pixels. */
  width: number;
  height: number;
  /** Side of a square tile; 16, 32, 256… (a multiple of 16, as the format asks). */
  tileSize: number;
  /** Samples per pixel. */
  bands: number;
  /** 1 unsigned (default), 2 signed, 3 floating point. */
  sampleFormat?: 1 | 2 | 3;
  /** 8 (default), 16 or 32. */
  bitsPerSample?: 8 | 16 | 32;
  /** The top-left corner of the top-left pixel, in the file's coordinate system. */
  origin: [number, number];
  /** Pixel width and height in the file's coordinate system (both positive). */
  pixelSize: [number, number];
  /** EPSG code of the coordinate system: 4326 is written as geographic, anything else as projected. */
  epsg: number;
  /** How many overviews, each half the size of the one before. */
  overviews: number;
  /** Written as GDAL_NODATA. */
  nodata?: number;
  /** Written as GDAL's STATISTICS_MINIMUM / STATISTICS_MAXIMUM for band 1. */
  statistics?: { min: number; max: number };
  /** When given, a mask is written beside every image: true where the pixel is shown. */
  mask?: (column: number, row: number) => boolean;
  /** The sample of a band (from 0) at a full-resolution pixel. Overviews take every 2^n-th one. */
  value: (band: number, column: number, row: number) => number;
}

const TYPE_SHORT = 3;
const TYPE_LONG = 4;
const TYPE_ASCII = 2;
const TYPE_DOUBLE = 12;

interface Entry {
  tag: number;
  type: number;
  values: number[] | string;
}

interface Ifd {
  entries: Entry[];
  /** The tiles' bytes, one buffer per tile, row by row. */
  tiles: Uint8Array[];
}

function typeSize(type: number): number {
  return type === TYPE_SHORT ? 2 : type === TYPE_DOUBLE ? 8 : type === TYPE_ASCII ? 1 : 4;
}

function count(entry: Entry): number {
  return typeof entry.values === 'string' ? entry.values.length + 1 : entry.values.length;
}

function writeSample(view: DataView, offset: number, format: number, bits: number, value: number) {
  if (format === 3) {
    if (bits === 32) view.setFloat32(offset, value, true);
    else view.setFloat64(offset, value, true);
    return;
  }
  const signed = format === 2;
  if (bits === 8) {
    if (signed) view.setInt8(offset, value);
    else view.setUint8(offset, value);
  } else if (bits === 16) {
    if (signed) view.setInt16(offset, value, true);
    else view.setUint16(offset, value, true);
  } else if (signed) {
    view.setInt32(offset, value, true);
  } else {
    view.setUint32(offset, value, true);
  }
}

function tilesOf(
  width: number,
  height: number,
  tileSize: number,
  samples: number,
  bytesPerSample: number,
  write: (view: DataView, offset: number, column: number, row: number, sample: number) => void,
): Uint8Array[] {
  const tiles: Uint8Array[] = [];
  const across = Math.ceil(width / tileSize);
  const down = Math.ceil(height / tileSize);
  for (let ty = 0; ty < down; ty++) {
    for (let tx = 0; tx < across; tx++) {
      const bytes = new Uint8Array(tileSize * tileSize * samples * bytesPerSample);
      const view = new DataView(bytes.buffer);
      for (let y = 0; y < tileSize; y++) {
        for (let x = 0; x < tileSize; x++) {
          const column = tx * tileSize + x;
          const row = ty * tileSize + y;
          if (column >= width || row >= height) continue;
          for (let s = 0; s < samples; s++) {
            write(view, ((y * tileSize + x) * samples + s) * bytesPerSample, column, row, s);
          }
        }
      }
      tiles.push(bytes);
    }
  }
  return tiles;
}

/** A tiled GeoTIFF with overviews (and masks), as an ArrayBuffer. */
export function writeCogFixture(options: CogFixtureOptions): ArrayBuffer {
  const format = options.sampleFormat ?? 1;
  const bits = options.bitsPerSample ?? 8;
  const bytesPerSample = bits / 8;
  const geographic = options.epsg === 4326;
  const ifds: Ifd[] = [];

  for (let level = 0; level <= options.overviews; level++) {
    const factor = 2 ** level;
    const width = Math.max(1, Math.ceil(options.width / factor));
    const height = Math.max(1, Math.ceil(options.height / factor));
    const common = (subfile: number, samples: number, photometric: number, bitsEach: number, sampleFormat: number) => [
      { tag: 254, type: TYPE_LONG, values: [subfile] },
      { tag: 256, type: TYPE_LONG, values: [width] },
      { tag: 257, type: TYPE_LONG, values: [height] },
      { tag: 258, type: TYPE_SHORT, values: Array(samples).fill(bitsEach) },
      { tag: 259, type: TYPE_SHORT, values: [1] },
      { tag: 262, type: TYPE_SHORT, values: [photometric] },
      { tag: 277, type: TYPE_SHORT, values: [samples] },
      { tag: 284, type: TYPE_SHORT, values: [1] },
      { tag: 322, type: TYPE_LONG, values: [options.tileSize] },
      { tag: 323, type: TYPE_LONG, values: [options.tileSize] },
      { tag: 339, type: TYPE_SHORT, values: Array(samples).fill(sampleFormat) },
    ];

    const entries: Entry[] = common(
      level === 0 ? 0 : 1,
      options.bands,
      options.bands >= 3 ? 2 : 1,
      bits,
      format,
    );
    if (level === 0) {
      entries.push(
        { tag: 33550, type: TYPE_DOUBLE, values: [options.pixelSize[0], options.pixelSize[1], 0] },
        { tag: 33922, type: TYPE_DOUBLE, values: [0, 0, 0, options.origin[0], options.origin[1], 0] },
        {
          tag: 34735,
          type: TYPE_SHORT,
          values: [
            1, 1, 0, 3,
            1024, 0, 1, geographic ? 2 : 1,
            1025, 0, 1, 1,
            geographic ? 2048 : 3072, 0, 1, options.epsg,
          ],
        },
      );
      if (options.statistics) {
        entries.push({
          tag: 42112,
          type: TYPE_ASCII,
          values:
            '<GDALMetadata>' +
            `<Item name="STATISTICS_MINIMUM" sample="0">${options.statistics.min}</Item>` +
            `<Item name="STATISTICS_MAXIMUM" sample="0">${options.statistics.max}</Item>` +
            '</GDALMetadata>',
        });
      }
    }
    if (options.nodata !== undefined) {
      entries.push({ tag: 42113, type: TYPE_ASCII, values: String(options.nodata) });
    }
    ifds.push({
      entries,
      tiles: tilesOf(width, height, options.tileSize, options.bands, bytesPerSample, (view, offset, column, row, sample) =>
        writeSample(view, offset, format, bits, options.value(sample, column * factor, row * factor)),
      ),
    });

    if (options.mask) {
      const mask = options.mask;
      ifds.push({
        entries: common(level === 0 ? 4 : 5, 1, 4, 8, 1),
        tiles: tilesOf(width, height, options.tileSize, 1, 1, (view, offset, column, row) =>
          view.setUint8(offset, mask(column * factor, row * factor) ? 255 : 0),
        ),
      });
    }
  }

  // Layout: header, then every directory with the values that do not fit in an entry, then tiles.
  const directorySizes = ifds.map((ifd) => {
    const withTileTags = ifd.entries.length + 2;
    const overflow = ifd.entries.reduce((total, entry) => {
      const size = count(entry) * typeSize(entry.type);
      return total + (size > 4 ? size + (size % 2) : 0);
    }, 0);
    const tileTables = ifd.tiles.length > 1 ? ifd.tiles.length * 8 : 0;
    return 2 + withTileTags * 12 + 4 + overflow + tileTables;
  });
  const directoriesEnd = 8 + directorySizes.reduce((a, b) => a + b, 0);
  const tileBytes = ifds.reduce((total, ifd) => total + ifd.tiles.reduce((t, tile) => t + tile.length, 0), 0);
  const buffer = new ArrayBuffer(directoriesEnd + tileBytes);
  const view = new DataView(buffer);
  const bytes = new Uint8Array(buffer);
  view.setUint8(0, 0x49);
  view.setUint8(1, 0x49);
  view.setUint16(2, 42, true);
  view.setUint32(4, 8, true);

  let directoryAt = 8;
  let tileAt = directoriesEnd;
  ifds.forEach((ifd, index) => {
    const offsets: number[] = [];
    const counts: number[] = [];
    for (const tile of ifd.tiles) {
      bytes.set(tile, tileAt);
      offsets.push(tileAt);
      counts.push(tile.length);
      tileAt += tile.length;
    }
    const entries = [
      ...ifd.entries,
      { tag: 324, type: TYPE_LONG, values: offsets },
      { tag: 325, type: TYPE_LONG, values: counts },
    ].sort((a, b) => a.tag - b.tag);

    view.setUint16(directoryAt, entries.length, true);
    let overflowAt = directoryAt + 2 + entries.length * 12 + 4;
    entries.forEach((entry, i) => {
      const at = directoryAt + 2 + i * 12;
      const n = count(entry);
      const size = n * typeSize(entry.type);
      view.setUint16(at, entry.tag, true);
      view.setUint16(at + 2, entry.type, true);
      view.setUint32(at + 4, n, true);
      const target = size > 4 ? overflowAt : at + 8;
      if (size > 4) {
        view.setUint32(at + 8, overflowAt, true);
        overflowAt += size + (size % 2);
      }
      if (typeof entry.values === 'string') {
        for (let c = 0; c < entry.values.length; c++) view.setUint8(target + c, entry.values.charCodeAt(c));
        view.setUint8(target + entry.values.length, 0);
      } else {
        entry.values.forEach((value, k) => {
          const offset = target + k * typeSize(entry.type);
          if (entry.type === TYPE_SHORT) view.setUint16(offset, value, true);
          else if (entry.type === TYPE_DOUBLE) view.setFloat64(offset, value, true);
          else view.setUint32(offset, value, true);
        });
      }
    });
    const next = index + 1 < ifds.length ? directoryAt + directorySizes[index]! : 0;
    view.setUint32(directoryAt + 2 + entries.length * 12, next, true);
    directoryAt += directorySizes[index]!;
  });
  return buffer;
}
