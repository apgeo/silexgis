// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

const order: string[] = [];

vi.mock('./auth.tsx', () => ({ userManager: {} }));
vi.mock('./endSession.ts', () => ({
  endSession: (_manager: unknown, destination: string) => {
    order.push(`end the session, held by ${useUiPrefsStore.getState().accountId ?? 'nobody'}, for ${destination}`);
    return Promise.resolve();
  },
}));
vi.mock('../api/hooks/accountPrefs.ts', () => ({
  flushAccountPreferences: () => {
    order.push(`flush while held by ${useUiPrefsStore.getState().accountId ?? 'nobody'}`);
    return Promise.resolve();
  },
}));

const { signOutAfterPasswordChange } = await import('./signOutAfterPasswordChange.ts');

afterEach(() => {
  useUiPrefsStore.getState().releaseAccount();
  order.length = 0;
});

describe('signOutAfterPasswordChange', () => {
  it('writes what is waiting, then releases this browser’s copy of the account’s preferences', async () => {
    useUiPrefsStore.getState().adoptAccount('account-a');
    useUiPrefsStore.getState().setCoordinateFormat('utm');

    await signOutAfterPasswordChange();

    expect(order).toEqual([
      'flush while held by account-a',
      'end the session, held by nobody, for /login?passwordChanged=1',
    ]);
    expect(useUiPrefsStore.getState().accountId).toBeUndefined();
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('dd');
  });
});
