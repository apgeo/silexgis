// SPDX-License-Identifier: AGPL-3.0-or-later
import { modelDeliveryIdentity } from './modelDelivery.ts';

/**
 * Survey files as the viewers on a page read them: one download per file, shared by every viewer
 * that asks for it, and kept for a while once it has arrived.
 *
 * <b>Why shared.</b> A survey file is the largest thing the application hands a browser — tens of
 * megabytes for an expedition's model — and on a slow server or a slow connection reading it is
 * most of what opening a viewer costs. The movie dialog opens over the trip's own viewer, which has
 * just read that very file; read again for the preview, the reader waited for the same bytes twice,
 * and on a connection that took a minute for the first read the preview looked as if it would
 * never load. A stored file is never rewritten (see {@link modelDeliveryIdentity}), so two
 * addresses with the same path are the same bytes, whatever their signatures say.
 *
 * <b>Why it can stall.</b> A download that stops arriving does not end by itself: the request stays
 * open, and the viewer waits behind a spinner for as long as the tab is open. So a download that
 * goes {@link SURVEY_FILE_STALL_MS} without a byte is called off and fails with
 * {@link SurveyFileStalledError}, which a viewer can word as what it is and offer to try again.
 */

/** How long a download may go without receiving anything before it is given up. */
export const SURVEY_FILE_STALL_MS = 45_000;

/** How many arrived files are kept: the trip's viewer and a dialog over it read one between them. */
const KEPT = 2;

/** How much of a file has arrived. `total` is null where the server did not say how much is coming. */
export interface SurveyFileProgress {
  received: number;
  total: number | null;
}

/** A download given up because nothing arrived for {@link SURVEY_FILE_STALL_MS}. */
export class SurveyFileStalledError extends Error {
  constructor() {
    super('the survey file stopped arriving');
    this.name = 'SurveyFileStalledError';
  }
}

interface Download {
  blob: Promise<Blob>;
  controller: AbortController;
  /** Callers still waiting; the download is called off when the last of them stops waiting. */
  waiting: number;
  arrived: boolean;
  progress: SurveyFileProgress;
  listeners: Set<(progress: SurveyFileProgress) => void>;
}

const downloads = new Map<string, Download>();

/**
 * The survey file at `url`, read once for every caller that asks for the same file.
 *
 * @param options.signal stops this caller's wait. The download itself goes on while anybody else
 *   is waiting for it, and is called off when nobody is.
 * @param options.onProgress told how much has arrived, as it arrives — at once, too, when the
 *   caller joins a download already under way.
 */
export function downloadSurveyFile(
  url: string,
  options: { signal?: AbortSignal; onProgress?: (progress: SurveyFileProgress) => void } = {},
): Promise<Blob> {
  const { signal, onProgress } = options;
  if (signal?.aborted) {
    return Promise.reject(signal.reason);
  }
  const key = modelDeliveryIdentity(url);
  let download = downloads.get(key);
  if (download === undefined) {
    download = start(key, url);
  } else {
    // Asked for again: the most recently wanted file is the last to be let go of.
    downloads.delete(key);
    downloads.set(key, download);
  }
  const entry = download;
  if (entry.arrived) {
    return entry.blob;
  }
  entry.waiting += 1;
  if (onProgress !== undefined) {
    entry.listeners.add(onProgress);
    onProgress(entry.progress);
  }
  return new Promise<Blob>((resolve, reject) => {
    const leave = () => {
      if (onProgress !== undefined) entry.listeners.delete(onProgress);
      entry.waiting -= 1;
    };
    const stop = () => {
      leave();
      if (entry.waiting === 0 && !entry.arrived) {
        entry.controller.abort();
        if (downloads.get(key) === entry) downloads.delete(key);
      }
      reject(signal!.reason);
    };
    signal?.addEventListener('abort', stop, { once: true });
    entry.blob.then(
      (blob) => {
        signal?.removeEventListener('abort', stop);
        leave();
        resolve(blob);
      },
      (error: unknown) => {
        signal?.removeEventListener('abort', stop);
        leave();
        reject(error);
      },
    );
  });
}

/** Forgets every file, arrived or on its way. For tests, which each stand up a fetch of their own. */
export function forgetSurveyFiles(): void {
  for (const download of downloads.values()) {
    if (!download.arrived) download.controller.abort();
  }
  downloads.clear();
}

function start(key: string, url: string): Download {
  const controller = new AbortController();
  const download: Download = {
    blob: Promise.resolve(new Blob()),
    controller,
    waiting: 0,
    arrived: false,
    progress: { received: 0, total: null },
    listeners: new Set(),
  };
  const report = (progress: SurveyFileProgress) => {
    download.progress = progress;
    for (const listener of [...download.listeners]) listener(progress);
  };
  let stalled = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const watch = () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      stalled = true;
      controller.abort();
    }, SURVEY_FILE_STALL_MS);
  };

  download.blob = (async () => {
    watch();
    try {
      const response = await fetch(url, { signal: controller.signal });
      if (!response.ok) throw new Error(`survey file request failed (${response.status})`);
      // The length a compressed answer declares is of the compressed bytes, and what is read off
      // the body is the file itself, so the two cannot be set against each other.
      const declared = Number(response.headers.get('content-length'));
      const total = response.headers.get('content-encoding') === null && declared > 0 ? declared : null;
      const reader = response.body?.getReader();
      if (reader === undefined) {
        return await response.blob();
      }
      const parts: Uint8Array<ArrayBuffer>[] = [];
      let received = 0;
      report({ received, total });
      for (;;) {
        watch();
        const { done, value } = await reader.read();
        if (done) break;
        parts.push(value);
        received += value.byteLength;
        report({ received, total });
      }
      return new Blob(parts);
    } catch (error) {
      throw stalled ? new SurveyFileStalledError() : error;
    } finally {
      clearTimeout(timer);
    }
  })();
  download.blob.then(
    () => {
      download.arrived = true;
      download.listeners.clear();
      // Arrived files beyond the few kept go, the longest unasked-for first.
      const arrived = [...downloads.entries()].filter(([, entry]) => entry.arrived);
      for (const [name] of arrived.slice(0, Math.max(0, arrived.length - KEPT))) downloads.delete(name);
    },
    () => {
      // A failed read is not kept: asked again, the file is read again.
      if (downloads.get(key) === download) downloads.delete(key);
    },
  );
  downloads.set(key, download);
  return download;
}
