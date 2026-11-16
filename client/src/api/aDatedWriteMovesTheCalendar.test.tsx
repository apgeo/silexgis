// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ExpeditionWrite, TripLogWrite } from './hooks.ts';

const put = vi.fn();
const del = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    GET: () => new Promise(() => {}),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: (...args: unknown[]) => del(...args),
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { queryKeys, useDeleteExpedition, useDeleteTripLog, useUpdateExpedition, useUpdateTripLog } =
  await import('./hooks.ts');

/**
 * Trips and camps are rows on the calendar and shapes on the map, and neither cache is held under
 * the trip or camp prefix. A write that moved a trip's dates or a camp's area and left those two
 * alone drew the old days and the old shape until the page was next looked at again, while an
 * event write already refreshed the calendar. What a dated record's write marks stale is decided
 * in one place, and these pin that every family goes through it.
 */
const month = { from: '2026-07-01', to: '2026-07-31' } as never;

function harness() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  client.setQueryData(queryKeys.calendar(month), { entries: [] });
  client.setQueryData(queryKeys.tripLogMap('bbox', 'a', 'b'), []);
  client.setQueryData(queryKeys.campAreasMap('bbox', 'a', 'b'), []);
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const stale = (key: readonly unknown[]) => client.getQueryState(key)?.isInvalidated;
  return { client, wrapper, stale };
}

function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

beforeEach(() => {
  put.mockReset();
  del.mockReset();
  del.mockImplementation(() => Promise.resolve({ response: new Response(null, { status: 204 }) }));
});

describe('a write on a dated record', () => {
  it('a trip saved marks the calendar and the trip map stale', async () => {
    put.mockImplementation(() => answers({ id: 'trip-1', title: 'After' }));
    const { wrapper, stale } = harness();
    const { result } = renderHook(() => useUpdateTripLog(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync({ id: 'trip-1', body: {} as TripLogWrite });
    });
    expect(stale(queryKeys.calendar(month))).toBe(true);
    expect(stale(queryKeys.tripLogMap('bbox', 'a', 'b'))).toBe(true);
  });

  it('a trip deleted marks the calendar and the trip map stale', async () => {
    const { wrapper, stale } = harness();
    const { result } = renderHook(() => useDeleteTripLog(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync('trip-1');
    });
    expect(stale(queryKeys.calendar(month))).toBe(true);
    expect(stale(queryKeys.tripLogMap('bbox', 'a', 'b'))).toBe(true);
  });

  it('a camp saved marks the calendar and the camp map stale', async () => {
    put.mockImplementation(() => answers({ id: 'camp-1' }));
    const { wrapper, stale } = harness();
    const { result } = renderHook(() => useUpdateExpedition(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync({ id: 'camp-1', body: {} as ExpeditionWrite });
    });
    expect(stale(queryKeys.calendar(month))).toBe(true);
    expect(stale(queryKeys.campAreasMap('bbox', 'a', 'b'))).toBe(true);
  });

  it('a camp deleted marks the calendar and the camp map stale', async () => {
    const { wrapper, stale } = harness();
    const { result } = renderHook(() => useDeleteExpedition(), { wrapper });
    await act(async () => {
      await result.current.mutateAsync('camp-1');
    });
    expect(stale(queryKeys.calendar(month))).toBe(true);
    expect(stale(queryKeys.campAreasMap('bbox', 'a', 'b'))).toBe(true);
  });
});
