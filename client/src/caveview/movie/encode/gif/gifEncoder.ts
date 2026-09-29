// SPDX-License-Identifier: AGPL-3.0-or-later
import { movieEncoderError, type MovieEncoder, type MovieEncoderOptions, type MovieQuality } from '../movieFormats.ts';
import type { GifFrameResult } from './gifFrame.ts';
import { createGifFramePool, gifWorkerCount, type GifFramePool } from './gifPool.ts';
import { GifWriter } from './gifWriter.ts';
import { buildColorMap, parseReservedColors, type GifColorMap, type GifSample, type Rgb24 } from './quantize.ts';

/**
 * A GIF encoder that streams: each frame is read, handed to the pool and forgotten, and only its
 * compressed bytes are kept, so memory grows with the file rather than with the number of raw
 * frames. The one raw frame held is the previous one, which the next frame is compared against.
 *
 * <b>Back-pressure</b> is the only thing `addFrame`'s promise waits for: at most two frames per
 * worker are in flight (one on this thread when there is no worker), so a fast renderer cannot pile
 * raw frames up in front of a slower pool.
 *
 * <b>Quality is the size of the colour table</b> — 64, 128 or 256 entries. A smaller table makes
 * shorter codes and a smaller file; reserved colours always get their entries, the table growing
 * past the quality's size when they need it to.
 */

/** Frames the palette is built from, spread across the movie by the caller. */
export const GIF_PALETTE_SAMPLES = 16;

export const GIF_QUALITY_COLORS: Record<MovieQuality, number> = { low: 64, medium: 128, high: 256 };

// A delay of one centisecond or less is slowed to about ten by browsers, so the fastest rate a GIF
// can play at the speed it was written at is 50 frames a second (two centiseconds each).
const MAX_GIF_FPS = 50;

export function openGifEncoder(options: MovieEncoderOptions, internals: { threads?: number } = {}): GifMovieEncoder {
  const { width, height, fps, quality } = options;
  const dimension = (v: number) => Number.isInteger(v) && v >= 1 && v <= 0xffff;
  if (!dimension(width) || !dimension(height)) {
    throw movieEncoderError('gif', 'open', `a frame of ${width}×${height} is not a GIF size`);
  }
  if (!Number.isFinite(fps) || fps <= 0 || fps > MAX_GIF_FPS) {
    throw movieEncoderError('gif', 'open', `${fps} frames a second is not a GIF frame rate`);
  }
  const maxColors = GIF_QUALITY_COLORS[quality];
  if (!maxColors) throw movieEncoderError('gif', 'open', `unknown quality '${quality}'`);
  let reserved: Rgb24[];
  try {
    reserved = parseReservedColors(options.reservedColors);
  } catch (e) {
    throw movieEncoderError('gif', 'open', e);
  }
  return new GifMovieEncoder(width, height, fps, maxColors, reserved, internals.threads ?? gifWorkerCount());
}

interface Waiter {
  resolve(): void;
  reject(error: Error): void;
}

export class GifMovieEncoder implements MovieEncoder {
  readonly format = 'gif' as const;
  readonly samplesWanted = GIF_PALETTE_SAMPLES;

  private map: GifColorMap | null = null;
  private pool: GifFramePool | null = null;
  private writer: GifWriter | null = null;
  private previous: Uint8Array | Uint8ClampedArray | null = null;
  private nextIndex = 0;
  private nextToWrite = 0;
  private inFlight = 0;
  private maxInFlight = 1;
  private readonly ready = new Map<number, GifFrameResult>();
  private waiters: Waiter[] = [];
  private idle: Waiter[] = [];
  private failure: Error | null = null;
  private closed = false;
  private reader: CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D | null = null;

  private readonly width: number;
  private readonly height: number;
  private readonly fps: number;
  private readonly maxColors: number;
  private readonly reserved: readonly Rgb24[];
  private readonly threads: number;

  constructor(width: number, height: number, fps: number, maxColors: number, reserved: readonly Rgb24[], threads: number) {
    this.width = width;
    this.height = height;
    this.fps = fps;
    this.maxColors = maxColors;
    this.reserved = reserved;
    this.threads = threads;
  }

  /** The colour map in use, once there is one. */
  get colorMap(): GifColorMap | null {
    return this.map;
  }

  /** Worker threads compressing frames; 0 when it is done on this thread. */
  get workerThreads(): number {
    return this.pool?.threads ?? 0;
  }

  async prime(samples: readonly ImageData[]): Promise<void> {
    if (this.closed) throw movieEncoderError('gif', 'prime', 'the encoder is closed');
    if (this.map) throw movieEncoderError('gif', 'prime', 'the palette is already built');
    try {
      for (const s of samples) {
        if (s.width !== this.width || s.height !== this.height) {
          throw new Error(`a ${s.width}×${s.height} sample for a ${this.width}×${this.height} movie`);
        }
      }
      this.start(samples);
    } catch (e) {
      throw movieEncoderError('gif', 'prime', e);
    }
  }

  addFrame(source: HTMLCanvasElement | OffscreenCanvas, index: number): Promise<void> {
    let rgba: Uint8ClampedArray;
    try {
      if (this.closed) throw new Error('the encoder is closed');
      rgba = this.read(source);
    } catch (e) {
      return Promise.reject(movieEncoderError('gif', 'addFrame', e));
    }
    return this.addPixels(rgba, index);
  }

  /**
   * Takes one frame as RGBA pixels, width × height. The array becomes the encoder's: it is kept as
   * the next frame's predecessor and may be transferred to a worker, so the caller must not reuse
   * it.
   */
  addPixels(rgba: Uint8Array | Uint8ClampedArray, index: number): Promise<void> {
    try {
      if (this.closed) throw new Error('the encoder is closed');
      if (this.failure) return Promise.reject(this.failure);
      if (index !== this.nextIndex) throw new Error(`frame ${index} was given where frame ${this.nextIndex} was expected`);
      if (rgba.length !== this.width * this.height * 4) {
        throw new Error(`frame ${index} has ${rgba.length / 4} pixels, not ${this.width}×${this.height}`);
      }
      // Without a palette built from samples, the first frame is the sample.
      if (!this.map) this.start([{ data: rgba, width: this.width, height: this.height }]);
    } catch (e) {
      return Promise.reject(movieEncoderError('gif', 'addFrame', e));
    }
    const pool = this.pool!;
    const previous = this.previous;
    this.nextIndex++;
    this.previous = rgba;
    // The predecessor is not needed here any more and can move to the worker; the current frame is
    // copied there, because the next frame is compared against it.
    const transfer =
      pool.threads > 0 && previous && previous.byteOffset === 0 && previous.byteLength === previous.buffer.byteLength
        ? [previous.buffer as ArrayBuffer]
        : [];
    this.inFlight++;
    pool.encode({ seq: index, current: rgba, previous }, transfer).then(
      (result) => {
        if (this.closed) return;
        this.inFlight--;
        this.ready.set(result.seq, result);
        try {
          this.drain();
        } catch (e) {
          this.fail(e);
          return;
        }
        this.wake();
      },
      (e) => {
        if (this.closed) return;
        this.inFlight--;
        this.fail(e);
      },
    );
    if (this.inFlight < this.maxInFlight) return Promise.resolve();
    return new Promise<void>((resolve, reject) => this.waiters.push({ resolve, reject }));
  }

  async finish(): Promise<Blob> {
    if (this.closed) throw movieEncoderError('gif', 'finish', 'the encoder is closed');
    if (this.failure) throw this.failure;
    if (this.nextIndex === 0 || !this.writer) throw movieEncoderError('gif', 'finish', 'no frames were added');
    if (this.inFlight > 0) await new Promise<void>((resolve, reject) => this.idle.push({ resolve, reject }));
    if (this.closed) throw movieEncoderError('gif', 'finish', 'the encoder was closed');
    let parts: Uint8Array[];
    try {
      this.drain();
      parts = this.writer.finish();
    } catch (e) {
      const error = movieEncoderError('gif', 'finish', e);
      this.close();
      throw error;
    }
    const blob = new Blob(parts as BlobPart[], { type: 'image/gif' });
    this.close();
    return blob;
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.pool?.close();
    this.previous = null;
    this.ready.clear();
    // Closing mid-run is how an export is cancelled, and cancelling is not an error: whoever is
    // waiting for room simply stops waiting.
    for (const w of [...this.waiters, ...this.idle]) w.resolve();
    this.waiters = [];
    this.idle = [];
  }

  private start(samples: readonly GifSample[]): void {
    const map = buildColorMap(samples, { maxColors: this.maxColors, reserved: this.reserved });
    this.map = map;
    this.pool = createGifFramePool(map, this.width, this.height, this.threads);
    this.maxInFlight = Math.max(1, this.pool.threads * 2);
    this.writer = new GifWriter({ width: this.width, height: this.height, fps: this.fps, map });
  }

  private drain(): void {
    for (let r = this.ready.get(this.nextToWrite); r; r = this.ready.get(this.nextToWrite)) {
      this.ready.delete(this.nextToWrite);
      this.writer!.add(r);
      this.nextToWrite++;
    }
  }

  private wake(): void {
    while (this.waiters.length > 0 && this.inFlight < this.maxInFlight) this.waiters.shift()!.resolve();
    if (this.inFlight === 0) {
      for (const w of this.idle) w.resolve();
      this.idle = [];
    }
  }

  private fail(e: unknown): void {
    this.failure ??= movieEncoderError('gif', 'encode', e);
    this.pool?.close();
    for (const w of [...this.waiters, ...this.idle]) w.reject(this.failure);
    this.waiters = [];
    this.idle = [];
  }

  // The source is drawn onto a canvas of the encoder's own, whatever kind of canvas it is — a
  // WebGL canvas cannot be read through a 2D context of its own — over black, so a frame is opaque
  // even if the source is not.
  private read(source: HTMLCanvasElement | OffscreenCanvas): Uint8ClampedArray {
    const { width, height } = this;
    if (!this.reader) {
      const canvas =
        typeof OffscreenCanvas !== 'undefined'
          ? new OffscreenCanvas(width, height)
          : Object.assign(document.createElement('canvas'), { width, height });
      const ctx = canvas.getContext('2d', { willReadFrequently: true }) as
        | CanvasRenderingContext2D
        | OffscreenCanvasRenderingContext2D
        | null;
      if (!ctx) throw new Error('there is no 2D canvas to read frames with');
      this.reader = ctx;
    }
    const ctx = this.reader;
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, width, height);
    ctx.drawImage(source, 0, 0, width, height);
    return ctx.getImageData(0, 0, width, height).data;
  }
}
