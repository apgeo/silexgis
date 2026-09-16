// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is when this browser asks
// again, not what comes back.
const post = vi.fn(() =>
  Promise.resolve({
    data: [{ stationName: 'p.g.119', surveyName: null, depthM: 119.6, deltaM: 0.4 }],
    response: new Response(null, { status: 200 }),
  }),
);
const put = vi.fn(() =>
  Promise.resolve({ data: {}, response: new Response(null, { status: 200 }) }),
);

vi.mock('./client.ts', () => ({
  api: { POST: () => post(), PUT: () => put() },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => 'W/"1"',
}));

const { useTrackingDepthReadings, useSetTripTracking } = await import('./hooks.ts');

/**
 * What one depth means in one cave, and how often it is worth asking.
 *
 * <b>Asking is not cheap and the log asks a lot.</b> Resolving a single depth reads every station
 * row of the survey model, so a cave holding tens of thousands of stations is a full table read per
 * question — and the tracking tab draws one question per distinct depth on a page of recent reports
 * plus the party, which is a couple of dozen. Before the log measured anything it issued none of
 * these at all, so the whole fan-out is new cost and has to be spent once rather than repeatedly.
 *
 * The temptation is to keep it fresh on a clock, and a clock is the wrong instrument: what a depth
 * means changes when the trip's model, datum or filter changes and at no other time. So the answers
 * are held for a long time and thrown away outright when that write lands.
 */
function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper, client };
}

/** Lets the queries settle and then stands still for as long as the caller says. */
async function waitOut(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

/** Leaving the window and coming back to it, which is what a coordinator does all callout long. */
async function leaveAndReturn() {
  await act(async () => {
    focusManager.setFocused(false);
    focusManager.setFocused(true);
    await vi.advanceTimersByTimeAsync(1);
  });
}

beforeEach(() => {
  post.mockClear();
  put.mockClear();
  vi.useFakeTimers();
});

afterEach(() => {
  focusManager.setFocused(undefined);
  vi.useRealTimers();
});

describe('what a depth means', () => {
  it('is asked once per distinct depth however many rows carry it', async () => {
    // A party of six reported at one depth is six rows and one question: the answer is a property
    // of the number and the trip, not of the row.
    const { wrapper } = harness();
    renderHook(() => useTrackingDepthReadings('trip-1', [120, 120, 120]), { wrapper });

    await waitOut(1);

    expect(post).toHaveBeenCalledTimes(1);
  });

  it('is asked separately for each depth that is genuinely a different question', async () => {
    // The twin of the count above, without which "one request" would also be satisfied by a screen
    // that asked about one depth and drew the rest from nothing.
    const { wrapper } = harness();
    renderHook(() => useTrackingDepthReadings('trip-1', [120, 300]), { wrapper });

    await waitOut(1);

    expect(post).toHaveBeenCalledTimes(2);
  });

  it('is not asked again because somebody came back to the window', async () => {
    // TanStack refetches on focus by default, which is right for a figure that moves on its own and
    // wrong for this one: a cave does not get deeper because a coordinator alt-tabbed. On a live
    // callout this screen is left and returned to constantly, and the default would fire the whole
    // fan-out every time — waited out past the held answer's life first, so that what is being
    // asserted is the focus rule rather than the clock standing in for it.
    const { wrapper } = harness();
    renderHook(() => useTrackingDepthReadings('trip-1', [120, 300]), { wrapper });
    await waitOut(1);
    expect(post).toHaveBeenCalledTimes(2);

    await waitOut(10 * 60_000);
    await leaveAndReturn();

    expect(post).toHaveBeenCalledTimes(2);
  });

  /**
   * And the twin of that silence, which is what makes holding the answers safe rather than merely
   * cheap: the one write that changes what a depth means drops every answer at once.
   *
   * <b>Every part of a configuration write does it — the model, the datum and the filter each move
   * every station's depth together.</b> An answer worked out under the old configuration and still
   * drawn under the new one is a distance measured somewhere else, on the surface that says where a
   * party is; that is worth more than the requests it costs to re-ask.
   */
  it('is forgotten the moment the trip is reconfigured', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(
      () => ({
        readings: useTrackingDepthReadings('trip-1', [120, 300]),
        save: useSetTripTracking(),
      }),
      { wrapper },
    );
    await waitOut(1);
    expect(post).toHaveBeenCalledTimes(2);

    await act(async () => {
      await result.current.save.mutateAsync({
        tripLogId: 'trip-1',
        state: 'armed',
        referenceStationName: 'the real entrance',
      });
      await vi.advanceTimersByTimeAsync(1);
    });

    // Both of them, not merely whichever one was on screen: the answers are reached by the prefix
    // that names the trip, because a stale one nobody is looking at right now is a stale one drawn
    // the moment a row scrolls back into view.
    expect(post).toHaveBeenCalledTimes(4);
  });

  it('is left alone by a write to some other trip', async () => {
    // The twin of the forgetting above. Dropping every held answer on any write at all would be a
    // fan-out per configuration change anywhere in the application, which is the cost this file is
    // about, bought back.
    const { wrapper } = harness();
    const { result } = renderHook(
      () => ({
        readings: useTrackingDepthReadings('trip-1', [120, 300]),
        save: useSetTripTracking(),
      }),
      { wrapper },
    );
    await waitOut(1);
    expect(post).toHaveBeenCalledTimes(2);

    await act(async () => {
      await result.current.save.mutateAsync({
        tripLogId: 'another-trip',
        state: 'armed',
        referenceStationName: 'somewhere else entirely',
      });
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(post).toHaveBeenCalledTimes(2);
  });
});
