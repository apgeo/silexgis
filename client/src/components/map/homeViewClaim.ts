// SPDX-License-Identifier: AGPL-3.0-or-later

const KEY = 'silexgis.homeApplied';

/**
 * The claim held in memory as well, for a browser that refuses session storage — a private window,
 * blocked site data — where reading or writing it throws. Without this the map page's first render
 * would throw too and be replaced by the error screen; with it the home view is applied once per
 * page load instead of once per session.
 */
let claimedHere = false;

/**
 * Whether the home view may be applied now: true exactly once per browser session, and claimed by
 * asking. The map page claims it on its first load of the saved views, whether or not it then
 * applies the home view, so that a requested view or a shared position is never overwritten by
 * the home view later in the session.
 */
export function claimHomeView(): boolean {
  if (claimedHere) {
    return false;
  }
  claimedHere = true;
  try {
    if (sessionStorage.getItem(KEY)) {
      return false;
    }
    sessionStorage.setItem(KEY, '1');
  } catch {
    // Storage refused: the in-memory claim above stands in for it.
  }
  return true;
}

/** Tests start each case as a fresh page load. */
export function forgetHomeViewClaimForTests(): void {
  claimedHere = false;
}
