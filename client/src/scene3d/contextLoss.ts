// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * What the view should do about a graphics context the browser has taken away.
 *
 * `recovering` — rebuild the scene once, silently. `lost` — stop, and tell the viewer.
 */
export type Scene3DContextLossState = 'recovering' | 'lost';

/**
 * How soon after a rebuild a second loss is treated as the same failure repeating rather than as a
 * new one, in milliseconds.
 *
 * Without a window like this, "rebuild once per loss" is a loop: a machine that cannot hold a
 * context at all loses it, gets a fresh scene, loses that one, and so on for as long as the page is
 * open — which on a phone is a flicker that never resolves and a battery that empties. Ten seconds
 * is long enough to cover a rebuild and the first frames after it, and short enough that a viewer
 * who genuinely loses the context twice in one sitting (a driver reset in the morning, another
 * after lunch) is not refused the second recovery.
 */
export const CONTEXT_LOSS_REPEAT_WINDOW_MS = 10_000;

/** Reads the clock. Injected so a test does not have to wait ten seconds to prove the window. */
export type Clock = () => number;

export interface ContextLossPolicy {
  /** Called when the browser reports the context gone. Says what should happen now. */
  recordLoss(): Scene3DContextLossState;
  /** Called once a rebuilt scene is drawing again, which is what arms the next recovery. */
  recordRecovered(): void;
}

/**
 * Decides whether a lost graphics context is worth rebuilding for.
 *
 * A browser revokes a context on its own initiative — a phone backgrounded and returned to, a
 * driver reset, memory pressure, too many live contexts across tabs — and the scene's GPU
 * resources do not survive it. Recovery therefore means building a new scene, not repairing the
 * old one, which is cheap here because the camera is already stored as plain degrees and metres
 * rather than as engine objects: a rebuilt scene can be put back where the viewer was looking.
 *
 * The policy is deliberately "once, then say so". Retrying without limit hides a machine that
 * cannot keep a context at all behind an endless flicker; refusing to retry at all turns the
 * commonest case — one transient loss — into an error the viewer has to act on for no reason.
 */
export function createContextLossPolicy(clock: Clock = () => Date.now()): ContextLossPolicy {
  // The moment the last recovery was armed. Undefined means no rebuild has happened yet, so the
  // next loss is a first loss however long the scene has been open.
  let recoveredAt: number | undefined;
  let recovering = false;

  return {
    recordLoss(): Scene3DContextLossState {
      // A second report while a rebuild is already under way is the same failure, not a new one.
      // Browsers can raise the event more than once for one loss, and the rebuild has not yet had
      // a chance to succeed or fail.
      if (recovering) {
        return 'lost';
      }
      if (recoveredAt !== undefined && clock() - recoveredAt < CONTEXT_LOSS_REPEAT_WINDOW_MS) {
        return 'lost';
      }
      recovering = true;
      return 'recovering';
    },
    recordRecovered(): void {
      recovering = false;
      recoveredAt = clock();
    },
  };
}
