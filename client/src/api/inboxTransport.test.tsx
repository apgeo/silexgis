// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * The header's unread count is kept current by whichever transport the installation was
 * configured for, and that choice reaches this client from the server rather than being compiled
 * into it. What is worth proving is the case where an operator has chosen something other than the
 * default: a client that ignores the setting still passes every test written around `poll`.
 */

const get = vi.fn();
vi.mock('./client.ts', async () => {
  const actual = await vi.importActual<typeof import('./client.ts')>('./client.ts');
  return { ...actual, api: { GET: get } };
});

const { useUnreadNotificationCount } = await import('./hooks.ts');
const { inboxPollIntervalMs } = await import('../notifications/transport.ts');

/** An answer in the shape openapi-fetch returns, for the two paths this hook asks for. */
function serving(badgeTransport: string | undefined) {
  get.mockImplementation((path: string) => {
    if (path === '/api/v1/notifications/config') {
      return badgeTransport === undefined
        ? Promise.reject(new Error('no answer'))
        : Promise.resolve({ data: { badgeTransport }, response: new Response() });
    }
    return Promise.resolve({ data: { unread: 3 }, response: new Response() });
  });
}

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

/** How many times the count itself has been asked for. */
function timesCounted() {
  return get.mock.calls.filter((call) => call[0] === '/api/v1/notifications/unread-count').length;
}

/** Mounts the header's count and lets its first requests settle. */
async function watching() {
  const { wrapper } = harness();
  renderHook(() => useUnreadNotificationCount(), { wrapper });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
}

/** How many further times the count was asked for over a stretch of somebody watching a page. */
async function askedAgainOver(ms: number) {
  const before = timesCounted();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
  return timesCounted() - before;
}

beforeEach(() => {
  get.mockReset();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the transport the unread count follows', () => {
  it('asks again on a timer when the installation has chosen polling', async () => {
    serving('poll');
    await watching();

    expect(await askedAgainOver(inboxPollIntervalMs * 2 + 1000)).toBeGreaterThanOrEqual(2);
  });

  it('runs no timer when the installation has chosen a stream to be pushed on', async () => {
    // The non-default value, and the reason the setting exists: a page told that the server will
    // push must not also be asking every minute. A client that ignored the setting would pass
    // every test written around the default and fail only here.
    serving('sse');
    await watching();

    expect(await askedAgainOver(inboxPollIntervalMs * 5)).toBe(0);
  });

  it('polls when it is never told, because a count that stops moving reads as a bug', async () => {
    // The answer this client falls back to is the transport that needs nothing else in place.
    serving(undefined);
    await watching();

    expect(await askedAgainOver(inboxPollIntervalMs * 2 + 1000)).toBeGreaterThanOrEqual(2);
  });
});
