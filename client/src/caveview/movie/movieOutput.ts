// SPDX-License-Identifier: AGPL-3.0-or-later
import { videoBitrate } from './encode/video/videoEncoder.ts';
import { movieAutoTitle, movieTitle } from './movieCaptions.ts';
import type { MovieQuality, MovieSettings } from './movieSettings.ts';
import { movieSize } from './movieSettings.ts';

/**
 * What a movie's file will be before it is made: roughly how large, and what it is called.
 *
 * <b>The size is an estimate, and said to be one.</b> A video's is its encoder's target bitrate
 * over its length, which the encoder keeps to or undercuts on a mostly black scene. A GIF's cannot
 * be known without encoding it: every frame stores the pixels that changed since the one before, so
 * a turning camera, which changes nearly every pixel of the model, costs many times what a still
 * one does, where only the markers and captions move. The figures below are deliberately rough
 * averages for a survey drawn on the viewer's black background; they only have to be right to
 * within a factor that tells a reasonable movie from one that will not send.
 */

/**
 * Bytes per pixel per frame of a GIF whose camera turns, at the richest palette. Measured on a
 * centre-line survey turning at 6° a second, 800 × 450 at the middle palette: about 0.012. Set
 * a few times higher, because walls, splays and a denser survey change far more of each frame,
 * and an estimate that errs should err towards the warning.
 */
const GIF_TURNING_BYTES_PER_PIXEL = 0.05;
/** Bytes per pixel per frame of a GIF whose camera stands still: only markers and captions change. */
const GIF_STILL_BYTES_PER_PIXEL = 0.01;
/** Bytes per pixel of a GIF's first frame, which is stored whole. */
const GIF_FIRST_FRAME_BYTES_PER_PIXEL = 0.25;
/** How a smaller palette shrinks the frames: fewer colours make shorter codes and longer runs. */
const GIF_QUALITY_FACTOR: Record<MovieQuality, number> = { low: 0.6, medium: 0.8, high: 1 };
/** The header, the palette and the loop block, whatever the frames are. */
const GIF_OVERHEAD_BYTES = 1024;

/**
 * The GIF size past which the dialog warns. Messaging apps and forums that take a GIF commonly
 * refuse one past about ten megabytes, and a GIF that large also takes a while to open anywhere;
 * past it the reader is told to shorten it, shrink it, lower its rate or quality, or make a video.
 */
export const MOVIE_GIF_SIZE_BUDGET = 10 * 1024 * 1024;

/**
 * What the GIFs made in this browser have really cost, per pixel per frame at the richest palette:
 * one figure for a camera that turns and one for a camera that stands still, each present only once
 * a GIF of that kind has been made here.
 *
 * <b>Why the estimate is corrected from the files themselves.</b> The built-in figures are one
 * guess for every cave. What a frame really costs follows the survey — a dense one with walls
 * changes several times the pixels of a bare centre line — the number of markers moving, and the
 * captions; none of that is known before a GIF is encoded, and all of it is much the same from one
 * movie to the next for a reader who films the same few caves. The last file says what the next
 * one of its kind will cost better than any constant can.
 */
export interface MovieGifCalibration {
  turning?: number;
  still?: number;
}

/**
 * The least and the most a measured figure is believed. Outside them the measurement is of
 * something else — a movie of an empty model, a file cut short — and one such file must not be
 * able to switch the size warning off, or on, for every movie after it.
 */
const GIF_CALIBRATION_MIN = 0.002;
const GIF_CALIBRATION_MAX = 0.5;
/** A GIF of fewer frames than this says too little about what a frame costs to be learnt from. */
const GIF_CALIBRATION_MIN_FRAMES = 10;
/**
 * How many times the reckoned first frame a GIF's later frames must come to before it is learnt
 * from.
 *
 * <b>The first frame is reckoned, not measured, and reckoned high.</b> A first frame of a survey
 * on black really costs about a fifth of the figure above (measured: 0.049 bytes a pixel at
 * 640 × 360), and whatever the reckoning is out by is taken from the later frames and shared
 * between them. In a GIF of two or three seconds that is most of what the later frames hold: a
 * ten-frame trial taught less than a third of what a frame cost, and every estimate after it was
 * several times too small — the direction that keeps the size warning from showing. Asked for four
 * times the reckoned first frame, the later frames can be put out by no more than about a fifth of
 * themselves, and a GIF too short for that is one more file that says too little.
 */
const GIF_CALIBRATION_MIN_LATER_SHARE = 4;

/** A figure read back from storage: kept within the believed range, or nothing when it is no figure at all. */
function believableGifFigure(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
    ? Math.min(GIF_CALIBRATION_MAX, Math.max(GIF_CALIBRATION_MIN, value))
    : undefined;
}

/**
 * A stored calibration made safe to use: whatever was read back — from an older version, or edited
 * by hand — comes out as figures within the believed range, or as nothing.
 */
export function normaliseMovieGifCalibration(stored: unknown): MovieGifCalibration {
  const from = (typeof stored === 'object' && stored !== null ? stored : {}) as Record<string, unknown>;
  const turning = believableGifFigure(from.turning);
  const still = believableGifFigure(from.still);
  return {
    ...(turning === undefined ? {} : { turning }),
    ...(still === undefined ? {} : { still }),
  };
}

/**
 * The calibration after a GIF of these settings and this many frames came to `bytes`.
 *
 * The estimate's own sum is turned round: what the file holds beyond its header and its first
 * frame, over the pixels of its later frames, at the palette it was made with. A figure past the
 * most that is believed is taken as that most — erring towards the warning — and the result is
 * <b>averaged with the one remembered</b>, so a single unusual movie moves the estimate half-way
 * and no further.
 *
 * Returned unchanged when the file says nothing usable: too few frames; later frames that come to
 * too little beside the reckoned first one for the reckoning's own error not to decide the figure;
 * or a figure below the least that is believed, which is a movie of next to nothing and must not
 * teach that a frame costs next to nothing.
 */
export function movieGifCalibrationFrom(
  bytes: number,
  settings: Pick<MovieSettings, 'size' | 'quality' | 'rotation'>,
  frameCount: number,
  before: MovieGifCalibration = {},
): MovieGifCalibration {
  const { width, height } = movieSize(settings as MovieSettings);
  const pixels = width * height;
  const firstFrame = pixels * GIF_FIRST_FRAME_BYTES_PER_PIXEL;
  const later = bytes - GIF_OVERHEAD_BYTES - firstFrame;
  if (
    !Number.isFinite(bytes)
    || frameCount < GIF_CALIBRATION_MIN_FRAMES
    || pixels <= 0
    || later < GIF_CALIBRATION_MIN_LATER_SHARE * firstFrame
  ) {
    return before;
  }
  const figure = later / ((frameCount - 1) * pixels * GIF_QUALITY_FACTOR[settings.quality]);
  if (!Number.isFinite(figure) || figure < GIF_CALIBRATION_MIN) {
    return before;
  }
  const measured = Math.min(GIF_CALIBRATION_MAX, figure);
  const kind = settings.rotation.enabled ? 'turning' : 'still';
  const held = believableGifFigure(before[kind]);
  return { ...before, [kind]: held === undefined ? measured : (held + measured) / 2 };
}

/** Whether a GIF of these settings is estimated from a file made here rather than from the built-in figure. */
export function movieEstimateIsCalibrated(
  settings: Pick<MovieSettings, 'format' | 'rotation'>,
  calibration: MovieGifCalibration | undefined,
): boolean {
  return (
    settings.format === 'gif'
    && believableGifFigure(calibration?.[settings.rotation.enabled ? 'turning' : 'still']) !== undefined
  );
}

/**
 * Roughly how many bytes a movie of these settings and this many frames comes to.
 *
 * @param calibration what GIFs made in this browser have cost; where it has a figure for this kind
 *   of movie the figure replaces the built-in one.
 */
export function movieFileSizeEstimate(
  settings: Pick<MovieSettings, 'format' | 'size' | 'fps' | 'quality' | 'rotation'>,
  frameCount: number,
  calibration?: MovieGifCalibration,
): number {
  const { width, height } = movieSize(settings as MovieSettings);
  const frames = Math.max(0, frameCount);
  if (settings.format === 'gif') {
    const pixels = width * height;
    const perPixel = settings.rotation.enabled
      ? (believableGifFigure(calibration?.turning) ?? GIF_TURNING_BYTES_PER_PIXEL)
      : (believableGifFigure(calibration?.still) ?? GIF_STILL_BYTES_PER_PIXEL);
    const later = Math.max(0, frames - 1) * pixels * perPixel * GIF_QUALITY_FACTOR[settings.quality];
    return Math.round(GIF_OVERHEAD_BYTES + (frames > 0 ? pixels * GIF_FIRST_FRAME_BYTES_PER_PIXEL : 0) + later);
  }
  const seconds = frames / settings.fps;
  return Math.round((videoBitrate(width, height, settings.fps, settings.quality) * seconds) / 8);
}

/** The most characters of a movie's title that go into its file name. */
const SLUG_MAX_LENGTH = 60;

/**
 * A file name's part made of a title: letters and digits only, lower case, accents dropped, runs
 * of anything else made one hyphen — so it survives every file system and every messaging app
 * that renames what it is sent. Empty when nothing of the title is left.
 */
export function movieSlug(title: string): string {
  return title
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, SLUG_MAX_LENGTH)
    .replace(/-+$/g, '');
}

/**
 * What a saved movie is called: `silexgis-<title>-<date>.<extension>`, the date being the day it
 * was made, as the map's saved image is named. A title with nothing usable in it is called `movie`.
 *
 * @param isoDate the day, as `yyyy-mm-dd`.
 */
export function movieFileName(title: string, isoDate: string, extension: string): string {
  const slug = movieSlug(title);
  return `silexgis-${slug.length > 0 ? slug : 'movie'}-${isoDate}.${extension}`;
}

/**
 * What the file of a movie is called, decided in this one place for the name the dialog shows
 * before an export and the name the export is saved under.
 *
 * <b>The file is named by the title the movie shows, and by nothing it does not show.</b> With the
 * title caption on, that is the reader's own words when they wrote any, and otherwise what the
 * movie is of — its one trip, or the cave. With the caption off the file is called `movie` and the
 * day: a reader who took the title off the picture has said the movie should not say whose trip or
 * which cave it is, and a file name travels with the file to everyone it is sent on to — further
 * than the picture's own caption, since it shows in a chat before the movie is even opened.
 *
 * The days an automatic title of several trips ends in are left out of the name: it already ends
 * in the day the file was made, and a second date beside it, in the reader's own order (9-29-2026,
 * 29-09-2026), reads as neither.
 *
 * @param place the cave's name, or the model's when the cave's is not known.
 * @param isoDate the day the file is made, as `yyyy-mm-dd`.
 */
export function movieExportName(
  captions: Pick<MovieSettings['captions'], 'title' | 'titleText'>,
  tripTitles: readonly string[],
  place: string,
  isoDate: string,
  extension: string,
): string {
  const shown = movieTitle(captions, movieAutoTitle(tripTitles, place, null));
  return movieFileName(shown ?? '', isoDate, extension);
}

/** The reader's own calendar day, as `yyyy-mm-dd` — the day they will say they made the file. */
export function localIsoDate(at: Date = new Date()): string {
  const month = String(at.getMonth() + 1).padStart(2, '0');
  const day = String(at.getDate()).padStart(2, '0');
  return `${at.getFullYear()}-${month}-${day}`;
}
