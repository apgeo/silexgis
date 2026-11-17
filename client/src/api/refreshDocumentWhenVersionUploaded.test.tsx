// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from './client.ts';
import { queryKeys, useUploadFileVersion } from './hooks.ts';

/**
 * A new version moves the document's head: its current file, version number, size and type all
 * change. The document page reads those from the document and the file records, so both have to
 * be fetched again, or the page goes on showing the version that was just replaced.
 */
afterEach(() => vi.restoreAllMocks());

describe('useUploadFileVersion', () => {
  it('refreshes the document and the file records as well as the attachment lists', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue();
    vi.spyOn(api, 'POST').mockResolvedValue({
      data: { id: 'file-2' },
      error: undefined,
      response: new Response(null, { status: 201 }),
    } as never);
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useUploadFileVersion(), { wrapper });

    await act(() => result.current.mutateAsync({ fileId: 'file-1', file: new File(['x'], 'r.pdf') }));

    const keys = invalidate.mock.calls.map(([filters]) => JSON.stringify(filters?.queryKey));
    // Prefixes: each covers the detail entries the document page reads.
    expect(queryKeys.document('d1').slice(0, 1)).toEqual(['documents']);
    expect(queryKeys.file('f1').slice(0, 1)).toEqual(['files']);
    expect(keys).toContain(JSON.stringify(['documents']));
    expect(keys).toContain(JSON.stringify(['files']));
  });
});
