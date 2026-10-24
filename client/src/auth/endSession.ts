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
 * <b>Why the cookie is ended, and the renewal key cancelled.</b> The cookie is the part of the
 * session that outlives the page: the tokens live in memory, so loading `destination` as a new
 * document is what discards them, together with every cached answer about the account. But a copy
 * of the refresh token taken out of the page beforehand would otherwise stay good for weeks, so it is
 * cancelled on the server first — which also ends every earlier key of this one sign-in, and no
 * other sign-in of the account. The renewal is stopped before that, so that no renewal spends the
 * key in between and leaves a fresh one uncancelled.
 *
 * <b>Never in the way of leaving.</b> Neither request can stop the sign-out: if the server cannot
 * be reached, the page still forgets its keys and leaves.
 */
export async function endSession(
  userManager: Pick<UserManager, 'stopSilentRenew' | 'revokeTokens'>,
  destination: string,
): Promise<void> {
  userManager.stopSilentRenew();
  try {
    // Only the refresh token: the access token is a signed document the API checks by its
    // signature, so cancelling it would change nothing for the quarter of an hour it has left.
    await userManager.revokeTokens(['refresh_token']);
  } catch {
    // Unreachable server, or a key already gone: the page forgets it either way.
  }
  try {
    await fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'same-origin' });
  } catch {
    // A cookie that outlives this only means the next sign-in is answered without the form, which
    // the reader can still sign out of; the tokens go with the page either way.
  }
  window.location.replace(destination);
}
