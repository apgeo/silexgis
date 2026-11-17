// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
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

/** A token with less than this left is renewed on return rather than spent on the next request. */
const RENEW_ON_RETURN_WITHIN_S = 60;

/**
 * Renews the held sign-in when the tab gets its connection back or is looked at again, if its
 * token has run out or is about to.
 *
 * The automatic renewal fires once, shortly before the token runs out; a laptop that slept or a
 * phone that lost signal across that moment comes back with an expired token and no renewal set.
 * The transport recovers from the first refused request anyway, but renewing here first means the
 * screens that refetch on return ask with a good token instead of each being refused once. Only a
 * renewal is started: a sign-in found to be over is left to the next refused request, which knows
 * the address to come back to — a redirect merely for looking at a tab would surprise.
 */
export function renewOnReturn(): () => void {
  const check = () => {
    if (document.visibilityState === 'hidden') return;
    void userManager
      .getUser()
      .then((user) => {
        const left = user?.expires_in;
        if (user?.refresh_token && left !== undefined && left <= RENEW_ON_RETURN_WITHIN_S) {
          void renewSignIn();
        }
      })
      .catch(() => undefined);
  };
  window.addEventListener('online', check);
  document.addEventListener('visibilitychange', check);
  return () => {
    window.removeEventListener('online', check);
    document.removeEventListener('visibilitychange', check);
  };
}

/** {@link renewOnReturn} for as long as the calling component is mounted. */
export function useRenewSignInOnReturn(): void {
  useEffect(() => renewOnReturn(), []);
}
