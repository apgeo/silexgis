// SPDX-License-Identifier: AGPL-3.0-or-later
import { flushAccountPreferences } from '../api/hooks/accountPrefs.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { userManager } from './auth.tsx';

/** The address the sign-in page reads to say why the reader is there. */
export const PASSWORD_CHANGED_LOGIN = '/login?passwordChanged=1';

/**
 * Ends this browser's session after its own password change and sends the reader to sign in.
 *
 * <b>Why not a silent renewal.</b> A password change ends every renewable session of the account
 * on the server, this browser's included: the refresh token a silent renewal would spend is
 * exactly what was just revoked. Leaving it to the renewal would keep the page working on the
 * access token for up to a quarter of an hour and then fail at some unrelated click.
 *
 * <b>Why the cookie is ended too.</b> The server reissues the caller's own sign-in cookie in the
 * same response, and the authorization endpoint accepts that cookie without asking anything. Left
 * alone, the next page load would trade it for fresh tokens and the reader would never be asked
 * for the password they just chose — which is the one moment it is worth asking.
 *
 * <b>Why a full navigation.</b> The tokens live in memory only, so loading the sign-in page as a
 * new document is what discards them, together with every cached answer about the account. Clearing
 * them first, in place, would let the guard on the page still open notice the missing session and
 * start a sign-in redirect of its own before the navigation below had happened.
 */
export async function signOutAfterPasswordChange(): Promise<void> {
  // A sign-out like any other for the preferences this browser holds for the account: what is
  // still waiting is written while the access token still works, then the cache is released, so
  // the sign-in page — and whoever signs in next — starts from the defaults.
  await flushAccountPreferences();
  useUiPrefsStore.getState().releaseAccount();
  userManager.stopSilentRenew();
  try {
    await fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'same-origin' });
  } catch {
    // The server has already ended every renewable session; a cookie that outlives this only
    // means the next sign-in is answered without the form, which the reader can still sign out of.
  }
  window.location.replace(PASSWORD_CHANGED_LOGIN);
}
