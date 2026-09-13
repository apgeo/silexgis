// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is when this browser asks
// again, not what comes back.
const get = vi.fn(() =>
  Promise.resolve({
    data: { items: [], page: 1, pageSize: 20, totalItems: 0 },
    response: new Response(null, { status: 200 }),
  }),
);

vi.mock('./client.ts', () => ({
  api: { GET: () => get() },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { queryKeys, useTripTrackingEvents } = await import('./hooks.ts');

/**
 * The tracking tab draws two views of the same arriving reports, one above the other: who is
 * where, folded from the log, and the log itself. They are two reads, and two coordinators with
 * the tab open is the case this feature was built for — one of them writing down what the radio
 * said has to reach the other's screen without anybody pressing anything.
 *
 * Polling one and not the other is worse than polling neither. The upper table advances every
 * half minute while the lower one stays as it was when the tab was opened, so one screen holds two
 * tables disagreeing about the same events, with nothing saying which is older. The log is also
 * the only surface a correction acts on: a wrong report is taken off it and said again. Choosing
 * which report to delete out of a list that stopped following the server minutes ago is how the
 * correction lands on the wrong row.
 */
function harness(trackingState: string | null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (trackingState !== null) {
    client.setQueryData(queryKeys.tripTracking('trip-1'), { state: trackingState });
  }
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper, client };
}

/** Lets the query settle and then stands still for as long as the caller says. */
async function waitOut(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

beforeEach(() => {
  get.mockClear();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("a trip's reports", () => {
  it('are asked for again while the watch is armed', async () => {
    const { wrapper } = harness('armed');
    renderHook(() => useTripTrackingEvents('trip-1', { pageSize: 20 }), { wrapper });

    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(1);

    await waitOut(30_000);
    expect(get).toHaveBeenCalledTimes(2);
  });

  // The other half of the same rule: a watch nobody is keeping costs no requests at all, exactly
  // as the folded state costs none. Reports cannot land on a closed watch, so there is nothing to
  // find out.
  it('are left alone once the watch is closed', async () => {
    const { wrapper } = harness('closed');
    renderHook(() => useTripTrackingEvents('trip-1', { pageSize: 20 }), { wrapper });

    await waitOut(1);
    expect(get).toHaveBeenCalledTimes(1);

    await waitOut(120_000);
    expect(get).toHaveBeenCalledTimes(1);
  });
});
