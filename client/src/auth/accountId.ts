// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { userManager } from './auth.tsx';

/**
 * Which account this tab is signed in as, or null when it is signed in as nobody.
 *
 * Read from the sign-in this tab already holds in memory, so it asks the server nothing — which is
 * the point of it: the caller that needs to know whose a thing is may be asking precisely because
 * the server cannot be reached. The value is the account's own id, the same one the server writes
 * as the subject of every token it issues, so it is stable across sign-ins and is not a name.
 *
 * A sign-in whose token has run out still names its account. What lapsed is the right to act, not
 * whose the tab is, and something kept for that account is still that account's.
 */
export async function signedInAccountId(): Promise<string | null> {
  try {
    return (await userManager.getUser())?.profile.sub ?? null;
  } catch {
    return null;
  }
}

/**
 * {@link signedInAccountId}, kept current for a component.
 *
 * Null until the sign-in has been read, and whenever the tab is signed in as nobody — so a surface
 * that shows what belongs to an account shows nothing in both cases, which is the safe way round.
 * Asked of the sign-in itself rather than of the provider above the router, so that a surface drawn
 * outside that provider is answered "nobody" instead of failing to draw.
 */
export function useSignedInAccountId(): string | null {
  const [accountId, setAccountId] = useState<string | null>(null);
  useEffect(() => {
    let mounted = true;
    const read = () => {
      void signedInAccountId().then((found) => {
        if (mounted) setAccountId(found);
      });
    };
    read();
    // The sign-in changes without this component being drawn again: a token is renewed in the
    // background, and a session that ends is taken out of memory.
    userManager.events.addUserLoaded(read);
    userManager.events.addUserUnloaded(read);
    return () => {
      mounted = false;
      userManager.events.removeUserLoaded(read);
      userManager.events.removeUserUnloaded(read);
    };
  }, []);
  return accountId;
}
