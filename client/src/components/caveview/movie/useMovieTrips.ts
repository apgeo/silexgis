// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useRereadTripTrackingEventLog,
  useTripLogsById,
  useTripTrackingEventLogs,
  useTripTrackings,
  type TrackingEvent,
  type TrackingState,
  type TripLogInfo,
} from '../../../api/hooks.ts';
import type { MovieTripData } from '../../../caveview/movie/movieParty.ts';
import {
  movieNewReports,
  movieTripIsLive,
  movieTripSpan,
  type MovieTripSpan,
} from '../../../caveview/movie/movieTimeline.ts';

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
 *
 * <b>The log of a trip still under way is read again, because nothing else does.</b> A trip's whole
 * log is a read of history and is not kept fresh by itself; only its folded watch is. But a movie of
 * a trip under way is drawn from the log, and its dialog can stand open for an hour while other
 * people record reports from their own browsers. So each time the watch of such a trip answers, its
 * log is asked for again — the movie previewed, and the count of reports arrived since opening, then
 * follow the party — and {@link MovieTripsState.rereadLive} reads it once more for an export, which
 * must not draw its last stretch from an hour-old log under a clock that runs to now.
 *
 * <b>"Since opening" is counted from the first log read after the dialog opened.</b> The log is held
 * under the key the trip's own replay reads it by, so what is handed over first is often a log read
 * some time ago, with the fresh one a moment behind it. Counted from the held one, every report
 * recorded between that older read and the opening would be announced as new.
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
  /**
   * Each ready trip still under way, with how many reports have arrived since its log was first
   * read after the dialog opened — none while that first read is still on its way.
   */
  newReports: ReadonlyMap<string, number>;
  /**
   * The ready trips again, each trip still under way with its whole log read anew just now. Refuses
   * when one of those logs could not be read to its end; finished trips are handed back as they are.
   */
  rereadLive: () => Promise<MovieTripData[]>;
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

/**
 * @param openedAt where a trip still under way ends in the movie.
 * @param paused true while nothing may change under the movie — an export is running — so the logs
 *   of trips under way are left alone until it is over.
 */
export function useMovieTrips(
  surveyModelId: string,
  tripLogIds: readonly string[],
  openedAt: number,
  paused = false,
): MovieTripsState {
  const { t } = useTranslation();
  const trackings = useTripTrackings(tripLogIds);
  const logs = useTripTrackingEventLogs(tripLogIds);
  const rosters = useTripLogsById(tripLogIds);
  const reread = useRereadTripTrackingEventLog();
  // When this movie's dialog opened: a log read before it is not what "since opening" is counted from.
  const [mountedAt] = useState(() => Date.now());

  // ---- the logs of trips still under way, read again whenever their watch answers ----
  // One entry per chosen trip still under way: its id and when its watch last answered.
  const liveKey = tripLogIds
    .map((id, index) => {
      const tracking = trackings[index].data;
      return tracking !== undefined && movieTripIsLive(tracking) ? `${id}@${trackings[index].dataUpdatedAt}` : '';
    })
    .filter((entry) => entry !== '')
    .join('|');
  const logsRef = useRef({ ids: tripLogIds, logs });
  logsRef.current = { ids: tripLogIds, logs };
  /** The watch answer each trip's log was last asked again for. */
  const askedForRef = useRef(new Map<string, string>());
  useEffect(() => {
    if (paused || liveKey === '') {
      return;
    }
    for (const entry of liveKey.split('|')) {
      const [id, answeredAt] = entry.split('@');
      const first = !askedForRef.current.has(id);
      if (askedForRef.current.get(id) === answeredAt) {
        continue;
      }
      askedForRef.current.set(id, answeredAt);
      const log = logsRef.current.logs[logsRef.current.ids.indexOf(id)];
      // The first time a trip is seen under way its log is usually arriving, or has just arrived,
      // from the read every ticked trip starts with; only a log held from before the dialog opened,
      // with nothing on its way, has to be asked for.
      if (first && log !== undefined && (log.isFetching || log.dataUpdatedAt >= mountedAt)) {
        continue;
      }
      // A re-read that fails leaves the log that was held, and the movie as good as it was; the
      // export's own re-read is the one that refuses.
      reread(id).catch(() => {});
    }
  }, [liveKey, paused, reread, mountedAt]);
  // One character per chosen trip: whether its log has been read since the dialog opened.
  const freshKey = logs.map((query) => (query.dataUpdatedAt >= mountedAt ? 'f' : '-')).join('');
  /** The reports each trip under way had in its first log read after the dialog opened. */
  const knownReportsRef = useRef(new Map<string, ReadonlySet<string>>());

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

  const state = useMemo(() => {
    const count = tripLogIds.length;
    const trips: MovieTripData[] = [];
    const spans: MovieTripSpan[] = [];
    const empty: string[] = [];
    const newReports = new Map<string, number>();
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
      if (movieTripIsLive(tracking)) {
        let known = knownReportsRef.current.get(tripLogId);
        if (known === undefined && freshKey[index] === 'f') {
          known = new Set(events.map((event) => event.id));
          knownReportsRef.current.set(tripLogId, known);
        }
        newReports.set(tripLogId, known === undefined ? 0 : movieNewReports(events, known));
      }
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
    return { loading, error, failed, logFailed, trips, spans, empty, newReports };
    // `answers` carries the ids and every read's data, `failedKey` which of them failed and
    // `freshKey` which logs were read since opening; `loading` and `error` are read as they are.
  }, [answers, failedKey, freshKey, openedAt, surveyModelId, t, loading, error, tripLogIds.length]);

  const tripsRef = useRef(state.trips);
  tripsRef.current = state.trips;
  const rereadLive = useCallback(
    () =>
      Promise.all(
        tripsRef.current.map(async (trip) =>
          movieTripIsLive(trip.tracking) ? { ...trip, events: await reread(trip.tripLogId) } : trip,
        ),
      ),
    [reread],
  );
  return useMemo(() => ({ ...state, rereadLive }), [state, rereadLive]);
}
