// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { openGifEncoder, type GifMovieEncoder } from './gifEncoder.ts';
import { handleGifWorkerMessage, type GifWorkerReply, type GifWorkerRequest, type GifWorkerState } from './gifFrame.ts';
import { gifCentisecondsAt } from './gifWriter.ts';
import { lzwEncode } from './lzw.ts';
import { buildColorMap, mapPixel, mapPixels, parseReservedColors } from './quantize.ts';

// ---- A GIF reader written from the format description, independent of the writer. ----

function lzwDecode(block: Uint8Array, expected: number): { indices: Uint8Array; clears: number; blockLengths: number[] } {
  const min = block[0];
  const blockLengths: number[] = [];
  const bytes: number[] = [];
  let o = 1;
  for (;;) {
    const len = block[o++];
    if (len === 0) break;
    blockLengths.push(len);
    for (let i = 0; i < len; i++) bytes.push(block[o++]);
  }
  expect(o).toBe(block.length);
  const clear = 1 << min;
  const eoi = clear + 1;
  const prefix = new Int32Array(4096);
  const suffix = new Uint8Array(4096);
  const out: number[] = [];
  let size = min + 1;
  let next = clear + 2;
  let prev = -1;
  let clears = 0;
  let bitPos = 0;
  const read = () => {
    let v = 0;
    for (let i = 0; i < size; i++, bitPos++) {
      if (bitPos >> 3 >= bytes.length) throw new Error('code stream ran out before end-of-information');
      v |= ((bytes[bitPos >> 3] >> (bitPos & 7)) & 1) << i;
    }
    return v;
  };
  const expand = (code: number): number[] => {
    const s: number[] = [];
    while (code >= clear) {
      s.push(suffix[code]);
      code = prefix[code];
    }
    s.push(code);
    return s.reverse();
  };
  for (;;) {
    const code = read();
    if (code === clear) {
      size = min + 1;
      next = clear + 2;
      prev = -1;
      clears++;
      continue;
    }
    if (code === eoi) break;
    if (prev === -1) {
      expect(code).toBeLessThan(clear);
      out.push(code);
      prev = code;
      continue;
    }
    let str: number[];
    if (code < next) str = expand(code);
    else if (code === next) {
      const p = expand(prev);
      str = [...p, p[0]];
    } else throw new Error(`code ${code} is beyond the table (${next})`);
    out.push(...str);
    if (next < 4096) {
      prefix[next] = prev;
      suffix[next] = str[0];
      next++;
      if (next === 1 << size && size < 12) size++;
    }
    prev = code;
  }
  // Nothing but padding after end-of-information.
  expect(Math.ceil(bitPos / 8)).toBe(bytes.length);
  expect(out.length).toBe(expected);
  return { indices: Uint8Array.from(out), clears, blockLengths };
}

interface DecodedImage {
  delay: number;
  disposal: number;
  transparent: number | null;
  x: number;
  y: number;
  width: number;
  height: number;
  indices: Uint8Array;
}

interface DecodedGif {
  width: number;
  height: number;
  tableSize: number;
  table: Uint8Array;
  loop: number | null;
  images: DecodedImage[];
  /** What is on screen after each image, as 0xRRGGBB per pixel. */
  screens: Uint32Array[];
}

function decodeGif(bytes: Uint8Array): DecodedGif {
  const ascii = (o: number, n: number) => String.fromCharCode(...bytes.subarray(o, o + n));
  const u16 = (o: number) => bytes[o] | (bytes[o + 1] << 8);
  expect(ascii(0, 6)).toBe('GIF89a');
  const width = u16(6);
  const height = u16(8);
  const packed = bytes[10];
  expect(packed & 0x80).toBe(0x80);
  const tableSize = 2 << (packed & 7);
  const table = bytes.slice(13, 13 + tableSize * 3);
  let o = 13 + tableSize * 3;
  let loop: number | null = null;
  const images: DecodedImage[] = [];
  const screens: Uint32Array[] = [];
  const screen = new Uint32Array(width * height);
  let gce: { delay: number; disposal: number; transparent: number | null } | null = null;
  for (;;) {
    const b = bytes[o++];
    if (b === 0x3b) break;
    if (b === 0x21) {
      const label = bytes[o++];
      if (label === 0xf9) {
        expect(bytes[o]).toBe(4);
        const flags = bytes[o + 1];
        gce = { delay: u16(o + 2), disposal: (flags >> 2) & 7, transparent: flags & 1 ? bytes[o + 4] : null };
        expect(bytes[o + 5]).toBe(0);
        o += 6;
      } else if (label === 0xff) {
        const app = ascii(o + 1, 11);
        o += 12;
        if (app === 'NETSCAPE2.0') {
          expect(bytes[o]).toBe(3);
          expect(bytes[o + 1]).toBe(1);
          loop = u16(o + 2);
        }
        while (bytes[o] !== 0) o += bytes[o] + 1;
        o++;
      } else {
        while (bytes[o] !== 0) o += bytes[o] + 1;
        o++;
      }
      continue;
    }
    expect(b).toBe(0x2c);
    const x = u16(o);
    const y = u16(o + 2);
    const w = u16(o + 4);
    const h = u16(o + 6);
    expect(bytes[o + 8]).toBe(0); // no local table, not interlaced
    o += 9;
    const start = o;
    o++;
    while (bytes[o] !== 0) o += bytes[o] + 1;
    o++;
    const { indices } = lzwDecode(bytes.subarray(start, o), w * h);
    expect(gce).not.toBeNull();
    const image = { ...gce!, x, y, width: w, height: h, indices };
    images.push(image);
    for (let yy = 0; yy < h; yy++) {
      for (let xx = 0; xx < w; xx++) {
        const i = indices[yy * w + xx];
        if (i === image.transparent) continue;
        expect(i).toBeLessThan(tableSize);
        screen[(y + yy) * width + x + xx] = (table[i * 3] << 16) | (table[i * 3 + 1] << 8) | table[i * 3 + 2];
      }
    }
    screens.push(screen.slice());
    gce = null;
  }
  expect(o).toBe(bytes.length);
  return { width, height, tableSize, table, loop, images, screens };
}

// ---- Synthetic frames ----

const W = 40;
const H = 30;

function frame(fill: number, shapes: { x: number; y: number; w: number; h: number; color: number }[] = []): Uint8ClampedArray {
  const d = new Uint8ClampedArray(W * H * 4);
  const put = (p: number, c: number) => {
    d[p * 4] = c >> 16;
    d[p * 4 + 1] = (c >> 8) & 0xff;
    d[p * 4 + 2] = c & 0xff;
    d[p * 4 + 3] = 255;
  };
  for (let p = 0; p < W * H; p++) put(p, fill);
  for (const s of shapes) {
    for (let y = s.y; y < s.y + s.h; y++) for (let x = s.x; x < s.x + s.w; x++) put(y * W + x, s.color);
  }
  return d;
}

function rgbAt(d: Uint8ClampedArray, p: number): number {
  return (d[p * 4] << 16) | (d[p * 4 + 1] << 8) | d[p * 4 + 2];
}

async function encode(
  frames: Uint8ClampedArray[],
  options: { fps?: number; reserved?: string[]; threads?: number; quality?: 'low' | 'medium' | 'high' } = {},
): Promise<{ bytes: Uint8Array; encoder: GifMovieEncoder }> {
  const encoder = openGifEncoder(
    { width: W, height: H, fps: options.fps ?? 10, quality: options.quality ?? 'high', reservedColors: options.reserved },
    { threads: options.threads ?? 0 },
  );
  await encoder.prime(frames.slice(0, 4).map((data) => ({ data, width: W, height: H }) as unknown as ImageData));
  // The encoder keeps (and may transfer) what it is given, so it gets copies.
  for (let i = 0; i < frames.length; i++) await encoder.addPixels(frames[i].slice(), i);
  const blob = await encoder.finish();
  expect(blob.type).toBe('image/gif');
  return { bytes: new Uint8Array(await blob.arrayBuffer()), encoder };
}

const RED = 0xff2020;
const CYAN = 0x20e0e0;
const PLATE = 0x1b1b1b;

function movingShapes(count: number): Uint8ClampedArray[] {
  const out: Uint8ClampedArray[] = [];
  for (let i = 0; i < count; i++) {
    const t = Math.min(i, count - 3); // the last three frames repeat
    out.push(
      frame(0x000000, [
        { x: 2 + (t % 20), y: 3, w: 6, h: 5, color: RED },
        { x: 5, y: 10 + (t % 12), w: 8, h: 4, color: CYAN },
        { x: 20, y: 20, w: 10, h: 6, color: PLATE },
      ]),
    );
  }
  return out;
}

// ---- LZW ----

describe('lzwEncode', () => {
  const seeded = (seed: number) => () => {
    seed = (seed * 1103515245 + 12345) >>> 0;
    // The high byte: a power-of-two LCG's low bits repeat after a few hundred steps.
    return seed >>> 24;
  };

  it('round-trips random data at every code size, through the 4096-entry reset', () => {
    for (const min of [2, 3, 4, 6, 8]) {
      const rand = seeded(min);
      const data = Uint8Array.from({ length: 60_000 }, () => rand() & ((1 << min) - 1));
      const block = lzwEncode(data, min);
      expect(block[0]).toBe(min);
      const { indices, clears, blockLengths } = lzwDecode(block, data.length);
      expect(indices).toEqual(data);
      // The leading clear, plus the table filling up at least once.
      expect(clears).toBeGreaterThan(1);
      expect(blockLengths.slice(0, -1).every((l) => l === 255)).toBe(true);
      expect(blockLengths.at(-1)!).toBeLessThanOrEqual(255);
    }
  });

  it('round-trips runs, a single pixel, and output that ends right at a width change', () => {
    const cases: Uint8Array[] = [
      new Uint8Array(100_000),
      Uint8Array.from({ length: 50_000 }, (_, i) => (Math.floor(i / 37) % 5) * 50),
      Uint8Array.of(7),
      Uint8Array.of(1, 1),
    ];
    // Lengths that put the end of the stream on each side of the 9→10-bit change for 8-bit data.
    for (let n = 250; n < 270; n++) cases.push(Uint8Array.from({ length: n }, (_, i) => i & 0xff));
    for (const data of cases) expect(lzwDecode(lzwEncode(data, 8), data.length).indices).toEqual(data);
    expect(lzwDecode(lzwEncode(new Uint8Array(100_000), 8), 100_000).clears).toBe(1);
  });

  it('refuses a code size GIF does not allow', () => {
    expect(() => lzwEncode(Uint8Array.of(0), 1)).toThrow(/2 to 8/);
    expect(() => lzwEncode(new Uint8Array(0), 8)).toThrow(/empty/);
  });
});

// ---- Palette ----

describe('the colour table', () => {
  it('keeps a few colours exactly, in a table just big enough, with its own transparent index', () => {
    const f = frame(0x000000, [
      { x: 0, y: 0, w: 5, h: 5, color: 0xff0000 },
      { x: 10, y: 10, w: 5, h: 5, color: 0x00ff00 },
    ]);
    const map = buildColorMap([{ data: f, width: W, height: H }], { maxColors: 256, reserved: [] });
    expect(map.colorCount).toBe(3);
    expect(map.tableSize).toBe(4);
    expect(map.transparentIndex).toBe(3);
    const colors = new Set<number>();
    for (let i = 0; i < map.colorCount; i++) colors.add((map.table[i * 3] << 16) | (map.table[i * 3 + 1] << 8) | map.table[i * 3 + 2]);
    expect(colors).toEqual(new Set([0x000000, 0xff0000, 0x00ff00]));
    const out = new Uint8Array(W * H);
    mapPixels(map, f, out);
    for (let p = 0; p < W * H; p++) {
      const i = out[p];
      expect((map.table[i * 3] << 16) | (map.table[i * 3 + 1] << 8) | map.table[i * 3 + 2]).toBe(rgbAt(f, p));
    }
  });

  it('stays within the quality’s table size however many colours there are, and never maps onto the transparent index', () => {
    let s = 99;
    const noise = new Uint8ClampedArray(200 * 200 * 4).map((_, i) => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return i % 4 === 3 ? 255 : s >>> 24;
    });
    for (const [maxColors, size] of [
      [64, 64],
      [128, 128],
      [256, 256],
    ]) {
      const map = buildColorMap([{ data: noise, width: 200, height: 200 }], { maxColors, reserved: [] });
      expect(map.tableSize).toBe(size);
      expect(map.colorCount).toBeLessThanOrEqual(size - 1);
      expect(map.colorCount).toBeGreaterThan(size / 2);
      expect(map.table.length).toBe(size * 3);
      expect(map.minCodeSize).toBe(Math.log2(size));
      const out = new Uint8Array(200 * 200);
      mapPixels(map, noise, out);
      expect(Math.max(...out)).toBeLessThan(map.transparentIndex);
    }
  });

  it('places reserved colours exactly, even among many near neighbours', () => {
    // A gradient crowding the reserved colours' neighbourhood.
    const d = new Uint8ClampedArray(64 * 64 * 4);
    for (let p = 0; p < 64 * 64; p++) {
      d[p * 4] = 200 + (p % 50);
      d[p * 4 + 1] = 20 + ((p >> 6) % 30);
      d[p * 4 + 2] = 30 + (p % 7);
      d[p * 4 + 3] = 255;
    }
    const reserved = parseReservedColors(['#e61e21', '#E61E22', '#1b1b1b', '#e61e21']);
    expect(reserved).toEqual([0xe61e21, 0xe61e22, 0x1b1b1b]);
    const map = buildColorMap([{ data: d, width: 64, height: 64 }], { maxColors: 64, reserved });
    expect(map.tableSize).toBe(64);
    for (let k = 0; k < reserved.length; k++) {
      const c = reserved[k];
      const i = mapPixel(map, c >> 16, (c >> 8) & 0xff, c & 0xff);
      expect(i).toBe(map.reservedIndex[k]);
      expect((map.table[i * 3] << 16) | (map.table[i * 3 + 1] << 8) | map.table[i * 3 + 2]).toBe(c);
    }
    expect(() => parseReservedColors(['red'])).toThrow(/#rrggbb/);
    expect(() => parseReservedColors(['#12345'])).toThrow(/#rrggbb/);
  });
});

// ---- The file ----

describe('GIF encoding', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('writes GIF89a with one global table, an endless loop, and frames that decode to what was drawn', async () => {
    const frames = movingShapes(24);
    const { bytes } = await encode(frames, { reserved: ['#ff2020', '#20e0e0', '#1b1b1b'] });
    const gif = decodeGif(bytes);
    expect(gif.width).toBe(W);
    expect(gif.height).toBe(H);
    expect(gif.loop).toBe(0);
    // Black from the frames, the three reserved colours, and the transparent entry.
    expect(gif.tableSize).toBe(8);
    // 24 frames, the last three the same: 22 images, the last held three frames long.
    expect(gif.images.length).toBe(22);
    expect(gif.images.map((i) => i.delay)).toEqual([...Array(21).fill(10), 30]);
    expect(gif.images.every((i) => i.disposal === 1)).toBe(true);
    expect(gif.images[0].transparent).toBeNull();
    expect(gif.images[0]).toMatchObject({ x: 0, y: 0, width: W, height: H });
    for (let k = 0; k < gif.images.length; k++) {
      const screen = gif.screens[k];
      const src = frames[k];
      for (let p = 0; p < W * H; p++) expect(screen[p]).toBe(rgbAt(src, p));
    }
  });

  it('writes only the rectangle that changed, with unchanged pixels inside it transparent', async () => {
    const a = frame(0x000000, [{ x: 20, y: 20, w: 4, h: 4, color: RED }]);
    // Two corners of a 3×2 rectangle at (5, 4) change; the rest of it does not.
    const b = frame(0x000000, [
      { x: 20, y: 20, w: 4, h: 4, color: RED },
      { x: 5, y: 4, w: 1, h: 1, color: CYAN },
      { x: 7, y: 5, w: 1, h: 1, color: CYAN },
    ]);
    const { bytes, encoder } = await encode([a, b], { reserved: ['#ff2020', '#20e0e0'] });
    const gif = decodeGif(bytes);
    const t = encoder.colorMap!.transparentIndex;
    expect(gif.images[1]).toMatchObject({ x: 5, y: 4, width: 3, height: 2, transparent: t });
    const cyan = mapPixel(encoder.colorMap!, 0x20, 0xe0, 0xe0);
    expect(Array.from(gif.images[1].indices)).toEqual([cyan, t, t, t, t, cyan]);
    for (let p = 0; p < W * H; p++) expect(gif.screens[1][p]).toBe(rgbAt(b, p));
  });

  it('merges identical frames into one longer delay and keeps the running total exact', async () => {
    const a = frame(0x000000);
    const b = frame(0x000000, [{ x: 1, y: 1, w: 2, h: 2, color: RED }]);
    const merged = decodeGif((await encode([a, a, a, b, b, a], { reserved: ['#ff2020'] })).bytes);
    expect(merged.images.map((i) => i.delay)).toEqual([30, 20, 10]);

    // 12.5 fps is eight centiseconds a frame, every frame.
    const at125 = decodeGif((await encode(movingShapes(10), { fps: 12.5 })).bytes);
    expect(at125.images.map((i) => i.delay)).toEqual([8, 8, 8, 8, 8, 8, 8, 24]);

    // A rate that does not divide 100: the delays alternate and sum to the movie's length.
    const at30 = decodeGif((await encode(movingShapes(20), { fps: 30 })).bytes);
    const delays = at30.images.map((i) => i.delay);
    expect(delays.reduce((s, d) => s + d, 0)).toBe(gifCentisecondsAt(20, 30));
    expect(gifCentisecondsAt(20, 30)).toBe(67);
    expect(new Set(delays.slice(0, -1))).toEqual(new Set([3, 4]));
  });

  it('produces the same bytes on every run, and with a pool of workers finishing out of order', async () => {
    const frames = movingShapes(30);
    const one = (await encode(frames, { reserved: ['#ff2020'] })).bytes;
    const two = (await encode(frames, { reserved: ['#ff2020'] })).bytes;
    expect(two).toEqual(one);

    const workers: FakeWorker[] = [];
    let busiest = 0;
    class FakeWorker {
      onmessage: ((e: { data: GifWorkerReply }) => void) | null = null;
      onerror: unknown = null;
      onmessageerror: unknown = null;
      private readonly state: GifWorkerState = { map: null, width: 0, height: 0 };
      private queued = 0;
      readonly url: string;
      constructor(url: URL) {
        this.url = url.href;
        workers.push(this);
      }
      postMessage(message: GifWorkerRequest, transfer: ArrayBuffer[] = []) {
        // What a real worker receives: a structured clone, with the listed buffers moved.
        const copy = structuredClone(message, { transfer });
        if (copy.type === 'init') {
          handleGifWorkerMessage(this.state, copy, () => {});
          return;
        }
        this.queued++;
        busiest = Math.max(busiest, workers.reduce((s, w) => s + w.queued, 0));
        // Later frames often finish first.
        setTimeout(() => {
          handleGifWorkerMessage(this.state, copy, (reply, t) => {
            this.queued--;
            this.onmessage?.({ data: structuredClone(reply, { transfer: t }) });
          });
        }, (copy.job.seq * 7) % 5);
      }
      terminate() {}
    }
    vi.stubGlobal('Worker', FakeWorker);
    const pooled = await encode(frames, { reserved: ['#ff2020'], threads: 3 });
    expect(workers.length).toBe(3);
    expect(workers[0].url).toMatch(/gifWorker\.ts/);
    expect(pooled.encoder.workerThreads).toBe(3);
    expect(busiest).toBeLessThanOrEqual(6);
    expect(busiest).toBeGreaterThan(1);
    expect(pooled.bytes).toEqual(one);
  });

  it('refuses frames out of order or of the wrong size, and a closed encoder ends quietly', async () => {
    const encoder = openGifEncoder({ width: W, height: H, fps: 10, quality: 'medium' }, { threads: 0 });
    await expect(encoder.addPixels(frame(0), 1)).rejects.toThrow(/^GIF encoder, addFrame: frame 1 .* frame 0/);
    await expect(encoder.addPixels(new Uint8ClampedArray(16), 0)).rejects.toThrow(/GIF encoder, addFrame/);
    await encoder.addPixels(frame(0), 0);
    encoder.close();
    encoder.close();
    await expect(encoder.finish()).rejects.toThrow(/GIF encoder, finish: the encoder is closed/);
    await expect(encoder.addPixels(frame(0), 1)).rejects.toThrow(/closed/);
    expect(() => openGifEncoder({ width: W, height: H, fps: 60, quality: 'high' })).toThrow(/GIF encoder, open/);
    expect(() => openGifEncoder({ width: 0, height: H, fps: 10, quality: 'high' })).toThrow(/GIF encoder, open/);
    expect(() => openGifEncoder({ width: W, height: H, fps: 10, quality: 'high', reservedColors: ['blue'] })).toThrow(
      /GIF encoder, open: reserved colour/,
    );
  });

  it('builds its palette from the first frame when it was given no samples', async () => {
    const encoder = openGifEncoder({ width: W, height: H, fps: 10, quality: 'low' }, { threads: 0 });
    await encoder.addPixels(frame(0x000000, [{ x: 0, y: 0, w: 3, h: 3, color: RED }]), 0);
    const gif = decodeGif(new Uint8Array(await (await encoder.finish()).arrayBuffer()));
    expect(gif.images.length).toBe(1);
    expect(gif.screens[0][0]).toBe(RED);
  });
});
