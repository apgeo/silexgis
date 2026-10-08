// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';

/**
 * A movie of a trip still under way, on the real read and a real cache.
 *
 * What is being held to here is something a mocked hook cannot show: that a report somebody else
 * recorded after this browser first read the log — which nothing in this browser did, and nothing
 * told it about — does reach the movie. The server below is one answer the test can change; the
 * count of how often it was asked is the proof that it was asked again at all.
 */
const MODEL = 'model-1';
const TRIP = 'trip-live';
const ARMED = '2026-09-12T08:00:00Z';

interface Report {
  id: string;
  recordedAt: string;
  surveyModelId: string;
  /** Null on a note about the cave, which is about nobody; left out here on a report about somebody. */
  caverId?: string | null;
}
const server = vi.hoisted(() => ({
  /** The trip's log as the server holds it, newest first. */
  log: [] as Report[],
  closedAt: null as string | null,
  /** How often the trips were asked for, and which trips each time. */
  asked: [] as string[][],
  /** While true the trips cannot be read. */
  refused: false,
  /** While true the log is longer than one answer carries. */
  cut: false,
}));

vi.mock('../../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../../api/client.ts')>()),
  api: {
    GET: (path: string, init: { params: { path: { surveyModelId: string }; query: { tripLogIds: string[] } } }) => {
      if (path !== '/api/v1/survey-models/{surveyModelId}/tracked-trips/replay') {
        // The movie's trips come from one read; a read of one trip's watch, log or roster is a
        // return to the three-reads-a-trip shape and fails here.
        throw new Error(`a read this test does not answer: ${path}`);
      }
      server.asked.push([...init.params.query.tripLogIds]);
      if (server.refused) {
        return Promise.resolve({ error: { code: 'unavailable' }, response: new Response(null, { status: 503 }) });
      }
      return Promise.resolve({
        data: init.params.query.tripLogIds.map((tripLogId) => ({
          tripLogId,
          title: 'Alpha',
          participants: [],
          tracking: {
            state: server.closedAt === null ? 'armed' : 'closed',
            armedAt: ARMED,
            closedAt: server.closedAt,
            participants: [],
          },
          events: [...server.log],
          eventsComplete: !server.cut,
        })),
        response: new Response(null, { status: 200 }),
      });
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

/** Where the movie's trips are held: under the model's list of tracked trips. */
const replayKey = [...queryKeys.surveyModelTrackedTrips(MODEL), 'replay', [TRIP]];

/** The trips are asked for again, as they are every half-minute while one of them is under way. */
function tripsAnswerAgain(client: QueryClient) {
  return act(() => client.refetchQueries({ queryKey: replayKey }));
}

beforeEach(() => {
  server.log = [report('r2', '2026-09-12T09:00:00Z'), report('r1', '2026-09-12T08:30:00Z')];
  server.closedAt = null;
  server.asked = [];
  server.refused = false;
  server.cut = false;
});

describe('a movie of a trip still under way', () => {
  it('reads every chosen trip in one asking', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP, 'trip-other'], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(2));

    expect(server.asked).toEqual([[TRIP, 'trip-other']]);
  });

  it('keeps a trip under way fresh by asking again, so a report recorded elsewhere is counted and is in the movie', async () => {
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    expect(result.current.newReports.get(TRIP)).toBe(0);
    // Asked again by itself while the trip is under way: the read carries the interval.
    const held = client.getQueryCache().find({ queryKey: replayKey })!;
    const interval = held.observers[0].options.refetchInterval;
    expect(typeof interval === 'function' ? interval(held) : interval).toBe(30_000);
    const askedBefore = server.asked.length;

    // Another coordinator records a report from their own browser.
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    await tripsAnswerAgain(client);

    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
    expect(server.asked.length).toBeGreaterThan(askedBefore);
    expect(result.current.trips[0].events.map((event) => event.id)).toEqual(['r3', 'r2', 'r1']);
    expect(result.current.spans[0].moments).toContain(Date.parse('2026-09-12T09:30:00Z'));
  });

  it('leaves the movie alone while it is paused, and catches up when it is let go', async () => {
    const { client, wrapper } = harness();
    // One end for the whole test, as the dialog fixes it once: the list is the same list only then.
    const opened = Date.now();
    const { result, rerender } = renderHook(({ paused }) => useMovieTrips(MODEL, [TRIP], opened, paused), {
      wrapper,
      initialProps: { paused: false },
    });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    const before = result.current.trips;

    rerender({ paused: true });
    // Nothing asks again by itself while paused …
    const held = client.getQueryCache().find({ queryKey: replayKey })!;
    const interval = held.observers[0].options.refetchInterval;
    expect(typeof interval === 'function' ? interval(held) : interval).toBe(false);
    // … and an answer that arrives all the same — something recorded in this browser asks for one —
    // is not folded into the movie being made.
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    await tripsAnswerAgain(client);
    expect(client.getQueryData<{ events: Report[] }[]>(replayKey)?.[0].events).toHaveLength(3);
    expect(result.current.trips).toBe(before);
    expect(result.current.newReports.get(TRIP)).toBe(0);

    rerender({ paused: false });
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
    expect(result.current.trips[0].events).toHaveLength(3);
  });

  it('hands an export the log as it is on the server at that moment, without waiting for the watch', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    server.log = [report('r4', '2026-09-12T10:00:00Z'), report('r3', '2026-09-12T09:30:00Z'), ...server.log];
    const askedBefore = server.asked.length;
    let fresh: Awaited<ReturnType<typeof result.current.rereadLive>> = [];
    await act(async () => {
      fresh = await result.current.rereadLive();
    });

    expect(server.asked.length).toBe(askedBefore + 1);
    expect(fresh.map((trip) => trip.events.map((event) => event.id))).toEqual([['r4', 'r3', 'r2', 'r1']]);
    // What the export read is what the dialog shows from then on.
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(2));
  });

  it('leaves a note about the cave out of the movie, when the log is first read and when an export reads it again', async () => {
    // About nobody, and for whoever may read the trip: the log's read carries it, a movie does not.
    const caveNote = (id: string, recordedAt: string): Report => ({ ...report(id, recordedAt), caverId: null });
    server.log = [caveNote('n1', '2026-09-12T09:10:00Z'), ...server.log];
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    expect(result.current.trips[0].events.map((event) => event.id)).toEqual(['r2', 'r1']);

    server.log = [caveNote('n2', '2026-09-12T10:10:00Z'), report('r3', '2026-09-12T10:00:00Z'), ...server.log];
    let fresh: Awaited<ReturnType<typeof result.current.rereadLive>> = [];
    await act(async () => {
      fresh = await result.current.rereadLive();
    });

    expect(fresh.map((trip) => trip.events.map((event) => event.id))).toEqual([['r3', 'r2', 'r1']]);
    // One report arrived since the dialog opened. The second note is word about the cave, not about
    // anybody in the party, and is not counted among them.
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
  });

  it('refuses the export’s read when the log cannot be read, and keeps the movie it had', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    server.refused = true;
    let refused = false;
    await act(async () => {
      await result.current.rereadLive().catch(() => {
        refused = true;
      });
    });

    expect(refused).toBe(true);
    // The log held from before is still a whole log, so the trip is not failed for the preview.
    expect(result.current.logFailed).toEqual([]);
    expect(result.current.failed).toEqual([]);
    expect(result.current.trips).toHaveLength(1);
  });

  it('refuses the export’s read when the log no longer arrives whole', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));

    server.cut = true;
    let refused = false;
    await act(async () => {
      await result.current.rereadLive().catch(() => {
        refused = true;
      });
    });

    expect(refused).toBe(true);
    // And the dialog, which now holds that answer, says which trip's log it is.
    await waitFor(() => expect(result.current.logFailed).toEqual([TRIP]));
    expect(result.current.trips).toEqual([]);
  });

  it('counts new reports from the first log read after opening, not from a log held from before', async () => {
    const { client, wrapper } = harness();
    // An earlier opening of the dialog read the trip an hour ago, when its log had one report; two
    // more were recorded elsewhere before this opening.
    client.setQueryData(
      replayKey,
      [
        {
          tripLogId: TRIP,
          title: 'Alpha',
          participants: [],
          tracking: { state: 'armed', armedAt: ARMED, closedAt: null, participants: [] },
          events: [report('r1', '2026-09-12T08:30:00Z')],
          eventsComplete: true,
        },
      ],
      { updatedAt: Date.now() - 3_600_000 },
    );
    server.log = [report('r3', '2026-09-12T09:30:00Z'), ...server.log];

    const { result } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });

    await waitFor(() => expect(result.current.trips[0]?.events).toHaveLength(3));
    // Nothing has arrived since opening, whatever the held log lacked.
    expect(result.current.newReports.get(TRIP)).toBe(0);

    // And what does arrive afterwards is counted from there.
    server.log = [report('r4', '2026-09-12T10:00:00Z'), ...server.log];
    await tripsAnswerAgain(client);
    await waitFor(() => expect(result.current.newReports.get(TRIP)).toBe(1));
  });

  it('asks nothing more of a finished trip, however often its dialog is drawn', async () => {
    server.closedAt = '2026-09-12T12:00:00Z';
    const { client, wrapper } = harness();
    const { result, rerender } = renderHook(() => useMovieTrips(MODEL, [TRIP], Date.now()), { wrapper });
    await waitFor(() => expect(result.current.trips).toHaveLength(1));
    const held = client.getQueryCache().find({ queryKey: replayKey })!;
    const interval = held.observers[0].options.refetchInterval;
    expect(typeof interval === 'function' ? interval(held) : interval).toBe(false);
    const askedBefore = server.asked.length;

    rerender();
    const fresh = await result.current.rereadLive();

    expect(server.asked.length).toBe(askedBefore);
    expect(fresh).toEqual(result.current.trips);
    expect(result.current.newReports.size).toBe(0);
  });
});
