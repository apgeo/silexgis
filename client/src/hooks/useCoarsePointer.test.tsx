// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COARSE_POINTER_QUERY, useCoarsePointer } from './useCoarsePointer.ts';

/** A device whose pointer can be changed under a mounted component, as a docked tablet's is. */
function withPointer(coarse: boolean) {
  const listeners = new Set<() => void>();
  let matches = coarse;
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query: string) =>
      ({
        matches: query === COARSE_POINTER_QUERY ? matches : false,
        media: query,
        addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
        removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
      }) as unknown as MediaQueryList,
  );
  return {
    becomes(next: boolean) {
      matches = next;
      act(() => listeners.forEach((listener) => listener()));
    },
    listenerCount: () => listeners.size,
  };
}

afterEach(() => vi.restoreAllMocks());

describe('useCoarsePointer', () => {
  it('reports a mouse at the default test environment', () => {
    const { result } = renderHook(() => useCoarsePointer());
    expect(result.current).toBe(false);
  });

  it('reports a finger', () => {
    withPointer(true);
    const { result } = renderHook(() => useCoarsePointer());
    expect(result.current).toBe(true);
  });

  it('follows a device that changes pointer without reloading the page', () => {
    // A tablet dropped into a keyboard dock. A layout that read this once would leave that viewer
    // with finger-sized chrome, and one that read it only on the next render would leave them with
    // it until something else happened to re-render.
    const device = withPointer(true);
    const { result } = renderHook(() => useCoarsePointer());

    device.becomes(false);

    expect(result.current).toBe(false);
  });

  it('lets go of the device when the component does', () => {
    const device = withPointer(true);
    const { unmount } = renderHook(() => useCoarsePointer());
    expect(device.listenerCount()).toBe(1);

    unmount();

    expect(device.listenerCount()).toBe(0);
  });
});
