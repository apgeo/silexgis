// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';

/**
 * The station pictures' links read again on the clock, through the real reader and a faked server.
 *
 * The thumbnails come with signed addresses that run out, so the links are read again while the
 * viewer stays open. The hook's own test only sees the interval it hands to its reader; this one
 * counts the requests that actually leave, so a reader that dropped the interval on the way to the
 * query library is caught too.
 */

const get = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, api: { ...actual.api, GET: (...args: unknown[]) => get(...args) } };
});

const { useStationMedia, STATION_MEDIA_REFRESH_MILLISECONDS } = await import('./useStationMedia.ts');

afterEach(() => {
  vi.useRealTimers();
  get.mockReset();
});

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const linksAsked = () => get.mock.calls.filter(([path]) => path === '/api/v1/reslinks/for-target').length;

it('asks for the links again once the refresh interval has passed, and not before', async () => {
  vi.useFakeTimers();
  get.mockImplementation(() =>
    Promise.resolve({ data: { items: [], page: 1, pageSize: 200, totalItems: 0 }, response: new Response(null, { status: 200 }) }),
  );

  renderHook(() => useStationMedia('model-1', true), { wrapper });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
  expect(linksAsked()).toBe(1);

  await act(async () => {
    await vi.advanceTimersByTimeAsync(STATION_MEDIA_REFRESH_MILLISECONDS - 1_000);
  });
  expect(linksAsked()).toBe(1);

  await act(async () => {
    await vi.advanceTimersByTimeAsync(1_000);
  });
  expect(linksAsked()).toBe(2);
});

it('asks for nothing while the viewer is closed', async () => {
  vi.useFakeTimers();
  get.mockResolvedValue({ data: { items: [] }, response: new Response(null, { status: 200 }) });

  renderHook(() => useStationMedia('model-1', false), { wrapper });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(STATION_MEDIA_REFRESH_MILLISECONDS * 2);
  });
  expect(linksAsked()).toBe(0);
});
