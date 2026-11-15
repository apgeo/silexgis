// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * One colour table for a whole GIF, and the mapping of every pixel onto it.
 *
 * <b>One table, not one per frame.</b> A camera orbiting a model changes almost every pixel of
 * every frame, and a table rebuilt per frame moves every colour a little each time — the model
 * visibly shimmers. A single table built from frames sampled across the movie keeps a surface the
 * same colour from the first frame to the last, and lets frames be compressed independently of
 * each other, which is what makes encoding them in parallel possible.
 *
 * <b>Reserved colours are placed exactly.</b> Marker dots, label plates and caption text are flat
 * colours the host chose, and a nearest-colour table would shift them to whatever the model's
 * shading made common. They get entries of their own, and a pixel of exactly that colour always
 * maps to its entry.
 *
 * <b>No dithering.</b> It adds noise that ruins LZW's runs and looks wrong on line art.
 *
 * <b>The table is median cut over a 15-bit histogram</b> (five bits a channel): the box with the
 * largest spread is split at its weighted median along its widest channel until the table is full,
 * and each box contributes the mean of the actual pixels it holds. Mapping goes through a 32768-cell
 * lookup built once from the same histogram — each cell maps to the entry nearest the mean of the
 * sampled pixels that fell in it, or to the entry nearest its centre when none did — so mapping a
 * pixel is one table read, and every thread that maps with the same lookup produces the same
 * indices.
 */

/** A colour as 0xRRGGBB. */
export type Rgb24 = number;

export interface GifColorMap {
  /** The global colour table: `tableSize` RGB triples, unused entries black. */
  table: Uint8Array;
  /** A power of two from 2 to 256. */
  tableSize: number;
  /** Entries 0..colorCount-1 are colours; nothing maps to an index at or above it. */
  colorCount: number;
  /** The one index kept for "unchanged from the previous frame". Equal to colorCount. */
  transparentIndex: number;
  /** The LZW minimum code size for this table. */
  minCodeSize: number;
  /** 32768 cells (five bits per channel, red highest) to a colour index. */
  lut: Uint8Array;
  /** 1 for a cell that contains a reserved colour, whose pixels are first matched exactly. */
  reservedCells: Uint8Array;
  /** The reserved colours as 0xRRGGBB, and the entry each one has. */
  reservedRgb: Uint32Array;
  reservedIndex: Uint8Array;
}

/** What the palette is built from: RGBA frames, alpha ignored (captured frames are opaque). */
export interface GifSample {
  data: Uint8Array | Uint8ClampedArray;
  width: number;
  height: number;
}

const CELLS = 1 << 15;
// Enough to see every colour a frame has in quantity without walking millions of pixels per sample.
const MAX_SAMPLED_PIXELS = 1_500_000;

/** Parses '#rrggbb' colours, dropping repeats. Anything else is refused rather than guessed. */
export function parseReservedColors(colors: readonly string[] | undefined): Rgb24[] {
  const out: Rgb24[] = [];
  for (const c of colors ?? []) {
    if (!/^#[0-9a-fA-F]{6}$/.test(c)) throw new Error(`reserved colour '${c}' is not #rrggbb`);
    const v = parseInt(c.slice(1), 16);
    if (!out.includes(v)) out.push(v);
  }
  return out;
}

function cellOf(r: number, g: number, b: number): number {
  return ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
}

interface Box {
  cells: number[];
  count: number;
  score: number;
  axis: number;
}

/**
 * Builds the colour map from sampled frames.
 *
 * `maxColors` is the table size to stay within, the transparent entry included (so at most
 * `maxColors - 1` colours); reserved colours are placed first and the rest of the table goes to
 * the median cut.
 */
export function buildColorMap(
  samples: readonly GifSample[],
  options: { maxColors: number; reserved: readonly Rgb24[] },
): GifColorMap {
  const reserved = [...new Set(options.reserved)];
  const maxColors = Math.min(256, Math.max(2, Math.floor(options.maxColors)));
  if (reserved.length > 255) throw new Error(`${reserved.length} reserved colours do not fit a GIF colour table`);
  const cap = Math.max(maxColors, nextPowerOfTwo(reserved.length + 1));
  if (samples.length === 0) throw new Error('no sample frames to build a palette from');

  const reservedSet = new Set(reserved);
  const reservedCells = new Uint8Array(CELLS);
  for (const c of reserved) reservedCells[cellOf(c >> 16, (c >> 8) & 0xff, c & 0xff)] = 1;

  // Histogram of the sampled pixels that are not a reserved colour exactly.
  const count = new Float64Array(CELLS);
  const sumR = new Float64Array(CELLS);
  const sumG = new Float64Array(CELLS);
  const sumB = new Float64Array(CELLS);
  let total = 0;
  for (const s of samples) total += s.width * s.height;
  for (const s of samples) {
    const pixels = s.width * s.height;
    if (s.data.length < pixels * 4) throw new Error('a sample frame is shorter than its size');
    // A stride sharing no factor with the row length, so the samples do not line up in columns.
    let stride = Math.max(1, Math.ceil(total / MAX_SAMPLED_PIXELS));
    while (stride > 1 && gcd(stride, s.width) !== 1) stride++;
    const d = s.data;
    for (let p = 0; p < pixels; p += stride) {
      const o = p * 4;
      const r = d[o];
      const g = d[o + 1];
      const b = d[o + 2];
      const cell = cellOf(r, g, b);
      if (reservedCells[cell] === 1 && reservedSet.has((r << 16) | (g << 8) | b)) continue;
      count[cell]++;
      sumR[cell] += r;
      sumG[cell] += g;
      sumB[cell] += b;
    }
  }

  const occupied: number[] = [];
  for (let c = 0; c < CELLS; c++) if (count[c] > 0) occupied.push(c);
  const target = cap - 1 - reserved.length;
  const mean = (c: number, axis: number) =>
    (axis === 0 ? sumR[c] : axis === 1 ? sumG[c] : sumB[c]) / count[c];

  const makeBox = (cells: number[]): Box => {
    let n = 0;
    const s = [0, 0, 0];
    const sq = [0, 0, 0];
    for (const c of cells) {
      const w = count[c];
      n += w;
      for (let a = 0; a < 3; a++) {
        const m = mean(c, a);
        s[a] += w * m;
        sq[a] += w * m * m;
      }
    }
    const variance = [0, 1, 2].map((a) => sq[a] - (s[a] * s[a]) / n);
    const axis = variance[0] >= variance[1] && variance[0] >= variance[2] ? 0 : variance[1] >= variance[2] ? 1 : 2;
    const spread = variance[0] + variance[1] + variance[2];
    return { cells, count: n, axis, score: cells.length > 1 ? spread : -1 };
  };

  const boxes: Box[] = [];
  if (occupied.length > 0 && target > 0) boxes.push(makeBox(occupied));
  while (boxes.length < target) {
    let pick = -1;
    for (let i = 0; i < boxes.length; i++) {
      if (boxes[i].score > 0 && (pick < 0 || boxes[i].score > boxes[pick].score)) pick = i;
    }
    if (pick < 0) break;
    const box = boxes[pick];
    const axis = box.axis;
    const sorted = [...box.cells].sort((a, b) => mean(a, axis) - mean(b, axis) || a - b);
    const half = box.count / 2;
    let acc = 0;
    let cut = 1;
    for (let i = 0; i < sorted.length - 1; i++) {
      acc += count[sorted[i]];
      cut = i + 1;
      if (acc >= half) break;
    }
    boxes.splice(pick, 1, makeBox(sorted.slice(0, cut)), makeBox(sorted.slice(cut)));
  }

  const colors: Rgb24[] = [];
  for (const box of boxes) {
    let n = 0;
    let r = 0;
    let g = 0;
    let b = 0;
    for (const c of box.cells) {
      n += count[c];
      r += sumR[c];
      g += sumG[c];
      b += sumB[c];
    }
    const rgb = (Math.round(r / n) << 16) | (Math.round(g / n) << 8) | Math.round(b / n);
    if (!reservedSet.has(rgb) && !colors.includes(rgb)) colors.push(rgb);
  }
  const reservedIndex = new Uint8Array(reserved.length);
  for (let i = 0; i < reserved.length; i++) {
    reservedIndex[i] = colors.length;
    colors.push(reserved[i]);
  }

  const colorCount = colors.length;
  const tableSize = Math.max(2, nextPowerOfTwo(colorCount + 1));
  const table = new Uint8Array(tableSize * 3);
  const cr = new Int32Array(colorCount);
  const cg = new Int32Array(colorCount);
  const cb = new Int32Array(colorCount);
  colors.forEach((c, i) => {
    cr[i] = c >> 16;
    cg[i] = (c >> 8) & 0xff;
    cb[i] = c & 0xff;
    table.set([cr[i], cg[i], cb[i]], i * 3);
  });

  const lut = new Uint8Array(CELLS);
  for (let c = 0; c < CELLS; c++) {
    const r = count[c] > 0 ? sumR[c] / count[c] : ((c >> 10) << 3) | 4;
    const g = count[c] > 0 ? sumG[c] / count[c] : (((c >> 5) & 31) << 3) | 4;
    const b = count[c] > 0 ? sumB[c] / count[c] : ((c & 31) << 3) | 4;
    let best = 0;
    let bestD = Infinity;
    for (let i = 0; i < colorCount; i++) {
      const dr = r - cr[i];
      const dg = g - cg[i];
      const db = b - cb[i];
      const d = dr * dr + dg * dg + db * db;
      if (d < bestD) {
        bestD = d;
        best = i;
      }
    }
    lut[c] = best;
  }

  return {
    table,
    tableSize,
    colorCount,
    transparentIndex: colorCount,
    minCodeSize: Math.max(2, Math.log2(tableSize)),
    lut,
    reservedCells,
    reservedRgb: Uint32Array.from(reserved),
    reservedIndex,
  };
}

/** The colour index of one pixel. */
export function mapPixel(map: GifColorMap, r: number, g: number, b: number): number {
  const cell = cellOf(r, g, b);
  if (map.reservedCells[cell] === 1) {
    const rgb = (r << 16) | (g << 8) | b;
    const rs = map.reservedRgb;
    for (let i = 0; i < rs.length; i++) if (rs[i] === rgb) return map.reservedIndex[i];
  }
  return map.lut[cell];
}

/** Maps `out.length` RGBA pixels of `rgba` to colour indices. */
export function mapPixels(map: GifColorMap, rgba: Uint8Array | Uint8ClampedArray, out: Uint8Array): void {
  const { lut, reservedCells } = map;
  for (let p = 0, o = 0; p < out.length; p++, o += 4) {
    const r = rgba[o];
    const g = rgba[o + 1];
    const b = rgba[o + 2];
    const cell = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
    out[p] = reservedCells[cell] === 1 ? mapPixel(map, r, g, b) : lut[cell];
  }
}

function nextPowerOfTwo(n: number): number {
  let p = 1;
  while (p < n) p <<= 1;
  return p;
}

function gcd(a: number, b: number): number {
  while (b !== 0) [a, b] = [b, a % b];
  return a;
}
