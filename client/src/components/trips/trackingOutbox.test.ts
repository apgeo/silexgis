// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook, act } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HeldReport } from './trackingOutbox.ts';

type Outbox = typeof import('./trackingOutbox.ts');

/**
 * The module as a tab that has just been opened finds it: what it remembers is whatever storage
 * holds at that moment and nothing else. Asking for it afresh is what a reload is.
 */
async function openTab(): Promise<Outbox> {
  vi.resetModules();
  return import('./trackingOutbox.ts');
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
      recordedAt: '2026-05-01T10:00:00.000Z',
    },
    composedAt: `2026-05-01T10:00:0${made}.000Z`,
    state: 'held',
    problemCode: null,
    attempts: 1,
    ...over,
  };
}

const storedKeys = () =>
  Object.keys(window.localStorage).filter((key) => key.startsWith('silexgis.trackingOutbox.'));

beforeEach(() => {
  made = 0;
  window.localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('the outbox of tracking reports', () => {
  /**
   * The whole reason it is in storage rather than in memory. Read back through a module loaded
   * afresh, because a list read back from the same module would be answered from what that module
   * remembers and would pass with nothing written at all.
   */
  it('gives a held report back, whole, to a tab opened after the one that held it', async () => {
    const before = await openTab();
    const kept = report();
    expect(before.holdReport(kept)).toBe(true);

    const after = await openTab();

    expect(after.heldReportsOf(ANA)).toEqual([{ ...kept, sending: false }]);
  });

  it('lists oldest composition first, and one trip at a time when asked', async () => {
    const outbox = await openTab();
    const late = report({ composedAt: '2026-05-01T12:00:00.000Z' });
    const early = report({ composedAt: '2026-05-01T09:00:00.000Z', tripLogId: 'trip-2' });
    outbox.holdReport(late);
    outbox.holdReport(early);

    expect(outbox.heldReportsOf(ANA).map((one) => one.clientKey)).toEqual([
      early.clientKey,
      late.clientKey,
    ]);
    expect(outbox.heldReportsOf(ANA, 'trip-1').map((one) => one.clientKey)).toEqual([
      late.clientKey,
    ]);
  });

  /**
   * Somebody's notes about people, on a phone that is handed round. Both halves are asserted on
   * the same queue: the report is there for the account that wrote it, and is not there for the
   * next account to sign in — nor for nobody.
   */
  it('shows a report to the account that composed it and to no other', async () => {
    const outbox = await openTab();
    const hers = report({ accountId: ANA });
    const his = report({ accountId: BOGDAN });
    outbox.holdReport(hers);
    outbox.holdReport(his);

    expect(outbox.heldReportsOf(ANA).map((one) => one.clientKey)).toEqual([hers.clientKey]);
    expect(outbox.heldReportsOf(BOGDAN).map((one) => one.clientKey)).toEqual([his.clientKey]);
    expect(outbox.heldReportsOf(null)).toEqual([]);
    expect(outbox.heldReportsOf('account-nobody-here')).toEqual([]);
  });

  it('lets no account discard or change what another composed', async () => {
    const outbox = await openTab();
    const hers = report({ accountId: ANA });
    outbox.holdReport(hers);

    outbox.dropHeldReport(BOGDAN, hers.clientKey);
    outbox.heldReportRefused(BOGDAN, hers.clientKey, 'tracking.not_writable');

    expect(outbox.heldReportsOf(ANA)).toEqual([{ ...hers, sending: false }]);

    // And the account it belongs to can: the refusal above was the account, not the call.
    outbox.dropHeldReport(ANA, hers.clientKey);
    expect(outbox.heldReportsOf(ANA)).toEqual([]);
    expect(storedKeys()).toEqual([]);
  });

  it('keeps the list a component holds current', async () => {
    const outbox = await openTab();
    const { result } = renderHook(() => outbox.useHeldReports(ANA, 'trip-1'));
    expect(result.current).toEqual([]);

    const kept = report();
    act(() => {
      outbox.holdReport(kept);
      outbox.holdReport(report({ accountId: BOGDAN }));
    });
    expect(result.current.map((one) => one.clientKey)).toEqual([kept.clientKey]);

    act(() => outbox.dropHeldReport(ANA, kept.clientKey));
    expect(result.current).toEqual([]);
  });

  describe('when the browser will not store anything', () => {
    /**
     * A private window and blocked site data throw on access. The answer must be "not kept", said
     * out loud, with nothing left behind in memory to be shown as held.
     */
    it('says a report was not kept, and lists nothing, when writing throws', async () => {
      const outbox = await openTab();
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });

      expect(outbox.holdReport(report())).toBe(false);
      expect(outbox.heldReportsOf(ANA)).toEqual([]);
    });

    it('opens with an empty queue, and goes on working, when reading throws', async () => {
      const first = await openTab();
      first.holdReport(report());
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });
      vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });

      const outbox = await openTab();

      expect(outbox.heldReportsOf(ANA)).toEqual([]);
      expect(() => outbox.refreshHeldReports()).not.toThrow();
      expect(() => outbox.dropHeldReport(ANA, 'key-1')).not.toThrow();
      expect(() => outbox.heldReportUnanswered(ANA, 'key-1')).not.toThrow();
    });
  });

  /**
   * Storage is not the application's alone. Something there that does not read as a report is not
   * sent, and is not destroyed either.
   */
  it('ignores what it did not write, and leaves it where it is', async () => {
    const good = report();
    const outbox = await openTab();
    outbox.holdReport(good);
    window.localStorage.setItem('silexgis.trackingOutbox.torn', '{"v":1,"clientKey":"torn"');
    window.localStorage.setItem(
      'silexgis.trackingOutbox.later',
      JSON.stringify({ ...report({ clientKey: 'later' }), v: 2 }),
    );
    // Stored under a name that is not its own key: not the report that name promises.
    window.localStorage.setItem(
      'silexgis.trackingOutbox.misfiled',
      JSON.stringify({ ...report({ clientKey: 'elsewhere' }), v: 1 }),
    );
    window.localStorage.setItem(
      'silexgis.trackingOutbox.noMoment',
      JSON.stringify({
        ...report({ clientKey: 'noMoment' }),
        v: 1,
        body: { ...good.body, recordedAt: null },
      }),
    );

    const after = await openTab();

    expect(after.heldReportsOf(ANA).map((one) => one.clientKey)).toEqual([good.clientKey]);
    expect(storedKeys()).toHaveLength(5);
  });

  /**
   * Two tabs of one browser. The second tab's copy is stale by construction — it was read before
   * the first tab held anything — and what it then does to its own report must leave the first
   * tab's report in storage.
   */
  it('cannot lose a report another tab has just held', async () => {
    const stale = await openTab();
    const mine = report();
    stale.holdReport(mine);

    // What another tab writes, which this tab's memory has not heard about.
    const theirs = report();
    window.localStorage.setItem(
      `silexgis.trackingOutbox.${theirs.clientKey}`,
      JSON.stringify({ v: 1, ...theirs }),
    );

    stale.heldReportUnanswered(ANA, mine.clientKey);
    stale.dropHeldReport(ANA, mine.clientKey);

    expect(storedKeys()).toEqual([`silexgis.trackingOutbox.${theirs.clientKey}`]);
    expect((await openTab()).heldReportsOf(ANA)).toEqual([{ ...theirs, sending: false }]);
  });

  it('hears of what another tab did when the browser says storage changed', async () => {
    const outbox = await openTab();
    const theirs = report();
    const storageKey = `silexgis.trackingOutbox.${theirs.clientKey}`;
    window.localStorage.setItem(storageKey, JSON.stringify({ v: 1, ...theirs }));
    expect(outbox.heldReportsOf(ANA)).toEqual([]);

    window.dispatchEvent(new StorageEvent('storage', { key: storageKey }));
    expect(outbox.heldReportsOf(ANA).map((one) => one.clientKey)).toEqual([theirs.clientKey]);

    // Sent by the other tab meanwhile: this tab must not write it back.
    window.localStorage.removeItem(storageKey);
    outbox.heldReportUnanswered(ANA, theirs.clientKey);
    expect(storedKeys()).toEqual([]);
    expect(outbox.heldReportsOf(ANA)).toEqual([]);
  });

  it('marks a refusal with its code, counts attempts, and waits again when asked to', async () => {
    const outbox = await openTab();
    const kept = report();
    outbox.holdReport(kept);

    outbox.heldReportUnanswered(ANA, kept.clientKey);
    expect(outbox.heldReportsOf(ANA)[0]).toMatchObject({ state: 'held', attempts: 2 });

    outbox.heldReportRefused(ANA, kept.clientKey, 'tracking.recorded_in_future');
    expect(outbox.heldReportsOf(ANA)[0]).toMatchObject({
      state: 'refused',
      problemCode: 'tracking.recorded_in_future',
      attempts: 3,
    });
    // A refusal is kept across a reload, like the report it is about.
    expect((await openTab()).heldReportsOf(ANA)[0]).toMatchObject({
      state: 'refused',
      problemCode: 'tracking.recorded_in_future',
    });
  });

  it('puts a refused report back among the waiting, under the key it always had', async () => {
    const outbox = await openTab();
    const kept = report({ state: 'refused', problemCode: 'tracking.recorded_in_future' });
    outbox.holdReport(kept);

    outbox.heldReportQueuedAgain(ANA, kept.clientKey);

    expect(outbox.heldReportsOf(ANA)).toEqual([
      { ...kept, state: 'held', problemCode: null, sending: false },
    ]);
  });

  /**
   * A report is "held" to its author once its first send has ended without an answer — not during
   * the second or two that send takes with a connection.
   */
  it('lists nothing while a report is first being sent, and says so while it is sent again', async () => {
    const outbox = await openTab();
    const kept = report();

    expect(outbox.claimHeldReport(kept.clientKey, 'first')).toBe(true);
    outbox.holdReport(kept);
    expect(outbox.heldReportsOf(ANA)).toEqual([]);
    // One sender at a time in a tab.
    expect(outbox.claimHeldReport(kept.clientKey, 'again')).toBe(false);

    outbox.releaseHeldReport(kept.clientKey);
    expect(outbox.heldReportsOf(ANA)).toEqual([{ ...kept, sending: false }]);

    expect(outbox.claimHeldReport(kept.clientKey, 'again')).toBe(true);
    expect(outbox.heldReportsOf(ANA)).toEqual([{ ...kept, sending: true }]);
    outbox.releaseHeldReport(kept.clientKey);

    // A send in progress is this tab's alone: a reload ends it and leaves the report held.
    outbox.claimHeldReport(kept.clientKey, 'first');
    expect((await openTab()).heldReportsOf(ANA)).toEqual([{ ...kept, sending: false }]);
  });
});
