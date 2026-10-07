// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';

const MODEL = 'model-1';
const ARMED = '2026-09-12T08:00:00Z';
const CLOSED = '2026-09-12T12:00:00Z';
const OPENED = Date.parse('2026-09-13T08:00:00Z');

type Answer = { data: unknown; isPending: boolean; error: unknown; isFetching: boolean; dataUpdatedAt: number };
const answers = vi.hoisted(() => ({
  trackings: new Map<string, unknown>(),
  logs: new Map<string, unknown>(),
  rosters: new Map<string, unknown>(),
  /** Reads that failed, by trip; a failed read may still hold an earlier answer. */
  failedTrackings: new Set<string>(),
  failedLogs: new Set<string>(),
}));
const answer = (held: Map<string, unknown>, failed: Set<string> = new Set()) => (ids: readonly string[]) =>
  ids.map((id): Answer => ({
    data: held.get(id),
    isPending: !held.has(id) && !failed.has(id),
    error: failed.has(id) ? new Error('the tracking log has more pages than one read follows') : null,
    isFetching: false,
    dataUpdatedAt: 0,
  }));
vi.mock('../../../api/hooks.ts', () => ({
  useTripTrackings: (ids: readonly string[]) => answer(answers.trackings, answers.failedTrackings)(ids),
  useTripTrackingEventLogs: (ids: readonly string[]) => answer(answers.logs, answers.failedLogs)(ids),
  useTripLogsById: (ids: readonly string[]) => answer(answers.rosters)(ids),
  // These tests are of finished trips, whose logs are never read again.
  useRereadTripTrackingEventLog: () => () => Promise.reject(new Error('no log is read again here')),
}));

const { useMovieTrips } = await import('./useMovieTrips.ts');

function arrive(tripLogId: string, title: string, options: { armed?: boolean; names?: Record<string, string> } = {}) {
  const armed = options.armed ?? true;
  answers.trackings.set(tripLogId, { armedAt: armed ? ARMED : null, closedAt: armed ? CLOSED : null, participants: [] });
  answers.logs.set(tripLogId, [
    { recordedAt: '2026-09-12T10:00:00Z', surveyModelId: MODEL },
    { recordedAt: '2026-09-12T09:00:00Z', surveyModelId: MODEL },
  ]);
  answers.rosters.set(tripLogId, {
    title,
    participants: Object.entries(options.names ?? {}).map(([caverId, name]) => ({ caverId, name })),
  });
}

beforeEach(() => {
  answers.trackings.clear();
  answers.logs.clear();
  answers.rosters.clear();
  answers.failedTrackings.clear();
  answers.failedLogs.clear();
});

describe('useMovieTrips', () => {
  it('hands on the chosen trips in the order they were chosen, each with its roster names', () => {
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

  it('leaves out a trip until all of it has arrived, and says it is still reading', () => {
    arrive('trip-a', 'Alpha');
    answers.logs.delete('trip-a');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));

    expect(result.current.loading).toBe(true);
    expect(result.current.trips).toEqual([]);
  });

  it('names a chosen trip with nothing to replay instead of handing it on', () => {
    arrive('trip-a', 'Alpha');
    arrive('trip-b', 'Never armed', { armed: false });

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(result.current.empty).toEqual(['trip-b']);
  });

  it('fails a trip whose log could not be read to its end, rather than replaying part of it', () => {
    arrive('trip-a', 'Alpha');
    arrive('trip-b', 'Bravo');
    answers.logs.delete('trip-b');
    answers.failedLogs.add('trip-b');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a', 'trip-b'], OPENED));

    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.failed).toEqual(['trip-b']);
    expect(result.current.logFailed).toEqual(['trip-b']);
    // The failed trip is not handed on as if it were a trip with nothing in it.
    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(result.current.empty).toEqual([]);
  });

  it('keeps a trip whose polled watch failed a re-read while its earlier answer is held', () => {
    arrive('trip-a', 'Alpha');
    answers.failedTrackings.add('trip-a');

    const { result } = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));

    expect(result.current.error).toBeNull();
    expect(result.current.failed).toEqual([]);
    expect(result.current.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);

    // With nothing held, the same failure fails the trip — but not as an unreadable log.
    answers.trackings.delete('trip-a');
    const again = renderHook(() => useMovieTrips(MODEL, ['trip-a'], OPENED));
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
});
