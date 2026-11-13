// SPDX-License-Identifier: AGPL-3.0-or-later
import { MOVIE_GIF_MAX_FRAMES, MOVIE_SIZES, MOVIE_VIDEO_FRAME_RATES } from '../caveview/movie/movieSettings.ts';
import { GIF_FRAME_RATES, type MovieFormat, type MovieQuality } from '../movie/encode/movieFormats.ts';

// What a movie of a flyby is made with, and the arithmetic of its frames.
//
// The file's own settings — format, frame size, rate, quality — are the tracking movie's words and
// tables, so a size offered for one is offered for the other and a GIF is held to the same limits.
// What is the flyby's own is how carefully each frame is taken, and which stretch of the flyby is
// filmed.

/**
 * How carefully each frame is taken.
 *
 * `quick` draws the frame and takes it at once: what a live view would show, tiles still arriving
 * included. `fine` waits before each frame until the scene has finished loading what it shows.
 * `fineLarge` waits as `fine` does and also draws the scene at the movie's own size when that is
 * larger than the view on the screen; the other two draw it no larger than the screen does and let
 * the frame be scaled up, which costs nothing extra per frame.
 */
export type FlybyMovieLevel = 'quick' | 'fine' | 'fineLarge';

/** The levels, in the order they are offered. */
export const FLYBY_MOVIE_LEVELS: readonly FlybyMovieLevel[] = ['quick', 'fine', 'fineLarge'];

export interface FlybyMovieSettings {
  format: MovieFormat;
  /** One of the tracking movie's frame sizes, by its id (e.g. '1280x720'). */
  size: string;
  fps: number;
  quality: MovieQuality;
  level: FlybyMovieLevel;
  /** Half the size, a few frames a second, taken quickly: for checking the timing of a flyby. */
  draft: boolean;
  /** Seconds the last moment is held for at the end of the movie. */
  holdEndS: number;
}

export const DEFAULT_FLYBY_MOVIE_SETTINGS: FlybyMovieSettings = {
  format: 'mp4',
  size: '1280x720',
  fps: 30,
  quality: 'high',
  level: 'fine',
  draft: false,
  holdEndS: 0,
};

/** The frame rate of a draft. A GIF cannot hold it exactly, and takes the slowest rate it can. */
export const FLYBY_DRAFT_FPS = 8;

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {};

function oneOf<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

/** The allowed rate nearest to the one asked for, the lower one on a tie. */
function nearestRate(fps: number, allowed: readonly number[]): number {
  let best = allowed[0];
  for (const rate of allowed) {
    if (Math.abs(rate - fps) < Math.abs(best - fps)) {
      best = rate;
    }
  }
  return best;
}

/**
 * Settings that can be used as they are, from anything read back from storage: each field falls
 * back to its default on its own, a size nothing offers is the default one, and a GIF is held to
 * the sizes and rates a GIF may have — its widest size of the same shape, so the framing chosen
 * survives the change of format.
 */
export function normaliseFlybyMovieSettings(raw: unknown): FlybyMovieSettings {
  const d = DEFAULT_FLYBY_MOVIE_SETTINGS;
  const top = record(raw);
  const format = oneOf<MovieFormat>(top.format, ['gif', 'webm', 'mp4'], d.format);
  let size = MOVIE_SIZES.find((entry) => entry.id === top.size) ?? MOVIE_SIZES.find((entry) => entry.id === d.size)!;
  if (format === 'gif' && !size.gif) {
    const shape = size.width / size.height;
    size =
      [...MOVIE_SIZES]
        .filter((entry) => entry.gif && Math.abs(entry.width / entry.height - shape) < 0.01)
        .sort((left, right) => right.width - left.width)[0] ?? MOVIE_SIZES.find((entry) => entry.gif)!;
  }
  const rates = format === 'gif' ? GIF_FRAME_RATES : MOVIE_VIDEO_FRAME_RATES;
  const fps = nearestRate(typeof top.fps === 'number' && Number.isFinite(top.fps) ? top.fps : d.fps, rates);
  const holdEndS =
    typeof top.holdEndS === 'number' && Number.isFinite(top.holdEndS) ? Math.min(10, Math.max(0, top.holdEndS)) : d.holdEndS;
  return {
    format,
    size: size.id,
    fps,
    quality: oneOf<MovieQuality>(top.quality, ['low', 'medium', 'high'], d.quality),
    level: oneOf<FlybyMovieLevel>(top.level, FLYBY_MOVIE_LEVELS, d.level),
    draft: typeof top.draft === 'boolean' ? top.draft : d.draft,
    holdEndS,
  };
}

/** The frame size a settings' size id names, before any draft halving. */
export function flybyMovieFrameSize(settings: Pick<FlybyMovieSettings, 'size'>): { width: number; height: number } {
  const size =
    MOVIE_SIZES.find((entry) => entry.id === settings.size) ??
    MOVIE_SIZES.find((entry) => entry.id === DEFAULT_FLYBY_MOVIE_SETTINGS.size)!;
  return { width: size.width, height: size.height };
}

function greatestCommonDivisor(a: number, b: number): number {
  return b === 0 ? a : greatestCommonDivisor(b, a % b);
}

/**
 * A draft's frame: about half the size, of exactly the same shape, with both sides even.
 *
 * Exactly the same shape because the scene's element is boxed to the movie's shape, and a frame
 * of another shape could not be taken off it. Even because a video encoder refuses an odd side —
 * so 480 × 270 halves to 224 × 126, not to 240 × 135.
 */
export function flybyDraftSize(width: number, height: number): { width: number; height: number } {
  const divisor = greatestCommonDivisor(width, height);
  const unitWidth = width / divisor;
  const unitHeight = height / divisor;
  // A multiple of the smallest box of the shape; an even multiple where that box has an odd side.
  const step = unitWidth % 2 === 0 && unitHeight % 2 === 0 ? 1 : 2;
  const times = Math.max(step, Math.floor(divisor / 2 / step) * step);
  return { width: unitWidth * times, height: unitHeight * times };
}

/** The file a movie of these settings is written as: what is encoded, and how each frame is taken. */
export interface FlybyMovieOutput {
  format: MovieFormat;
  width: number;
  height: number;
  fps: number;
  quality: MovieQuality;
  level: FlybyMovieLevel;
}

/** The settings as the recorder takes them, a draft made into its smaller, quicker self. */
export function flybyMovieOutput(settings: FlybyMovieSettings): FlybyMovieOutput {
  const full = flybyMovieFrameSize(settings);
  if (!settings.draft) {
    return { format: settings.format, ...full, fps: settings.fps, quality: settings.quality, level: settings.level };
  }
  return {
    format: settings.format,
    ...flybyDraftSize(full.width, full.height),
    fps: settings.format === 'gif' ? GIF_FRAME_RATES[0] : FLYBY_DRAFT_FPS,
    quality: settings.quality,
    level: 'quick',
  };
}

/** Which part of a flyby a movie is of, in seconds on the flyby's clock; left out, all of it. */
export interface FlybyStretch {
  from: number;
  to: number;
}

/**
 * The frames of a movie: when on the flyby's clock each one is.
 *
 * Frame `i` is at `from + i / fps` until the stretch's end, and the frames held at the end are all
 * at its end. A movie of `n` moving frames lasts `n / fps`, the length of the stretch to the
 * nearest frame; the end itself is shown by the held frames, or not at all when there are none —
 * as a clock stopped at the last tick never shows the next one.
 */
export interface FlybyFrameSchedule {
  from: number;
  to: number;
  fps: number;
  /** Frames of the flight. */
  moving: number;
  /** Frames the end is held for. */
  held: number;
  count: number;
  at(index: number): number;
}

export function flybyFrameSchedule(
  length: number,
  fps: number,
  stretch?: Partial<FlybyStretch>,
  holdEndS = 0,
): FlybyFrameSchedule {
  const whole = Math.max(0, length);
  const from = Math.min(Math.max(stretch?.from ?? 0, 0), whole);
  const to = Math.min(Math.max(stretch?.to ?? whole, from), whole);
  const moving = Math.max(1, Math.round((to - from) * fps));
  const held = Math.max(0, Math.round(holdEndS * fps));
  return {
    from,
    to,
    fps,
    moving,
    held,
    count: moving + held,
    at: (index) => (index < moving ? Math.min(to, from + index / fps) : to),
  };
}

/** Why a movie of these settings cannot be made as they stand; null when it can. */
export type FlybyMovieRefusal = { kind: 'gifTooLong'; frames: number; max: number } | null;

export function flybyMovieRefusal(output: FlybyMovieOutput, schedule: FlybyFrameSchedule): FlybyMovieRefusal {
  if (output.format === 'gif' && schedule.count > MOVIE_GIF_MAX_FRAMES) {
    return { kind: 'gifTooLong', frames: schedule.count, max: MOVIE_GIF_MAX_FRAMES };
  }
  return null;
}
