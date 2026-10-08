// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { App } from 'antd';
import { act, cleanup, renderHook, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError, tellsWhenTheServerAnswered } from '../../api/client.ts';
import type { SignInRenewal } from '../../auth/renewSignIn.ts';
import type { HeldReport } from './trackingOutbox.ts';

const sendHeld = vi.fn();
vi.mock('../../api/hooks.ts', () => ({
  useSendHeldTrackingReport: () => sendHeld,
}));

// The sign-in, stood in for: it lives in memory behind a redirect to the server. What is under
// test is what the sender does with a renewal's outcome and with the notice that one happened.
const renewSignIn = vi.fn<() => Promise<SignInRenewal>>();
const whenRenewed = new Set<() => void>();
vi.mock('../../auth/renewSignIn.ts', () => ({
  renewSignIn: () => renewSignIn(),
  onSignInRenewed: (listener: () => void) => {
    whenRenewed.add(listener);
    return () => whenRenewed.delete(listener);
  },
}));

const { useTrackingOutboxDrain } = await import('./useTrackingOutboxDrain.ts');
const { holdReport, heldReportsOf, refreshHeldReports } = await import('./trackingOutbox.ts');
const { resetHeldReportsDrain, isHeldReportsSignInLapsed } = await import(
  './trackingOutboxDrain.ts'
);

/** The server answering some other request of the page's, as the transport reports it. */
const serverAnswers = (status = 200) =>
  tellsWhenTheServerAnswered.onResponse({ response: new Response(null, { status }) });

const ANA = 'account-ana';

let made = 0;
function held(over: Partial<HeldReport> = {}): HeldReport {
  made += 1;
  const entry: HeldReport = {
    clientKey: `key-${made}`,
    accountId: ANA,
    tripLogId: 'trip-1',
    body: {
      caverIds: ['caver-1'],
      kind: 'entered',
      stationName: null,
      toStationName: null,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt: `2026-05-01T10:00:0${made}.000Z`,
    },
    composedAt: `2026-05-01T10:00:0${made}.000Z`,
    state: 'held',
    problemCode: null,
    attempts: 1,
    ...over,
  };
  holdReport(entry);
  return entry;
}

function mount(accountId: string | null, automatic: boolean) {
  const wrapper = ({ children }: { children: ReactNode }) => <App>{children}</App>;
  return renderHook(() => useTrackingOutboxDrain(accountId, { automatic }), { wrapper });
}

let online = true;
let visibility: DocumentVisibilityState = 'visible';

beforeEach(() => {
  made = 0;
  online = true;
  visibility = 'visible';
  vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
  window.localStorage.clear();
  refreshHeldReports();
  resetHeldReportsDrain();
  sendHeld.mockReset().mockResolvedValue([{}]);
  renewSignIn.mockReset().mockResolvedValue('lapsed');
  whenRenewed.clear();
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

const sentKeys = () => sendHeld.mock.calls.map(([request]) => (request as { clientKey: string }).clientKey);

describe('the sender mounted for the whole of a signed-in session', () => {
  it('sends what is held as soon as it is mounted, and says how many went', async () => {
    held();
    held();

    mount(ANA, true);

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sentKeys()).toEqual(['key-1', 'key-2']);
    expect(await screen.findByText('Held reports sent: 2.')).toBeInTheDocument();
  });

  it('makes no request when nothing is held', async () => {
    mount(ANA, true);
    await act(async () => {
      window.dispatchEvent(new Event('online'));
      await Promise.resolve();
    });

    expect(sendHeld).not.toHaveBeenCalled();
  });

  it('sends when the browser says the connection is back', async () => {
    online = false;
    held();
    mount(ANA, true);
    await act(async () => {
      await Promise.resolve();
    });
    // Known to be offline: asking could only fail.
    expect(sendHeld).not.toHaveBeenCalled();

    online = true;
    act(() => {
      window.dispatchEvent(new Event('online'));
    });

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sentKeys()).toEqual(['key-1']);
  });

  it('sends when the page comes back into view, and not when it goes out of it', async () => {
    sendHeld.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    held();
    mount(ANA, true);
    await waitFor(() => expect(sendHeld).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));

    visibility = 'hidden';
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await act(async () => {
      await Promise.resolve();
    });
    expect(sendHeld).toHaveBeenCalledTimes(1);

    visibility = 'visible';
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sendHeld).toHaveBeenCalledTimes(2);
  });

  it('says nothing when a send it started by itself found the server still away', async () => {
    sendHeld.mockRejectedValue(new TypeError('Failed to fetch'));
    held();

    mount(ANA, true);

    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));
    expect(screen.queryByText(/Still no answer/)).toBeNull();
  });

  it('says how many the server refused, and that they are kept', async () => {
    sendHeld.mockRejectedValue(new ApiError(409, 'tracking.not_writable'));
    held();

    mount(ANA, true);

    expect(await screen.findByText(/Held reports the server refused: 1\./)).toBeInTheDocument();
    expect(heldReportsOf(ANA)).toEqual([expect.objectContaining({ state: 'refused' })]);
  });

  it('says apart what the server already had and was taken off the log since', async () => {
    sendHeld.mockResolvedValue([]);
    held();

    mount(ANA, true);

    expect(await screen.findByText(/taken off the log since: 1\./)).toBeInTheDocument();
    expect(screen.queryByText(/Held reports sent/)).toBeNull();
    expect(heldReportsOf(ANA)).toEqual([]);
  });

  it('sends again when a wait the server asked for is over', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    try {
      sendHeld.mockRejectedValueOnce(new ApiError(429, undefined, undefined, undefined, 20_000));
      held();
      mount(ANA, true);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(sendHeld).toHaveBeenCalledTimes(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(19_000);
      });
      expect(sendHeld).toHaveBeenCalledTimes(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1_000);
      });
      expect(sendHeld).toHaveBeenCalledTimes(2);
      expect(heldReportsOf(ANA)).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  /**
   * The browser calls itself online throughout — a weak signal, a network with no way out, a
   * server restarting — so it will never announce a connection. What does show the server is
   * there again is that it answered something.
   */
  it('sends what is held when the server answers any other request, without a press', async () => {
    sendHeld.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    held();
    mount(ANA, true);
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));
    expect(sendHeld).toHaveBeenCalledTimes(1);
    // Past the few seconds in which an answer is not taken as a reason to try again.
    const now = Date.now();
    vi.spyOn(Date, 'now').mockReturnValue(now + 6_000);

    // A refusal is not evidence of anything the report could use.
    act(() => {
      serverAnswers(401);
    });
    await act(async () => {
      await Promise.resolve();
    });
    expect(sendHeld).toHaveBeenCalledTimes(1);

    act(() => {
      serverAnswers();
    });

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sendHeld).toHaveBeenCalledTimes(2);
    expect(await screen.findByText('Held reports sent: 1.')).toBeInTheDocument();
  });

  it('asks nothing when the server answers while nothing is waiting', async () => {
    held({ state: 'refused', problemCode: 'tracking.not_writable' });
    mount(ANA, true);
    await act(async () => {
      await Promise.resolve();
    });

    act(() => {
      serverAnswers();
    });
    await act(async () => {
      await Promise.resolve();
    });

    expect(sendHeld).not.toHaveBeenCalled();
  });

  it('tries again by itself a few seconds after a send that got no answer', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    try {
      sendHeld.mockRejectedValueOnce(new TypeError('Failed to fetch'));
      held();
      mount(ANA, true);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(sendHeld).toHaveBeenCalledTimes(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(4_000);
      });
      expect(sendHeld).toHaveBeenCalledTimes(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1_000);
      });
      expect(sendHeld).toHaveBeenCalledTimes(2);
      expect(heldReportsOf(ANA)).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  describe('when the sign-in ran out during the outage', () => {
    /**
     * The case the queue exists for: underground longer than a token lasts. The connection is back,
     * the tab still holds the token that ran out, and nobody presses anything.
     */
    it('renews it and sends the report, by itself', async () => {
      let signedIn = false;
      sendHeld.mockImplementation(() =>
        signedIn ? Promise.resolve([{}]) : Promise.reject(new ApiError(401)),
      );
      renewSignIn.mockImplementation(() => {
        signedIn = true;
        return Promise.resolve('renewed');
      });
      held();

      mount(ANA, true);

      await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
      expect(sentKeys()).toEqual(['key-1', 'key-1']);
      expect(renewSignIn).toHaveBeenCalledTimes(1);
      expect(await screen.findByText('Held reports sent: 1.')).toBeInTheDocument();
      expect(screen.queryByText(/Your sign-in has lapsed/)).toBeNull();
    });

    it('says so although nobody pressed anything when it cannot be renewed, and says it once', async () => {
      sendHeld.mockRejectedValue(new ApiError(401));
      held();

      mount(ANA, true);

      expect(await screen.findByText(/Your sign-in has lapsed, so the held reports cannot be sent/)).toBeInTheDocument();
      expect(heldReportsOf(ANA)).toEqual([expect.objectContaining({ state: 'held' })]);
      expect(isHeldReportsSignInLapsed()).toBe(true);

      // The page comes back into view: tried again, and nothing new to tell.
      act(() => {
        document.dispatchEvent(new Event('visibilitychange'));
      });
      await waitFor(() => expect(sendHeld).toHaveBeenCalledTimes(2));
      await act(async () => {
        await Promise.resolve();
      });
      expect(screen.getAllByText(/Your sign-in has lapsed/)).toHaveLength(1);
    });

    it('sends when the sign-in is renewed by something else', async () => {
      sendHeld.mockRejectedValueOnce(new ApiError(401));
      held();
      mount(ANA, true);
      await waitFor(() => expect(isHeldReportsSignInLapsed()).toBe(true));

      act(() => {
        for (const renewed of whenRenewed) renewed();
      });

      await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
      expect(isHeldReportsSignInLapsed()).toBe(false);
    });
  });

  /**
   * The other account's report is sent the moment the hook is mounted for that account — the last
   * lines — so its staying where it is above is the account rule and nothing else.
   */
  it('never sends what another account composed in this browser', async () => {
    held({ accountId: 'account-bogdan' });

    const first = mount(ANA, true);
    await act(async () => {
      await Promise.resolve();
    });
    expect(sendHeld).not.toHaveBeenCalled();
    expect(heldReportsOf('account-bogdan')).toHaveLength(1);
    first.unmount();

    mount('account-bogdan', true);
    await waitFor(() => expect(heldReportsOf('account-bogdan')).toEqual([]));
    expect(sendHeld).toHaveBeenCalledTimes(1);
  });

  it('sends nothing for a tab signed in as nobody it can name', async () => {
    held();

    const { result } = mount(null, true);
    await act(async () => {
      await result.current.sendNow();
    });

    expect(sendHeld).not.toHaveBeenCalled();
    expect(heldReportsOf(ANA)).toHaveLength(1);
  });
});

describe('sending because somebody pressed', () => {
  it('does not send by itself where it is not the session’s sender', async () => {
    held();

    mount(ANA, false);
    await act(async () => {
      window.dispatchEvent(new Event('online'));
      await Promise.resolve();
    });

    expect(sendHeld).not.toHaveBeenCalled();
  });

  it.each([
    ['no answer came', () => new TypeError('Failed to fetch'), /Still no answer from the server/],
    ['the sign-in has lapsed', () => new ApiError(401), /Your sign-in has lapsed/],
    ['the server is busy', () => new ApiError(503), /cannot take reports right now/],
  ])('says why nothing left when %s', async (_, failure, said) => {
    sendHeld.mockImplementation(() => Promise.reject(failure()));
    held();
    const { result } = mount(ANA, false);

    await act(async () => {
      await result.current.sendNow();
    });

    expect(await screen.findByText(said)).toBeInTheDocument();
    expect(heldReportsOf(ANA)).toEqual([expect.objectContaining({ state: 'held' })]);
  });
});
