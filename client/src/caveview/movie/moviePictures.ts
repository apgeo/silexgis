// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReplayPicture } from '../trackingReplay.ts';
import type { MovieSettings } from './movieSettings.ts';
import type { MovieFrame, MovieTimeline } from './movieTimeline.ts';

/**
 * The photographs hung on a trip's moments, shown in a movie of that trip: when each comes up,
 * how strongly it is drawn, and where on the frame.
 *
 * <b>A picture is shown for a length of the movie, not of the trip.</b> A trip's clock runs
 * hundreds of times faster than life in a movie, so a picture kept up for as long as the party
 * stood where it was taken would be on screen for two frames. The reader says how many seconds of
 * the movie each picture is given; it comes up on the first frame that has reached its moment and
 * stays for that long, while the clock and the party go on underneath it.
 *
 * <b>One at a time, in the order they were taken.</b> Two pictures of one moment, or of moments the
 * movie reaches within one showing of each other, are shown one after the other rather than over
 * each other: the second waits for the first to end. What the end of the movie cuts short is cut
 * short, and what would start after the last frame is not shown — the movie is as long as the
 * reader made it.
 *
 * <b>Everything here is a function of the frame's number</b>, like the rest of a movie: the
 * schedule is worked out once from the frame schedule, and a frame that took a second to render
 * shows the picture exactly as long as one that took a millisecond.
 */

/** One photograph on one moment of one of a movie's trips. */
export interface MoviePicture {
  /** What the picture's image is kept under: the address it is loaded from. */
  key: string;
  tripLogId: string;
  /** The instant it was hung on, epoch milliseconds. */
  at: number;
  /** Who it is about, where the link names exactly one person. */
  caverId: string | null;
  caption: string | null;
}

/** A picture and the frames it is on: `from` inclusive, `to` exclusive. */
export interface MoviePictureShowing {
  picture: MoviePicture;
  from: number;
  to: number;
}

/** A trip's moment pictures as a movie takes them. */
export function moviePicturesOf(tripLogId: string, pictures: readonly ReplayPicture[]): MoviePicture[] {
  return pictures.map((picture) => ({
    key: picture.entry.url,
    tripLogId,
    at: picture.at,
    caverId: picture.caverId,
    caption: picture.entry.caption ?? null,
  }));
}

/** How many frames a picture is given, at least one. */
export function moviePictureFrames(settings: Pick<MovieSettings, 'fps' | 'pictures'>): number {
  return Math.max(1, Math.round(settings.pictures.seconds * settings.fps));
}

/**
 * When each picture is on screen.
 *
 * `tripLogIds` is the movie's trips in the order the timeline was built from, which is the order
 * {@link MovieTimeline.instants} answers in. `shows` says whether a picture about somebody is of
 * somebody the movie shows: a person the reader left out of the movie is left out of its pictures
 * too, while a picture about nobody in particular — the party, a place — is always shown.
 */
export function moviePictureSchedule(
  pictures: readonly MoviePicture[],
  tripLogIds: readonly string[],
  timeline: Pick<MovieTimeline, 'instants'>,
  frames: { count: number; frame(index: number): Pick<MovieFrame, 'position'> },
  settings: Pick<MovieSettings, 'fps' | 'pictures'>,
  shows: (tripLogId: string, caverId: string) => boolean = () => true,
): MoviePictureShowing[] {
  if (settings.pictures.mode === 'off' || frames.count === 0) {
    return [];
  }
  const tripIndex = new Map(tripLogIds.map((id, index) => [id, index]));
  const wanted = pictures
    .filter((picture) => tripIndex.has(picture.tripLogId))
    .filter((picture) => picture.caverId === null || shows(picture.tripLogId, picture.caverId));
  if (wanted.length === 0) {
    return [];
  }

  // The first frame each picture's moment has been reached on. Every trip's instant only ever
  // moves forward along the frames, so one pass over the frames places every picture.
  const due = new Map<MoviePicture, number>();
  const waiting = [...wanted].sort((left, right) => left.at - right.at);
  for (let index = 0; index < frames.count && due.size < waiting.length; index++) {
    const instants = timeline.instants(frames.frame(index).position);
    for (const picture of waiting) {
      if (!due.has(picture) && instants[tripIndex.get(picture.tripLogId)!] >= picture.at) {
        due.set(picture, index);
      }
    }
  }

  const length = moviePictureFrames(settings);
  const showings: MoviePictureShowing[] = [];
  let free = 0;
  // In the order the movie reaches them, and within one frame in the order they were taken.
  const reached = waiting
    .filter((picture) => due.has(picture))
    .sort((left, right) => due.get(left)! - due.get(right)! || left.at - right.at);
  for (const picture of reached) {
    const from = Math.max(due.get(picture)!, free);
    if (from >= frames.count) {
      break;
    }
    const to = Math.min(from + length, frames.count);
    showings.push({ picture, from, to });
    free = to;
  }
  return showings;
}

/**
 * The picture on a frame and how strongly it is drawn there (0..1), or null when the frame has
 * none. With fading on, a picture comes up and goes down over two fifths of a second, or a third
 * of its own showing where that is shorter; a showing cut short by the end of the movie does not
 * fade out, since there is no frame after it to fade towards.
 */
export function moviePictureAt(
  schedule: readonly MoviePictureShowing[],
  index: number,
  settings: Pick<MovieSettings, 'fps' | 'pictures'>,
): { picture: MoviePicture; alpha: number } | null {
  const showing = schedule.find((entry) => index >= entry.from && index < entry.to);
  if (showing === undefined) {
    return null;
  }
  if (!settings.pictures.fade) {
    return { picture: showing.picture, alpha: 1 };
  }
  const length = showing.to - showing.from;
  const ramp = Math.min(Math.round(0.4 * settings.fps), Math.floor(length / 3));
  if (ramp < 1) {
    return { picture: showing.picture, alpha: 1 };
  }
  const since = index - showing.from;
  const left = showing.to - 1 - index;
  const whole = length === moviePictureFrames(settings);
  const up = Math.min(1, (since + 1) / (ramp + 1));
  const down = whole ? Math.min(1, (left + 1) / (ramp + 1)) : 1;
  return { picture: showing.picture, alpha: Math.min(up, down) };
}

/** Anything a 2D canvas draws that says its own size: an image element, a bitmap, a canvas. */
export type MoviePictureImage = CanvasImageSource & { width: number; height: number };

const INK = '#ffffff';
const PLATE = '#000000';
const FONT_FAMILY = 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Arial, sans-serif';

/** Where on a frame a picture is drawn, in frame pixels. */
export function moviePictureBox(
  width: number,
  height: number,
  image: { width: number; height: number },
  pictures: MovieSettings['pictures'],
): { x: number; y: number; width: number; height: number } {
  const shape = image.width > 0 && image.height > 0 ? image.width / image.height : 4 / 3;
  if (pictures.mode === 'full') {
    // As large as fits, whole: a photograph cropped to the frame's shape loses the person it is of.
    const scale = Math.min(width / shape, height);
    const boxHeight = Math.round(scale);
    const boxWidth = Math.round(scale * shape);
    return { x: Math.round((width - boxWidth) / 2), y: Math.round((height - boxHeight) / 2), width: boxWidth, height: boxHeight };
  }
  // A third of the frame's width, and no taller than two fifths of it, clear of the edge by the
  // captions' own margin.
  const margin = Math.max(4, Math.round(height * 0.025));
  let boxWidth = Math.round(width * 0.34);
  let boxHeight = Math.round(boxWidth / shape);
  const tallest = Math.round(height * 0.42);
  if (boxHeight > tallest) {
    boxHeight = tallest;
    boxWidth = Math.round(boxHeight * shape);
  }
  // Above the progress bar and below the clock and the title, which have the frame's edges.
  const clear = Math.round(height * 0.085);
  const left = pictures.corner === 'bottomLeft' || pictures.corner === 'topLeft';
  const top = pictures.corner === 'topLeft' || pictures.corner === 'topRight';
  return {
    x: left ? margin : width - margin - boxWidth,
    y: top ? margin + clear : height - margin - Math.round(height * 0.02) - boxHeight,
    width: boxWidth,
    height: boxHeight,
  };
}

/**
 * Draws one picture on a frame.
 *
 * Over the whole frame it stands on a darkened drawing, so that the model does not show through
 * the bars either side of a tall photograph; in a corner it has a thin light edge, which is what
 * tells it from the drawing behind it. Its caption, where asked for and where it has one, is a
 * line along its bottom edge, cut to fit.
 */
export function drawMoviePicture(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  image: MoviePictureImage,
  alpha: number,
  pictures: MovieSettings['pictures'],
  caption: string | null,
  captionSize = 1,
): void {
  if (pictures.mode === 'off' || !(alpha > 0)) {
    return;
  }
  const box = moviePictureBox(width, height, image, pictures);
  ctx.save();
  ctx.globalAlpha = Math.min(1, alpha);
  if (pictures.mode === 'full') {
    ctx.fillStyle = PLATE;
    ctx.globalAlpha = Math.min(1, alpha) * 0.88;
    ctx.fillRect(0, 0, width, height);
    ctx.globalAlpha = Math.min(1, alpha);
  } else {
    const edge = Math.max(1, Math.round(height * 0.004));
    ctx.fillStyle = INK;
    ctx.fillRect(box.x - edge, box.y - edge, box.width + 2 * edge, box.height + 2 * edge);
  }
  ctx.drawImage(image, box.x, box.y, box.width, box.height);

  if (pictures.captions && caption !== null && caption.trim().length > 0) {
    const size = Number.isFinite(captionSize) && captionSize > 0 ? captionSize : 1;
    const fontPx = Math.max(8, Math.round(height * (pictures.mode === 'full' ? 0.036 : 0.028) * size));
    const pad = Math.max(2, Math.round(fontPx * 0.35));
    const line = Math.round(fontPx * 1.4);
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    ctx.textBaseline = 'middle';
    ctx.textAlign = 'left';
    let text = caption.trim();
    const room = box.width - 2 * pad;
    if (ctx.measureText(text).width > room) {
      while (text.length > 1 && ctx.measureText(`${text}…`).width > room) {
        text = text.slice(0, -1);
      }
      text = `${text.trimEnd()}…`;
    }
    ctx.globalAlpha = Math.min(1, alpha) * 0.6;
    ctx.fillStyle = PLATE;
    ctx.fillRect(box.x, box.y + box.height - line, box.width, line);
    ctx.globalAlpha = Math.min(1, alpha);
    ctx.fillStyle = INK;
    ctx.fillText(text, box.x + pad, box.y + box.height - line / 2);
  }
  ctx.restore();
}

/**
 * Draws whatever picture a frame has, if its image is in hand. A picture whose image did not load
 * is left out of the frame rather than failing the movie: the movie is of the trip, and a
 * photograph the server would not hand over is one caption short of it, not a reason for no file.
 */
export function drawMoviePictureAt(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  schedule: readonly MoviePictureShowing[],
  index: number,
  settings: Pick<MovieSettings, 'fps' | 'pictures' | 'captions'>,
  images: ReadonlyMap<string, MoviePictureImage>,
): void {
  const shown = moviePictureAt(schedule, index, settings);
  if (shown === null) {
    return;
  }
  const image = images.get(shown.picture.key);
  if (image === undefined) {
    return;
  }
  drawMoviePicture(ctx, width, height, image, shown.alpha, settings.pictures, shown.picture.caption, settings.captions.size);
}
