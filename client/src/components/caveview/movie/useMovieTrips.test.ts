// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';

const MODEL = 'model-1';
const ARMED = '2026-09-12T08:00:00Z';
const CLOSED = '2026-09-12T12:00:00Z';
const OPENED = Date.parse('2026-09-13T08:00:00Z');

interface Replay {
  tripLogId: string;
  title: string;
  participants: { caverId: string; name: string }[];
  tracking: unknown;
  events: unknown[];
  eventsComplete: boolean;
}
/** The one read as the hook sees it: what it holds, and how the last asking went. */
const read = vi.hoisted(() => ({
  /** The trips of the answer held, or null while nothing is held. */
  held: null as Replay[] | null,
  /** True when what is held is the answer about the trips chosen before. */
  placeholder: false,
  error: null as unknown,
  asked: [] as { surveyModelId: string; tripLogIds: readonly string[]; polled: boolean }[],
}));
vi.mock('../../../api/hooks.ts', () => ({
  useTrackedTripReplays: (surveyModelId: string, tripLogIds: readonly string[], polled: boolean) => {
    read.asked.push({ surveyModelId, tripLogIds, polled });
    return {
      data: read.held ?? undefined,
      isPlaceholderData: read.placeholder,
      error: read.error,
      dataUpdatedAt: 0,
    };
  },
  // These tests are of finished trips, which are never read again.
  useRereadTrackedTripReplays: () => () => Promise.reject(new Error('nothing is read again here')),
}));

const { useMovieTrips } = await import('./useMovieTrips.ts');

function arrive(
  tripLogId: string,
  title: string,
  options: { armed?: boolean; names?: Record<string, string>; complete?: boolean } = {},
) {
  const armed = options.armed ?? true;
  read.held = [
    ...(read.held ?? []),
    {
      tripLogId,
      title,
      participants: Object.entries(options.names ?? {}).map(([caverId, name]) => ({ caverId, name })),
      tracking: { armedAt: armed ? ARMED : null, closedAt: armed ? CLOSED : null, participants: [] },
      events: [
        { recordedAt: '2026-09-12T10:00:00Z', surveyModelId: MODEL },
        { recordedAt: '2026-09-12T09:00:00Z', surveyModelId: MODEL },
      ],
      eventsComplete: options.complete ?? true,
    },
  ];
}

beforeEach(() => {
  read.held = null;
  read.placeholder = false;
  read.error = null;
  read.asked = [];
});

describe('useMovieTrips', () => {
  it('asks for the chosen trips together, on the model, in the order chosen', () => {
    arrive('trip-a', 'Alpha');
    arrive('trip-b', 'Bravo');

    renderHook(() => useMovieTrips(MODEL, ['trip-b', 'trip-a'], OPENED));

    expect(read.asked.at(-1)).toEqual({ surveyModelId: MODEL, tripLogIds: ['trip-b', 'trip-a'], polled: true });
  });

  it('hands on the chosen trips in the order they were chosen, each with its roster names', () => {
    // The answer comes in the order asked; the movie's order is the choosing, whatever the answer's.
    arrive('trip-a', 'Alpha', { names: { 'caver-1': 'Ana Pop' } });
    arrive('trip-b', 'Bravo');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-b', 'trip-a'], OPENED));

    expect(result.current.loading).toBe(false);
    expect(result.current.trips.map((trip) => trip.title)).toEqual(['Bravo', 'Alpha']);
    expect(result.current.spans.map((span) => span.tripLogId)).toEqual(['trip-b', 'trip-a']);
    expect(result.current.trips[1].nameOf('caver-1')).toBe('Ana Pop');
    // Somebody not on the roster is named as such, never by an id.
    expect(result.current.trips[1].nameOf('caver-9')).toBe('Somebody not on the roster');
  });

  it('says it is still reading while nothing has arrived, and fails nothing', () => {
    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));

    expect(result.current.loading).toBe(true);
    expect(result.current.trips).toEqual([]);
    expect(result.current.failed).toEqual([]);
  });

  it('keeps the trips of the previous answer while one more is on its way, and waits for that one', () => {
    arrive('trip-a', 'Alpha');
    read.placeholder = true;

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(result.current.loading).toBe(true);
    // A trip a placeholder does not carry has not been refused: it has not been asked about yet.
    expect(result.current.failed).toEqual([]);
  });

  it('fails a chosen trip the answer does not carry, as a trip that could not be read', () => {
    arrive('trip-a', 'Alpha');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.loading).toBe(false);
    expect(result.current.failed).toEqual(['trip-b']);
    // Not as an unreadable log, and not as a trip with nothing in it.
    expect(result.current.logFailed).toEqual([]);
    expect(result.current.empty).toEqual([]);
    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
  });

  it('names a chosen trip with nothing to replay instead of handing it on', () => {
    arrive('trip-a', 'Alpha');
    arrive('trip-b', 'Never armed', { armed: false });

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(result.current.empty).toEqual(['trip-b']);
  });

  it('fails a trip whose log did not arrive whole, rather than replaying part of it', () => {
    arrive('trip-a', 'Alpha');
    arrive('trip-b', 'Bravo', { complete: false });

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.loading).toBe(false);
    expect(result.current.failed).toEqual(['trip-b']);
    expect(result.current.logFailed).toEqual(['trip-b']);
    // The failed trip is not handed on as if it were a trip with nothing in it.
    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(result.current.empty).toEqual([]);
  });

  it('keeps the trips when a re-read fails while the earlier answer is held, and fails them all with nothing held', () => {
    arrive('trip-a', 'Alpha');
    read.error = new Error('unreachable');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));

    expect(result.current.error).toBeNull();
    expect(result.current.failed).toEqual([]);
    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);

    // With nothing held, the same failure fails the trip — but not as an unreadable log.
    read.held = null;
    const again = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));
    expect(again.result.current.error).toBeInstanceOf(Error);
    expect(again.result.current.loading).toBe(false);
    expect(again.result.current.failed).toEqual(['trip-a']);
    expect(again.result.current.logFailed).toEqual([]);
  });

  it('answers the same trips across a re-read that changed nothing', () => {
    arrive('trip-a', 'Alpha');
    const { result, rerender } = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));
    const before = result.current.trips;

    rerender();

    expect(result.current.trips).toBe(before);
  });

  it('stops asking again while it is paused', () => {
    arrive('trip-a', 'Alpha');

    renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED, true));

    expect(read.asked.at(-1)?.polled).toBe(false);
  });
});
