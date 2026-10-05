// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { TripLogWrite } from './hooks.ts';

// The transport is stubbed rather than the network: what is under test is when a save is
// finished and what the page holds once it is, not what the server says.
const get = vi.fn();
const put = vi.fn();
const post = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    GET: () => get(),
    PUT: (...args: unknown[]) => put(...args),
    POST: (...args: unknown[]) => post(...args),
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => '"7"',
}));

const { queryKeys, useMoveTripLog, useTripLog, useUpdateTripLog } = await import('./hooks.ts');

/**
 * A save on a trip used to be finished only once the trip had been read back, because only a
 * read handed out the version the next save had to carry; the button spun for the read on top of
 * the write, every time. The server now hands the version over with the answer, and the answer
 * is the trip as it stands, so a save is finished when it is answered: the page redraws from the
 * answer, and nothing waits on a read.
 */
const before = { id: 'trip-1', title: 'Before', state: 'draft' };
const after = { id: 'trip-1', title: 'After', state: 'draft' };

function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

function harness() {
  // Nothing is stale on its own: the only thing that may ask the server again is the save.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  client.setQueryData(queryKeys.tripLog('trip-1'), before);
  client.setQueryData(queryKeys.tripLogs({}), { items: [before], page: 1, pageSize: 20, totalItems: 1 });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

beforeEach(() => {
  get.mockReset();
  put.mockReset();
  post.mockReset();
  // A read that never answers: if finishing the save depended on it, nothing here would finish.
  get.mockImplementation(() => new Promise(() => {}));
});

describe('a write on a trip', () => {
  it('is finished when the server answers, and the page holds the answer', async () => {
    put.mockImplementation(() => answers(after));
    const { client, wrapper } = harness();
    const { result } = renderHook(
      () => ({ save: useUpdateTripLog(), trip: useTripLog('trip-1') }),
      { wrapper },
    );
    expect(result.current.trip.data).toEqual(before);

    await act(async () => {
      await result.current.save.mutateAsync({
        id: 'trip-1',
        body: { title: 'After', tripDate: '2026-07-01' } as TripLogWrite,
      });
    });

    // The page redraws from the answer, not from a read that followed it.
    await waitFor(() => expect(result.current.trip.data).toEqual(after));
    expect(get).not.toHaveBeenCalled();
    // What else is held under the trip prefix is stale, to be read again when next looked at;
    // the trip itself is not, because asking for it again would read back what is held.
    expect(client.getQueryState(queryKeys.tripLogs({}))?.isInvalidated).toBe(true);
    expect(client.getQueryState(queryKeys.tripLog('trip-1'))?.isInvalidated).toBe(false);
  });

  it('moving the trip carries the version held under the trip and takes the answer the same way', async () => {
    const proposed = { ...after, state: 'proposed' };
    post.mockImplementation(() => answers(proposed));
    const { wrapper } = harness();
    const { result } = renderHook(
      () => ({ move: useMoveTripLog(), trip: useTripLog('trip-1') }),
      { wrapper },
    );

    await act(async () => {
      await result.current.move.mutateAsync({ id: 'trip-1', state: 'proposed' });
    });

    expect(post).toHaveBeenCalledWith(
      '/api/v1/trip-logs/{id}/state',
      expect.objectContaining({ headers: { 'If-Match': '"7"' } }),
    );
    await waitFor(() => expect(result.current.trip.data).toEqual(proposed));
    expect(get).not.toHaveBeenCalled();
  });
});
