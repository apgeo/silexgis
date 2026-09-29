// SPDX-License-Identifier: AGPL-3.0-or-later
import { videoBitrate } from './encode/video/videoEncoder.ts';
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

/** Roughly how many bytes a movie of these settings and this many frames comes to. */
export function movieFileSizeEstimate(
  settings: Pick<MovieSettings, 'format' | 'size' | 'fps' | 'quality' | 'rotation'>,
  frameCount: number,
): number {
  const { width, height } = movieSize(settings as MovieSettings);
  const frames = Math.max(0, frameCount);
  if (settings.format === 'gif') {
    const pixels = width * height;
    const perPixel = settings.rotation.enabled ? GIF_TURNING_BYTES_PER_PIXEL : GIF_STILL_BYTES_PER_PIXEL;
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

/** The reader's own calendar day, as `yyyy-mm-dd` — the day they will say they made the file. */
export function localIsoDate(at: Date = new Date()): string {
  const month = String(at.getMonth() + 1).padStart(2, '0');
  const day = String(at.getDate()).padStart(2, '0');
  return `${at.getFullYear()}-${month}-${day}`;
}
