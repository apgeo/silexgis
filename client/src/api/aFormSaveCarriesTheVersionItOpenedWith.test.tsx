// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { TripLogWrite } from './hooks.ts';

// The network is what is stubbed here, and nothing above it: the question is which version
// reaches the server, and that is decided between the hook and the transport's own replay of
// versions — the two things a stub of either would take out of the test.
const sent: Request[] = [];
let version = 1;
// How long a read takes to answer, for the cases about what the cache holds while one is on its way.
let readTakesMs = 0;

vi.stubGlobal(
  'fetch',
  vi.fn(async (input: Request) => {
    sent.push(input);
    const answeredAt = version;
    if (input.method === 'GET' && readTakesMs > 0) {
      await new Promise((resolve) => setTimeout(resolve, readTakesMs));
    }
    return new Response(JSON.stringify({ id: 'trip-1', title: 'A trip', revision: answeredAt }), {
      status: 200,
      headers: { 'Content-Type': 'application/json', ETag: `"${answeredAt}"` },
    });
  }),
);

// The application asks by path and the browser supplies the origin; outside a browser a request
// cannot be built from a path alone, so the origin is supplied here.
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

const { api, lastReadETag } = await import('./client.ts');
const { versionReadWith } = await import('./readingVersion.ts');
const { fetchFeature } = await import('./hooks/features.ts');
const {
  useArrangeTripCallout,
  useCave,
  useFeature,
  useDeleteChecklist,
  useEditEventSeriesFollowing,
  useReadChecklistForChange,
  useReadTripLogAgain,
  useUpdateChecklist,
  useUpdateCave,
  useUpdateEvent,
  useUpdateExpedition,
  useUpdateFeature,
  useUpdateTripLog,
} = await import('./hooks.ts');

const path = '/api/v1/trip-logs/trip-1';
const read = () => api.GET('/api/v1/trip-logs/{id}', { params: { path: { id: 'trip-1' } } });

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const lastPut = () => sent.filter((request) => request.method === 'PUT').at(-1);

/**
 * Two people open a trip's form. The first saves a change to the roster, which moves the trip's
 * version. The second switches away and comes back: the page under their form reads the trip
 * again and the newer version is on file. Their save still holds the roster as it was — and it
 * used to carry the newer version, so the server found nothing stale and the first person's
 * change was written back to nothing. The save has to carry the version the form opened with.
 */
describe('a save from a form opened before the trip was read again', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 1;
  });

  it('carries the version the form opened with, so the server can refuse it', async () => {
    await read();
    const openedWith = lastReadETag(path);
    expect(openedWith).toBe('"1"');

    version = 2;
    await read();
    expect(lastReadETag(path)).toBe('"2"');

    const { result } = renderHook(() => useUpdateTripLog(), { wrapper });
    await act(() =>
      result.current.mutateAsync({ id: 'trip-1', body: {} as TripLogWrite, ifMatch: openedWith }),
    );

    expect(lastPut()?.headers.get('If-Match')).toBe('"1"');
  });

  it('carries the newest version read when the caller names none', async () => {
    await read();
    version = 2;
    await read();

    const { result } = renderHook(() => useUpdateTripLog(), { wrapper });
    await act(() => result.current.mutateAsync({ id: 'trip-1', body: {} as TripLogWrite }));

    expect(lastPut()?.headers.get('If-Match')).toBe('"2"');
  });
});

/**
 * Every other form that is filled once and saved later names the version it opened with in the
 * same way, and the version it names is the one that reaches the server — whatever a later read
 * of the same record has filed since.
 */
describe('each form-backed write sends the version it is handed', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 2;
  });

  const cases: [string, string, () => { mutateAsync: (vars: any) => Promise<unknown> }, object][] = [
    ['a camp', '/api/v1/expeditions/x-1', useUpdateExpedition, { id: 'x-1', body: {} }],
    ['an event', '/api/v1/events/e-1', useUpdateEvent, { id: 'e-1', body: {} }],
    ['a feature', '/api/v1/features/f-1', useUpdateFeature, { id: 'f-1', body: {} }],
    ['a cave', '/api/v1/caves/c-1', () => useUpdateCave('c-1'), { body: {} }],
    ['a callout', '/api/v1/trip-logs/trip-1', useArrangeTripCallout, { id: 'trip-1' }],
    ['a checklist', '/api/v1/checklists/l-1', useUpdateChecklist, { id: 'l-1', body: {} }],
    ['a checklist deleted', '/api/v1/checklists/l-1', useDeleteChecklist, { id: 'l-1' }],
  ];

  it.each(cases)('%s', async (_name, resource, hook, variables) => {
    // A read files the newer version under the record's own path.
    await api.GET(resource as never, {} as never);
    expect(lastReadETag(resource)).toBe('"2"');

    const { result } = renderHook(() => hook(), { wrapper });
    await act(() => result.current.mutateAsync({ ...variables, ifMatch: '"1"' }));

    const write = sent.filter((request) => request.method !== 'GET').at(-1);
    expect(write?.headers.get('If-Match')).toBe('"1"');
  });

  it('the rest of a run of events', async () => {
    const { result } = renderHook(() => useEditEventSeriesFollowing(), { wrapper });
    await act(() =>
      result.current.mutateAsync({ id: 'e-1', body: {} as never, runVersion: '"run-1"' }),
    );

    expect(lastPut()?.headers.get('If-Match')).toBe('"run-1"');
  });
});

/**
 * The checklist page's listing carries no version per list, so the page reads the one list it is
 * about to change and sends the version of that answer. The version is the answer's own: a read
 * of the same list landing afterwards files a newer one, and the change must still carry the
 * version its lines came from.
 */
describe('a checklist read for a change', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 1;
  });

  it('hands over the version of its own answer, and the rewrite carries it', async () => {
    const { result: read } = renderHook(() => useReadChecklistForChange(), { wrapper });
    const held = await act(() => read.current('l-1'));
    expect(held.version).toBe('"1"');

    version = 2;
    await api.GET('/api/v1/checklists/{id}', { params: { path: { id: 'l-1' } } });
    expect(lastReadETag('/api/v1/checklists/l-1')).toBe('"2"');

    const { result } = renderHook(() => useUpdateChecklist(), { wrapper });
    await act(() =>
      result.current.mutateAsync({ id: 'l-1', body: {} as never, ifMatch: held.version }),
    );
    expect(lastPut()?.headers.get('If-Match')).toBe('"1"');
  });
});

/**
 * The newest version filed for a record can be ahead of the copy the cache holds: a read made
 * outside the cache files one, and so does a save before its re-read lands. A form is filled from
 * the cached copy, so the version it saves under has to be that copy's — kept with the copy rather
 * than looked up by address.
 */
describe('the version of a cached copy', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 1;
    readTakesMs = 0;
  });

  it('stays that of the read that supplied it when a newer one is filed elsewhere', async () => {
    const { result } = renderHook(() => useFeature('f-1'), { wrapper });
    await waitFor(() => expect(result.current.data).toBeDefined());
    expect(versionReadWith(result.current.data)).toBe('"1"');

    version = 2;
    await fetchFeature('f-1');
    expect(lastReadETag('/api/v1/features/f-1')).toBe('"2"');
    expect(versionReadWith(result.current.data)).toBe('"1"');
  });

  it.each([
    ['a cave', ['caves', 'detail', 'c-1'], () => useCave('c-1'), () => useUpdateCave('c-1'), { body: {} }],
    ['a feature', ['features', 'detail', 'f-1'], () => useFeature('f-1'), useUpdateFeature, { id: 'f-1', body: {} }],
  ] as const)(
    'of %s is the saved one by the time its save has finished',
    async (_name, key, useRead, useSave, variables) => {
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => ({ read: useRead(), save: useSave() }), {
        wrapper: ({ children }: { children: ReactNode }) => (
          <QueryClientProvider client={client}>{children}</QueryClientProvider>
        ),
      });
      await waitFor(() => expect(result.current.read.data).toBeDefined());

      version = 2;
      readTakesMs = 30;
      const save = result.current.save as { mutateAsync: (v: unknown) => Promise<unknown> };
      // What the cache holds at the very moment the save says it is finished: a form opened on the
      // record then is filled from this copy.
      const held = await save.mutateAsync(variables).then(() => client.getQueryData(key));

      expect((held as { revision?: number }).revision).toBe(2);
      expect(versionReadWith(held)).toBe('"2"');
    },
  );
});

/**
 * A callout save refused as stale is followed by a read of the trip, and the dialog is refilled
 * from that read: the copy the page held is what the refused save came from.
 */
describe('a trip read again after a refusal', () => {
  beforeEach(() => {
    sent.length = 0;
    version = 1;
    readTakesMs = 0;
  });

  it('asks the server anew and hands back what it answered with its version', async () => {
    await read();
    version = 2;

    const { result } = renderHook(() => useReadTripLogAgain(), { wrapper });
    const again = await act(() => result.current('trip-1'));

    expect(sent.filter((request) => request.method === 'GET')).toHaveLength(2);
    expect((again.trip as unknown as { revision: number }).revision).toBe(2);
    expect(again.version).toBe('"2"');
  });
});
