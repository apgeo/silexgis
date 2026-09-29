// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useTripLogsById,
  useTripTrackingEventLogs,
  useTripTrackings,
  type TrackingEvent,
  type TrackingState,
  type TripLogInfo,
} from '../../../api/hooks.ts';
import type { MovieTripData } from '../../../caveview/movie/movieParty.ts';
import { movieTripSpan, type MovieTripSpan } from '../../../caveview/movie/movieTimeline.ts';

/**
 * The chosen trips of a movie, read and put together into what the movie is made from.
 *
 * <b>In the order they were chosen.</b> A trip's marker colour follows its place in the list, so a
 * trip added later is appended rather than sorted in — sorting would repaint every trip after it
 * the moment one more was ticked.
 *
 * <b>Only complete trips are handed on.</b> A trip is part of the movie once its watch, its whole
 * log and its roster have all arrived; a replay built on part of a log would show a party appearing
 * out of nowhere half-way through. A trip whose reports cover no stretch of time is left out and
 * named in `empty`, so the picker can say why it contributes nothing.
 *
 * <b>A re-read does not start anything over.</b> The watch of a trip under way is re-read every
 * so often; the answer is folded into a new list only when some trip's data actually changed, and
 * an export already running keeps the list it started with. For the same reason a re-read that
 * fails while an earlier answer is held is not a failure here: the trip is still whole.
 *
 * <b>A log that cannot be read to its end fails the trip.</b> The log read refuses to answer with
 * part of a log, for the reason the trip's own replay gives; such a trip is named in `logFailed`,
 * so the dialog can say what the replay says, and is never quietly left out of the movie.
 */
export interface MovieTripsState {
  /** Any chosen trip still being read. */
  loading: boolean;
  /** The first read that failed with nothing held, if one did. */
  error: unknown;
  /** Chosen trips some read of which failed with nothing held, in the order chosen. */
  failed: string[];
  /** Of those, the trips whose whole log could not be read. */
  logFailed: string[];
  /** The chosen trips ready to be in the movie, in the order chosen. */
  trips: MovieTripData[];
  /** Each ready trip's part of the timeline, in the same order. */
  spans: MovieTripSpan[];
  /** Chosen trips that have been read and have nothing to replay. */
  empty: string[];
}

/** The same array as last time while every element is the same, so a memo keyed on it holds. */
function useSameElements<T>(values: readonly T[]): readonly T[] {
  const held = useRef<readonly T[]>(values);
  const before = held.current;
  if (before.length !== values.length || before.some((value, index) => value !== values[index])) {
    held.current = values;
  }
  return held.current;
}

export function useMovieTrips(surveyModelId: string, tripLogIds: readonly string[], openedAt: number): MovieTripsState {
  const { t } = useTranslation();
  const trackings = useTripTrackings(tripLogIds);
  const logs = useTripTrackingEventLogs(tripLogIds);
  const rosters = useTripLogsById(tripLogIds);

  // A read counts as failed only while it holds no answer: a polled re-read that fails keeps the
  // answer it had, and a movie of that trip is as good as it was a moment ago.
  const broken = (query: { data: unknown; error: unknown }) => query.error !== null && query.data === undefined;
  const loading = [...trackings, ...logs, ...rosters].some((query) => query.isPending);
  const error = [...trackings, ...logs, ...rosters].find(broken)?.error ?? null;
  // One character per chosen trip — its log failed, another of its reads failed, or neither — so the
  // memo below sees a change in which trips failed without a fresh array on every render.
  const failedKey = tripLogIds
    .map((_, index) =>
      broken(logs[index]) ? 'l' : broken(trackings[index]) || broken(rosters[index]) ? 'x' : '-',
    )
    .join('');

  // What each read answered, flattened into one list whose identity changes only with the answers.
  const answers = useSameElements<unknown>([
    ...tripLogIds,
    ...trackings.map((query) => query.data),
    ...logs.map((query) => query.data),
    ...rosters.map((query) => query.data),
  ]);

  return useMemo(() => {
    const count = tripLogIds.length;
    const trips: MovieTripData[] = [];
    const spans: MovieTripSpan[] = [];
    const empty: string[] = [];
    for (let index = 0; index < count; index++) {
      const tripLogId = answers[index] as string;
      const tracking = answers[count + index] as TrackingState | undefined;
      const events = answers[2 * count + index] as TrackingEvent[] | undefined;
      const roster = answers[3 * count + index] as TripLogInfo | undefined;
      if (tracking === undefined || events === undefined || roster === undefined) {
        continue;
      }
      const span = movieTripSpan(tripLogId, tracking, events, openedAt, surveyModelId);
      if (span === null) {
        empty.push(tripLogId);
        continue;
      }
      const names = new Map(roster.participants.map((person) => [person.caverId, person.name]));
      const unknown = t('trips.tracking.unknownCaver');
      trips.push({
        tripLogId,
        title: roster.title,
        tracking,
        events,
        nameOf: (caverId) => names.get(caverId) ?? unknown,
      });
      spans.push(span);
    }
    const failed: string[] = [];
    const logFailed: string[] = [];
    for (let index = 0; index < count; index++) {
      if (failedKey[index] !== '-') {
        failed.push(answers[index] as string);
      }
      if (failedKey[index] === 'l') {
        logFailed.push(answers[index] as string);
      }
    }
    return { loading, error, failed, logFailed, trips, spans, empty };
    // `answers` carries the ids and every read's data, `failedKey` which of them failed; `loading`
    // and `error` are read as they are.
  }, [answers, failedKey, openedAt, surveyModelId, t, loading, error, tripLogIds.length]);
}
