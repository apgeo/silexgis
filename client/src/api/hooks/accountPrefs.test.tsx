// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { changedSince, pickAccountPrefs, type AccountPrefs } from '../../stores/accountPrefs.ts';
import { DEFAULT_DOCKS, useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/** Whether this browser holds a change the account has not been sent yet. */
function unsaved(): boolean {
  const state = useUiPrefsStore.getState();
  return changedSince((state.accountPrefsBaseline ?? {}) as AccountPrefs, pickAccountPrefs(state));
}

/** The account's stored document, as the server holds it; reassigned per test. */
let stored: Record<string, unknown> = {};
/** Each merge patch the server was sent. */
const put = vi.fn();

vi.mock('../client.ts', async () => {
  // The server's merging write, applied with the same rule it uses.
  const { applyMergePatch } = await import('../../stores/accountPrefs.ts');
  return {
    api: {
      GET: () => Promise.resolve({ data: { preferences: stored }, response: new Response() }),
      PATCH: (_path: string, { body }: { body: { preferences: Record<string, unknown> } }) => {
        put(body.preferences);
        stored = applyMergePatch(stored, body.preferences) as Record<string, unknown>;
        return Promise.resolve({ data: { preferences: stored }, response: new Response() });
      },
    },
    ApiError: class extends Error {},
    retryAfterOf: () => undefined,
  };
});

const { useAccountPrefsSync } = await import('./accountPrefs.ts');

function sync(accountId: string | null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return renderHook(({ id }) => useAccountPrefsSync(id), { initialProps: { id: accountId }, wrapper });
}

beforeEach(() => {
  stored = {};
  put.mockReset();
  useUiPrefsStore.getState().releaseAccount();
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe('account preferences between this browser and the account', () => {
  it('takes what the account stored when its member signs in on a fresh browser', async () => {
    stored = { coordinateFormat: 'utm', docks: { leftHidden: true, rightHidden: false }, other: 1 };

    sync('account-a');

    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm'));
    expect(useUiPrefsStore.getState().docks.leftHidden).toBe(true);
    expect(useUiPrefsStore.getState().accountId).toBe('account-a');
  });

  it('writes a change back, merged into the document, keeping what it does not own', async () => {
    stored = { appearance: { theme: 'dark', density: 'comfortable', reduceMotion: false }, other: { kept: true } };
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().appearance.theme).toBe('dark'));

    act(() => useUiPrefsStore.getState().setCoordinateFormat('dms'));
    expect(unsaved()).toBe(true);

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1), { timeout: 4000 });
    // Only what changed is sent; the server keeps the rest.
    expect(put).toHaveBeenCalledWith({ coordinateFormat: 'dms' });
    expect(stored).toEqual({
      appearance: { theme: 'dark', density: 'comfortable', reduceMotion: false },
      other: { kept: true },
      coordinateFormat: 'dms',
    });
    await waitFor(() => expect(unsaved()).toBe(false));
  });

  it('writes nothing when a screen writes back the arrangement it read', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { selectors: { map: { activeScopeIds: ['caves'] } } };
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().selectors.map?.activeScopeIds).toEqual(['caves']));

    act(() => useUiPrefsStore.getState().setSelectorPrefs('map', { activeScopeIds: ['caves'] }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(put).not.toHaveBeenCalled();
    expect(unsaved()).toBe(false);
  });

  it('does not hand one member’s arrangement to the next member signing in on the same browser', async () => {
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().accountId).toBe('account-a'));
    act(() => {
      useUiPrefsStore.getState().setCoordinateFormat('utm');
      useUiPrefsStore.getState().setDocks({ rightHidden: true });
    });
    cleanup();

    stored = {};
    sync('account-b');

    await waitFor(() => expect(useUiPrefsStore.getState().accountId).toBe('account-b'));
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('dd');
    expect(useUiPrefsStore.getState().docks).toEqual(DEFAULT_DOCKS);
  });

  it('writes nothing over the account after its member signed out', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().accountId).toBe('account-a'));

    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    act(() => useUiPrefsStore.getState().releaseAccount());
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(put).not.toHaveBeenCalled();
    // Nor is the reset the sign-out made counted as a change still to be written.
    expect(unsaved()).toBe(false);
  });

  it('keeps a change made here before a reload, and takes the account’s word for the rest', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { coordinateFormat: 'utm' };
    // As the last page left the cache: the account read as DD, overview opened here and not sent.
    useUiPrefsStore.setState({ accountId: 'account-a', accountPrefsBaseline: {}, overviewMap: true });

    sync('account-a');

    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm'));
    expect(useUiPrefsStore.getState().overviewMap).toBe(true);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(put).toHaveBeenCalledWith({ overviewMap: true });
  });
});
