// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed and every call through it is recorded, because what is under test is
// which questions are put to the server and which are not.
const get = vi.fn();

vi.mock('./client.ts', () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
  ApiError: class ApiError extends Error {
    readonly status: number;
    constructor(status: number) {
      super(`refused (${status})`);
      this.status = status;
    }
  },
  lastReadETag: () => undefined,
}));

const { queryKeys, useCave, useTripReportMapSources } = await import('./hooks.ts');

const HELD = '11111111-1111-1111-1111-111111111111';
const ON_ITS_WAY = '22222222-2222-2222-2222-222222222222';
const GONE = '33333333-3333-3333-3333-333333333333';

const held = { id: HELD, name: 'Peștera Mare', geom: { type: 'Point', coordinates: [25.4, 45.5] } };
const arriving = { id: ON_ITS_WAY, name: 'Avenul din Grind', geom: null };

function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

function refuses(status: number) {
  return Promise.resolve({ error: { code: 'cave.not_found' }, response: new Response(null, { status }) });
}

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

beforeEach(() => {
  get.mockReset();
});

/**
 * The map in a downloaded write-up shows where the caves a trip names are, and where a cave is
 * for a given reader is something the server decides. So the map may only be drawn from answers
 * the page was already given for its own purposes — the cave reads it makes to print their names
 * — and a question of its own would be a second place that decision is taken.
 */
describe('what a write-up’s map is drawn from', () => {
  it('hands back an answer the page already holds without asking for it again', async () => {
    const { client, wrapper } = harness();
    client.setQueryData(queryKeys.cave(HELD), held);
    const { result } = renderHook(() => useTripReportMapSources(), { wrapper });

    const caves = await result.current.caves([HELD]);

    expect(caves.get(HELD)).toBe(held);
    expect(get).not.toHaveBeenCalled();
  });

  /**
   * A download pressed before the page has finished reading a cave waits for that read rather
   * than starting one of its own: one request, under the key the page reads the cave by, so the
   * page and the map hold the very same answer.
   */
  it('waits for a cave the page is still reading, by the page’s own key and route', async () => {
    let arrive: (value: unknown) => void = () => {};
    get.mockImplementation(() => new Promise((resolve) => { arrive = resolve; }));
    const { client, wrapper } = harness();
    const { result } = renderHook(
      () => ({ page: useCave(ON_ITS_WAY), sources: useTripReportMapSources() }),
      { wrapper },
    );

    const asked = result.current.sources.caves([ON_ITS_WAY]);
    arrive(await answers(arriving));
    const caves = await asked;

    expect(caves.get(ON_ITS_WAY)).toEqual(arriving);
    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith('/api/v1/caves/{id}', { params: { path: { id: ON_ITS_WAY } } });
    expect(client.getQueryData(queryKeys.cave(ON_ITS_WAY))).toEqual(arriving);
  });

  it('leaves out a cave whose read is refused, and keeps the ones that answered', async () => {
    get.mockImplementation((_path: string, init: { params: { path: { id: string } } }) =>
      init.params.path.id === GONE ? refuses(404) : answers(held),
    );
    const { wrapper } = harness();
    const { result } = renderHook(() => useTripReportMapSources(), { wrapper });

    const caves = await result.current.caves([HELD, GONE]);

    expect([...caves.keys()]).toEqual([HELD]);
  });

  /**
   * The only routes this ever touches are the two the page touches anyway: one cave by its id,
   * and the catalogue of map backgrounds. Nothing about a trip, and nothing that lists.
   */
  it('asks nothing of the server but a cave by its id and the catalogue of backgrounds', async () => {
    get.mockImplementation((path: string) =>
      path === '/api/v1/map-layers' ? answers([{ id: 1, name: 'Open map' }]) : answers(held),
    );
    const { wrapper } = harness();
    const { result } = renderHook(() => useTripReportMapSources(), { wrapper });

    await result.current.caves([HELD]);
    const catalog = await result.current.catalog();

    expect(catalog).toEqual([{ id: 1, name: 'Open map' }]);
    expect(get.mock.calls.map((call) => call[0])).toEqual(['/api/v1/caves/{id}', '/api/v1/map-layers']);
  });

  it('has no catalogue, rather than an error, when the catalogue cannot be read', async () => {
    get.mockImplementation(() => refuses(403));
    const { wrapper } = harness();
    const { result } = renderHook(() => useTripReportMapSources(), { wrapper });

    expect(await result.current.catalog()).toBeUndefined();
  });
});
