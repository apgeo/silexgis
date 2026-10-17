// SPDX-License-Identifier: AGPL-3.0-or-later
import { isValidElement } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { matchRoutes, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/**
 * A pop-out window keeps the account's preferences in step itself: the arrangement made in it is
 * the account's, and without a writer of its own it stayed in that window's copy of the cache.
 */

const synced = vi.fn();
vi.mock('../../api/hooks/accountPrefs.ts', () => ({
  useAccountPrefsSync: (accountId: string | null, accessToken?: string) => synced(accountId, accessToken),
}));
vi.mock('../../auth/auth.tsx', () => ({
  useAuth: () => ({ user: { profile: { sub: 'account-a' }, access_token: 'token-a' } }),
}));

const { default: PanelShell } = await import('./PanelShell.tsx');
const { routes } = await import('../../App.tsx');

afterEach(() => {
  cleanup();
  useUiPrefsStore.getState().releaseAccount();
});

describe('a pop-out window', () => {
  it('claims the cache for its account and runs the writer, with the account’s token', () => {
    useUiPrefsStore.getState().adoptAccount('somebody-else');
    render(
      <MemoryRouter initialEntries={['/panel/selection']}>
        <Routes>
          <Route element={<PanelShell />}>
            <Route path="/panel/:panelId" element={<p>panel</p>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByText('panel')).toBeTruthy();
    expect(useUiPrefsStore.getState().accountId).toBe('account-a');
    expect(synced).toHaveBeenCalledWith('account-a', 'token-a');
  });

  it('is drawn inside that shell in the application’s routes', () => {
    const matched = matchRoutes(routes, '/panel/selection');
    expect(matched?.at(-1)?.route.path).toBe('/panel/:panelId');
    expect(matched?.some((m) => isValidElement(m.route.element) && m.route.element.type === PanelShell)).toBe(true);
  });
});
