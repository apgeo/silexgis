// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is which questions a
// delete makes the page ask, not what the server answers them with.
const get = vi.fn();
const del = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    GET: (path: string, init?: unknown) => get(path, init),
    DELETE: (path: string, init?: unknown) => del(path, init),
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const hooks = await import('./hooks.ts');

/**
 * Deleting a record used to make the page ask for it again. The delete told the cache that
 * everything under the record's kind was out of date, the page showing the record was still
 * mounted at that moment — leaving it happens on the next line — and so the record's own
 * readings were fetched once more and answered 404: wasted requests, errors in the console, and
 * a reading left failed on a page about to go.
 *
 * Each case mounts a record's own reading beside a listing, deletes the record, lets the record's
 * page render once more, and then leaves it — the order a page goes through.
 */
function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

/** How often each path has been read. */
const readsOf = (path: string) => get.mock.calls.filter(([asked]) => asked === path).length;

beforeEach(() => {
  get.mockReset();
  del.mockReset();
  get.mockImplementation(() => answers({ id: 'gone', items: [], page: 1, pageSize: 20, totalItems: 0 }));
  del.mockImplementation(() => answers(undefined));
});

const cases = [
  {
    kind: 'a cave',
    own: ['/api/v1/caves/{id}', '/api/v1/caves/{id}/summary'],
    listing: '/api/v1/caves',
    useOwn: () => [hooks.useCave('gone'), hooks.useCaveSummary('gone')],
    useListing: () => hooks.useCaves({}),
    useDelete: () => hooks.useDeleteCave(),
    held: [hooks.queryKeys.cave('gone'), hooks.queryKeys.caveSummary('gone')],
  },
  {
    kind: 'a trip',
    own: ['/api/v1/trip-logs/{id}'],
    listing: '/api/v1/trip-logs',
    useOwn: () => [hooks.useTripLog('gone')],
    useListing: () => hooks.useTripLogs({}),
    useDelete: () => hooks.useDeleteTripLog(),
    held: [hooks.queryKeys.tripLog('gone')],
  },
  {
    kind: 'an event',
    own: ['/api/v1/events/{id}'],
    listing: '/api/v1/events',
    useOwn: () => [hooks.useEvent('gone')],
    useListing: () => hooks.useEvents({}),
    useDelete: () => hooks.useDeleteEvent(),
    held: [hooks.queryKeys.event('gone')],
  },
  {
    kind: 'a surface feature',
    own: ['/api/v1/features/{id}'],
    listing: '/api/v1/features',
    useOwn: () => [hooks.useFeature('gone')],
    useListing: () => hooks.useFeatures({}),
    useDelete: () => hooks.useDeleteFeature(),
    held: [hooks.queryKeys.feature('gone')],
  },
  {
    kind: 'a camp',
    own: ['/api/v1/expeditions/{id}'],
    listing: '/api/v1/expeditions',
    useOwn: () => [hooks.useExpedition('gone')],
    useListing: () => hooks.useExpeditions({}),
    useDelete: () => hooks.useDeleteExpedition(),
    held: [hooks.queryKeys.expedition('gone')],
  },
  {
    kind: 'an album',
    own: ['/api/v1/albums/{id}'],
    listing: '/api/v1/albums',
    useOwn: () => [hooks.useAlbum('gone')],
    useListing: () => hooks.useAlbums({}),
    useDelete: () => hooks.useDeleteAlbum(),
    held: [hooks.queryKeys.album('gone')],
  },
  {
    kind: 'a caver',
    own: ['/api/v1/cavers/{id}'],
    listing: '/api/v1/cavers',
    useOwn: () => [hooks.useCaver('gone')],
    useListing: () => hooks.useCavers(),
    useDelete: () => hooks.useDeleteCaver(),
    held: [] as (readonly unknown[])[],
  },
  {
    kind: 'an event and the rest of its run',
    own: ['/api/v1/events/{id}'],
    listing: '/api/v1/events',
    useOwn: () => [hooks.useEvent('gone')],
    useListing: () => hooks.useEvents({}),
    useDelete: () => hooks.useDeleteEventSeriesFollowing(),
    held: [hooks.queryKeys.event('gone')],
  },
  {
    kind: 'a cabinet',
    own: ['/api/v1/cabinets/{id}/documents'],
    listing: '/api/v1/cabinets',
    useOwn: () => [hooks.useCabinetDocuments('gone')],
    useListing: () => hooks.useCabinets(),
    useDelete: () => hooks.useDeleteCabinet(),
    held: [] as (readonly unknown[])[],
  },
] as const;

describe('deleting a record', () => {
  it.each(cases)('asks again for the listing and never for $kind that has just gone', async (c) => {
    const { client, wrapper } = harness();
    const recordPage = renderHook(() => c.useOwn(), { wrapper });
    const rest = renderHook(() => ({ listing: c.useListing(), remove: c.useDelete() }), { wrapper });

    await waitFor(() => {
      for (const path of c.own) expect(readsOf(path)).toBe(1);
      expect(readsOf(c.listing)).toBe(1);
    });

    await act(async () => {
      await rest.result.current.remove.mutateAsync('gone');
    });
    // The page showing the record renders once more after the delete has answered and before it
    // is left: leaving is a navigation, which the router treats as a transition, and the delete's
    // own change of state is rendered ahead of it. That render is a chance to ask again for a
    // reading that was merely thrown away — which is how the first attempt at this still asked in
    // a browser. Rendered in a step of its own, so it really happens before the page goes.
    recordPage.rerender();
    recordPage.unmount();

    // The listing is asked again: it has one row fewer.
    await waitFor(() => expect(readsOf(c.listing)).toBe(2));
    // The record is not: each of its readings was read exactly once, before it was deleted.
    for (const path of c.own) expect(readsOf(path)).toBe(1);
    // And once the page has let go, nothing is kept of it, so a later visit to its address
    // shows no ghost before its 404.
    for (const key of c.held) expect(client.getQueryData(key)).toBeUndefined();
    expect(
      client.getQueryCache().findAll({ predicate: (q) => q.queryKey.includes('gone') }),
    ).toHaveLength(0);
  });
});
