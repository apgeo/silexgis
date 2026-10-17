// SPDX-License-Identifier: AGPL-3.0-or-later
import { StrictMode, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { changedSince, pickAccountPrefs, type AccountPrefs } from '../../stores/accountPrefs.ts';
import type { PanelLayout } from '../../stores/panelPrefs.ts';
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
/** The longest merged document the server accepts — its own count, which may not be this client's. */
let serverLimit = Infinity;
/** When set, the next read waits for this before answering; when it rejects, the read fails. */
let readGate: Promise<void> | undefined;

vi.mock('../client.ts', async () => {
  // The server's merging write, applied with the same rule it uses.
  const { applyMergePatch } = await import('../../stores/accountPrefs.ts');
  class ApiError extends Error {
    readonly status: number;
    readonly code?: string;

    constructor(status: number, code?: string) {
      super(code);
      this.status = status;
      this.code = code;
    }
  }
  return {
    api: {
      GET: async () => {
        const gate = readGate;
        readGate = undefined;
        await gate;
        return { data: { preferences: stored }, response: new Response() };
      },
      PATCH: (_path: string, { body }: { body: { preferences: Record<string, unknown> } }) => {
        put(body.preferences);
        const merged = applyMergePatch(stored, body.preferences) as Record<string, unknown>;
        if (JSON.stringify(merged).length > serverLimit) {
          return Promise.resolve({
            error: { code: 'preferences.too_large' },
            response: new Response(null, { status: 400 }),
          });
        }
        stored = merged;
        return Promise.resolve({ data: { preferences: stored }, response: new Response() });
      },
    },
    ApiError,
    retryAfterOf: () => undefined,
  };
});

const { flushAccountPreferences, useAccountPrefsSync } = await import('./accountPrefs.ts');

function sync(accountId: string | null, { strict = false, token }: { strict?: boolean; token?: string } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => {
    const tree = <QueryClientProvider client={client}>{children}</QueryClientProvider>;
    return strict ? <StrictMode>{tree}</StrictMode> : tree;
  };
  return renderHook(({ id }) => useAccountPrefsSync(id, token), { initialProps: { id: accountId }, wrapper });
}

beforeEach(() => {
  stored = {};
  serverLimit = Infinity;
  readGate = undefined;
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

  /** Named layouts, each about 160 characters of the document. */
  const layouts = (count: number): PanelLayout[] =>
    Array.from({ length: count }, (_, n) => ({
      id: `layout-${n}`,
      name: `Arrangement ${n}`,
      panels: { main: { width: 20 + n, order: ['details', 'tags', 'attachments', 'links', 'text', 'history'] } },
      mapChromeHidden: false,
    }));

  it('plans a write the server finds too large once more, with room to spare, and sends it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().accountPrefsBaseline).toBeDefined());
    // The server counts this document longer than the client does.
    serverLimit = 7600;

    act(() => useUiPrefsStore.setState({ layouts: layouts(48), coordinateFormat: 'utm' }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(put).toHaveBeenCalledTimes(2);
    expect(stored.coordinateFormat).toBe('utm');
    expect(JSON.stringify(stored).length).toBeLessThanOrEqual(7600);
    expect(unsaved()).toBe(true); // the layouts the account could not take are still this browser's
  });

  it('gives a refused write up rather than leaving it standing at every later save and sign-in', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { coordinateFormat: 'dms' };
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('dms'));
    serverLimit = 0; // refuses everything

    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(put).toHaveBeenCalledTimes(2);
    expect(stored.coordinateFormat).toBe('dms');
    expect(unsaved()).toBe(false);

    // The next sign-in takes the account's own value, rather than holding the refused one.
    cleanup();
    serverLimit = Infinity;
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('dms'));
  });

  it('leaves the layouts the account holds alone when one saved here does not fit, and keeps that one here', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    // Saved from another browser, filling the account's document nearly to its limit.
    stored = { layouts: layouts(19).map((layout) => ({ ...layout, name: `${layout.name} ${'x'.repeat(240)}` })) };
    expect(JSON.stringify(stored).length).toBeGreaterThan(7500);
    const held = stored.layouts;
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().layouts).toHaveLength(19));

    act(() => useUiPrefsStore.getState().saveLayout('One more '.repeat(30), false));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(stored.layouts).toEqual(held);

    // And after a reload, this browser still has it; the account still has every one of its own.
    cleanup();
    sync('account-a');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().layouts).toHaveLength(20);
    expect(stored.layouts).toEqual(held);
  });

  it('writes a change made after the account could not be read', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    readGate = Promise.reject(new Error('offline'));
    sync('account-a');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10);
    });

    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(put).toHaveBeenCalledWith({ coordinateFormat: 'utm' });
  });

  it('keeps a change made while the account was being read, and writes it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { coordinateFormat: 'dms', overviewMap: true };
    let answer!: () => void;
    readGate = new Promise((resolve) => (answer = resolve));
    sync('account-a');

    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    await act(async () => {
      answer();
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm');
    expect(useUiPrefsStore.getState().overviewMap).toBe(true);
    expect(stored.coordinateFormat).toBe('utm');
  });

  it('writes a change still waiting when it is flushed, as a sign-out does', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().accountPrefsBaseline).toBeDefined());

    act(() => useUiPrefsStore.getState().setDocks({ leftHidden: true }));
    await act(async () => {
      await flushAccountPreferences();
    });

    expect(put).toHaveBeenCalledWith({ docks: { leftHidden: true, rightHidden: false } });
  });

  it('behaves under StrictMode as it does in production: one write, of the one change', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    // Another member's cache, and an account with values of its own.
    useUiPrefsStore.getState().adoptAccount('account-b');
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 40 });
    stored = { coordinateFormat: 'dms' };

    sync('account-a', { strict: true });
    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('dms'));
    expect(useUiPrefsStore.getState().panels).toEqual({});

    act(() => useUiPrefsStore.getState().setOverviewMap(true));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(put.mock.calls).toEqual([[{ overviewMap: true }]]);
  });

  it('marks a layout the account could not hold as kept here, until a later write gets it in', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { layouts: layouts(19).map((layout) => ({ ...layout, name: `${layout.name} ${'x'.repeat(240)}` })) };
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().layouts).toHaveLength(19));

    act(() => useUiPrefsStore.getState().saveLayout('One more '.repeat(30), false));
    const added = useUiPrefsStore.getState().layouts.at(-1)!.id;
    // Not marked while it is merely waiting to be written: only once a write had to leave it out.
    expect(useUiPrefsStore.getState().layoutsKeptHere).toEqual([]);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().layoutsKeptHere).toEqual([added]);

    // Still kept here after a reload, when the account still has no room for it.
    cleanup();
    sync('account-a');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().layoutsKeptHere).toEqual([added]);

    // Room made by deleting one the account holds: the next write takes it, and the mark goes.
    act(() => useUiPrefsStore.getState().deleteLayout(useUiPrefsStore.getState().layouts[0].id));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect((stored.layouts as PanelLayout[]).map((layout) => layout.id)).toContain(added);
    expect(useUiPrefsStore.getState().layoutsKeptHere).toEqual([]);
  });

  it('marks a layout added here as kept here when the account refuses the write', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    sync('account-a');
    await waitFor(() => expect(useUiPrefsStore.getState().accountPrefsBaseline).toBeDefined());
    serverLimit = 0; // refuses everything

    act(() => useUiPrefsStore.getState().saveLayout('Survey', false));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().layoutsKeptHere).toEqual([useUiPrefsStore.getState().layouts[0].id]);
  });
});

describe('a change made just before the page goes away', () => {
  const sent = vi.fn<(url: string, init: RequestInit) => Promise<Response>>();

  beforeEach(() => {
    sent.mockReset();
    sent.mockResolvedValue(new Response());
    vi.stubGlobal('fetch', sent);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    Reflect.deleteProperty(document, 'visibilityState');
  });

  async function signedIn(token?: string) {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stored = { coordinateFormat: 'dms', overviewMap: true };
    sync('account-a', { token });
    await waitFor(() => expect(useUiPrefsStore.getState().coordinateFormat).toBe('dms'));
  }

  function leave() {
    window.dispatchEvent(new Event('pagehide'));
  }

  it('is sent as the page goes, with the account’s own token, and nothing else with it', async () => {
    await signedIn('token-a');
    act(() => {
      useUiPrefsStore.getState().setCoordinateFormat('utm');
      useUiPrefsStore.getState().setPanelPrefs('popout', { pinned: false });
    });
    leave();

    expect(sent).toHaveBeenCalledTimes(1);
    const [url, init] = sent.mock.calls[0];
    expect(url).toBe('/api/v1/me/preferences');
    expect(init.method).toBe('PATCH');
    expect(init.keepalive).toBe(true);
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer token-a');
    expect(JSON.parse(init.body as string)).toEqual({
      preferences: { coordinateFormat: 'utm', panels: { popout: { pinned: false } } },
    });
  });

  it('leaves a change to the named layouts to a write that reads first', async () => {
    await signedIn('token-a');
    act(() => useUiPrefsStore.getState().saveLayout('Late', false));
    leave();
    expect(sent).not.toHaveBeenCalled();

    act(() => useUiPrefsStore.getState().setOverviewMap(false));
    leave();
    expect(JSON.parse(sent.mock.calls[0][1].body as string)).toEqual({ preferences: { overviewMap: null } });
  });

  it('sends nothing when nothing is unsent, without the account’s token, or after a sign-out', async () => {
    await signedIn('token-a');
    leave();
    expect(sent).not.toHaveBeenCalled();

    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    act(() => useUiPrefsStore.getState().releaseAccount());
    leave();
    expect(sent).not.toHaveBeenCalled();

    cleanup();
    await signedIn(undefined);
    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    leave();
    expect(sent).not.toHaveBeenCalled();
  });

  it('is dropped, never applied, when another member signs in on this browser before it lands', async () => {
    await signedIn('token-a');
    sent.mockRejectedValue(new TypeError('offline'));
    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    leave();
    expect(sent).toHaveBeenCalledTimes(1);
    cleanup();

    // The cache still holds the change, as account A's.
    expect(useUiPrefsStore.getState().accountId).toBe('account-a');
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm');

    stored = {};
    sync('account-b');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().accountId).toBe('account-b');
    expect(useUiPrefsStore.getState().coordinateFormat).not.toBe('utm');
    for (const [patch] of put.mock.calls) {
      expect(patch).not.toHaveProperty('coordinateFormat');
    }
  });

  it('counts what it sent as written, so the next sign-in here does not send it again over a later choice', async () => {
    await signedIn('token-a');
    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    leave();
    expect(sent).toHaveBeenCalledTimes(1);
    expect(unsaved()).toBe(false);
    cleanup();

    // The request landed; in another browser the person then went back to what they had before.
    stored = { coordinateFormat: 'dms', overviewMap: true };
    sync('account-a', { token: 'token-a' });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('dms');
    for (const [patch] of put.mock.calls) {
      expect(patch).not.toHaveProperty('coordinateFormat');
    }
  });

  it('counts the change as unsent again when the page is still here to see the request refused', async () => {
    await signedIn('token-a');
    sent.mockResolvedValue(new Response(null, { status: 503 }));
    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    leave();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(unsaved()).toBe(true);

    // Back from the back-forward cache: the settling write still goes.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(put).toHaveBeenCalledWith({ coordinateFormat: 'utm' });
  });

  it('is written the ordinary way, without waiting, when the page is hidden', async () => {
    await signedIn('token-a');
    act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(50);
    });
    expect(put).toHaveBeenCalledWith({ coordinateFormat: 'utm' });
    expect(sent).not.toHaveBeenCalled();
  });
});
