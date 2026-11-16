// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { retryAfterOf } from '../api/client.ts';

/**
 * The sentence for an anonymous sign-in request (login, second factor, confirmation, reset) that
 * failed for a reason that has nothing to do with what the person typed, or `undefined` when the
 * server did answer about the request itself and the page's own wording applies.
 *
 * - no answer at all (the fetch rejected: no connection, a server restarting, a proxy that reset the
 *   connection) — "cannot reach the server";
 * - 429 — the sign-in endpoints sit behind a per-address window that a whole hut behind one NAT can
 *   fill; telling that person their password is wrong makes them retype it, which keeps the window
 *   full, or reset a password that was right. The wait the server names is passed on;
 * - 5xx (the server, or a proxy in front of it, failed) — "the server could not complete it".
 *
 * These pages talk to the server with plain `fetch`, not the generated client: the endpoints are
 * anonymous and the transport's sign-in renewal has nothing to do for them.
 */
export function authTransportProblem(response: Response | null, t: TFunction): string | undefined {
  if (response === null) {
    return t('auth.unreachable');
  }
  if (response.status === 429) {
    const waitMs = retryAfterOf(response);
    return waitMs === undefined
      ? t('auth.tooManyAttempts')
      : t('auth.tooManyAttemptsWait', { seconds: Math.max(1, Math.ceil(waitMs / 1000)) });
  }
  if (response.status >= 500) {
    return t('auth.serverFailed');
  }
  return undefined;
}

/** `fetch`, with a rejection (no answer) turned into `null` so a caller words it rather than throws. */
export function fetchOrNull(input: string, init?: RequestInit): Promise<Response | null> {
  return fetch(input, init).catch(() => null);
}
