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

const { usePublicTrip, usePublicLiveTrips, usePublicPastTrips } = await import('./hooks.ts');

const ENVELOPE = '/api/v1/public/trips/{token}';
const LIVE = '/api/v1/public/trips/{token}/live';
const PAST = '/api/v1/public/trips/{token}/past';

const ok = (data: unknown) => () =>
  Promise.resolve<Answer>({ data, response: new Response(null, { status: 200 }) });
const refused = (status: number) => () =>
  Promise.resolve<Answer>({
    error: { code: 'not_found' },
    response: new Response(null, { status }),
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

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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
  it('are not asked about again once every one of them is out', async () => {
    answers.set(LIVE, ok({ trips: [{ tripLogId: 'trip-0', state: 'closed' }], more: false }));
    const { wrapper } = harness();
    renderHook(() => usePublicLiveTrips('follow-token', true), { wrapper });
    await settle();

    await leaveAndReturnTwice();

    expect(count(LIVE)).toBe(1);
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
