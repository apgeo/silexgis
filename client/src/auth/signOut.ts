// SPDX-License-Identifier: AGPL-3.0-or-later
import { flushAccountPreferences } from '../api/hooks/accountPrefs.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { userManager } from './auth.tsx';
import { endSession } from './endSession.ts';

/**
 * Signs the account out of this browser and loads `destination` as a new document — the one way
 * every sign-out goes, whoever asks for it (the account menu, a password change, anything later).
 *
 * The arrangement this browser holds is the member's, not the browser's: a preference change still
 * waiting is written to their account first, while the access token still works; then this
 * browser's copy is released, so whoever signs in here next starts from the defaults rather than
 * from the previous member's layout; only then is the session ended. A caller that ended the
 * session alone would drop the unsent change and hand the next person the previous one's layout.
 *
 * It is not offered through the sign-in context: that module sits underneath the API transport,
 * and the preferences it must flush are written through that transport.
 */
export async function signOutOfThisBrowser(destination = '/'): Promise<void> {
  await flushAccountPreferences();
  useUiPrefsStore.getState().releaseAccount();
  await endSession(userManager, destination);
}
