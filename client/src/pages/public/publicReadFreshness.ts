// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/** The members of a read that say whether it is still landing. */
export interface PublicReadState {
  /** The failure the read ended on, once it has stopped trying. */
  error: unknown;
  /** The failure of the latest attempt, while further attempts are still to come. */
  failureReason?: unknown;
  /** True while an attempt is being held back because the browser knows it has no connection. */
  isPaused?: boolean;
}

/** Why a read that failed nothing is not landing: the browser held it back for want of a connection. */
export const HELD_BACK_OFFLINE = Object.freeze({ kind: 'held-back-offline' as const });

/**
 * Why the read feeding the screen is not landing, or null while it is.
 *
 * <b>Three ways of not being refreshed, and a reader is owed the same sentence for all of them.</b>
 * What somebody waiting needs to know is whether the figures in front of them are still being
 * kept up — not which layer stopped keeping them up.
 *
 * A read that failed and has stopped trying is the plain case. The other two leave no failure
 * behind at all, and a page that looked only for one would go on looking live through both:
 *
 * - <b>A browser that knows it is offline does not send the read.</b> Nothing fails, because
 *   nothing was attempted; the read is held until the connection is back. On a phone at a cave's
 *   car park that is the ordinary way a page stops refreshing, and it is exactly as stale as one
 *   whose request went out and died.
 * - <b>A read the server asked to have back later is waiting, not failing.</b> The wait it named
 *   can be most of a minute, three times over, before the read is finally given up on — a long
 *   time to show a party's positions as current while deliberately not asking for them.
 *
 * A read being retried on this client's own short pacing is neither: that is a blip of a few
 * seconds that usually clears on the next attempt, and a warning that flashed up for it would
 * teach readers to ignore the warning.
 */
export function readNotLanding(read: PublicReadState): unknown {
  if (read.error != null) {
    return read.error;
  }
  if (read.isPaused === true) {
    return HELD_BACK_OFFLINE;
  }
  if (read.failureReason instanceof ApiError && read.failureReason.retryAfterMs !== undefined) {
    return read.failureReason;
  }
  return null;
}
