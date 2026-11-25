// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { queryKeys } from './queryKeys.ts';

vi.mock('../../auth/auth.tsx', () => ({
  userManager: { getUser: () => Promise.resolve(null) },
}));

const BrowserlessRequest = globalThis.Request;
vi.stubGlobal(
  'Request',
  class extends BrowserlessRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, 'http://localhost') : input, init);
    }
  },
);

vi.stubGlobal(
  'fetch',
  vi.fn(() =>
    Promise.resolve(
      new Response(
        JSON.stringify({
          batchId: 'b1',
          tripLogId: 't1',
          createdTrip: true,
          createdEventCount: 2,
          skippedCount: 0,
          failures: [],
          held: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    ),
  ),
);

const { useCommitRecordingImport } = await import('./recordings.ts');

describe('confirming a kept recording', () => {
  // The review stays on screen behind the result of its confirmation. Asked again, its preview is
  // answered "already made into a trip report": a refusal logged by the browser, and a warning
  // drawn over the page next to the dialog that says it worked.
  it('does not ask the recording’s own review again, and refreshes what lists recordings', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const preview = queryKeys.recordingImportPreview('r1', { page: 1 });
    const listed = ['trip-recordings', 'cave', 'c1'];
    client.setQueryData(preview, { points: [] });
    client.setQueryData(listed, []);
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useCommitRecordingImport(), { wrapper });

    await act(() =>
      result.current.mutateAsync({
        recordingId: 'r1',
        body: { options: {}, pointIds: ['p1'], decisions: {} },
      }),
    );

    expect(client.getQueryState(listed)?.isInvalidated).toBe(true);
    expect(client.getQueryState(preview)?.isInvalidated).toBe(false);
  });
});
