// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is what the page holds
// once a restore has been answered or refused, not what the server says.
const get = vi.fn();
const post = vi.fn();

vi.mock('./client.ts', () => {
  class ApiError extends Error {
    status: number;
    code?: string;
    constructor(status: number, code?: string) {
      super(code ?? `${status}`);
      this.status = status;
      this.code = code;
    }
  }
  return {
    api: {
      GET: () => get(),
      POST: (...args: unknown[]) => post(...args),
    },
    ApiError,
    lastReadETag: () => undefined,
    // A refused call is asked how long the server wants the caller to wait. These refusals name
    // no wait.
    retryAfterOf: () => undefined,
  };
});

const { queryKeys, useRestoreTripLog, useTripLog } = await import('./hooks.ts');

/**
 * A deleted trip answers as not found everywhere, so nothing is held under its key while it is
 * deleted. Restoring it answers the trip itself, and that answer is what the page it opens on is
 * drawn from — no read stands between the button and the trip. The lists are another matter:
 * the deleted list has just lost a row and the trip list has just gained one, so both are stale.
 */
const restored = { id: 'trip-1', title: 'Back again', state: 'done' };
const deletedPage = {
  items: [{ id: 'trip-1', title: 'Back again', tripDate: '2026-07-20', deletedAt: '2026-10-01T10:00:00Z' }],
  page: 1,
  pageSize: 50,
  totalItems: 1,
};

function harness() {
  // Nothing is stale on its own: the only thing that may mark a reading stale is the restore.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  });
  client.setQueryData(queryKeys.deletedTripLogs(1), deletedPage);
  client.setQueryData(queryKeys.tripLogs({}), { items: [], page: 1, pageSize: 20, totalItems: 0 });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

beforeEach(() => {
  get.mockReset();
  post.mockReset();
  // A read that never answers: if the trip's page depended on one, it would never be drawn.
  get.mockImplementation(() => new Promise(() => {}));
});

describe('restoring a deleted trip', () => {
  it('holds the trip from the answer, and marks the lists it moved between as stale', async () => {
    post.mockImplementation(() =>
      Promise.resolve({ data: restored, response: new Response(null, { status: 200 }) }),
    );
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useRestoreTripLog(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('trip-1');
    });

    expect(post).toHaveBeenCalledWith(
      '/api/v1/trip-logs/{id}/restore',
      expect.objectContaining({ params: { path: { id: 'trip-1' } } }),
    );

    // The page the restore opens on is drawn from the answer: asked for, it is there already
    // and nothing is read.
    const { result: page } = renderHook(() => useTripLog('trip-1'), { wrapper });
    await waitFor(() => expect(page.current.data).toEqual(restored));
    expect(get).not.toHaveBeenCalled();
    expect(client.getQueryState(queryKeys.tripLog('trip-1'))?.isInvalidated).toBe(false);

    // The list it left and the list it returned to are both to be read again.
    expect(client.getQueryState(queryKeys.deletedTripLogs(1))?.isInvalidated).toBe(true);
    expect(client.getQueryState(queryKeys.tripLogs({}))?.isInvalidated).toBe(true);
  });

  it('reads the deleted list again when the restore is refused, and holds no trip', async () => {
    // Past its window, put back by somebody else, or gone for good: each is a refusal, and each
    // means the row this was pressed on should no longer be offered.
    post.mockImplementation(() =>
      Promise.resolve({
        error: { code: 'trip_log.restore_window_passed' },
        response: new Response(null, { status: 409 }),
      }),
    );
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useRestoreTripLog(), { wrapper });

    await act(async () => {
      await expect(result.current.mutateAsync('trip-1')).rejects.toMatchObject({
        status: 409,
        code: 'trip_log.restore_window_passed',
      });
    });

    expect(client.getQueryState(queryKeys.deletedTripLogs(1))?.isInvalidated).toBe(true);
    // Nothing came back, so nothing is held as the trip and the trip list is left alone.
    expect(client.getQueryData(queryKeys.tripLog('trip-1'))).toBeUndefined();
    expect(client.getQueryState(queryKeys.tripLogs({}))?.isInvalidated).toBe(false);
  });
});
