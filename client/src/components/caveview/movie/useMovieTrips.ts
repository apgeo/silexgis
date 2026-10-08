// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useRereadTrackedTripReplays, useTrackedTripReplays, type TrackedTripReplay } from '../../../api/hooks.ts';
import { aboutPeople } from '../../../caveview/caveNotes.ts';
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
 * <b>One read for all of them.</b> The chosen trips are asked for together — each trip's title, its
 * roster's names, its watch and its whole log arrive in one answer, each exactly as the trip's own
 * reads would give it to this reader. A trip is therefore whole or not there: a replay built on part
 * of a log would show a party appearing out of nowhere half-way through. A trip whose reports cover
 * no stretch of time is left out and named in `empty`, so the picker can say why it contributes
 * nothing.
 *
 * <b>A trip the answer does not carry could not be read.</b> The answer leaves out a trip this reader
 * may not read and says nothing of why; here that is a failed trip, named in `failed`, exactly as a
 * refused read of that trip alone was. While a different set of trips is on its way the trips of the
 * previous answer stay in hand, so ticking one more does not take the others off the screen.
 *
 * <b>A re-read does not start anything over.</b> The answer is asked for again every so often while
 * a trip is under way; it is folded into a new list only when some trip's data actually changed.
 * A re-read that fails while an earlier answer is held is not a failure here: the trips are still
 * whole. And while an export is running nothing is folded at all — the movie being made keeps the
 * answer it started with, and what arrived meanwhile is taken up when it is over.
 *
 * <b>A log that cannot be read to its end fails the trip.</b> An answer carries a log whole or says
 * that it does not; such a trip is named in `logFailed`, so the dialog can say what the replay says,
 * and is never quietly left out of the movie nor replayed in part.
 *
 * <b>The log of a trip still under way keeps up, because it arrives with the watch.</b> A movie of a
 * trip under way is drawn from the log, and its dialog can stand open for an hour while other people
 * record reports from their own browsers. Each answer carries the watch and the log of one moment —
 * so the movie previewed, and the count of reports arrived since opening, follow the party — and
 * {@link MovieTripsState.rereadLive} asks once more for an export, which must not draw its last
 * stretch from a log half a minute old under a clock that runs to now.
 *
 * <b>"Since opening" is counted from the first answer read after the dialog opened.</b> What is
 * handed over first can be an answer held from an earlier opening, with the fresh one a moment
 * behind it. Counted from the held one, every report recorded between that older read and the
 * opening would be announced as new.
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
  const read = useTrackedTripReplays(surveyModelId, tripLogIds, !paused);
  const reread = useRereadTrackedTripReplays();
  // When this movie's dialog opened: an answer read before it is not what "since opening" is counted from.
  const [mountedAt] = useState(() => Date.now());

  // Whether what is held answers these very trips — a placeholder is the answer about the trips
  // chosen before, which says nothing of a trip it does not carry.
  const answered = read.data !== undefined && !read.isPlaceholderData;
  // A read counts as failed only while it holds no answer: a polled re-read that fails keeps the
  // answer it had, and a movie of those trips is as good as it was a moment ago.
  const broken = read.error !== null && read.data === undefined;

  // What the movie is folded from. Left as it was while paused: an export is running on it.
  const live = {
    data: read.data,
    answered,
    // Read since the dialog opened, and not kept from an earlier opening.
    fresh: answered && read.dataUpdatedAt >= mountedAt,
    error: broken ? (read.error as unknown) : null,
  };
  const heldRef = useRef(live);
  if (!paused) {
    heldRef.current = live;
  }
  const { data, answered: whole, fresh, error } = heldRef.current;

  const ids = useSameElements(tripLogIds);
  /** The reports each trip under way had in the first answer read after the dialog opened. */
  const knownReportsRef = useRef(new Map<string, ReadonlySet<string>>());

  const state = useMemo(() => {
    const byId = new Map<string, TrackedTripReplay>((data ?? []).map((trip) => [trip.tripLogId, trip]));
    const trips: MovieTripData[] = [];
    const spans: MovieTripSpan[] = [];
    const empty: string[] = [];
    const failed: string[] = [];
    const logFailed: string[] = [];
    const newReports = new Map<string, number>();
    let loading = false;
    for (const tripLogId of ids) {
      const replay = byId.get(tripLogId);
      if (replay === undefined) {
        // Not in an answer about these trips: it could not be read. Not in a placeholder: on its way.
        if (whole || error !== null) {
          failed.push(tripLogId);
        } else {
          loading = true;
        }
        continue;
      }
      if (!replay.eventsComplete) {
        failed.push(tripLogId);
        logFailed.push(tripLogId);
        continue;
      }
      const { tracking } = replay;
      // A movie is a copy people keep and pass on, and a note about the cave is for whoever may
      // read the trip: it is left out where the log comes in, so nothing that builds the movie —
      // its party, its routes, its captions, the moments it stops at — ever holds one.
      const events = aboutPeople(replay.events);
      const span = movieTripSpan(tripLogId, tracking, events, openedAt, surveyModelId);
      if (span === null) {
        empty.push(tripLogId);
        continue;
      }
      const names = new Map(replay.participants.map((person) => [person.caverId, person.name]));
      const unknown = t('trips.tracking.unknownCaver');
      trips.push({
        tripLogId,
        title: replay.title,
        tracking,
        events,
        nameOf: (caverId) => names.get(caverId) ?? unknown,
      });
      spans.push(span);
      if (movieTripIsLive(tracking)) {
        let known = knownReportsRef.current.get(tripLogId);
        if (known === undefined && fresh) {
          known = new Set(events.map((event) => event.id));
          knownReportsRef.current.set(tripLogId, known);
        }
        newReports.set(tripLogId, known === undefined ? 0 : movieNewReports(events, known));
      }
    }
    return { loading, error, failed, logFailed, trips, spans, empty, newReports };
    // `data` keeps its identity across a re-read that changed nothing, and `ids` across a render that
    // chose the same trips; the three flags say what kind of answer `data` is.
  }, [data, ids, whole, fresh, error, openedAt, surveyModelId, t]);

  const tripsRef = useRef(state.trips);
  tripsRef.current = state.trips;
  const askedRef = useRef({ surveyModelId, ids });
  askedRef.current = { surveyModelId, ids };
  const rereadLive = useCallback(async () => {
    const ready = tripsRef.current;
    // Finished trips are history: nothing more is asked of them.
    if (!ready.some((trip) => movieTripIsLive(trip.tracking))) {
      return ready;
    }
    const asked = askedRef.current;
    const answer = await reread(asked.surveyModelId, asked.ids);
    return ready.map((trip) => {
      if (!movieTripIsLive(trip.tracking)) {
        return trip;
      }
      const again = answer.find((candidate) => candidate.tripLogId === trip.tripLogId);
      if (again === undefined || !again.eventsComplete) {
        throw new Error('the log of a trip under way could not be read to its end');
      }
      // The log comes in here too, and the notes about the cave are left out of it as above.
      return { ...trip, events: aboutPeople(again.events) };
    });
  }, [reread]);
  return useMemo(() => ({ ...state, rereadLive }), [state, rereadLive]);
}
