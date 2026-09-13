// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

/** What each page of the pretend log holds, and how long the log claims to be. */
let totalItems = 0;
let pageSizeAsked: number | undefined;
const pagesAsked: number[] = [];

const get = vi.fn((options: { params: { query: { page: number; pageSize: number } } }) => {
  const { page, pageSize } = options.params.query;
  pagesAsked.push(page);
  pageSizeAsked = pageSize;
  const first = (page - 1) * pageSize;
  const count = Math.max(0, Math.min(pageSize, totalItems - first));
  return Promise.resolve({
    data: {
      items: Array.from({ length: count }, (_value, index) => ({ id: `event-${first + index}` })),
      page,
      pageSize,
      totalItems,
    },
    response: new Response(null, { status: 200 }),
  });
});

vi.mock('./client.ts', () => ({
  api: { GET: (_path: string, options: unknown) => get(options as never) },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { useTripTrackingEventLog } = await import('./hooks.ts');

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return wrapper;
}

beforeEach(() => {
  get.mockClear();
  pagesAsked.length = 0;
  pageSizeAsked = undefined;
});

/**
 * The read the replay is built on, and the one place the rule lives.
 *
 * Reports come back newest first, so half a log is the *end* of the trip. A replay over one would
 * open with the party already underground at places nothing on screen says they walked to, and
 * would animate that as confidently as it animates anything else — a picture of a trip that did not
 * happen, on the surface a rescue co-ordinator reads. So the pages are followed to the end, and a
 * read that cannot reach the end is a failure rather than a short answer.
 */
describe("a trip's whole tracking log", () => {
  it('follows every page until the log is complete', async () => {
    totalItems = 450;
    const { result } = renderHook(() => useTripTrackingEventLog('trip-1'), { wrapper: harness() });

    await waitFor(() => expect(result.current.data).toBeDefined());

    expect(result.current.data).toHaveLength(450);
    expect(pagesAsked).toEqual([1, 2, 3]);
    // Asked for in few large pages: this is one read of a whole log, not a list somebody scrolls.
    expect(pageSizeAsked).toBeGreaterThanOrEqual(100);
  });

  it('asks once for a log that fits in one page', async () => {
    totalItems = 12;
    const { result } = renderHook(() => useTripTrackingEventLog('trip-1'), { wrapper: harness() });

    await waitFor(() => expect(result.current.data).toBeDefined());
    expect(result.current.data).toHaveLength(12);
    expect(pagesAsked).toEqual([1]);
  });

  it('fails rather than answering with the part of a log it managed to read', async () => {
    // A log longer than any number of pages this follows. What must not happen is an answer: the
    // caller cannot tell a short log from a truncated one, and the whole feature's honesty rests
    // on nobody having to.
    totalItems = 1_000_000;
    const { result } = renderHook(() => useTripTrackingEventLog('trip-1'), { wrapper: harness() });

    await waitFor(() => expect(result.current.error).toBeTruthy());
    expect(result.current.data).toBeUndefined();
  });

  it('is not asked for at all until somebody wants it', () => {
    totalItems = 12;
    renderHook(() => useTripTrackingEventLog('trip-1', false), { wrapper: harness() });
    renderHook(() => useTripTrackingEventLog(undefined), { wrapper: harness() });

    expect(get).not.toHaveBeenCalled();
  });
});
