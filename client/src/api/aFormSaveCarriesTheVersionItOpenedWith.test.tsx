// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { TripLogWrite } from './hooks.ts';

// The network is what is stubbed here, and nothing above it: the question is which version
// reaches the server, and that is decided between the hook and the transport's own replay of
// versions — the two things a stub of either would take out of the test.
const sent: Request[] = [];
let version = 1;

vi.stubGlobal(
  'fetch',
  vi.fn((input: Request) => {
    sent.push(input);
    return Promise.resolve(
      new Response(JSON.stringify({ id: 'trip-1', title: 'A trip' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: `"${version}"` },
      }),
    );
  }),
);

// The application asks by path and the browser supplies the origin; outside a browser a request
// cannot be built from a path alone, so the origin is supplied here.
const BrowserlessRequest = globalThis.Request;
vi.stubGlobal(
  'Request',
  class extends BrowserlessRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, 'http://localhost') : input, init);
    }
  },
);

vi.mock('../auth/auth.tsx', () => ({
  userManager: { getUser: () => Promise.resolve(null) },
}));

const { api, lastReadETag } = await import('./client.ts');
const { useUpdateTripLog } = await import('./hooks.ts');

const path = '/api/v1/trip-logs/trip-1';
const read = () => api.GET('/api/v1/trip-logs/{id}', { params: { path: { id: 'trip-1' } } });

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const lastPut = () => sent.filter((request) => request.method === 'PUT').at(-1);

/**
 * Two people open a trip's form. The first saves a change to the roster, which moves the trip's
 * version. The second switches away and comes back: the page under their form reads the trip
 * again and the newer version is on file. Their save still holds the roster as it was — and it
 * used to carry the newer version, so the server found nothing stale and the first person's
 * change was written back to nothing. The save has to carry the version the form opened with.
 */
describe('a save from a form opened before the trip was read again', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 1;
  });

  it('carries the version the form opened with, so the server can refuse it', async () => {
    await read();
    const openedWith = lastReadETag(path);
    expect(openedWith).toBe('"1"');

    version = 2;
    await read();
    expect(lastReadETag(path)).toBe('"2"');

    const { result } = renderHook(() => useUpdateTripLog(), { wrapper });
    await act(() =>
      result.current.mutateAsync({ id: 'trip-1', body: {} as TripLogWrite, ifMatch: openedWith }),
    );

    expect(lastPut()?.headers.get('If-Match')).toBe('"1"');
  });

  it('carries the newest version read when the caller names none', async () => {
    await read();
    version = 2;
    await read();

    const { result } = renderHook(() => useUpdateTripLog(), { wrapper });
    await act(() => result.current.mutateAsync({ id: 'trip-1', body: {} as TripLogWrite }));

    expect(lastPut()?.headers.get('If-Match')).toBe('"2"');
  });
});
