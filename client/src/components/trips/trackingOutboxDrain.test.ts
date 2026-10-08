// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TrackingReportRequest } from '../../api/hooks.ts';
import type { HeldReport } from './trackingOutbox.ts';

/**
 * Everything one tab has: its own copy of the queue's module, of the sender and of the transport's
 * error type. Asked for afresh it is a tab just opened — which is how two tabs of one browser are
 * stood up here, sharing nothing but storage, exactly as they do.
 */
async function openTab() {
  vi.resetModules();
  const outbox = await import('./trackingOutbox.ts');
  const drain = await import('./trackingOutboxDrain.ts');
  const { ApiError } = await import('../../api/client.ts');
  return { ...outbox, ...drain, ApiError };
}

const ANA = 'account-ana';
const BOGDAN = 'account-bogdan';

let made = 0;
function report(over: Partial<HeldReport> = {}): HeldReport {
  made += 1;
  return {
    clientKey: `key-${made}`,
    accountId: ANA,
    tripLogId: 'trip-1',
    body: {
      caverIds: ['caver-1'],
      kind: 'atStation',
      stationName: 'p.g.42',
      depthM: null,
      teamId: null,
      note: 'waiting at the pitch head',
      recordedAt: `2026-05-01T10:00:0${made}.000Z`,
    },
    composedAt: `2026-05-01T10:00:0${made}.000Z`,
    state: 'held',
    problemCode: null,
    attempts: 1,
    ...over,
  };
}

const storedKeys = () =>
  Object.keys(window.localStorage)
    .filter((key) => key.startsWith('silexgis.trackingOutbox.'))
    .map((key) => key.slice('silexgis.trackingOutbox.'.length))
    .sort();

/**
 * A log that keeps the server's rule about a repeated key: the first send under a key writes, and
 * every later one is answered with what that wrote. The rule itself is the server's and is proved
 * against the real server; what is under test here is what this browser does given that rule.
 */
function log() {
  const written = new Map<string, { id: string }[]>();
  const asked: TrackingReportRequest[] = [];
  const send = async (request: TrackingReportRequest) => {
    asked.push(request);
    // The request is out for a moment, as a real one is: whatever else is running gets a turn.
    await Promise.resolve();
    const key = request.clientKey!;
    if (!written.has(key)) {
      written.set(key, request.caverIds.map((caverId) => ({ id: `${key}/${caverId}` })));
    }
    return written.get(key)!;
  };
  return { send, asked, written };
}

beforeEach(() => {
  made = 0;
  window.localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('sending the held reports', () => {
  it('sends them oldest first, one at a time, exactly as kept, and keeps none that landed', async () => {
    const tab = await openTab();
    // Kept out of order, to show the order sent is the order composed and not the order stored.
    const second = report({ composedAt: '2026-05-01T10:05:00.000Z' });
    const first = report({ composedAt: '2026-05-01T10:01:00.000Z', tripLogId: 'trip-2' });
    tab.holdReport(second);
    tab.holdReport(first);
    let out = 0;
    let mostOutAtOnce = 0;
    const asked: TrackingReportRequest[] = [];
    const send = async (request: TrackingReportRequest) => {
      asked.push(request);
      out += 1;
      mostOutAtOnce = Math.max(mostOutAtOnce, out);
      await new Promise((resolve) => setTimeout(resolve, 5));
      out -= 1;
      return [{}];
    };

    const result = await tab.drainHeldReports(ANA, send);

    expect(asked).toEqual([
      { tripLogId: 'trip-2', ...first.body, clientKey: first.clientKey },
      { tripLogId: 'trip-1', ...second.body, clientKey: second.clientKey },
    ]);
    // The moment it was about travels with it: never null, which would mean "now" on arrival.
    expect(asked[0].recordedAt).toBe(first.body.recordedAt);
    expect(mostOutAtOnce).toBe(1);
    expect(result).toEqual({ sent: 2, alreadyRemoved: 0, refused: 0, stopped: null, joined: false });
    expect(storedKeys()).toEqual([]);
    expect(tab.heldReportsOf(ANA)).toEqual([]);
  });

  /**
   * The other account's report is in the same storage and would be sent by exactly the same call
   * if it were asked for under its own account — which the last lines show, so that its staying
   * put above is the account rule at work and not a report that could never be sent.
   */
  it('sends only what the account it is asked for composed', async () => {
    const tab = await openTab();
    const anas = report();
    const bogdans = report({ accountId: BOGDAN });
    tab.holdReport(anas);
    tab.holdReport(bogdans);
    const server = log();

    await tab.drainHeldReports(ANA, server.send);

    expect(server.asked.map((request) => request.clientKey)).toEqual([anas.clientKey]);
    expect(storedKeys()).toEqual([bogdans.clientKey]);
    expect(tab.heldReportsOf(BOGDAN)).toHaveLength(1);

    await tab.drainHeldReports(BOGDAN, server.send);

    expect(server.asked.map((request) => request.clientKey)).toEqual([
      anas.clientKey,
      bogdans.clientKey,
    ]);
    expect(storedKeys()).toEqual([]);
  });

  it('counts apart a report the server already had and that was taken off the log since', async () => {
    const tab = await openTab();
    tab.holdReport(report());

    // Answered yes, listing nothing: the act is on record and none of its reports is left.
    const result = await tab.drainHeldReports(ANA, () => Promise.resolve([]));

    expect(result).toMatchObject({ sent: 0, alreadyRemoved: 1, refused: 0, stopped: null });
    expect(storedKeys()).toEqual([]);
  });

  it('keeps a refused report, marked with the server’s code, and goes on to the next', async () => {
    const tab = await openTab();
    const refusedOne = report();
    const fine = report();
    tab.holdReport(refusedOne);
    tab.holdReport(fine);
    const send = vi.fn((request: TrackingReportRequest) =>
      request.clientKey === refusedOne.clientKey
        ? Promise.reject(new tab.ApiError(400, 'tracking.recorded_in_future'))
        : Promise.resolve([{}]),
    );

    const result = await tab.drainHeldReports(ANA, send);

    expect(result).toMatchObject({ sent: 1, refused: 1, stopped: null });
    expect(tab.heldReportsOf(ANA)).toEqual([
      expect.objectContaining({
        clientKey: refusedOne.clientKey,
        state: 'refused',
        problemCode: 'tracking.recorded_in_future',
        attempts: 2,
        // Still exactly what was typed: a refusal changes nothing of the report itself.
        body: refusedOne.body,
      }),
    ]);

    // Never again by itself: the same request cannot be answered differently.
    send.mockClear();
    expect(await tab.drainHeldReports(ANA, send)).toMatchObject({ sent: 0, refused: 0 });
    expect(send).not.toHaveBeenCalled();

    // Sent again only once its author has asked for that.
    tab.heldReportQueuedAgain(ANA, refusedOne.clientKey);
    send.mockImplementation(() => Promise.resolve([{}]));
    expect(await tab.drainHeldReports(ANA, send)).toMatchObject({ sent: 1 });
    expect(storedKeys()).toEqual([]);
  });

  it.each([
    [403, 'forbidden'],
    [404, 'trip_log.not_found'],
    [409, 'tracking.not_writable'],
  ])('treats a %i as the server’s answer about the report', async (status, code) => {
    const tab = await openTab();
    tab.holdReport(report());

    const result = await tab.drainHeldReports(ANA, () =>
      Promise.reject(new tab.ApiError(status, code)),
    );

    expect(result).toMatchObject({ refused: 1, stopped: null });
    expect(tab.heldReportsOf(ANA)[0]).toMatchObject({ state: 'refused', problemCode: code });
  });

  it('stops at a report that got no answer, and leaves it and everything after it held', async () => {
    const tab = await openTab();
    const first = report();
    const second = report();
    tab.holdReport(first);
    tab.holdReport(second);
    const send = vi.fn(() => Promise.reject(new TypeError('Failed to fetch')));

    const result = await tab.drainHeldReports(ANA, send);

    expect(result).toMatchObject({ sent: 0, refused: 0, stopped: 'noAnswer' });
    // The second was not tried: whatever stopped the first would stop it too.
    expect(send).toHaveBeenCalledTimes(1);
    expect(tab.heldReportsOf(ANA)).toEqual([
      expect.objectContaining({ clientKey: first.clientKey, state: 'held', attempts: 2 }),
      expect.objectContaining({ clientKey: second.clientKey, state: 'held', attempts: 1 }),
    ]);
  });

  it('leaves a report held when the sign-in has lapsed, rather than calling it refused', async () => {
    const tab = await openTab();
    tab.holdReport(report());
    tab.holdReport(report());
    const send = vi.fn(() => Promise.reject(new tab.ApiError(401)));

    const result = await tab.drainHeldReports(ANA, send);

    expect(result).toMatchObject({ refused: 0, stopped: 'signedOut' });
    expect(send).toHaveBeenCalledTimes(1);
    expect(tab.heldReportsOf(ANA).map((entry) => entry.state)).toEqual(['held', 'held']);
  });

  it.each([429, 503])(
    'leaves a report held on a %i and sends nothing more until the wait it named is over',
    async (status) => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-01T11:00:00.000Z'));
      const tab = await openTab();
      tab.holdReport(report());
      const send = vi.fn<(request: TrackingReportRequest) => Promise<unknown[]>>(() =>
        Promise.reject(new tab.ApiError(status, undefined, undefined, undefined, 30_000)),
      );
      const woken = vi.fn();
      tab.wakeHeldReportsWith(woken);

      expect(await tab.drainHeldReports(ANA, send)).toMatchObject({ stopped: 'serverBusy' });
      expect(tab.heldReportsOf(ANA)[0]).toMatchObject({ state: 'held', attempts: 2 });

      // Asked again inside the wait — by a press, by the page coming back into view: not sent.
      send.mockClear();
      vi.advanceTimersByTime(29_000);
      expect(await tab.drainHeldReports(ANA, send)).toMatchObject({ stopped: 'waiting', sent: 0 });
      expect(send).not.toHaveBeenCalled();
      expect(woken).not.toHaveBeenCalled();

      // When it is over the session's own sender is woken, once, and the report goes.
      vi.advanceTimersByTime(1_000);
      expect(woken).toHaveBeenCalledTimes(1);
      send.mockImplementation(() => Promise.resolve([{}]));
      expect(await tab.drainHeldReports(ANA, send)).toMatchObject({ sent: 1, stopped: null });
    },
  );

  it.each([
    ['the server fails without naming a wait', 'serverBusy'],
    ['no answer comes', 'noAnswer'],
  ] as const)(
    'has the sender woken a little later when %s, and less often each time it fails again',
    async (_, stopped) => {
      vi.useFakeTimers();
      const tab = await openTab();
      tab.holdReport(report());
      const woken = vi.fn();
      tab.wakeHeldReportsWith(woken);
      const fails = () =>
        Promise.reject(stopped === 'serverBusy' ? new tab.ApiError(502) : new TypeError('Failed to fetch'));

      expect(await tab.drainHeldReports(ANA, fails)).toMatchObject({ refused: 0, stopped });
      expect(tab.heldReportsOf(ANA)[0]).toMatchObject({ state: 'held' });
      // No wait was named, so nothing is refused to a press meanwhile.
      expect(tab.heldReportsWaitMs()).toBe(0);

      // The browser will announce no connection it never saw go: the sender is woken anyway.
      vi.advanceTimersByTime(4_999);
      expect(woken).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);
      expect(woken).toHaveBeenCalledTimes(1);

      // Each run that fails again waits longer than the one before it, up to a minute, and one
      // timer is all there ever is.
      for (const [after, times] of [
        [10_000, 2],
        [20_000, 3],
        [40_000, 4],
        [60_000, 5],
        [60_000, 6],
      ] as const) {
        await tab.drainHeldReports(ANA, fails);
        vi.advanceTimersByTime(after - 1);
        expect(woken).toHaveBeenCalledTimes(times - 1);
        vi.advanceTimersByTime(1);
        expect(woken).toHaveBeenCalledTimes(times);
      }

      // Once a run gets through nothing is left waiting to wake anybody, and the next failure
      // starts from the short wait again.
      await tab.drainHeldReports(ANA, () => Promise.resolve([{}]));
      vi.advanceTimersByTime(10 * 60_000);
      expect(woken).toHaveBeenCalledTimes(6);
      tab.holdReport(report());
      await tab.drainHeldReports(ANA, fails);
      vi.advanceTimersByTime(5_000);
      expect(woken).toHaveBeenCalledTimes(7);
    },
  );

  it('wakes the sender once for a report that has just become held, however often it is asked', async () => {
    vi.useFakeTimers();
    const tab = await openTab();
    const woken = vi.fn();
    tab.wakeHeldReportsWith(woken);

    tab.retryHeldReportsLater();
    tab.retryHeldReportsLater();

    vi.advanceTimersByTime(5_000);
    expect(woken).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(10 * 60_000);
    expect(woken).toHaveBeenCalledTimes(1);
  });

  it('wakes the sender when the server answers something else, but not straight after a run that failed', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-01T11:00:00.000Z'));
    const tab = await openTab();
    tab.holdReport(report());
    const woken = vi.fn();
    tab.wakeHeldReportsWith(woken);

    tab.heldReportsServerAnswered();
    expect(woken).toHaveBeenCalledTimes(1);

    await tab.drainHeldReports(ANA, () => Promise.reject(new TypeError('Failed to fetch')));
    // A page that opens is answered a dozen times in a second: none of those is a fresh attempt.
    tab.heldReportsServerAnswered();
    vi.advanceTimersByTime(4_999);
    tab.heldReportsServerAnswered();
    expect(woken).toHaveBeenCalledTimes(1);
    // The timer the failed run set fires at five seconds; an answer after that counts again.
    vi.advanceTimersByTime(1);
    expect(woken).toHaveBeenCalledTimes(2);
    tab.heldReportsServerAnswered();
    expect(woken).toHaveBeenCalledTimes(3);
  });

  it('does not wake the sender on another answer during a wait the server named', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-01T11:00:00.000Z'));
    const tab = await openTab();
    tab.holdReport(report());
    const woken = vi.fn();
    tab.wakeHeldReportsWith(woken);
    await tab.drainHeldReports(ANA, () =>
      Promise.reject(new tab.ApiError(429, undefined, undefined, undefined, 30_000)),
    );

    vi.advanceTimersByTime(29_000);
    tab.heldReportsServerAnswered();
    // Neither by the answer nor by a timer of the sender's own: the wait is the only clock.
    expect(woken).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1_000);
    expect(woken).toHaveBeenCalledTimes(1);
  });

  describe('when the sign-in ran out while the report was held', () => {
    it('renews it and sends the same report again under the same key', async () => {
      const tab = await openTab();
      const first = report();
      const second = report();
      tab.holdReport(first);
      tab.holdReport(second);
      const asked: string[] = [];
      let signedIn = false;
      const send = async (request: TrackingReportRequest) => {
        asked.push(request.clientKey!);
        if (!signedIn) throw new tab.ApiError(401);
        return [{}];
      };
      const renew = vi.fn(async () => {
        signedIn = true;
        return 'renewed' as const;
      });

      const result = await tab.drainHeldReports(ANA, send, renew);

      expect(result).toMatchObject({ sent: 2, refused: 0, stopped: null });
      expect(asked).toEqual([first.clientKey, first.clientKey, second.clientKey]);
      expect(renew).toHaveBeenCalledTimes(1);
      expect(tab.heldReportsOf(ANA)).toEqual([]);
      expect(tab.isHeldReportsSignInLapsed()).toBe(false);
    });

    it('leaves it held and says the sign-in is over when it cannot be renewed', async () => {
      vi.useFakeTimers();
      const tab = await openTab();
      tab.holdReport(report());
      const woken = vi.fn();
      tab.wakeHeldReportsWith(woken);
      const send = vi.fn(() => Promise.reject(new tab.ApiError(401)));

      const result = await tab.drainHeldReports(ANA, send, async () => 'lapsed');

      expect(result).toMatchObject({ sent: 0, refused: 0, stopped: 'signedOut' });
      expect(send).toHaveBeenCalledTimes(1);
      expect(tab.heldReportsOf(ANA)[0]).toMatchObject({ state: 'held' });
      expect(tab.isHeldReportsSignInLapsed()).toBe(true);
      // Waiting changes nothing about a sign-in that is over, so nothing is set to try again.
      vi.advanceTimersByTime(10 * 60_000);
      expect(woken).not.toHaveBeenCalled();
    });

    it('renews once in a run: a second refusal straight after a renewal ends it', async () => {
      const tab = await openTab();
      tab.holdReport(report());
      const send = vi.fn(() => Promise.reject(new tab.ApiError(401)));
      const renew = vi.fn(async () => 'renewed' as const);

      const result = await tab.drainHeldReports(ANA, send, renew);

      expect(result).toMatchObject({ stopped: 'signedOut' });
      expect(send).toHaveBeenCalledTimes(2);
      expect(renew).toHaveBeenCalledTimes(1);
      expect(tab.heldReportsOf(ANA)[0]).toMatchObject({ state: 'held' });
    });

    it('treats a renewal nobody answered as no answer, to be tried again, and not as a sign-in that is over', async () => {
      vi.useFakeTimers();
      const tab = await openTab();
      tab.holdReport(report());
      const woken = vi.fn();
      tab.wakeHeldReportsWith(woken);

      const result = await tab.drainHeldReports(
        ANA,
        () => Promise.reject(new tab.ApiError(401)),
        async () => 'noAnswer',
      );

      expect(result).toMatchObject({ stopped: 'noAnswer' });
      expect(tab.isHeldReportsSignInLapsed()).toBe(false);
      vi.advanceTimersByTime(5_000);
      expect(woken).toHaveBeenCalledTimes(1);
    });

    it('stops saying the sign-in is over once a report is answered', async () => {
      const tab = await openTab();
      tab.holdReport(report());
      await tab.drainHeldReports(ANA, () => Promise.reject(new tab.ApiError(401)));
      expect(tab.isHeldReportsSignInLapsed()).toBe(true);

      await tab.drainHeldReports(ANA, () => Promise.resolve([{}]));

      expect(tab.isHeldReportsSignInLapsed()).toBe(false);
    });
  });

  it('makes no request at all while nothing is held', async () => {
    const tab = await openTab();
    const send = vi.fn();

    expect(await tab.drainHeldReports(ANA, send)).toEqual({
      sent: 0,
      alreadyRemoved: 0,
      refused: 0,
      stopped: null,
      joined: false,
    });
    expect(send).not.toHaveBeenCalled();
  });

  it('leaves alone a report whose first send is still under way in this tab', async () => {
    const tab = await openTab();
    const composing = report();
    tab.claimHeldReport(composing.clientKey, 'first');
    tab.holdReport(composing);
    const send = vi.fn();

    await tab.drainHeldReports(ANA, send);

    expect(send).not.toHaveBeenCalled();
    expect(storedKeys()).toEqual([composing.clientKey]);
  });

  it('joins a run already under way in this tab instead of starting a second', async () => {
    const tab = await openTab();
    tab.holdReport(report());
    tab.holdReport(report());
    const server = log();

    const [started, joined] = await Promise.all([
      tab.drainHeldReports(ANA, server.send),
      tab.drainHeldReports(ANA, server.send),
    ]);

    expect(server.asked).toHaveLength(2);
    expect(started).toMatchObject({ sent: 2, joined: false });
    expect(joined).toMatchObject({ sent: 2, joined: true });
  });
});

describe('two tabs of one browser sending at once', () => {
  it('leave every report on the log once and the queue empty, neither calling anything refused', async () => {
    const one = await openTab();
    const other = await openTab();
    const reports = [report(), report(), report()];
    for (const each of reports) one.holdReport(each);
    other.refreshHeldReports();
    const server = log();

    const results = await Promise.all([
      one.drainHeldReports(ANA, server.send),
      other.drainHeldReports(ANA, server.send),
    ]);

    // Both tabs really did send: otherwise this would show nothing about two senders.
    expect(server.asked.length).toBeGreaterThan(reports.length);
    // One write per report, however many times it was asked for: the key is what makes the
    // second request a repeat and not a second report.
    expect([...server.written.keys()].sort()).toEqual(reports.map((each) => each.clientKey));
    for (const result of results) {
      expect(result).toMatchObject({ refused: 0, alreadyRemoved: 0, stopped: null });
    }
    expect(storedKeys()).toEqual([]);
    one.refreshHeldReports();
    expect(one.heldReportsOf(ANA)).toEqual([]);
    expect(other.heldReportsOf(ANA)).toEqual([]);
  });

  /**
   * The slower tab loses its connection after the faster one has sent and removed the report. It
   * has an unanswered request in its hands for a report that is no longer in the queue — and must
   * not put it back, or a sent report would be sent again for ever.
   */
  it('do not put back a report one of them has sent when the other got no answer for it', async () => {
    const one = await openTab();
    const other = await openTab();
    const only = report();
    one.holdReport(only);
    other.refreshHeldReports();
    let letTheSlowOneFail: () => void = () => {};
    const slow = () =>
      new Promise<unknown[]>((_, reject) => {
        letTheSlowOneFail = () => reject(new TypeError('Failed to fetch'));
      });

    const slower = other.drainHeldReports(ANA, slow);
    await one.drainHeldReports(ANA, () => Promise.resolve([{}]));
    expect(storedKeys()).toEqual([]);
    letTheSlowOneFail();

    expect(await slower).toMatchObject({ sent: 0, stopped: 'noAnswer' });
    expect(storedKeys()).toEqual([]);
    expect(other.heldReportsOf(ANA)).toEqual([]);
  });
});
