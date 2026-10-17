// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Two tabs of one browser signed in to the same account. Each tab is its own copy of the
 * application — its own preference store and its own writer — sharing only the browser's storage
 * and the server, as two tabs do.
 */

/** The account's stored document, as the server holds it. */
let stored: Record<string, unknown> = {};
const put = vi.fn();
const read = vi.fn();

vi.mock('../client.ts', async () => {
  const { applyMergePatch } = await import('../../stores/accountPrefs.ts');
  return {
    api: {
      GET: () => {
        read();
        return Promise.resolve({ data: { preferences: stored }, response: new Response() });
      },
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

async function openTab() {
  vi.resetModules();
  const { useUiPrefsStore } = await import('../../stores/uiPrefsStore.ts');
  const { useAccountPrefsSync } = await import('./accountPrefs.ts');
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const reads = read.mock.calls.length;
  renderHook(() => useAccountPrefsSync('account-a'), { wrapper });
  // Signed in once the account's document has been read and taken.
  await waitFor(() => expect(read.mock.calls.length).toBeGreaterThan(reads));
  await act(async () => {
    await vi.advanceTimersByTimeAsync(10);
  });
  return useUiPrefsStore;
}

afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.useRealTimers();
});

describe('two tabs signed in to one account', () => {
  it('do not send each other’s older values back', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { layouts: [{ id: 'a', name: 'Survey', panels: {} }] };
    const desktop = await openTab();
    const laptop = await openTab();

    act(() => desktop.getState().setCoordinateFormat('utm'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(stored.coordinateFormat).toBe('utm');

    act(() => {
      laptop.getState().setPanelPrefs('main', { width: 31 });
      laptop.getState().saveLayout('Mapping', false);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    // The laptop sent what it changed, and only that: the desktop's format stands, and both
    // layouts are kept.
    expect(stored.coordinateFormat).toBe('utm');
    expect((stored.panels as { main: { width: number } }).main.width).toBe(31);
    expect((stored.layouts as { name: string }[]).map((l) => l.name)).toEqual(['Survey', 'Mapping']);
    for (const patch of put.mock.calls.map(([body]) => body as Record<string, unknown>)) {
      expect(patch).not.toHaveProperty('coordinateFormat', null);
    }
    // And the laptop has the desktop's choice now, from its own write's answer.
    expect(laptop.getState().coordinateFormat).toBe('utm');
  });

  it('a pop-out window writes its own arrangement, and the main window’s next write keeps it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = {};
    const main = await openTab();
    const popout = await openTab();

    act(() => popout.getState().setPanelPrefs('popout', { collapsed: ['history'], pinned: false }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    // Written from the pop-out itself, not left in its copy of the cache.
    expect((stored.panels as Record<string, unknown>).popout).toEqual({ collapsed: ['history'], pinned: false });

    act(() => main.getState().setPanelPrefs('main', { width: 33 }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    // The main window sent its own change only, and has the pop-out's from its write's answer.
    expect(stored.panels).toEqual({ main: { width: 33 }, popout: { collapsed: ['history'], pinned: false } });
    expect(put.mock.calls.at(-1)?.[0]).toEqual({ panels: { main: { width: 33 } } });
    expect(main.getState().panels.popout).toEqual({ collapsed: ['history'], pinned: false });

    // And the next pop-out opened, anywhere, starts from it.
    localStorage.clear();
    const another = await openTab();
    expect(another.getState().panels.popout).toEqual({ collapsed: ['history'], pinned: false });
  });
});
