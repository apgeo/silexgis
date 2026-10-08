// SPDX-License-Identifier: AGPL-3.0-or-later
import { ErrorResponse } from 'oidc-client-ts';
import { userManager } from './auth.tsx';

/**
 * What became of an attempt to renew the sign-in this tab holds.
 *
 * - `renewed`: the tab holds a token the server takes again;
 * - `lapsed`: the sign-in is over — there was nothing to renew it with, or the server said no. Only
 *   signing in again, which is a full redirect, brings it back;
 * - `noAnswer`: nobody answered the renewal. The sign-in may well still be good, and asking again
 *   later is the whole of what there is to do.
 */
export type SignInRenewal = 'renewed' | 'lapsed' | 'noAnswer';

let renewing: Promise<SignInRenewal> | null = null;

async function renew(): Promise<SignInRenewal> {
  try {
    // Nothing to renew with is a sign-in that is over, said without asking the server anything.
    if (!(await userManager.getUser())?.refresh_token) return 'lapsed';
    const user = await userManager.signinSilent();
    return user && !user.expired ? 'renewed' : 'lapsed';
  } catch (error) {
    // The server's own refusal is an answer; anything else is a request that got none.
    return error instanceof ErrorResponse ? 'lapsed' : 'noAnswer';
  }
}

/**
 * Renews the sign-in now, for a request the server refused as coming from nobody.
 *
 * <b>Why anything needs this.</b> The sign-in is held in memory and its token lasts minutes. It is
 * renewed once, shortly before it runs out, and a renewal that finds no connection is not tried
 * again — so a tab that sat through an outage longer than a token's life comes back holding a
 * token the server no longer takes, with nothing set to replace it. What can still replace it is
 * the longer-lived renewal token beside it, and this is the asking.
 *
 * One renewal at a time: a renewal token is spent by being used, so two requests refused in the
 * same moment must share one renewal rather than each spend the token the other needed.
 */
export function renewSignIn(): Promise<SignInRenewal> {
  renewing ??= renew().finally(() => {
    renewing = null;
  });
  return renewing;
}

/**
 * Starts signing in again, to come back to `returnTo`.
 *
 * A full redirect: everything this tab holds in memory goes with it, and only what the browser
 * stores is there afterwards.
 */
export function signInAgain(returnTo: string): Promise<void> {
  return userManager.signinRedirect({ state: returnTo });
}

/** Tells `listener` whenever this tab comes to hold a sign-in — a first one, or a renewed one. */
export function onSignInRenewed(listener: () => void): () => void {
  userManager.events.addUserLoaded(listener);
  return () => userManager.events.removeUserLoaded(listener);
}
