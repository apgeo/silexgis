// SPDX-License-Identifier: AGPL-3.0-or-later
import { lzwEncode } from './lzw.ts';
import { mapPixel, mapPixels, type GifColorMap } from './quantize.ts';

/**
 * One frame of a GIF, compressed on its own: the work a pool worker does, and what the encoder
 * does itself where there is no worker.
 *
 * <b>Only what changed is written.</b> Every frame after the first is drawn over the one before
 * it (disposal "do not dispose"), so a pixel whose colour index is the same as the previous
 * frame's is written as the transparent index, and only the rectangle bounding the pixels that did
 * change is written at all. The comparison is on colour indices, not on raw colours: a pixel whose
 * colour moved a little but maps to the same entry needs no rewriting, and because the pixel shown
 * after every frame is then exactly that frame's index everywhere, the next frame's comparison
 * against its own previous indices stays correct by induction. A frame with nothing changed says
 * so and carries no image, which is how identical frames get merged into one longer one.
 *
 * Each frame is a function of its own pixels and its predecessor's only, so frames can be
 * compressed on several threads at once and still come out byte-identical to a single-threaded
 * run.
 */

export interface GifFrameJob {
  seq: number;
  /** RGBA, width × height. */
  current: Uint8Array | Uint8ClampedArray;
  /** The previous frame's RGBA, or null for the first frame. */
  previous: Uint8Array | Uint8ClampedArray | null;
}

export interface GifFrameResult {
  seq: number;
  /** False when every pixel has the previous frame's colour index: there is nothing to write. */
  changed: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
  /** The LZW image data block for the rectangle; null when nothing changed. */
  data: Uint8Array | null;
}

export function encodeGifFrame(map: GifColorMap, width: number, height: number, job: GifFrameJob): GifFrameResult {
  const n = width * height;
  if (job.current.length < n * 4 || (job.previous && job.previous.length < n * 4)) {
    throw new Error(`frame ${job.seq} is smaller than ${width}×${height}`);
  }
  const indices = new Uint8Array(n);
  mapPixels(map, job.current, indices);
  if (!job.previous) {
    return { seq: job.seq, changed: true, x: 0, y: 0, width, height, data: lzwEncode(indices, map.minCodeSize) };
  }

  const cur = words(job.current, n);
  const prev = words(job.previous, n);
  const p8 = job.previous;
  const changed = new Uint8Array(n);
  let minX = width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  for (let y = 0, p = 0; y < height; y++) {
    for (let x = 0; x < width; x++, p++) {
      if (cur[p] === prev[p]) continue;
      const o = p * 4;
      if (indices[p] === mapPixel(map, p8[o], p8[o + 1], p8[o + 2])) continue;
      changed[p] = 1;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }
  if (maxX < 0) return { seq: job.seq, changed: false, x: 0, y: 0, width: 0, height: 0, data: null };

  const rw = maxX - minX + 1;
  const rh = maxY - minY + 1;
  const rect = new Uint8Array(rw * rh);
  const t = map.transparentIndex;
  for (let y = 0; y < rh; y++) {
    const row = (minY + y) * width + minX;
    for (let x = 0; x < rw; x++) rect[y * rw + x] = changed[row + x] === 1 ? indices[row + x] : t;
  }
  return { seq: job.seq, changed: true, x: minX, y: minY, width: rw, height: rh, data: lzwEncode(rect, map.minCodeSize) };
}

// Whole pixels compared as one 32-bit word each, before the colour lookup is paid for.
function words(a: Uint8Array | Uint8ClampedArray, n: number): Uint32Array {
  return a.byteOffset % 4 === 0
    ? new Uint32Array(a.buffer, a.byteOffset, n)
    : new Uint32Array(Uint8Array.from(a.subarray(0, n * 4)).buffer);
}

// ---- The worker's side of the pool, kept here so it runs the same in a test as in a worker. ----

export type GifWorkerRequest =
  | { type: 'init'; map: GifColorMap; width: number; height: number }
  | { type: 'frame'; job: GifFrameJob };

export type GifWorkerReply =
  | { type: 'result'; result: GifFrameResult }
  | { type: 'error'; seq: number; message: string };

export interface GifWorkerState {
  map: GifColorMap | null;
  width: number;
  height: number;
}

/** Handles one message to a pool worker; `post` sends the reply, transferring the listed buffers. */
export function handleGifWorkerMessage(
  state: GifWorkerState,
  message: GifWorkerRequest,
  post: (reply: GifWorkerReply, transfer: ArrayBuffer[]) => void,
): void {
  if (message.type === 'init') {
    state.map = message.map;
    state.width = message.width;
    state.height = message.height;
    return;
  }
  const { job } = message;
  try {
    if (!state.map) throw new Error('the worker was given a frame before its palette');
    const result = encodeGifFrame(state.map, state.width, state.height, job);
    post({ type: 'result', result }, result.data ? [result.data.buffer as ArrayBuffer] : []);
  } catch (e) {
    post({ type: 'error', seq: job.seq, message: e instanceof Error ? e.message : String(e) }, []);
  }
}
