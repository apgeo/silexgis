// SPDX-License-Identifier: AGPL-3.0-or-later
import { useLayoutEffect, useState, type ReactNode } from 'react';
import { useAccountPrefsSync } from '../api/hooks/accountPrefs.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

/**
 * The signed-in shell, drawn only once this browser's account preferences are the signed-in
 * account's — and kept in step with the account from then on.
 *
 * The cache is claimed for the account before anything inside is drawn: claimed afterwards, the
 * first frame of the workspace would be drawn with the arrangement of whoever used this browser
 * before (their panels, their docks, their theme), and every screen that reads a preference once
 * as it opens would keep it. Claiming happens in a layout effect, which runs before the browser
 * paints, so the moment of drawing nothing is never seen either.
 */
export default function AccountPrefsGate({
  accountId,
  accessToken,
  children,
}: {
  accountId: string | null;
  /** The account's access token, for the write a closing page sends without the API client. */
  accessToken?: string;
  children: ReactNode;
}) {
  // The account the cache has been claimed for, as far as drawing goes. Not read live from the
  // store: a sign-out releases the cache while the page is still on screen, and the shell is left
  // drawn — with the defaults — until the sign-out has taken the page away.
  const [claimedFor, setClaimedFor] = useState(() =>
    accountId && useUiPrefsStore.getState().accountId === accountId ? accountId : null,
  );
  useLayoutEffect(() => {
    if (accountId) {
      useUiPrefsStore.getState().adoptAccount(accountId);
      setClaimedFor(accountId);
    }
  }, [accountId]);
  useAccountPrefsSync(accountId, accessToken);
  return accountId && claimedFor !== accountId ? null : children;
}
