// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, type LazyExoticComponent } from 'react';

/**
 * The words a browser uses when a part of the application's own code could not be downloaded.
 * Chromium, Firefox and Safari each word it their own way; the last is the bundler's, for a
 * stylesheet that travels with the code.
 *
 * Two things end in this error, and the browser does not say which: the connection was too slow or
 * dropped before the file arrived, or the installation was updated after the page was opened and the
 * page still names the files of the version it was opened on. Either way it is not a fault of the
 * screen that asked, and the person needs to be told what to do about it rather than shown a bug.
 */
const CODE_DOWNLOAD_FAILED =
  /dynamically imported module|Importing a module script failed|Unable to preload CSS/i;

/** Whether an error is the browser failing to download a part of the application's code. */
export function isCodeDownloadFailure(error: unknown): boolean {
  const message = error instanceof Error ? error.message : typeof error === 'string' ? error : '';
  return CODE_DOWNLOAD_FAILED.test(message);
}

/**
 * The pauses before each further attempt. A connection that dropped a request usually answers the
 * next one; one that is merely slow gets two more chances spread over a few seconds before the
 * person is asked to decide.
 */
export const CODE_RETRY_DELAYS_MS: readonly number[] = [1_000, 3_000];

/**
 * Loads a part of the application's code, asking again when the download fails.
 *
 * Every current browser asks the server again when a module that failed to download is imported a
 * second time (Chromium, WebKit and Firefox were each checked against a server that refused the
 * first request), so asking again is a real second chance and not the same failure replayed.
 * Only a download failure is retried: an error thrown by the code itself as it starts would only
 * be thrown again.
 */
export async function importWithRetry<T>(
  load: () => Promise<T>,
  delaysMs: readonly number[] = CODE_RETRY_DELAYS_MS,
  wait: (ms: number) => Promise<void> = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
): Promise<T> {
  for (let attempt = 0; ; attempt += 1) {
    try {
      return await load();
    } catch (error) {
      if (attempt >= delaysMs.length || !isCodeDownloadFailure(error)) {
        throw error;
      }
      await wait(delaysMs[attempt]);
    }
  }
}

/**
 * React's `lazy`, with the download asked for again when it fails. A lazy component remembers the
 * first answer it was given for good, so the retrying has to happen before that answer is given.
 */
export function lazyRetrying<T extends LazyComponent>(load: () => Promise<{ default: T }>): LazyExoticComponent<T> {
  return lazy(() => importWithRetry(load));
}

/** Whatever component React's own `lazy` accepts, read from its declaration. */
type LazyComponent = Parameters<typeof lazy>[0] extends () => Promise<{ default: infer C }> ? C : never;
