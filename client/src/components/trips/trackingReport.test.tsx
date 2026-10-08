// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { App } from 'antd';
import { act, cleanup, renderHook, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SignInRenewal } from '../../auth/renewSignIn.ts';

const recordEvents = vi.fn();
vi.mock('../../api/hooks.ts', () => ({
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
}));

// Who the tab is signed in as. Stood in for rather than signed in, because the sign-in lives in
// memory behind a redirect to the server; what is under test is what a report does with the answer.
let account: string | null = 'account-ana';
vi.mock('../../auth/accountId.ts', () => ({
  signedInAccountId: () => Promise.resolve(account),
}));

// Renewing the sign-in, stood in for like the sign-in itself: over, unless a test says otherwise.
const renewSignIn = vi.fn<() => Promise<SignInRenewal>>();
vi.mock('../../auth/renewSignIn.ts', () => ({
  renewSignIn: () => renewSignIn(),
}));

const { useTrackingReport } = await import('./trackingReport.ts');
const { heldReportsOf, refreshHeldReports } = await import('./trackingOutbox.ts');
const { isHeldReportsSignInLapsed, resetHeldReportsDrain, wakeHeldReportsWith } = await import(
  './trackingOutboxDrain.ts'
);

const ANA = 'account-ana';
const COMPOSED = new Date('2026-05-01T10:15:00.000Z');

/** A request nobody answered: what the browser raises with no signal or a dropped connection. */
const noAnswer = () => Promise.reject(new TypeError('Failed to fetch'));
/** An answer from the server, saying no. */
const refusal = (status: number, code?: string) => () =>
  Promise.reject(new ApiError(status, code));

function hook() {
  const wrapper = ({ children }: { children: ReactNode }) => <App>{children}</App>;
  return renderHook(() => useTrackingReport(), { wrapper }).result;
}

type Send = ReturnType<typeof useTrackingReport>['send'];
async function sent(send: Send, ...args: Parameters<Send>) {
  let outcome: Awaited<ReturnType<Send>> | undefined;
  await act(async () => {
    outcome = await send(...args);
  });
  return outcome!;
}

const AT_STATION = { kind: 'atStation', stationName: 'p.g.42', note: 'at the pitch head' } as const;

/** What reached the transport on the nth send. */
const posted = (n = 0) => recordEvents.mock.calls[n][0] as { clientKey: string } & object;

beforeEach(() => {
  account = ANA;
  window.localStorage.clear();
  refreshHeldReports();
  resetHeldReportsDrain();
  recordEvents.mockReset().mockResolvedValue([{}]);
  renewSignIn.mockReset().mockResolvedValue('lapsed');
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(COMPOSED);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('sending a tracking report', () => {
  it('sends a report under a key of its own, with no moment when none was named, and keeps nothing once it lands', async () => {
    const send = hook().current.send;

    const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

    expect(outcome).toEqual({ recorded: true, held: false, code: null });
    expect(posted()).toEqual({
      tripLogId: 'trip-1',
      caverIds: ['caver-1'],
      kind: 'atStation',
      stationName: 'p.g.42',
      depthM: null,
      teamId: null,
      note: 'at the pitch head',
      // The server's clock, while the server is there to be asked.
      recordedAt: null,
      clientKey: expect.stringMatching(/^[0-9a-f]{8}-[0-9a-f]{4}-/),
    });
    expect(heldReportsOf(ANA)).toEqual([]);
    expect(Object.keys(window.localStorage)).toEqual([]);
    expect(screen.getByText('Recorded for 1.')).toBeInTheDocument();
  });

  /**
   * The report is in storage before the server has said anything — which is what is left if the
   * tab is discarded, the page reloaded or the phone put to sleep while the request is out.
   */
  it('has the report in storage while the request is still out', async () => {
    let storedDuringSend: string[] = [];
    let listedDuringSend = -1;
    recordEvents.mockImplementation(() => {
      storedDuringSend = Object.keys(window.localStorage);
      listedDuringSend = heldReportsOf(ANA).length;
      return Promise.resolve([{}]);
    });
    const send = hook().current.send;

    await sent(send, 'trip-1', ['caver-1'], AT_STATION);

    expect(storedDuringSend).toEqual([`silexgis.trackingOutbox.${posted().clientKey}`]);
    // Kept, but not yet "held" to anybody: its author is still watching the button.
    expect(listedDuringSend).toBe(0);
    expect(Object.keys(window.localStorage)).toEqual([]);
  });

  describe('when no answer comes at all', () => {
    it('holds the report, under the key it was sent with and at the moment it was composed', async () => {
      recordEvents.mockImplementation(noAnswer);
      const send = hook().current.send;

      const outcome = await sent(send, 'trip-1', ['caver-1', 'caver-2'], AT_STATION);

      expect(outcome).toEqual({ recorded: false, held: true, code: null });
      // Sent again, this is what goes: the same key, so the server can tell a repeat from a second
      // report, and this browser's clock at composition rather than "now" whenever that turns out
      // to be.
      expect(heldReportsOf(ANA, 'trip-1')).toEqual([
        {
          clientKey: posted().clientKey,
          accountId: ANA,
          tripLogId: 'trip-1',
          body: {
            caverIds: ['caver-1', 'caver-2'],
            kind: 'atStation',
            stationName: 'p.g.42',
            depthM: null,
            teamId: null,
            note: 'at the pitch head',
            recordedAt: COMPOSED.toISOString(),
          },
          composedAt: COMPOSED.toISOString(),
          state: 'held',
          problemCode: null,
          attempts: 1,
          sending: false,
        },
      ]);
      expect(screen.getByText(/kept in this browser/)).toBeInTheDocument();
    });

    /**
     * The browser may have called itself online all along, and then it announces no connection
     * afterwards: the report that has just become held is tried again without that.
     */
    it('has the held report tried again a few seconds later, whatever the browser says of its connection', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      vi.setSystemTime(COMPOSED);
      recordEvents.mockImplementation(noAnswer);
      const woken = vi.fn();
      wakeHeldReportsWith(woken);
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      vi.advanceTimersByTime(4_999);
      expect(woken).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);
      expect(woken).toHaveBeenCalledTimes(1);
    });

    it('sets nothing to try again when the report landed', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      vi.setSystemTime(COMPOSED);
      const woken = vi.fn();
      wakeHeldReportsWith(woken);
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      vi.advanceTimersByTime(10 * 60_000);
      expect(woken).not.toHaveBeenCalled();
    });

    it('keeps a moment its author named, rather than the moment of composition', async () => {
      recordEvents.mockImplementation(noAnswer);
      const send = hook().current.send;
      const { default: dayjs } = await import('dayjs');

      await sent(send, 'trip-1', ['caver-1'], {
        kind: 'exited',
        recordedAt: dayjs('2026-05-01T08:30:00.000Z'),
      });

      expect(heldReportsOf(ANA)[0].body.recordedAt).toBe('2026-05-01T08:30:00.000Z');
      expect(heldReportsOf(ANA)[0].composedAt).toBe(COMPOSED.toISOString());
    });

    it('holds it for the account that composed it and for nobody else', async () => {
      recordEvents.mockImplementation(noAnswer);
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(heldReportsOf(ANA)).toHaveLength(1);
      expect(heldReportsOf('account-bogdan')).toEqual([]);
    });

    it('gives the same words typed again, after one was held, a key of their own', async () => {
      recordEvents.mockImplementation(noAnswer);
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);
      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      // Two reports that say the same thing are two reports: the first left the form.
      expect(posted(1).clientKey).not.toBe(posted(0).clientKey);
      expect(heldReportsOf(ANA)).toHaveLength(2);
    });

    /**
     * Where the browser keeps nothing the tab behaves as it did before there was a queue: nothing
     * is promised, and the author is told the report did not go.
     */
    it('says the report was not sent, and holds nothing, when the browser will not store it', async () => {
      recordEvents.mockImplementation(noAnswer);
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });
      const send = hook().current.send;

      const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(outcome).toEqual({ recorded: false, held: false, code: null });
      expect(heldReportsOf(ANA)).toEqual([]);
      expect(screen.getByText(/^Not sent:/)).toBeInTheDocument();
      expect(screen.queryByText(/kept in this browser/)).toBeNull();
    });

    it('still sends, and records, where the browser will not store anything', async () => {
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });
      const send = hook().current.send;

      expect(await sent(send, 'trip-1', ['caver-1'], AT_STATION)).toEqual({
        recorded: true,
        held: false,
        code: null,
      });
    });

    it('holds nothing where the tab is signed in as nobody it can name', async () => {
      account = null;
      recordEvents.mockImplementation(noAnswer);
      const send = hook().current.send;

      const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(outcome).toEqual({ recorded: false, held: false, code: null });
      expect(Object.keys(window.localStorage)).toEqual([]);
      expect(screen.getByText(/^Not sent:/)).toBeInTheDocument();
    });
  });

  describe('when the server answers no', () => {
    /**
     * The other half of "only no answer is held", asserted against the same hook and the same
     * storage the held case passes through: an answer is not queued, whatever its status.
     */
    it.each([
      [400, 'tracking.recorded_in_future'],
      [403, 'forbidden'],
      [409, 'tracking.not_writable'],
      [429, 'rate_limited'],
      [503, undefined],
    ])('does not hold a report refused with %i', async (status, code) => {
      recordEvents.mockImplementation(refusal(status, code));
      const send = hook().current.send;

      const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(outcome).toEqual({ recorded: false, held: false, code: code ?? null });
      expect(heldReportsOf(ANA)).toEqual([]);
      expect(Object.keys(window.localStorage)).toEqual([]);
      expect(screen.queryByText(/kept in this browser/)).toBeNull();
    });

    /**
     * Refused as coming from nobody is not an answer about the report: it is what a tab meets
     * after an outage longer than its token lasts.
     */
    describe('because the sign-in ran out', () => {
      it('renews the sign-in and sends the same report once more, under the same key', async () => {
        recordEvents.mockImplementationOnce(refusal(401));
        renewSignIn.mockResolvedValue('renewed');
        const send = hook().current.send;

        const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

        expect(outcome).toEqual({ recorded: true, held: false, code: null });
        expect(recordEvents).toHaveBeenCalledTimes(2);
        expect(posted(1)).toEqual(posted(0));
        expect(renewSignIn).toHaveBeenCalledTimes(1);
        expect(heldReportsOf(ANA)).toEqual([]);
        expect(Object.keys(window.localStorage)).toEqual([]);
        expect(screen.getByText('Recorded for 1.')).toBeInTheDocument();
      });

      /**
       * Signing in again means leaving the page, and the form goes with the page. So the kept copy
       * is what must survive, and the report is handed to the queue like one nobody answered.
       */
      it('keeps the report, and says it waits for a new sign-in, when the sign-in cannot be renewed', async () => {
        recordEvents.mockImplementation(refusal(401));
        const send = hook().current.send;

        const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

        expect(outcome).toEqual({ recorded: false, held: true, code: null });
        expect(recordEvents).toHaveBeenCalledTimes(1);
        expect(heldReportsOf(ANA, 'trip-1')).toEqual([
          expect.objectContaining({
            clientKey: posted().clientKey,
            state: 'held',
            body: expect.objectContaining({
              note: 'at the pitch head',
              recordedAt: COMPOSED.toISOString(),
            }),
          }),
        ]);
        expect(Object.keys(window.localStorage)).toEqual([
          `silexgis.trackingOutbox.${posted().clientKey}`,
        ]);
        expect(
          screen.getByText(/Your sign-in has lapsed — not sent yet\. The report is kept in this browser/),
        ).toBeInTheDocument();
        expect(isHeldReportsSignInLapsed()).toBe(true);
      });

      it('keeps the report when a renewed sign-in is refused all the same', async () => {
        recordEvents.mockImplementation(refusal(401));
        renewSignIn.mockResolvedValue('renewed');
        const send = hook().current.send;

        const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

        expect(outcome).toEqual({ recorded: false, held: true, code: null });
        // Once more and no further: this is not a loop.
        expect(recordEvents).toHaveBeenCalledTimes(2);
        expect(heldReportsOf(ANA)).toHaveLength(1);
      });

      it('shows the refusal a renewed sign-in was then given, and holds nothing', async () => {
        recordEvents
          .mockImplementationOnce(refusal(401))
          .mockImplementationOnce(refusal(409, 'tracking.not_writable'));
        renewSignIn.mockResolvedValue('renewed');
        const send = hook().current.send;

        const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

        expect(outcome).toEqual({ recorded: false, held: false, code: 'tracking.not_writable' });
        expect(heldReportsOf(ANA)).toEqual([]);
        expect(Object.keys(window.localStorage)).toEqual([]);
      });

      it('leaves the form its report, and holds nothing, where the browser will not store it', async () => {
        recordEvents.mockImplementation(refusal(401));
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
          throw new DOMException('blocked', 'SecurityError');
        });
        const send = hook().current.send;

        const outcome = await sent(send, 'trip-1', ['caver-1'], AT_STATION);

        expect(outcome).toEqual({ recorded: false, held: false, code: null });
        expect(heldReportsOf(ANA)).toEqual([]);
        expect(screen.queryByText(/kept in this browser/)).toBeNull();
      });
    });

    it('words the refusal as it always did', async () => {
      recordEvents.mockImplementation(refusal(409, 'tracking.not_writable'));
      const send = hook().current.send;
      const { default: i18n } = await import('../../i18n');

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(
        screen.getByText(i18n.t('trips.tracking.problems.notWritable')),
      ).toBeInTheDocument();
    });

    /**
     * An answer from something in front of the server does not say whether the server wrote the
     * report. Pressing the button again on the same report must therefore be the same act.
     */
    it('sends the very same report again under the key it first went with', async () => {
      recordEvents.mockImplementationOnce(refusal(504));
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);
      await sent(send, 'trip-1', ['caver-1'], AT_STATION);

      expect(posted(1).clientKey).toBe(posted(0).clientKey);
    });

    it('gives a report that was changed before it was sent again a key of its own', async () => {
      recordEvents.mockImplementationOnce(refusal(400, 'tracking.station_unknown'));
      const send = hook().current.send;

      await sent(send, 'trip-1', ['caver-1'], AT_STATION);
      await sent(send, 'trip-1', ['caver-1'], { ...AT_STATION, stationName: 'p.g.43' });

      expect(posted(1).clientKey).not.toBe(posted(0).clientKey);
    });
  });

  it('sends the same report again under the same key when neither an answer nor storage was to be had', async () => {
    recordEvents.mockImplementationOnce(noAnswer);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    const send = hook().current.send;

    await sent(send, 'trip-1', ['caver-1'], AT_STATION);
    await sent(send, 'trip-1', ['caver-1'], AT_STATION);

    expect(posted(1).clientKey).toBe(posted(0).clientKey);
  });

  it('gives the next report a key of its own once one has landed', async () => {
    const send = hook().current.send;

    await sent(send, 'trip-1', ['caver-1'], AT_STATION);
    await sent(send, 'trip-1', ['caver-1'], AT_STATION);

    expect(posted(1).clientKey).not.toBe(posted(0).clientKey);
  });
});
