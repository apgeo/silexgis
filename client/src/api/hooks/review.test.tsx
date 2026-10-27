// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
vi.mock('../client.ts', async () => {
  const real = await vi.importActual<typeof import('../client.ts')>('../client.ts');
  return { ...real, api: { GET: get } };
});

const { useHeldPreview } = await import('./review.ts');

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe('useHeldPreview', () => {
  const created: Blob[] = [];
  const revoked: string[] = [];

  beforeEach(() => {
    get.mockReset();
    created.length = 0;
    revoked.length = 0;
    vi.stubGlobal('URL', Object.assign(Object.create(URL), {
      createObjectURL: (blob: Blob) => {
        created.push(blob);
        return `blob:test/${created.length}`;
      },
      revokeObjectURL: (address: string) => revoked.push(address),
    }));
  });

  afterEach(() => vi.unstubAllGlobals());

  it('reads the rendering through the client, as a blob, and lets its address go when it leaves', async () => {
    const picture = new Blob(['webp'], { type: 'image/webp' });
    get.mockResolvedValue({ data: picture, response: new Response(null, { status: 200 }) });
    const { result, unmount } = renderHook(() => useHeldPreview('R1', 1200, true), { wrapper });
    await waitFor(() => expect(result.current.url).toBe('blob:test/1'));
    expect(get).toHaveBeenCalledWith('/api/v1/review/items/{reviewId}/preview', {
      params: { path: { reviewId: 'R1' }, query: { size: 1200 } },
      parseAs: 'blob',
    });
    expect(created).toEqual([picture]);
    unmount();
    expect(revoked).toEqual(['blob:test/1']);
  });

  it('asks nothing until it is wanted, and says so when the server has no picture', async () => {
    renderHook(() => useHeldPreview('R2', 480, false), { wrapper });
    expect(get).not.toHaveBeenCalled();

    get.mockResolvedValue({
      error: { code: 'review.preview_unavailable' },
      response: new Response(null, { status: 404 }),
    });
    const { result } = renderHook(() => useHeldPreview('R3', 480, true), { wrapper });
    await waitFor(() => expect(result.current.failed).toBe(true));
    expect(result.current.url).toBeUndefined();
  });
});
