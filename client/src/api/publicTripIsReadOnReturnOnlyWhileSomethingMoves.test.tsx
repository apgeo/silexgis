// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import {
  QueryClient,
  QueryClientProvider,
  focusManager,
  onlineManager,
} from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is when this browser asks
// again, not what comes back. Everything else about the client is the real one, so a refusal is
// told from a fault by the same rule the application uses.
type Answer = { data?: unknown; error?: unknown; response: Response };
const answers = new Map<string, () => Promise<Answer>>();
const asked: string[] = [];

vi.mock('./client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client.ts')>()),
  api: {
    GET: (path: string) => {
      asked.push(path);
      return answers.get(path)!();
    },
  },
}));

const {
  usePublicTrip,
  usePublicLiveTrips,
  usePublicPastTrips,
  publicTripPollInterval,
  publicLiveTripsPollInterval,
  PUBLIC_ARCHIVE_FRESH_MS,
  PUBLIC_IDLE_POLL_MS,
} = await import('./hooks.ts');
// The real rules, which the mock above leaves in place: the application's own client is built
// with both — whether a failed read is attempted again, and how long it waits first.
const { retryQuery, retryDelay } = await import('./client.ts');

const ENVELOPE = '/api/v1/public/trips/{token}';
const LIVE = '/api/v1/public/trips/{token}/live';
const PAST = '/api/v1/public/trips/{token}/past';

const ok = (data: unknown) => () =>
  Promise.resolve<Answer>({ data, response: new Response(null, { status: 200 }) });
const refused = (status: number, headers: Record<string, string> = {}) => () =>
  Promise.resolve<Answer>({
    error: { code: status === 429 ? 'rate_limited' : 'not_found' },
    response: new Response(null, { status, headers }),
  });
const unreachable = () => Promise.reject(new TypeError('Failed to fetch'));

/** A published trip, said only in the parts the decision reads. */
const envelope = (state: 'armed' | 'closed', pictures = 0) => ({
  state,
  model:
    pictures === 0
      ? null
      : {
          pictures: Array.from({ length: pictures }, (_, i) => ({
            stationName: `p.g.${i}`,
            thumbnailUrl: `/api/v1/files/f${i}/thumbnail?size=480&token=sig`,
            caption: null,
          })),
          rasterMaps: [],
        },
});

/**
 * A query client for one test. Without retries by default, so that a count of requests is a count
 * of decisions to ask; with the application's own retry rule where what is under test is what a
 * reader is left looking at once that rule has given up.
 */
function harness(retry: false | typeof retryQuery = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry, retryDelay } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper };
}

const count = (path: string) => asked.filter((one) => one === path).length;

async function settle() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1);
  });
}

/** Leaving the tab and coming back to it — twice, which is what a reader with a link open does. */
async function leaveAndReturnTwice() {
  for (let i = 0; i < 2; i++) {
    await act(async () => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
      await vi.advanceTimersByTimeAsync(1);
    });
  }
}

/** The phone losing its signal and finding it again. */
async function dropAndRegainSignal() {
  await act(async () => {
    onlineManager.setOnline(false);
    onlineManager.setOnline(true);
    await vi.advanceTimersByTimeAsync(1);
  });
}

beforeEach(() => {
  answers.clear();
  asked.length = 0;
  vi.useFakeTimers();
});

afterEach(() => {
  focusManager.setFocused(undefined);
  onlineManager.setOnline(true);
  vi.useRealTimers();
});

/**
 * A published link is opened in tabs nobody closes, by readers the server knows nothing about.
 *
 * <b>The interval is only half of what keeps that cheap.</b> TanStack also re-reads a query every
 * time its tab regains focus or the connection comes back, and a page that stopped polling a
 * finished trip would still ask about it on every return to the tab — one request per glance, per
 * reader, for an answer that cannot change. So a return re-reads exactly when the interval would
 * have kept reading: a party underground, a finished trip whose signed picture addresses go stale,
 * or a page that never got an answer at all. It does not re-read a finished trip with nothing
 * left to expire, nor a link the server has refused for good.
 */
describe('a published trip, when its reader comes back to the tab', () => {
  it('is not asked about again once it is finished and nothing in it expires', async () => {
    answers.set(ENVELOPE, ok(envelope('closed')));
    answers.set(PAST, ok({ trips: [], more: false }));
    const { wrapper } = harness();
    renderHook(
      () => {
        usePublicTrip('follow-token');
        usePublicPastTrips('follow-token', true);
      },
      { wrapper },
    );
    await settle();
    expect(count(ENVELOPE)).toBe(1);

    await leaveAndReturnTwice();
    await dropAndRegainSignal();

    expect(count(ENVELOPE)).toBe(1);
    expect(count(PAST)).toBe(1);
  });

  it('is asked about again at once while the party is underground', async () => {
    // The twin that makes the silence above a decision rather than a switch turned off: a family
    // coming back to the tab while the party is still in wants where they are now, not in a minute.
    answers.set(ENVELOPE, ok(envelope('armed')));
    const { wrapper } = harness();
    renderHook(() => usePublicTrip('follow-token'), { wrapper });
    await settle();

    await leaveAndReturnTwice();

    expect(count(ENVELOPE)).toBe(3);
  });

  it('is asked about again when it carries pictures whose addresses expire', async () => {
    // A backgrounded tab's interval does not fire, so an article read an hour after it was opened
    // would otherwise offer its reader a thumbnail address that ran out long ago — until the next
    // tick, minutes after they looked.
    answers.set(ENVELOPE, ok(envelope('closed', 1)));
    const { wrapper } = harness();
    renderHook(() => usePublicTrip('follow-token'), { wrapper });
    await settle();

    await leaveAndReturnTwice();

    expect(count(ENVELOPE)).toBe(3);
  });

  it('tries again on a return when the first read never got through', async () => {
    // Opened with no signal at the cave's car park: there is no envelope to decide by, and a
    // return to the tab — or the signal coming back — is the moment to try again.
    answers.set(ENVELOPE, unreachable);
    const { wrapper } = harness();
    renderHook(() => usePublicTrip('follow-token'), { wrapper });
    await settle();
    expect(count(ENVELOPE)).toBe(1);

    answers.set(ENVELOPE, ok(envelope('closed')));
    await dropAndRegainSignal();

    expect(count(ENVELOPE)).toBe(2);
  });

  it('is not asked about again once the link has been refused for good', async () => {
    answers.set(ENVELOPE, refused(404));
    const { wrapper } = harness();
    renderHook(() => usePublicTrip('follow-token'), { wrapper });
    await settle();

    await leaveAndReturnTwice();
    await dropAndRegainSignal();

    expect(count(ENVELOPE)).toBe(1);
  });
});

describe('the parties being followed, when their reader comes back to the tab', () => {
  it('are not asked about again on every glance once every one of them is out', async () => {
    answers.set(LIVE, ok({ trips: [{ tripLogId: 'trip-0', state: 'closed' }], more: false }));
    const { wrapper } = harness();
    renderHook(() => usePublicLiveTrips('follow-token', true), { wrapper });
    await settle();

    await leaveAndReturnTwice();
    await dropAndRegainSignal();

    expect(count(LIVE)).toBe(1);
  });

  it('are asked about again on a return once the slow pace is owed', async () => {
    // The other half of the rule above, and the reason it is not "never": a tab that was hidden
    // does not tick, so a reader back after an hour holds a list an hour old. The return is the
    // tick that was missed.
    answers.set(LIVE, ok({ trips: [{ tripLogId: 'trip-0', state: 'closed' }], more: false }));
    const { wrapper } = harness();
    renderHook(() => usePublicLiveTrips('follow-token', true), { wrapper });
    await settle();

    await act(async () => {
      focusManager.setFocused(false);
      await vi.advanceTimersByTimeAsync(PUBLIC_IDLE_POLL_MS * 3);
    });
    // Hidden, so nothing was asked however long it was.
    expect(count(LIVE)).toBe(1);

    await act(async () => {
      focusManager.setFocused(true);
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(count(LIVE)).toBe(2);
  });

  it('are asked about again while somebody is still underground', async () => {
    answers.set(LIVE, ok({ trips: [{ tripLogId: 'trip-0', state: 'armed' }], more: false }));
    const { wrapper } = harness();
    renderHook(() => usePublicLiveTrips('follow-token', true), { wrapper });
    await settle();

    await leaveAndReturnTwice();

    expect(count(LIVE)).toBe(3);
  });
});

/**
 * How long the application's client goes on asking before it calls a read failed: three further
 * attempts, a second, two and four apart. Longer than that with room to spare, and far short of
 * the next poll, so that advancing by it lands between the failure and whatever follows.
 */
const RETRIES_SPENT_MS = 10_000;

/**
 * A read as the pages take it: the three members they destructure, taken on every render.
 *
 * Not a convenience. The query client re-renders a component only for the members it has read, so
 * a test that looked at `data` before the outage and `error` only after it would be shown the
 * result of a render that never happened — and would report the failure as missing when it is
 * there. The pages read the result and its failure on every render, and so does this.
 */
function asThePageReads<T>(read: { data: T | undefined; error: unknown; dataUpdatedAt: number }) {
  const { data, error, dataUpdatedAt } = read;
  return { data, error, dataUpdatedAt };
}

/**
 * The server going away while the page is open — a restart, a proxy with nothing behind it, a
 * phone in a valley.
 *
 * <b>What the reader holds must outlive the read that failed.</b> The page's two failure screens
 * ("this link is not known", "the server cannot be reached") are both for a reader who has
 * nothing; somebody who was looking at the party a minute ago has the party, and what they need
 * is the party plus the news that it is no longer being refreshed. That rests on three things the
 * query client does and nothing else pins: a failed re-read leaves the data standing, the failure
 * is reported beside it, and the page goes on asking so that the report clears by itself. A page
 * tested only against a hook that is handed `data` and `error` together proves the page reads
 * them, not that they ever arrive together.
 */
describe('a published trip, when the server stops answering while it is open', () => {
  it('keeps the party it last read, reports the failed read, and clears it when a read lands', async () => {
    const first = { ...envelope('armed'), title: 'as first read' };
    const interval = publicTripPollInterval(first as never);
    // The twin without which every wait below would be a wait for nothing.
    expect(interval).toBeGreaterThan(RETRIES_SPENT_MS);
    answers.set(ENVELOPE, ok(first));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(() => asThePageReads(usePublicTrip('follow-token')), { wrapper });
    await settle();
    expect(result.current.data).toEqual(first);
    expect(result.current.error).toBeNull();
    const readAt = result.current.dataUpdatedAt;

    answers.set(ENVELOPE, unreachable);
    await act(async () => {
      await vi.advanceTimersByTimeAsync((interval as number) + RETRIES_SPENT_MS);
    });

    // The poll did go out, and was given up on only after the client's own further attempts.
    expect(count(ENVELOPE)).toBeGreaterThan(2);
    expect(result.current.error).toBeInstanceOf(TypeError);
    expect(result.current.data).toEqual(first);
    // The moment of the last read that landed is left standing: it is the age of what is shown.
    expect(result.current.dataUpdatedAt).toBe(readAt);

    const second = { ...envelope('armed'), title: 'as read again' };
    answers.set(ENVELOPE, ok(second));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(interval as number);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.data).toEqual(second);
    expect(result.current.dataUpdatedAt).toBeGreaterThan(readAt);
  });

  it('keeps the list of parties being followed the same way', async () => {
    const first = { trips: [{ tripLogId: 'trip-0', state: 'armed' }], more: false };
    const interval = publicLiveTripsPollInterval(first as never, null);
    expect(interval).toBeGreaterThan(RETRIES_SPENT_MS);
    answers.set(LIVE, ok(first));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(
      () => asThePageReads(usePublicLiveTrips('follow-token', true)),
      { wrapper },
    );
    await settle();
    expect(result.current.data).toEqual(first);
    expect(result.current.error).toBeNull();

    answers.set(LIVE, unreachable);
    await act(async () => {
      await vi.advanceTimersByTimeAsync((interval as number) + RETRIES_SPENT_MS);
    });

    expect(count(LIVE)).toBeGreaterThan(2);
    expect(result.current.error).toBeInstanceOf(TypeError);
    expect(result.current.data).toEqual(first);

    const second = {
      trips: [
        { tripLogId: 'trip-0', state: 'armed' },
        { tripLogId: 'trip-1', state: 'armed' },
      ],
      more: false,
    };
    answers.set(LIVE, ok(second));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(interval as number);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.data).toEqual(second);
  });
});

/**
 * A list in which nobody is underground used to be read once. That was wrong in two ways a reader
 * could see: a list left open said "nobody is being followed" for as long as the tab lived,
 * whoever went in meanwhile; and a party being watched out of that list never visibly left it.
 * So it is kept up slowly — for as long as it is being read at all, and only in a tab somebody
 * can see.
 */
describe('the parties being followed, while nobody in them is underground', () => {
  const nobodyIn = { trips: [{ tripLogId: 'trip-0', state: 'closed' }], more: false };
  const idle = async () => {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_IDLE_POLL_MS);
    });
  };

  it('are asked about again at the slow pace, and at the followed pace once somebody goes in', async () => {
    answers.set(LIVE, ok(nobodyIn));
    const { wrapper } = harness();
    const { result } = renderHook(() => asThePageReads(usePublicLiveTrips('follow-token', true)), {
      wrapper,
    });
    await settle();
    expect(count(LIVE)).toBe(1);

    // Not sooner: the followed pace passes several times over with nothing asked.
    const followedPace = publicLiveTripsPollInterval(
      { trips: [{ state: 'armed' }], more: false } as never,
      null,
    ) as number;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(followedPace * 2);
    });
    expect(count(LIVE)).toBe(1);

    const somebodyIn = {
      trips: [
        { tripLogId: 'trip-0', state: 'closed' },
        { tripLogId: 'trip-1', state: 'armed' },
      ],
      more: false,
    };
    answers.set(LIVE, ok(somebodyIn));
    // The rest of the slow pace, and no further: what follows is counted at the other pace.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_IDLE_POLL_MS - followedPace * 2);
    });
    expect(count(LIVE)).toBe(2);
    expect(result.current.data).toEqual(somebodyIn);

    // And from then on at the pace a party underground is followed at.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(followedPace + 1);
    });
    expect(count(LIVE)).toBe(3);
  });

  it('are what shows a finished party leaving the list', async () => {
    answers.set(LIVE, ok(nobodyIn));
    const { wrapper } = harness();
    const { result } = renderHook(() => asThePageReads(usePublicLiveTrips('follow-token', true)), {
      wrapper,
    });
    await settle();

    answers.set(LIVE, ok({ trips: [], more: false }));
    await idle();

    expect(result.current.data).toEqual({ trips: [], more: false });
  });

  it('are not asked about at all in a tab nobody is looking at, nor with their section shut', async () => {
    answers.set(LIVE, ok(nobodyIn));
    const { wrapper } = harness();
    const { rerender } = renderHook(
      ({ open }: { open: boolean }) => usePublicLiveTrips('follow-token', open),
      { wrapper, initialProps: { open: true } },
    );
    await settle();
    expect(count(LIVE)).toBe(1);

    await act(async () => {
      focusManager.setFocused(false);
      await vi.advanceTimersByTimeAsync(PUBLIC_IDLE_POLL_MS * 3);
    });
    expect(count(LIVE)).toBe(1);

    // Back in view with the section shut: the reader is not asking about the cave any more.
    rerender({ open: false });
    await act(async () => {
      focusManager.setFocused(true);
      await vi.advanceTimersByTimeAsync(PUBLIC_IDLE_POLL_MS * 3);
    });
    expect(count(LIVE)).toBe(1);

    // The twin: with the section open and the tab in view the same wait does ask.
    rerender({ open: true });
    await settle();
    const asOpened = count(LIVE);
    await idle();
    expect(count(LIVE)).toBe(asOpened + 1);
  });

  it('stop being asked about once the link has been refused for good', async () => {
    answers.set(LIVE, ok(nobodyIn));
    const { wrapper } = harness();
    renderHook(() => asThePageReads(usePublicLiveTrips('follow-token', true)), { wrapper });
    await settle();

    answers.set(LIVE, refused(404));
    await idle();
    expect(count(LIVE)).toBe(2);

    await idle();
    await idle();
    await leaveAndReturnTwice();
    expect(count(LIVE)).toBe(2);
  });
});

/**
 * The archive of a cave used to be read once per tab, for ever: a list reopened after lunch still
 * showed the cave as it stood at breakfast, without the trip that finished meanwhile. It is now
 * believed for a few minutes and re-read after that the next time somebody is looking — and still
 * never polled, because nothing in it moves while it is being looked at.
 */
describe('the past trips of a cave, as time passes over an open list', () => {
  const asRead = { trips: [{ tripLogId: 'trip-0' }], more: false };

  it('are not asked about again inside the time they are believed for, whatever the reader does', async () => {
    answers.set(PAST, ok(asRead));
    const { wrapper } = harness();
    const { rerender } = renderHook(
      ({ open }: { open: boolean }) => usePublicPastTrips('follow-token', open),
      { wrapper, initialProps: { open: true } },
    );
    await settle();
    expect(count(PAST)).toBe(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS - 10_000);
    });
    await leaveAndReturnTwice();
    await dropAndRegainSignal();
    // Shutting the list and opening it again is a glance too.
    rerender({ open: false });
    rerender({ open: true });
    await settle();

    expect(count(PAST)).toBe(1);
  });

  it('are never asked about by a clock, however long the list stands open', async () => {
    answers.set(PAST, ok(asRead));
    const { wrapper } = harness();
    renderHook(() => usePublicPastTrips('follow-token', true), { wrapper });
    await settle();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS * 6);
    });

    expect(count(PAST)).toBe(1);
  });

  it('are read again on a return to the tab once they are older than that', async () => {
    answers.set(PAST, ok(asRead));
    const { wrapper } = harness();
    const { result } = renderHook(() => asThePageReads(usePublicPastTrips('follow-token', true)), {
      wrapper,
    });
    await settle();

    const later = { trips: [{ tripLogId: 'trip-1' }, { tripLogId: 'trip-0' }], more: false };
    answers.set(PAST, ok(later));
    await act(async () => {
      focusManager.setFocused(false);
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS + 1);
      focusManager.setFocused(true);
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(count(PAST)).toBe(2);
    expect(result.current.data).toEqual(later);
  });

  it('are read again when the list is opened again after that long', async () => {
    answers.set(PAST, ok(asRead));
    const { wrapper } = harness();
    const { rerender } = renderHook(
      ({ open }: { open: boolean }) => usePublicPastTrips('follow-token', open),
      { wrapper, initialProps: { open: true } },
    );
    await settle();

    rerender({ open: false });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS + 1);
    });
    expect(count(PAST)).toBe(1);
    rerender({ open: true });
    await settle();

    expect(count(PAST)).toBe(2);
  });

  it('keep the list in hand when a later read of it does not land', async () => {
    answers.set(PAST, ok(asRead));
    const { wrapper } = harness();
    const { result } = renderHook(() => asThePageReads(usePublicPastTrips('follow-token', true)), {
      wrapper,
    });
    await settle();

    answers.set(PAST, unreachable);
    await act(async () => {
      focusManager.setFocused(false);
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS + 1);
      focusManager.setFocused(true);
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(count(PAST)).toBe(2);
    expect(result.current.error).toBeInstanceOf(TypeError);
    expect(result.current.data).toEqual(asRead);
  });

  it('are not asked for again on a return once the server has refused them for good', async () => {
    // An installation that does not open its archive to visitors answers this route as it answers
    // an unknown link, and a list never handed over counts as out of date for ever — so without a
    // rule of its own every glance at the tab would ask again.
    answers.set(PAST, refused(404));
    const { wrapper } = harness();
    renderHook(() => usePublicPastTrips('follow-token', true), { wrapper });
    await settle();
    expect(count(PAST)).toBe(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUBLIC_ARCHIVE_FRESH_MS + 1);
    });
    await leaveAndReturnTwice();
    await dropAndRegainSignal();

    expect(count(PAST)).toBe(1);
  });
});

/**
 * A read as a page that states its own freshness takes it: what {@link asThePageReads} takes, and
 * the two members that say a read is not landing without having failed.
 */
function asAFreshnessLineReads<T>(read: {
  data: T | undefined;
  error: unknown;
  failureReason: unknown;
  isPaused: boolean;
}) {
  const { data, error, failureReason, isPaused } = read;
  return { data, error, failureReason, isPaused };
}

/**
 * A server that refuses for being asked too often says when to come back.
 *
 * <b>Asking sooner is not merely wasted — it is counted.</b> Each early attempt comes out of the
 * allowance the refusal was about, so a page that retried a second, two and four later would keep
 * itself refused and take the other tabs behind the same address with it. The wait the server
 * names is the wait; and a refusal that names none is waited out exactly as it always was.
 */
describe('a published trip, when the server asks to be left alone for a while', () => {
  const NAMED_WAIT_S = 30;

  it('waits as long as the server said before asking again, and then reads', async () => {
    const first = { ...envelope('armed'), title: 'as first read' };
    const interval = publicTripPollInterval(first as never) as number;
    answers.set(ENVELOPE, ok(first));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(() => asAFreshnessLineReads(usePublicTrip('follow-token')), {
      wrapper,
    });
    await settle();
    expect(count(ENVELOPE)).toBe(1);

    answers.set(ENVELOPE, refused(429, { 'Retry-After': String(NAMED_WAIT_S) }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(interval);
    });
    // The poll went out and was refused.
    expect(count(ENVELOPE)).toBe(2);

    // Well past the second, two and four of the ordinary pacing, and still short of the wait
    // named: nothing more has been asked.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RETRIES_SPENT_MS);
    });
    expect(count(ENVELOPE)).toBe(2);
    // Meanwhile the refusal is in hand as the reason the read is not landing, beside the party
    // last read — which is what lets a page say it is not being refreshed while it waits.
    expect(result.current.error).toBeNull();
    expect(result.current.failureReason).toMatchObject({
      status: 429,
      retryAfterMs: NAMED_WAIT_S * 1000,
    });
    expect(result.current.data).toEqual(first);

    const second = { ...envelope('armed'), title: 'as read again' };
    answers.set(ENVELOPE, ok(second));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(NAMED_WAIT_S * 1000 - RETRIES_SPENT_MS + 1);
    });

    expect(count(ENVELOPE)).toBe(3);
    expect(result.current.data).toEqual(second);
    expect(result.current.error).toBeNull();
    expect(result.current.failureReason).toBeNull();
  });

  it('falls back to its own pacing when the refusal names no wait', async () => {
    const first = { ...envelope('armed'), title: 'as first read' };
    const interval = publicTripPollInterval(first as never) as number;
    answers.set(ENVELOPE, ok(first));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(() => asAFreshnessLineReads(usePublicTrip('follow-token')), {
      wrapper,
    });
    await settle();

    answers.set(ENVELOPE, refused(429));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(interval);
    });
    expect(count(ENVELOPE)).toBe(2);

    // A second later the first further attempt has gone out; two after that, the next.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(count(ENVELOPE)).toBe(3);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(count(ENVELOPE)).toBe(4);

    const second = { ...envelope('armed'), title: 'as read again' };
    answers.set(ENVELOPE, ok(second));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4000);
    });

    expect(count(ENVELOPE)).toBe(5);
    expect(result.current.data).toEqual(second);
  });
});

/**
 * A browser that knows it is offline does not send a read at all: nothing fails, and the read is
 * held until the connection is back. To a reader that is the same thing as a read that went out
 * and died — the figures are not being kept up — so the held read has to be visible to the page,
 * and has to go out by itself when the signal returns.
 */
describe('a published trip, when the browser knows it has no connection', () => {
  it('holds the read back without failing it, says so, and sends it when the connection returns', async () => {
    const first = { ...envelope('armed'), title: 'as first read' };
    const interval = publicTripPollInterval(first as never) as number;
    answers.set(ENVELOPE, ok(first));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(() => asAFreshnessLineReads(usePublicTrip('follow-token')), {
      wrapper,
    });
    await settle();
    expect(result.current.isPaused).toBe(false);

    await act(async () => {
      onlineManager.setOnline(false);
      await vi.advanceTimersByTimeAsync(interval + RETRIES_SPENT_MS);
    });

    // Nothing was sent and nothing failed — and the read is held, which is what a page reads.
    expect(count(ENVELOPE)).toBe(1);
    expect(result.current.error).toBeNull();
    expect(result.current.isPaused).toBe(true);
    expect(result.current.data).toEqual(first);

    const second = { ...envelope('armed'), title: 'as read again' };
    answers.set(ENVELOPE, ok(second));
    await act(async () => {
      onlineManager.setOnline(true);
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(result.current.isPaused).toBe(false);
    expect(result.current.data).toEqual(second);
  });

  it('holds a first read back the same way, with nothing in hand', async () => {
    onlineManager.setOnline(false);
    answers.set(ENVELOPE, ok(envelope('armed')));
    const { wrapper } = harness(retryQuery);
    const { result } = renderHook(() => asAFreshnessLineReads(usePublicTrip('follow-token')), {
      wrapper,
    });
    await settle();

    expect(count(ENVELOPE)).toBe(0);
    expect(result.current.data).toBeUndefined();
    expect(result.current.isPaused).toBe(true);

    await act(async () => {
      onlineManager.setOnline(true);
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(count(ENVELOPE)).toBe(1);
    expect(result.current.data).toEqual(envelope('armed'));
  });
});
