// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

const order: string[] = [];

vi.mock('./auth.tsx', () => ({
  userManager: { stopSilentRenew: () => order.push('stop renewal') },
}));
vi.mock('../api/hooks/accountPrefs.ts', () => ({
  flushAccountPreferences: () => {
    order.push(`flush while held by ${useUiPrefsStore.getState().accountId ?? 'nobody'}`);
    return Promise.resolve();
  },
}));

const { signOutAfterPasswordChange } = await import('./signOutAfterPasswordChange.ts');

afterEach(() => {
  vi.unstubAllGlobals();
  useUiPrefsStore.getState().releaseAccount();
  order.length = 0;
});

describe('signOutAfterPasswordChange', () => {
  it('writes what is waiting, then releases this browser’s copy of the account’s preferences', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response())));
    const replace = vi.fn();
    vi.stubGlobal('location', { replace });
    useUiPrefsStore.getState().adoptAccount('account-a');
    useUiPrefsStore.getState().setCoordinateFormat('utm');

    await signOutAfterPasswordChange();

    expect(order).toEqual(['flush while held by account-a', 'stop renewal']);
    expect(useUiPrefsStore.getState().accountId).toBeUndefined();
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('dd');
    expect(replace).toHaveBeenCalledWith('/login?passwordChanged=1');
  });
});
