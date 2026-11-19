// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
vi.mock('../client.ts', async () => {
  const real = await vi.importActual<typeof import('../client.ts')>('../client.ts');
  return { ...real, api: { GET: get } };
});

const { useHistory } = await import('./documents.ts');

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const event = (id: number) => ({
  id,
  at: '2026-07-14T10:00:00Z',
  userId: null,
  userName: 'Ana',
  action: 'updated',
  entityType: 'Cave',
  entityId: 'c1',
  changes: null,
  redactedProperties: [],
  source: null,
  changeSetId: null,
  importBatchId: null,
});

/** Pages of a history 150 long, newest first, served 100 at a time as the server pages it. */
function answerPages(total = 150) {
  get.mockImplementation((path: string, init?: { params: { query: { page?: number; pageSize?: number } } }) => {
    if (!init) throw new Error(`unexpected GET ${path}`);
    const page = init.params.query.page ?? 1;
    const size = init.params.query.pageSize ?? 50;
    const ids = Array.from({ length: total }, (_, i) => total - i).slice((page - 1) * size, page * size);
    return Promise.resolve({
      data: { items: ids.map(event), page, pageSize: size, totalItems: total },
      response: new Response(null, { status: 200 }),
    });
  });
}

describe('useHistory', () => {
  beforeEach(() => {
    get.mockReset();
  });

  it('reads the older changes on request and adds them below the newest page', async () => {
    answerPages();
    const { result } = renderHook(() => useHistory('cave', 'c1'), { wrapper });
    await waitFor(() => expect(result.current.data?.items).toHaveLength(100));
    expect(result.current.data?.totalItems).toBe(150);
    expect(result.current.hasMore).toBe(true);

    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.data?.items).toHaveLength(150));
    expect(get).toHaveBeenLastCalledWith('/api/v1/history', {
      params: { query: { entityType: 'cave', entityId: 'c1', page: 2, pageSize: 100 } },
    });
    expect(result.current.data?.items.map((e) => e.id).slice(99, 101)).toEqual([51, 50]);
    expect(result.current.hasMore).toBe(false);
  });

  it('shows a change once when a new one pushed it onto the next page between reads', async () => {
    answerPages();
    const { result } = renderHook(() => useHistory('cave', 'c1'), { wrapper });
    await waitFor(() => expect(result.current.data?.items).toHaveLength(100));

    // A change recorded meanwhile moves every row one place down: page two now starts with the row
    // page one already ended on.
    answerPages(151);
    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.hasMore).toBe(false));
    const ids = result.current.data!.items.map((e) => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('keeps the changes it has when the older ones fail, and says the older ones failed', async () => {
    answerPages();
    const { result } = renderHook(() => useHistory('cave', 'c1'), { wrapper });
    await waitFor(() => expect(result.current.data?.items).toHaveLength(100));

    get.mockResolvedValueOnce({
      error: { code: 'internal.unexpected' },
      response: new Response(null, { status: 500 }),
    });
    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.moreFailed).toBe(true));
    expect(result.current.data?.items).toHaveLength(100);
    expect(result.current.hasMore).toBe(true);
  });
});
