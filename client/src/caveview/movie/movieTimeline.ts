// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReplayWindow } from '../trackingReplay.ts';
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

export interface MovieTimeline {
  mode: MovieTimelineMode;
  /** Length of the (possibly shortened) timeline, in its own milliseconds. */
  length: number;
  /** The instant each trip is replayed at, in the order the trips were given, at a position on the timeline (0..length). */
  instants(position: number): number[];
  /** What the clock caption says at a position. */
  clock(position: number): { kind: 'calendar'; at: number } | { kind: 'elapsed'; ms: number };
}

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
    clock: (position) => ({ kind: 'elapsed', ms: axisAt(pieces, position) }),
  };
}

export interface MovieFrame {
  index: number;
  /** Where on the timeline this frame shows, 0..timeline.length. */
  position: number;
  /** How far through the replay part this frame is, 0..1; 1 on every still frame at the end. */
  progress: number;
  /** Radians added to the camera's starting azimuth; positive is clockwise seen from above. */
  azimuthOffset: number;
  /** Milliseconds of marker motion to run before this frame is drawn. */
  advanceMs: number;
  /** Whether this is one of the still frames at the end. */
  holding: boolean;
}

/**
 * Which way a positive azimuth offset turns the camera. The viewer measures azimuth the way a
 * survey bearing is measured — growing clockwise seen from above — so clockwise is the positive
 * direction.
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
  const replayFrames = Math.max(1, Math.round(settings.durationS * fps));
  const holdFrames = Math.max(0, Math.round(settings.holdEndS * fps));
  const count = replayFrames + holdFrames;
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
