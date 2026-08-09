// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useRef, useState } from 'react';
import { ApiError } from '../../api/client.ts';
import {
  UploadConcurrency,
  planUpload,
  resetForRetry,
  summarise,
  withItem,
  type UploadItem,
  type UploadSummary,
} from './uploadPlan.ts';
import {
  DuplicateUploadError,
  uploadOne,
  type UploadLimits,
  type UploadOptions,
  type UploadTarget,
} from './uploadTransport.ts';

export interface UploadQueue {
  items: UploadItem[];
  summary: UploadSummary;
  /** Queues files and starts sending them. Safe to call while a drop is already running. */
  add: (files: readonly File[]) => void;
  /** Re-sends only what failed. */
  retryFailed: () => void;
  /** Abandons anything in flight and empties the list. */
  clear: () => void;
  /** Whether anything is queued or moving. */
  running: boolean;
}

/**
 * The drop: a list of files, a small pool of concurrent uploads, and a row per file that says
 * what became of it.
 *
 * <p>
 * The pool is what makes a four-hundred-file drop usable. Sending them one at a time wastes
 * the connection; sending them all at once opens four hundred requests the browser queues
 * anyway, and makes the progress list meaningless. A few at a time means the list moves
 * steadily and one large file cannot hold up the rest.
 * </p>
 * <p>
 * Failures are per file and never stop the drop. That is the whole difference between
 * something a club will hand its archive to and something it will not: a run that stops dead
 * on the first unreadable scan has to be restarted by hand, and restarting it re-sends
 * everything that already worked.
 * </p>
 */
export function useUploadQueue(
  limits: UploadLimits,
  targetOf: (item: UploadItem) => UploadTarget,
  options: Pick<UploadOptions, 'onDuplicate'> = {},
): UploadQueue {
  const [items, setItems] = useState<UploadItem[]>([]);

  // The queue is driven outside React's render cycle: the loop needs the current list and the
  // current target, and reading them from state inside an async pump would capture whatever
  // they were when it started.
  const itemsRef = useRef<UploadItem[]>([]);
  const runningRef = useRef(false);
  const abortRef = useRef<AbortController | null>(null);
  const targetRef = useRef(targetOf);
  const duplicateRef = useRef(options.onDuplicate);
  const nextIdRef = useRef(0);

  targetRef.current = targetOf;
  duplicateRef.current = options.onDuplicate;

  const apply = useCallback((change: (current: UploadItem[]) => UploadItem[]) => {
    itemsRef.current = change(itemsRef.current);
    setItems(itemsRef.current);
  }, []);

  const pump = useCallback(async () => {
    if (runningRef.current) {
      return;
    }

    runningRef.current = true;
    abortRef.current = new AbortController();
    const { signal } = abortRef.current;

    const worker = async () => {
      for (;;) {
        if (signal.aborted) {
          return;
        }

        const next = itemsRef.current.find((item) => item.status === 'pending');
        if (!next) {
          return;
        }

        // Claimed before the await, so two workers cannot pick the same file up.
        apply((current) => withItem(current, next.id, { status: 'uploading', progress: 0 }));

        try {
          const result = await uploadOne(next.file, targetRef.current(next), limits, {
            signal,
            onProgress: (fraction) =>
              apply((current) => withItem(current, next.id, { progress: fraction })),
            onDuplicate: duplicateRef.current,
          });
          apply((current) =>
            withItem(current, next.id, {
              status: 'stored',
              progress: 1,
              documentId: result.documentId,
            }),
          );
        } catch (error) {
          if (signal.aborted) {
            return;
          }

          // A duplicate somebody declined is not a failure: they were asked and said no, so
          // offering to retry it would ask the same question again.
          apply((current) =>
            withItem(current, next.id, {
              status: error instanceof DuplicateUploadError ? 'skipped' : 'failed',
              progress: 0,
              errorCode: error instanceof ApiError ? error.code : undefined,
            }),
          );
        }
      }
    };

    await Promise.all(Array.from({ length: UploadConcurrency }, worker));
    runningRef.current = false;

    // Anything added while the pool was draining starts a fresh pass rather than sitting
    // pending for ever.
    if (itemsRef.current.some((item) => item.status === 'pending') && !signal.aborted) {
      void pump();
    }
  }, [apply, limits]);

  const add = useCallback(
    (files: readonly File[]) => {
      if (files.length === 0) {
        return;
      }

      // Ids continue across drops so a second folder dropped onto a running list cannot
      // collide with rows already in it.
      const planned = planUpload(files, `u${nextIdRef.current}`);
      nextIdRef.current += 1;
      apply((current) => [...current, ...planned]);
      void pump();
    },
    [apply, pump],
  );

  const retryFailed = useCallback(() => {
    apply(resetForRetry);
    void pump();
  }, [apply, pump]);

  const clear = useCallback(() => {
    abortRef.current?.abort();
    runningRef.current = false;
    apply(() => []);
  }, [apply]);

  const summary = summarise(items);
  return { items, summary, add, retryFailed, clear, running: summary.running };
}
