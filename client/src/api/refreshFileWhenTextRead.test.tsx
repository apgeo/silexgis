// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { queryKeys, useRefreshFileWhenTextRead } from './hooks.ts';

/**
 * A document's page count is written by the pass that reads its words, and the file — not the
 * document — carries it. The document is polled while the reading is in flight; the file is
 * cached for minutes and is not. So the moment the reading finishes has to reach the file, or
 * a reader who has just uploaded a two-page report is shown page one and nothing to say there
 * is a second: the page strip appears only when the count does.
 */

/** A provider whose cache survives re-renders, with the invalidations it was asked for. */
function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper, invalidate };
}

describe('useRefreshFileWhenTextRead', () => {
  it('fetches the file again when the reading finishes, so the page count can arrive', () => {
    const { wrapper, invalidate } = harness();
    const { rerender } = renderHook(
      ({ state }: { state: string }) => useRefreshFileWhenTextRead('file-1', state),
      { wrapper, initialProps: { state: 'pending' } },
    );

    // Still reading: there is nothing new to fetch yet.
    expect(invalidate).not.toHaveBeenCalled();

    rerender({ state: 'extracted' });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.file('file-1') });
  });

  it('fetches again when the reading ends without words, which also settles the count', () => {
    const { wrapper, invalidate } = harness();
    const { rerender } = renderHook(
      ({ state }: { state: string }) => useRefreshFileWhenTextRead('file-2', state),
      { wrapper, initialProps: { state: 'pending' } },
    );

    rerender({ state: 'noText' });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.file('file-2') });
  });

  // Opening a document read long ago is the common case, and it must not cost a second request
  // for every visit — which is what tying this to the state rather than the change would do.
  it('leaves a document whose words were read long ago alone', () => {
    const { wrapper, invalidate } = harness();
    renderHook(() => useRefreshFileWhenTextRead('file-3', 'extracted'), { wrapper });
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('waits for a file to be known before asking for it again', () => {
    const { wrapper, invalidate } = harness();
    const { rerender } = renderHook(
      ({ id, state }: { id: string | undefined; state: string }) =>
        useRefreshFileWhenTextRead(id, state),
      { wrapper, initialProps: { id: undefined as string | undefined, state: 'pending' } },
    );

    rerender({ id: undefined, state: 'extracted' });
    expect(invalidate).not.toHaveBeenCalled();
  });
});
