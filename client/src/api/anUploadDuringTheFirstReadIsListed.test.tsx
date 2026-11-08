// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

// The transport is stubbed: what is under test is whether the list is asked for again after an
// upload, not what either answer holds.
const reads: { resolve: (v: unknown) => void }[] = [];
const get = vi.fn(
  () =>
    new Promise((resolve) => {
      reads.push({ resolve });
    }),
);

vi.mock('./client.ts', () => ({
  api: { GET: () => get() },
  ApiError: class ApiError extends Error {},
}));
vi.mock('./uploadWithProgress.ts', () => ({
  uploadWithProgress: () => Promise.resolve({ id: 'm-1', name: 'sheet' }),
}));

const { useRasterMaps, useUploadRasterMap } = await import('./hooks/geodata.ts');

const page = (names: string[]) => ({
  data: { items: names.map((name) => ({ name })), total: names.length },
  response: new Response(null, { status: 200 }),
});

/**
 * A file dropped while its tab's first read of the list is still on its way. Marking the list
 * stale is not enough there: a query with no answer yet keeps the request already running — which
 * the server may have answered before the upload landed — so the new file never showed until the
 * page was reloaded. The list has to be read again after the upload.
 */
describe('an upload made during the first read of the list', () => {
  it('reads the list again once the upload is answered', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(
      () => ({ list: useRasterMaps({ page: 1, pageSize: 20 }), upload: useUploadRasterMap() }),
      { wrapper },
    );
    await waitFor(() => expect(reads).toHaveLength(1));

    await act(() => result.current.upload.mutateAsync({ file: new File(['x'], 'sheet.pdf') }));
    // The first read comes back with the list as it was before the upload…
    await act(async () => reads[0]!.resolve(page([])));

    // …and a second read, made after the upload, is what the list shows.
    await waitFor(() => expect(reads).toHaveLength(2));
    await act(async () => reads[1]!.resolve(page(['sheet'])));
    await waitFor(() => expect(result.current.list.data?.items.map((m) => m.name)).toEqual(['sheet']));
  });
});
