// SPDX-License-Identifier: AGPL-3.0-or-later
import type { UserManager } from 'oidc-client-ts';

/**
 * Ends this browser's session and leaves for `destination` as a new document.
 *
 * <b>Why not the authorization server's end-session redirect.</b> The library's own sign-out
 * removes the signed-in user from memory first and only then navigates, and the guard on the page
 * still open reacts to the missing user at once by starting a sign-in redirect of its own. That
 * redirect wins the race, and because the server's sign-in cookie is still there, the authorization
 * endpoint answers it without asking anything: the reader lands back where they were, signed in.
 * Nothing here clears the user in place, so nothing on the page can react before it is gone.
 *
 * <b>Why the cookie is what is ended.</b> It is the only part of the session that outlives the page:
 * the tokens live in memory, so loading `destination` as a new document is what discards them,
 * together with every cached answer about the account. The renewal is stopped first so that no
 * renewal spends the refresh token in between.
 */
export async function endSession(
  userManager: Pick<UserManager, 'stopSilentRenew'>,
  destination: string,
): Promise<void> {
  userManager.stopSilentRenew();
  try {
    await fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'same-origin' });
  } catch {
    // A cookie that outlives this only means the next sign-in is answered without the form, which
    // the reader can still sign out of; the tokens go with the page either way.
  }
  window.location.replace(destination);
}
