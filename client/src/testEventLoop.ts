// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Waits until Node has had its turn to report a promise rejected with no handler so far.
 *
 * Node reports such a rejection once the microtasks of the task it happened in have run, before
 * the next task starts; this resolves in a task of its own, so by then any report is in. It is a
 * turn of the event loop rather than a stretch of time, so a loaded machine cannot make it too
 * short — which a fixed sleep can, and then passes for the wrong reason.
 *
 * Only what has already been rejected is covered: a test first awaits the thing that marks the
 * work as done (a field drawn as invalid, a call made), then this.
 */
export function afterRejectionsAreReported(): Promise<void> {
  return new Promise((resolve) => setImmediate(resolve));
}
