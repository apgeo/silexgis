// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';

/**
 * A movie of a trip still under way, on the real reads and a real cache.
 *
 * What is being held to here is something a mocked hook cannot show: that a report somebody else
 * recorded after this browser first read the log — which nothing in this browser did, and nothing
 * told it about — does reach the movie. The server below is three answers the test can change; the
 * count of how often the log was asked for is the proof that it was asked again at all.
 */
const MODEL = 'model-1';
const TRIP = 'trip-live';
const ARMED = '2026-09-12T08:00:00Z';

interface Report {
  id: string;
  recordedAt: string;
  surveyModelId: string;
}
const server = vi.hoisted(() => ({
  /** The trip's log as the server holds it, newest first. */
  log: [] as Report[],
  closedAt: null as string | null,
  logAsked: 0,
  /** While true the log cannot be read. */
  logRefused: false,
}));

vi.mock('../../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../../api/client.ts')>()),
  api: {
    GET: (path: string) => {
      const ok = new Response(null, { status: 200 });
      if (path === '/api/v1/trip-logs/{tripLogId}/tracking') {
        return Promise.resolve({
          data: {
            state: server.closedAt === null ? 'armed' : 'closed',
            armedAt: ARMED,
            closedAt: server.closedAt,
            participants: [],
          },
          response: ok,
        });
      }
      if (path === '/api/v1/trip-logs/{tripLogId}/tracking/events') {
        server.logAsked += 1;
        if (server.logRefused) {
          return Promise.resolve({ error: { code: 'unavailable' }, response: new Response(null, { status: 503 }) });
        }
        return Promise.resolve({
          data: { items: [...server.log], totalItems: server.log.length },
          response: ok,
        });
      }
      if (path === '/api/v1/trip-logs/{id}') {
        return Promise.resolve({ data: { title: 'Alpha', participants: [] }, response: ok });
      }
      throw new Error(`a read this test does not answer: ${path}`);
    },
  },
}));

const { queryKeys } = await import('../../../api/hooks.ts');
const { useMovieTrips } = await import('./useMovieTrips.ts');

const report = (id: string, recordedAt: string): Report => ({ id, recordedAt, surveyModelId: MODEL });

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

/** The watch answers again, as it does every half-minute while a trip is under way. */
function watchAnswersAgain(client: QueryClient) {
  return act(() => client.refetchQueries({ queryKey: queryKeys.tripTracking(TRIP) }));
}

beforeEach(() => {
  server.log = [report('r2', '2026-09-12T09:00:00Z'), report('r1', '2026-09-12T08:30:00Z')];
  server.closedAt = null;
  server.logAsked = 0;
  server.logRefused = false;
});

describe('a movie of a trip still under way', () => {
  it('reads the log again when the watch answers, so a report recorded elsewhere is counted and is in the movie', async () => {
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    expect(result.current.newReports.get(TRIP)).toBe(0);
    const askedBefore = server.logAsked;

    // Another coordinator records a report from their own browser.
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    await watchAnswersAgain(client);

    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
    expect(server.logAsked).toBeGreaterThan(askedBefore);
    expect(result.current.trips[0].events.map((event) => event.id)).toEqual(['r3', 'r2', 'r1']);
    expect(result.current.spans[0].moments).toContain(Date.parse('2026-09-12T09:30:00Z'));
  });

  it('leaves the log alone while it is paused, and catches up when it is let go', async () => {
    const { client, wrapper } = harness();
    const { result, rerender } = renderHook(({ paused }) => useMovieTrips(MODEL, [TRIP], Date.now(), paused), {
      wrapper,
      initialProps: { paused: false },
    });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    rerender({ paused: true });
    const askedBefore = server.logAsked;
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    await watchAnswersAgain(client);
    expect(server.logAsked).toBe(askedBefore);
    expect(result.current.newReports.get(TRIP)).toBe(0);

    rerender({ paused: false });
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
  });

  it('hands an export the log as it is on the server at that moment, without waiting for the watch', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    server.log = [report('r4', '2026-09-12T10:00:00Z'), report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    const askedBefore = server.logAsked;
    let fresh: Awaited<ReturnType<typeof result.current.rereadLive>> = [];
    await act(async () => {
      fresh = await result.current.rereadLive();
    });

    expect(server.logAsked).toBe(askedBefore + 1);
    expect(fresh.map((trip) => trip.events.map((event) => event.id))).toEqual([['r4', 'r3', 'r2', 'r1']]);
    // What the export read is what the dialog shows from then on.
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(2));
  });

  it('refuses the export’s read when the log cannot be read, and keeps the movie it had', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    server.logRefused = true;
    let refused = false;
    await act(async () => {
      await result.current.rereadLive().catch(() => {
        refused = true;
      });
    });

    expect(refused).toBe(true);
    // The log held from before is still a whole log, so the trip is not failed for the preview.
    expect(result.current.logFailed).toEqual([]);
    expect(result.current.trips).toHaveLength(1);
  });

  it('counts new reports from the first log read after opening, not from a log held from before', async () => {
    const { client, wrapper } = harness();
    // The trip's replay read the log an hour ago, when it had one report; two more were recorded
    // elsewhere before this dialog was opened.
    client.setQueryData(queryKeys.tripTrackingEventLog(TRIP), [report('r1', '2026-09-12T08:30:00Z')], {
      updatedAt: Date.now() - 3_600_000,
    });
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];

    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });

    await waitFor(() => expect(result.current.trips[0]?.events).toHaveLength(3));
    // Nothing has arrived since opening, whatever the held log lacked.
    expect(result.current.newReports.get(TRIP)).toBe(0);

    // And what does arrive afterwards is counted from there.
    server.log = [report('r4', '2026-09-12T10:00:00Z'), ...server.log];
    await watchAnswersAgain(client);
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
  });

  it('asks nothing more of a finished trip, however often its dialog is drawn', async () => {
    server.closedAt = '2026-09-12T12:00:00Z';
    const { wrapper } = harness();
    const { result, rerender } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    const askedBefore = server.logAsked;

    rerender();
    const fresh = await result.current.rereadLive();

    expect(server.logAsked).toBe(askedBefore);
    expect(fresh).toEqual(result.current.trips);
    expect(result.current.newReports.size).toBe(0);
  });
});
