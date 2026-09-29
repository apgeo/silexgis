// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is when this browser asks
// again, not what comes back.
const get = vi.fn();
const put = vi.fn(() =>
  Promise.resolve({ data: {}, response: new Response(null, { status: 200 }) }),
);

vi.mock('./client.ts', () => ({
  api: { GET: () => get(), PUT: () => put() },
  ApiError: class ApiError extends Error {},
  isSettledRefusal: () => false,
  lastReadETag: () => 'W/"1"',
}));

const { useTrackingPlaces, useSetTripTracking } = await import('./hooks.ts');

/** The server's answer to a watch that names no model: a refusal, and a settled one. */
const noModel = () =>
  Promise.resolve({
    data: undefined,
    error: { code: 'tracking.model_unavailable' },
    response: new Response(null, { status: 409 }),
  });

/** The same question once the watch has a model. */
const declared = () =>
  Promise.resolve({
    data: [{ depthM: 120, stationName: 'p.g.119', placeLabel: 'the big room' }],
    response: new Response(null, { status: 200 }),
  });

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

beforeEach(() => {
  get.mockReset().mockImplementationOnce(noModel).mockImplementation(declared);
  put.mockClear();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

/**
 * The places a cave has declared, as the report card holds them across the arming of a watch.
 *
 * <b>The question is answered against the watch's model, and before there is one it is refused.</b>
 * That refusal is settled — the model does not appear between two attempts — so it is held rather
 * than retried, which is right until the one act that changes the answer: the configuration write
 * that gives the watch its model. The card stays mounted across that write, so nothing else would
 * ask again; a coordinator opening the tab, arming with a model and ticking "at depth" would find
 * no place chooser although the cave declared places, until the window happened to regain focus.
 */
describe('the places a cave has declared', () => {
  it('are asked again the moment the watch is configured', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(
      () => ({ places: useTrackingPlaces('trip-1'), save: useSetTripTracking() }),
      { wrapper },
    );
    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(1);
    expect(result.current.places.isError).toBe(true);

    await act(async () => {
      await result.current.save.mutateAsync({
        tripLogId: 'trip-1',
        state: 'armed',
        surveyModelId: 'model-1',
      });
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(get).toHaveBeenCalledTimes(2);
    expect(result.current.places.isError).toBe(false);
    expect(result.current.places.data).toHaveLength(1);
  });

  it('are left alone by a write to some other trip', async () => {
    // The twin of the forgetting above: the answer is dropped by the write that changes it and by
    // no other, so configuring one trip does not re-ask about every trip that has a card open.
    const { wrapper } = harness();
    const { result } = renderHook(
      () => ({ places: useTrackingPlaces('trip-1'), save: useSetTripTracking() }),
      { wrapper },
    );
    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(1);

    await act(async () => {
      await result.current.save.mutateAsync({
        tripLogId: 'another-trip',
        state: 'armed',
        surveyModelId: 'model-1',
      });
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(get).toHaveBeenCalledTimes(1);
  });

  it('are not asked for at all while the caller says the watch is off', async () => {
    // A refusal fetched for a watch that is off would be held, and then drawn as "no places" the
    // moment the watch came on — so the card does not ask until there is a watch to ask about.
    const { wrapper } = harness();
    renderHook(() => useTrackingPlaces('trip-1', false), { wrapper });
    await waitOut(1);

    expect(get).not.toHaveBeenCalled();
  });
});
