// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  encodeGifFrame,
  type GifFrameJob,
  type GifFrameResult,
  type GifWorkerReply,
  type GifWorkerRequest,
} from './gifFrame.ts';
import type { GifColorMap } from './quantize.ts';

/**
 * Where GIF frames get compressed: a few module workers, or this thread when there are none.
 *
 * <b>A small pool.</b> Rendering the frames already occupies the main thread, and the machine's
 * other cores are shared with everything else the browser runs, so the pool takes one worker per
 * core beyond the first and never more than four. Frames go to whichever worker has the fewest
 * waiting; results come back in any order and the caller puts them back in sequence.
 *
 * <b>The same work on this thread when there are no workers</b> — where `Worker` does not exist
 * (a test environment), where constructing one throws, or on a single-core machine. The output is
 * byte-identical either way, because a frame is a pure function of its pixels, its predecessor's
 * and the colour map.
 *
 * A worker that fails fails the encode: its frames' pixels were transferred to it and are gone, so
 * there is nothing to retry them with.
 */
export interface GifFramePool {
  /** Worker threads in use; 0 when frames are compressed on this thread. */
  readonly threads: number;
  /**
   * Compresses a frame. The buffers listed in `transfer` are moved to the worker and unusable
   * afterwards; on this thread they are left alone.
   */
  encode(job: GifFrameJob, transfer: ArrayBuffer[]): Promise<GifFrameResult>;
  close(): void;
}

export const MAX_GIF_WORKERS = 4;

/** How many workers a pool would start here. */
export function gifWorkerCount(): number {
  if (typeof Worker === 'undefined') return 0;
  const cores = typeof navigator !== 'undefined' && navigator.hardwareConcurrency > 0 ? navigator.hardwareConcurrency : 1;
  return Math.max(0, Math.min(cores - 1, MAX_GIF_WORKERS));
}

export function createGifFramePool(map: GifColorMap, width: number, height: number, threads = gifWorkerCount()): GifFramePool {
  const workers: Worker[] = [];
  try {
    for (let i = 0; i < threads; i++) {
      workers.push(new Worker(new URL('./gifWorker.ts', import.meta.url), { type: 'module', name: 'gif-encoder' }));
    }
  } catch {
    for (const w of workers) w.terminate();
    workers.length = 0;
  }
  if (workers.length === 0) return inThreadPool(map, width, height);
  return workerPool(workers, map, width, height);
}

function inThreadPool(map: GifColorMap, width: number, height: number): GifFramePool {
  let closed = false;
  return {
    threads: 0,
    encode(job) {
      if (closed) return Promise.reject(new Error('the frame pool is closed'));
      try {
        return Promise.resolve(encodeGifFrame(map, width, height, job));
      } catch (e) {
        return Promise.reject(e instanceof Error ? e : new Error(String(e)));
      }
    },
    close() {
      closed = true;
    },
  };
}

interface Waiting {
  resolve(result: GifFrameResult): void;
  reject(error: Error): void;
}

function workerPool(workers: Worker[], map: GifColorMap, width: number, height: number): GifFramePool {
  const waiting = workers.map(() => new Map<number, Waiting>());
  let failure: Error | null = null;
  let closed = false;

  const failAll = (error: Error) => {
    failure ??= error;
    for (const m of waiting) {
      for (const w of m.values()) w.reject(failure);
      m.clear();
    }
  };

  workers.forEach((worker, i) => {
    worker.onmessage = (event: MessageEvent<GifWorkerReply>) => {
      const reply = event.data;
      const seq = reply.type === 'result' ? reply.result.seq : reply.seq;
      const w = waiting[i].get(seq);
      if (!w) return;
      waiting[i].delete(seq);
      if (reply.type === 'result') w.resolve(reply.result);
      else w.reject(new Error(reply.message));
    };
    worker.onerror = (event: ErrorEvent) => {
      event.preventDefault();
      failAll(new Error(`a worker failed${event.message ? `: ${event.message}` : ''}`));
    };
    worker.onmessageerror = () => failAll(new Error('a worker reply could not be read'));
    const init: GifWorkerRequest = { type: 'init', map, width, height };
    worker.postMessage(init);
  });

  return {
    threads: workers.length,
    encode(job, transfer) {
      if (closed) return Promise.reject(new Error('the frame pool is closed'));
      if (failure) return Promise.reject(failure);
      let pick = 0;
      for (let i = 1; i < workers.length; i++) if (waiting[i].size < waiting[pick].size) pick = i;
      return new Promise<GifFrameResult>((resolve, reject) => {
        waiting[pick].set(job.seq, { resolve, reject });
        const message: GifWorkerRequest = { type: 'frame', job };
        workers[pick].postMessage(message, transfer);
      });
    },
    close() {
      if (closed) return;
      closed = true;
      for (const w of workers) w.terminate();
      // Closing is how an export is cancelled, which is not a failure: nothing still waiting is
      // settled, and the caller has stopped listening.
      for (const m of waiting) m.clear();
    },
  };
}
