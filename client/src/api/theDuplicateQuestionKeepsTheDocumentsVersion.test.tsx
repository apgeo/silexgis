// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

// The duplicate question re-reads the document it names through the same cache entry the
// document page reads. Whatever it leaves in that entry is what the page's details form saves
// from, so the version that reading was answered at has to stay attached to it.
let version = 1;

vi.stubGlobal(
  'fetch',
  vi.fn(async (input: Request) => {
    const url = new URL(input.url);
    if (url.pathname === '/api/v1/cabinets') {
      return new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(
      JSON.stringify({ id: 'd-1', title: `Title ${version}`, cabinetIds: [], textExtraction: 'done' }),
      { status: 200, headers: { 'Content-Type': 'application/json', ETag: `"${version}"` } },
    );
  }),
);

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

const { versionReadWith } = await import('./readingVersion.ts');
const { useDocument, useReadDocumentFiling } = await import('./hooks.ts');

describe('the duplicate question reading a document the page also holds', () => {
  it('leaves the version it read at attached to what the cache now holds', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const page = renderHook(() => useDocument('d-1'), { wrapper });
    await waitFor(() => expect(page.result.current.data?.title).toBe('Title 1'));

    // The document changes elsewhere; the duplicate question reads it again.
    version = 2;
    const question = renderHook(() => useReadDocumentFiling(), { wrapper });
    await act(async () => {
      await question.result.current('d-1');
    });

    await waitFor(() => expect(page.result.current.data?.title).toBe('Title 2'));
    expect(versionReadWith(page.result.current.data)).toBe('"2"');
  });
});
