// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is when this browser asks
// again, not what comes back.
const { get } = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock('./client.ts', () => ({
  api: { GET: () => get() },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { expeditionSurfaceLogPollInterval, useExpeditionSurfaceLog } = await import('./hooks.ts');

interface Log {
  trips: { tripLogId: string; state: 'armed' | 'closed' }[];
}

/** A camp's head count, said only in how each listed watch stands. */
const listing = (...states: ('armed' | 'closed')[]): Log => ({
  trips: states.map((state, index) => ({ tripLogId: `trip-${index}`, state })),
});

const answer = (log: Log) =>
  Promise.resolve({ data: log, response: new Response(null, { status: 200 }) });

const answering = (log: Log) => get.mockImplementation(() => answer(log));

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper };
}

/** Lets the query settle and then stands still for as long as the caller says. */
async function waitOut(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

/**
 * When a camp's head count is asked for again.
 *
 * The count changes because of what happens at the cave, not because this browser did anything:
 * somebody radios a report in, a party arms its watch, a closed watch is armed again. So for as
 * long as the section is on screen it has to be re-read by itself — at the trip's own interval, or
 * the camp would show somebody underground whom the trip it links to already shows out — and
 * whatever the last answer held, because the answer that most needs replacing is the one that
 * says nobody is being followed. And it has to stop when nobody is looking: a camp page is left
 * open in a tab for the length of a camp, and its sections stay mounted behind one another.
 */
describe('how often a camp’s head count is asked for', () => {
  it('is every half minute while the section is on screen', () => {
    expect(expeditionSurfaceLogPollInterval(true)).toBe(30_000);
  });

  it('is never while the section is behind another one', () => {
    expect(expeditionSurfaceLogPollInterval(false)).toBe(false);
  });
});

describe('a camp’s head count, on a real query client', () => {
  beforeEach(() => {
    get.mockReset();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('is asked for again while a watch is running', async () => {
    answering(listing('armed', 'closed'));
    const { wrapper } = harness();
    renderHook(() => useExpeditionSurfaceLog('camp-1', true), { wrapper });

    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(1);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(2);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(3);
  });

  it('shows a watch armed elsewhere on the next interval, having listed nothing until then', async () => {
    // The coordinator opens the section before any party has started; a leader then arms a
    // watch from a phone. Nothing is done on this screen in between.
    get.mockImplementationOnce(() => answer(listing()));
    get.mockImplementation(() => answer(listing('armed')));
    const { wrapper } = harness();
    const { result } = renderHook(() => useExpeditionSurfaceLog('camp-1', true), { wrapper });

    await waitOut(1);
    expect(result.current.data?.trips).toEqual([]);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(2);
    expect(result.current.data?.trips.map((trip) => trip.state)).toEqual(['armed']);
  });

  it('shows a closed watch armed again on the next interval', async () => {
    // Everybody was out and the watch closed; then somebody went back in and it was re-armed.
    get.mockImplementationOnce(() => answer(listing('closed')));
    get.mockImplementation(() => answer(listing('armed')));
    const { wrapper } = harness();
    const { result } = renderHook(() => useExpeditionSurfaceLog('camp-1', true), { wrapper });

    await waitOut(1);
    expect(result.current.data?.trips.map((trip) => trip.state)).toEqual(['closed']);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(2);
    expect(result.current.data?.trips.map((trip) => trip.state)).toEqual(['armed']);
  });

  it('is asked for again after a read that failed', async () => {
    get.mockImplementationOnce(() => Promise.reject(new Error('unreachable')));
    get.mockImplementation(() => answer(listing('armed')));
    const { wrapper } = harness();
    const { result } = renderHook(() => useExpeditionSurfaceLog('camp-1', true), { wrapper });

    await waitOut(1);
    expect(result.current.isError).toBe(true);

    await waitOut(30_000);
    expect(result.current.isError).toBe(false);
    expect(result.current.data?.trips.map((trip) => trip.state)).toEqual(['armed']);
  });

  it('stops being asked for when the section goes behind another, and is read afresh on return', async () => {
    answering(listing('armed'));
    const { wrapper } = harness();
    const { rerender } = renderHook(
      ({ shown }: { shown: boolean }) => useExpeditionSurfaceLog('camp-1', shown),
      { wrapper, initialProps: { shown: true } },
    );

    await waitOut(1);
    await waitOut(30_000);
    // The control: it was polling, so the silence below is the section's doing and not a
    // harness in which nothing is ever asked twice.
    expect(get).toHaveBeenCalledTimes(2);

    rerender({ shown: false });
    await waitOut(120_000);
    expect(get).toHaveBeenCalledTimes(2);

    // Back on screen: asked at once rather than at the next interval, because the count somebody
    // returns to after two minutes elsewhere must not be the two-minute-old one.
    rerender({ shown: true });
    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(3);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(4);
  });

  it('is not asked for at all by a section that has never been on screen', async () => {
    answering(listing('armed'));
    const { wrapper } = harness();
    renderHook(() => useExpeditionSurfaceLog('camp-1', false), { wrapper });

    await waitOut(120_000);
    expect(get).not.toHaveBeenCalled();
  });
});
