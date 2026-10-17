// SPDX-License-Identifier: AGPL-3.0-or-later
import { Outlet } from 'react-router-dom';
import { useAuth } from '../../auth/auth.tsx';
import AccountPrefsGate from '../../components/AccountPrefsGate.tsx';

/**
 * A pop-out window's account preferences, kept in step with the account as the main window's are.
 *
 * A pop-out is drawn without the signed-in shell, but what is arranged in it — a pop-out selection
 * panel's sections, its pinned and dense settings, a layout saved from it — is the account's like
 * any other arrangement. Each window is its own copy of the application, so the window claims the
 * browser's cache for the account and runs the one writer itself: what is changed there is
 * written to the account from there, measured from what that window last read, rather than waiting
 * in its own copy of the cache for a write from the main window that never carries it.
 */
export default function PanelShell() {
  const { user } = useAuth();
  return (
    <AccountPrefsGate accountId={user?.profile.sub ?? null} accessToken={user?.access_token}>
      <Outlet />
    </AccountPrefsGate>
  );
}
