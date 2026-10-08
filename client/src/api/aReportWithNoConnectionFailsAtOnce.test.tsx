// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider, onlineManager } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is whether the report is
// asked for at all while the browser believes it has no connection, and what its caller is told.
const posts: { path: string; body: unknown }[] = [];
let answer: () => Promise<unknown>;

vi.mock('./client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client.ts')>()),
  api: {
    POST: (path: string, init: { body: unknown }) => {
      posts.push({ path, body: init.body });
      return answer();
    },
  },
}));

const { useRecordTrackingEvents } = await import('./hooks.ts');
const { ApiError } = await import('./client.ts');

function harness() {
  const client = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return renderHook(() => useRecordTrackingEvents(), { wrapper }).result;
}

const REPORT = {
  tripLogId: 'trip-1',
  caverIds: ['caver-1'],
  kind: 'entered' as const,
  clientKey: '3f2c7a54-9a51-4c1e-8f0e-0d5b3c0a7e11',
};

/** What became of a send, without waiting on it for longer than the test is prepared to. */
async function outcomeOf(send: Promise<unknown>) {
  let outcome: { landed: unknown } | { failed: unknown } | 'still waiting' = 'still waiting';
  send.then(
    (landed) => (outcome = { landed }),
    (failed: unknown) => (outcome = { failed }),
  );
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 20));
  });
  return outcome as { landed?: unknown; failed?: unknown } | 'still waiting';
}

beforeEach(() => {
  posts.length = 0;
});

afterEach(() => {
  onlineManager.setOnline(true);
});

/**
 * A write made while the browser knows it is offline is, by the query library's default, neither
 * sent nor failed: it waits for the connection, its caller is told nothing, a reload loses it, and
 * when it does go a report made "now" lands at the minute the signal came back. The report's caller
 * keeps an unanswered report under its key instead — which it can only do if it is told.
 */
describe('a tracking report sent with no connection', () => {
  it('fails at once with no answer, having asked, rather than waiting unseen for the signal', async () => {
    answer = () => Promise.reject(new TypeError('Failed to fetch'));
    const record = harness();
    onlineManager.setOnline(false);

    const outcome = await outcomeOf(record.current.mutateAsync({ ...REPORT }));

    expect(outcome).not.toBe('still waiting');
    expect((outcome as { failed: unknown }).failed).toBeInstanceOf(TypeError);
    // Not the transport's own error type, which is what says "the server answered".
    expect((outcome as { failed: unknown }).failed).not.toBeInstanceOf(ApiError);
    expect(posts).toHaveLength(1);
  });

  it('carries the key of its act to the server, so a repeat can be told from a second report', async () => {
    answer = () =>
      Promise.resolve({ data: [{ id: 'event-1' }], response: new Response(null, { status: 200 }) });
    const record = harness();

    const outcome = await outcomeOf(record.current.mutateAsync({ ...REPORT }));

    expect(outcome).toEqual({ landed: [{ id: 'event-1' }] });
    expect(posts).toEqual([
      {
        path: '/api/v1/trip-logs/{tripLogId}/tracking/events',
        body: {
          caverIds: ['caver-1'],
          kind: 'entered',
          stationName: null,
          depthM: null,
          teamId: null,
          note: null,
          recordedAt: null,
          clientKey: REPORT.clientKey,
        },
      },
    ]);
  });
});
