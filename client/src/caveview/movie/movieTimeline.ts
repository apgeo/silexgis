// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import { replayWindow, type ReplayWindow } from '../trackingReplay.ts';
import type { MovieSettings, MovieTimelineMode } from './movieSettings.ts';

/**
 * The clock of an exported movie: which instant of each trip is shown at each point of the movie,
 * and which frame is which point.
 *
 * <b>Every frame is a function of its index and nothing else.</b> The model is rendered in
 * software on some machines, so one frame may take a second to draw and the next a tenth of that.
 * Were the replay instant, the camera's angle or the markers' motion read off a wall clock, a slow
 * render would make an uneven movie; computed from the frame number, it only makes a slow export.
 */

/** One trip's part in a movie: the stretch its replay covers, and the instants it reported at. */
export interface MovieTripSpan {
  tripLogId: string;
  window: ReplayWindow;
  /** Report instants, ascending, epoch milliseconds. */
  moments: readonly number[];
}

/**
 * One trip's part in a movie of a model, from the trip's watch and its whole log; null when the
 * trip has nothing to replay (its watch was never armed, or its reports cover no stretch of time).
 *
 * The window is the trip's own replay window, the one its replay bar scrubs over. The moments are
 * the instants something happened in the movie: every report except one measured against another
 * survey model, which the replay does not draw on this one — so a stretch in which the party was
 * reporting on another survey reads as quiet here, which on this model it is.
 *
 * @param openedAt where a trip still under way is taken to end: when the movie's dialog was opened
 *   while it is previewed, and when the export began in the file (see {@link movieSpansAt}).
 */
export function movieTripSpan(
  tripLogId: string,
  tracking: Pick<TrackingState, 'armedAt' | 'closedAt'>,
  events: readonly Pick<TrackingEvent, 'recordedAt' | 'surveyModelId'>[],
  openedAt: number,
  surveyModelId: string,
): MovieTripSpan | null {
  const window = replayWindow(tracking, events, openedAt);
  if (window === null) {
    return null;
  }
  return { tripLogId, window, moments: reportMoments(events, surveyModelId) };
}

/** The instants of the reports a movie of this model replays, ascending. */
function reportMoments(
  events: readonly Pick<TrackingEvent, 'recordedAt' | 'surveyModelId'>[],
  surveyModelId: string,
): number[] {
  return events
    .filter((event) => event.surveyModelId === null || event.surveyModelId === surveyModelId)
    .map((event) => Date.parse(event.recordedAt))
    .filter((at) => Number.isFinite(at))
    .sort((left, right) => left - right);
}

/**
 * The spans of a movie that shows only some of its trips' people: each trip's stretch as it was,
 * and for its moments only the reports of somebody who appears.
 *
 * <b>A person left out of a movie must not pace it.</b> With quiet stretches shortened, the clock
 * slows down around every report and jumps across the hours between them. Cut at the reports of
 * somebody who is not shown, the file would dwell on a moment at which nobody in the picture did
 * anything — and that moment is the time of a report by the person who was taken out. The stretch
 * a trip covers is not touched: it is the trip's, whoever is shown of it.
 *
 * A span whose trip is not among `trips` is handed back as it is.
 *
 * @param appears whether a caver of a trip is shown in the movie.
 */
export function movieSpansShowing(
  spans: readonly MovieTripSpan[],
  trips: readonly {
    tripLogId: string;
    events: readonly Pick<TrackingEvent, 'recordedAt' | 'surveyModelId' | 'caverId'>[];
  }[],
  surveyModelId: string,
  appears: (tripLogId: string, caverId: string) => boolean,
): MovieTripSpan[] {
  return spans.map((span) => {
    const trip = trips.find((candidate) => candidate.tripLogId === span.tripLogId);
    if (trip === undefined) {
      return span;
    }
    const shown = trip.events.filter((event) => appears(trip.tripLogId, event.caverId));
    return shown.length === trip.events.length
      ? span
      : { ...span, moments: reportMoments(shown, surveyModelId) };
  });
}

/** Whether a trip's watch is still running: started and not ended, so its replay has no end of its own. */
export function movieTripIsLive(tracking: Pick<TrackingState, 'armedAt' | 'closedAt'>): boolean {
  return tracking.armedAt !== null && tracking.closedAt === null;
}

/**
 * Every trip's part in a movie whose trips still under way end at `endAt`, in the order given;
 * null when some trip has nothing to replay, which a trip already in a movie never has.
 *
 * <b>A movie of a trip still under way ends when it is exported, not when its dialog was opened.</b>
 * The dialog fixes that end once, so the slider does not creep under the reader's hand; but a
 * dialog can stand open for an hour while the party goes on, and a movie exported then would stop
 * an hour short of the moment it was made, with nobody told. So the export asks for the spans
 * again as of its own start. A finished trip's span is the same whenever it is asked for.
 */
export function movieSpansAt(
  trips: readonly {
    tripLogId: string;
    tracking: Pick<TrackingState, 'armedAt' | 'closedAt'>;
    events: readonly Pick<TrackingEvent, 'recordedAt' | 'surveyModelId'>[];
  }[],
  endAt: number,
  surveyModelId: string,
): MovieTripSpan[] | null {
  const spans: MovieTripSpan[] = [];
  for (const trip of trips) {
    const span = movieTripSpan(trip.tripLogId, trip.tracking, trip.events, endAt, surveyModelId);
    if (span === null) {
      return null;
    }
    spans.push(span);
  }
  return spans;
}

/**
 * How many of a trip's reports are not among the ones `known`.
 *
 * Counted by which reports they are, never by the time they carry: a report's time is when the
 * thing happened, written by whoever recorded it, and a report entered now about an hour ago would
 * not count as new by its time although it has only just arrived.
 */
export function movieNewReports(events: readonly Pick<TrackingEvent, 'id'>[], known: ReadonlySet<string>): number {
  let count = 0;
  for (const event of events) {
    if (!known.has(event.id)) {
      count += 1;
    }
  }
  return count;
}

export interface MovieTimeline {
  mode: MovieTimelineMode;
  /** Length of the (possibly shortened) timeline, in its own milliseconds. */
  length: number;
  /** The instant each trip is replayed at, in the order the trips were given, at a position on the timeline (0..length). */
  instants(position: number): number[];
  /** What the clock caption says at a position. */
  clock(position: number): MovieClock;
}

/**
 * What a movie's clock shows at one of its moments: the instant being replayed, or — trips played
 * side by side — how long they have been under way. An elapsed clock also says how far it will run
 * in all, `totalMs`, which is the longest trip's real length whatever was shortened on the way:
 * that decides the unit the whole movie's clock is written in, so it does not change mid-movie.
 */
export type MovieClock = { kind: 'calendar'; at: number } | { kind: 'elapsed'; ms: number; totalMs: number };

/**
 * One piece of the map from the timeline to the axis being replayed: `[start, start + span)` of the
 * timeline shows `[from, to]` of the axis. A piece is shortened where `span < to - from`.
 */
interface Piece {
  start: number;
  span: number;
  from: number;
  to: number;
}

/**
 * The axis position shown at a timeline position.
 *
 * A piece shown at its own length maps straight across. A shortened one plays the first and last
 * halves of what it is allowed at real speed and skips the middle, so the clock caption visibly
 * jumps — the reader sees that hours went by, and a marker still arriving at its station, or
 * leaving one, is seen doing so at the pace everything else moves at.
 */
function axisAt(pieces: readonly Piece[], position: number): number {
  let piece = pieces[0];
  for (const candidate of pieces) {
    if (candidate.start > position) {
      break;
    }
    piece = candidate;
  }
  const into = Math.min(Math.max(position - piece.start, 0), piece.span);
  if (piece.span >= piece.to - piece.from) {
    return piece.from + into;
  }
  const half = piece.span / 2;
  return into <= half ? piece.from + into : piece.to - (piece.span - into);
}

/**
 * The pieces covering `[from, to]` of an axis, cut at every anchor, each stretch between anchors
 * that is longer than `quietGap` shortened to it.
 */
function piecesOver(from: number, to: number, anchors: readonly number[], quietGap: number | null): Piece[] {
  const cuts = [...new Set([from, to, ...anchors.filter((at) => at > from && at < to)])].sort(
    (left, right) => left - right,
  );
  const pieces: Piece[] = [];
  let start = 0;
  for (let index = 0; index + 1 < cuts.length; index++) {
    const real = cuts[index + 1] - cuts[index];
    const span = quietGap !== null && real > quietGap ? quietGap : real;
    pieces.push({ start, span, from: cuts[index], to: cuts[index + 1] });
    start += span;
  }
  return pieces;
}

/**
 * The timeline several trips are replayed along, or null where there is nothing to replay.
 *
 * - **calendar** — real chronology across every trip: one instant for all of them at each point.
 * - **together** — every trip starts at the same moment, its replay window's start, so trips can be
 *   compared side by side; the clock then says how long each has been under way.
 *
 * In either mode a stretch in which no trip reports anything for longer than `quietGapMs` is
 * shortened to that length. What counts as a report is a moment of any trip; in together mode the
 * moments are measured from each trip's own start, since that is the axis being played.
 *
 * @param quietGapMs null (or not a positive number) to keep every stretch at its real length.
 */
export function buildMovieTimeline(
  trips: readonly MovieTripSpan[],
  options: { mode: MovieTimelineMode; quietGapMs: number | null },
): MovieTimeline | null {
  const usable = trips.filter(
    (trip) => Number.isFinite(trip.window.from) && Number.isFinite(trip.window.to) && trip.window.to > trip.window.from,
  );
  if (usable.length !== trips.length || trips.length === 0) {
    return null;
  }
  const quietGap =
    options.quietGapMs !== null && Number.isFinite(options.quietGapMs) && options.quietGapMs > 0
      ? options.quietGapMs
      : null;

  if (options.mode === 'calendar') {
    const from = Math.min(...trips.map((trip) => trip.window.from));
    const to = Math.max(...trips.map((trip) => trip.window.to));
    const pieces = piecesOver(from, to, trips.flatMap((trip) => trip.moments), quietGap);
    const length = pieces.reduce((sum, piece) => sum + piece.span, 0);
    return {
      mode: 'calendar',
      length,
      instants: (position) => {
        const at = axisAt(pieces, position);
        return trips.map(() => at);
      },
      clock: (position) => ({ kind: 'calendar', at: axisAt(pieces, position) }),
    };
  }

  const longest = Math.max(...trips.map((trip) => trip.window.to - trip.window.from));
  const elapsedMoments = trips.flatMap((trip) => trip.moments.map((at) => at - trip.window.from));
  const pieces = piecesOver(0, longest, elapsedMoments, quietGap);
  const length = pieces.reduce((sum, piece) => sum + piece.span, 0);
  return {
    mode: 'together',
    length,
    instants: (position) => {
      const elapsed = axisAt(pieces, position);
      // A trip shorter than the longest one runs on past its own end rather than stopping there.
      // Its picture does not change for it, since its window already holds every report it has;
      // what does change is that its instant keeps saying how long ago each of its reports was on
      // the clock being played. Held at its end, a finished trip's last note would stop ageing and
      // outrank the newer notes of the trips still under way. Calendar mode already hands a trip
      // instants outside its own window, so neither mode clamps.
      return trips.map((trip) => trip.window.from + elapsed);
    },
    clock: (position) => ({ kind: 'elapsed', ms: axisAt(pieces, position), totalMs: longest }),
  };
}

export interface MovieFrame {
  index: number;
  /** Where on the timeline this frame shows, 0..timeline.length. */
  position: number;
  /** How far through the replay part this frame is, 0..1; 1 on every still frame at the end. */
  progress: number;
  /** Radians added to the camera's starting azimuth; positive turns the picture clockwise. */
  azimuthOffset: number;
  /** Milliseconds of marker motion to run before this frame is drawn. */
  advanceMs: number;
  /** Whether this is one of the still frames at the end. */
  holding: boolean;
}

/**
 * How many frames a movie has — the replay part, at least one frame, then the still frames at the
 * end — which depends on nothing but its length and rate, so it can be told before any trip is
 * chosen.
 */
export function movieFrameCount(
  settings: Pick<MovieSettings, 'fps' | 'durationS' | 'holdEndS'>,
): { replayFrames: number; holdFrames: number; count: number } {
  const replayFrames = Math.max(1, Math.round(settings.durationS * settings.fps));
  const holdFrames = Math.max(0, Math.round(settings.holdEndS * settings.fps));
  return { replayFrames, holdFrames, count: replayFrames + holdFrames };
}

/**
 * How many times faster than life a movie's clock runs while it plays, or null when the movie has
 * no rate to speak of.
 *
 * <b>One figure for the whole movie, known before any frame is drawn.</b> Consecutive frames of the
 * replay part are a fixed step of the timeline apart and a fixed time apart on screen, so the rate
 * is the same at every frame — which is what lets the clock caption carry it on each of them, the
 * still frames at the end included, without the plate changing width as the movie plays.
 *
 * It is the timeline's length that is divided, not the trips' real span: where a quiet stretch was
 * shortened the clock jumps over the middle of it, and a jump is an instant, not a speed. What the
 * figure says is how fast everything that <i>is</i> shown goes by.
 *
 * Measured between the first and the last frame of the replay part rather than over the length
 * the reader asked for: the last frame shows the end of the timeline, so the timeline is crossed in
 * one frame's time less than that length. On a long movie the two agree to within a rounding; on a
 * two-second GIF at ten frames a second they differ by a twentieth, and the caption would be wrong
 * by that much against a stopwatch held to the clock it stands beside.
 */
export function movieSpeedFactor(
  timeline: Pick<MovieTimeline, 'length'>,
  settings: Pick<MovieSettings, 'fps' | 'durationS' | 'holdEndS'>,
): number | null {
  const { replayFrames } = movieFrameCount(settings);
  // A single frame shows one moment: nothing runs, at any speed.
  if (replayFrames < 2 || !(settings.fps > 0)) {
    return null;
  }
  const playedMs = ((replayFrames - 1) * 1000) / settings.fps;
  const factor = timeline.length / playedMs;
  return Number.isFinite(factor) && factor > 0 ? factor : null;
}

/**
 * The sign of an azimuth step that turns the picture clockwise.
 *
 * "Clockwise" is said of what the reader sees: the model turning like a clock's hands, seen from
 * above. The viewer's azimuth is the orbit's own angle about the vertical, zero with the camera due
 * south of the target and growing as it swings round through east — so the camera travels
 * counter-clockwise seen from above as the angle grows, and the model, held still while the camera
 * goes round it, appears to turn the other way, clockwise. A growing azimuth is therefore the
 * clockwise turn.
 */
const CLOCKWISE_SIGN = 1;

/**
 * The frame schedule: how many frames the movie has and what each one shows.
 *
 * The replay part is `durationS` of frames, its first showing the start of the timeline and its
 * last the end; the still frames after it repeat that end, with the camera stopped where the
 * replay part left it. The camera turns at `degreesPerSecond` of movie time, or once round over
 * the replay part — completing the turn one frame after the last, so a looping movie does not show
 * the same angle twice across the seam.
 *
 * Every frame advances the markers by one frame's time except the first, which is the picture
 * before any time has passed. Advancing on the still frames lets a marker that was still sliding
 * reach its station.
 */
export function movieFrames(
  timeline: MovieTimeline,
  settings: MovieSettings,
): { count: number; fps: number; frame(index: number): MovieFrame } {
  const fps = settings.fps;
  const { replayFrames, count } = movieFrameCount(settings);
  const { rotation } = settings;
  const radiansPerFrame = !rotation.enabled
    ? 0
    : rotation.mode === 'fullTurn'
      ? (2 * Math.PI) / replayFrames
      : (rotation.degreesPerSecond * Math.PI) / 180 / fps;
  const turn = (rotation.clockwise ? CLOCKWISE_SIGN : -CLOCKWISE_SIGN) * radiansPerFrame;

  return {
    count,
    fps,
    frame(index: number): MovieFrame {
      const clamped = Math.min(Math.max(Math.trunc(index), 0), count - 1);
      const holding = clamped >= replayFrames;
      const replayIndex = Math.min(clamped, replayFrames - 1);
      const progress = replayFrames === 1 ? 1 : replayIndex / (replayFrames - 1);
      return {
        index: clamped,
        position: progress * timeline.length,
        progress,
        // `+ 0` turns the negative zero a counter-clockwise start would give into a plain one.
        azimuthOffset: turn * replayIndex + 0,
        advanceMs: clamped === 0 ? 0 : 1000 / fps,
        holding,
      };
    },
  };
}
