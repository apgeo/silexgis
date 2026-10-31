// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is which readings are
// stale once a delete or a restore has been answered or refused, not what the server says.
const del = vi.fn();
const post = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    DELETE: (...args: unknown[]) => del(...args),
    POST: (...args: unknown[]) => post(...args),
    GET: () => new Promise(() => {}),
  },
  ApiError: class extends Error {},
  lastReadETag: () => undefined,
  retryAfterOf: () => undefined,
}));

const { queryKeys, useDeleteDocument, useRestoreDocument } = await import('./hooks.ts');

/**
 * A deleted document is absent from every list that named it — cabinets, the inbox, search, the
 * gallery, links — and none of them is told which document went. So a delete or a restore makes
 * all of them stale, and a refused restore makes the deleted list stale, since the refusal is
 * news about the row the button was on.
 */
function harness() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  });
  const held = {
    cabinets: ['cabinets', 'c1', 'documents', {}] as const,
    search: ['search', 'documents', 'word'] as const,
    photos: ['photos', 'list', {}] as const,
    links: ['reslinks', 'for-target', 'document', 'd1', {}] as const,
    deleted: queryKeys.deletedDocuments(1),
    config: queryKeys.documentConfig,
  };
  for (const key of Object.values(held)) {
    client.setQueryData(key, { held: true });
  }
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const stale = (key: readonly unknown[]) => client.getQueryState(key)?.isInvalidated ?? false;
  return { client, wrapper, held, stale };
}

beforeEach(() => {
  del.mockReset().mockResolvedValue({ data: undefined, error: undefined, response: { ok: true, status: 204 } });
  post.mockReset().mockResolvedValue({ data: undefined, error: undefined, response: { ok: true, status: 204 } });
});

describe('document deletion and the readings it leaves behind', () => {
  it('makes every list that could name the document stale when it is deleted', async () => {
    const { wrapper, held, stale } = harness();
    const { result } = renderHook(() => useDeleteDocument(), { wrapper });

    await act(() => result.current.mutateAsync('d1'));

    expect(del).toHaveBeenCalledOnce();
    expect(stale(held.cabinets)).toBe(true);
    expect(stale(held.search)).toBe(true);
    expect(stale(held.photos)).toBe(true);
    expect(stale(held.links)).toBe(true);
    expect(stale(held.deleted)).toBe(true);
    // Describes the installation, not a document, so a delete does not make it stale.
    expect(stale(held.config)).toBe(false);
  });

  it('makes the same lists stale when it is restored', async () => {
    const { wrapper, held, stale } = harness();
    const { result } = renderHook(() => useRestoreDocument(), { wrapper });

    await act(() => result.current.mutateAsync('d1'));

    expect(stale(held.cabinets)).toBe(true);
    expect(stale(held.search)).toBe(true);
    expect(stale(held.deleted)).toBe(true);
    expect(stale(held.config)).toBe(false);
  });

  it('reads the deleted list again when a restore is refused, and leaves the other lists alone', async () => {
    post.mockResolvedValue({
      data: undefined,
      error: { code: 'document.restore_window_passed' },
      response: { ok: false, status: 409 },
    });
    const { wrapper, held, stale } = harness();
    const { result } = renderHook(() => useRestoreDocument(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('d1').catch(() => undefined);
    });

    expect(stale(held.deleted)).toBe(true);
    expect(stale(held.cabinets)).toBe(false);
  });
});
