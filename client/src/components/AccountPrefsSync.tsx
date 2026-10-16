// SPDX-License-Identifier: AGPL-3.0-or-later
import { useAccountPrefsSync } from '../api/hooks/accountPrefs.ts';

/**
 * Keeps this browser's account preferences in step with the signed-in account — mounted once in
 * the signed-in shell, drawing nothing.
 */
export default function AccountPrefsSync({ accountId }: { accountId: string | null }) {
  useAccountPrefsSync(accountId);
  return null;
}
