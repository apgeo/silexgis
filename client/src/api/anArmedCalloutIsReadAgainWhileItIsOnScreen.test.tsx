// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const get = vi.fn();

vi.mock('./client.ts', () => ({
  api: { GET: () => get() },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { useTripLog } = await import('./hooks.ts');

/**
 * The server moves an armed callout to overdue when its alarm passes, and records each time its
 * watcher looks. A trip page read once and left open — a coordinator's desk while the party is
 * underground — showed neither until somebody clicked away and back, so the alarm read as armed
 * after it had gone off. While a callout is live the trip is read again by itself; once it is not,
 * nothing is asked that a person did not ask for.
 */
function trip(calloutState: string) {
  return Promise.resolve({
    data: { id: 'trip-1', calloutState },
    response: new Response(null, { status: 200 }),
  });
}

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return wrapper;
}

beforeEach(() => {
  get.mockReset();
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('a trip on screen', () => {
  it('is read again by itself while its callout is armed', async () => {
    get.mockImplementation(() => trip('armed'));
    const { result } = renderHook(() => useTripLog('trip-1'), { wrapper: harness() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(get).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(61_000);
    });

    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
  });

  it('is not read again by itself when no callout is live', async () => {
    get.mockImplementation(() => trip('none'));
    const { result } = renderHook(() => useTripLog('trip-1'), { wrapper: harness() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5 * 60_000);
    });

    expect(get).toHaveBeenCalledTimes(1);
  });
});
