// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { MOVIE_MARKER_PALETTE, type MovieLegendEntry, type MovieParty } from './movieParty.ts';
import type { MovieSettings } from './movieSettings.ts';
import { movieSpeedFactor, type MovieClock, type MovieFrame, type MovieTimeline } from './movieTimeline.ts';

/**
 * The words and marks an exported movie carries over the model: a title, the replay clock, the
 * legend of marker colours, a progress bar and the note in force.
 *
 * <b>Nothing drawn here is ever a coordinate or an altitude.</b> A movie is passed on well beyond
 * the reader it was made for, and the model it shows is already the most a reader of the trip may
 * see; the captions say only what a reader of the trip could read on its page — titles, the moment
 * being replayed, team titles, caver labels, and a note somebody wrote.
 *
 * <b>Drawn with the system's own sans-serif</b>, not the viewer's glyph atlas: a trip title or a
 * note can be written in any script, and only the platform's fonts are sure to hold it.
 */

export interface MovieCaptions {
  title: string | null;
  clock: string | null;
  /**
   * The clock with the time-lapse figure after it — the whole text, "… · ×240" — or null when no
   * figure applies. It is drawn in place of `clock` only when all of it fits the room the clock has:
   * a figure cut short is a different figure ("×2…" for ×240), so where the two do not fit the clock
   * stands alone, exactly as it would without the figure.
   */
  clockSpeed?: string | null;
  legend: MovieLegendEntry[];
  /**
   * The line that stands in for the legend entries a frame has no room for, given how many were
   * left out. Without it the line reads `+N`.
   */
  legendMore?: (hidden: number) => string;
  /** 0..1, or null for no progress bar. */
  progress: number | null;
  note: string | null;
  /** The reader's caption size, a multiplier on sizes that otherwise follow the frame height. */
  size: number;
}

const FONT_FAMILY = 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Arial, sans-serif';
const INK = '#ffffff';
/** The plate's colour; it is drawn translucent, at {@link PLATE_ALPHA}. */
const PLATE = '#000000';
const PLATE_ALPHA = 0.6;
const PROGRESS_TRACK = '#555555';
const PROGRESS_FILL = '#ffffff';
/** The most lines a note is wrapped into; what does not fit ends in an ellipsis. */
const NOTE_LINES = 3;
const ELLIPSIS = '…';

/**
 * Every colour the captions can draw as a solid colour, for a GIF to reserve exactly.
 *
 * The plate is listed by its own colour although it is drawn translucent: what it becomes over the
 * model is a mixture, which the GIF's palette approximates like any other pixel of the scene.
 */
export function movieCaptionColors(): string[] {
  return [
    ...new Set([
      INK,
      PLATE,
      PROGRESS_TRACK,
      PROGRESS_FILL,
      ...MOVIE_MARKER_PALETTE,
      trackedCaverPalette.out,
    ]),
  ];
}

/**
 * The legend's lines when only `room` of them fit.
 *
 * <b>A legend that runs out of room says so.</b> Cut silently, a movie of twenty trips showed the
 * first ten and nothing else: the other ten trips' markers moved about in colours no line explained,
 * and the key to the grey of somebody who has come out — added after every trip — was the first line
 * dropped. So the entries that explain a kind of marker rather than name a trip or a team
 * (`pinned`) are kept whatever else is cut, and the last line left for the rest says how many of
 * them are not shown.
 */
function legendRows(
  legend: readonly MovieLegendEntry[],
  room: number,
  more: ((hidden: number) => string) | undefined,
): { color: string | null; label: string }[] {
  if (legend.length <= room) {
    return [...legend];
  }
  const pinned = legend.filter((entry) => entry.pinned === true);
  const rest = legend.filter((entry) => entry.pinned !== true);
  const restRoom = Math.max(0, room - pinned.length - 1);
  const hidden = rest.length - restRoom;
  const moreLine = { color: null, label: more === undefined ? `+${hidden}` : more(hidden) };
  return [...rest.slice(0, restRoom), moreLine, ...pinned].slice(0, room);
}

/** Text cut to fit a width, ending in an ellipsis where it was cut. */
function fitted(ctx: CanvasRenderingContext2D, text: string, width: number): string {
  if (ctx.measureText(text).width <= width) {
    return text;
  }
  const characters = Array.from(text);
  let low = 0;
  let high = characters.length;
  while (low < high) {
    const middle = Math.ceil((low + high) / 2);
    if (ctx.measureText(characters.slice(0, middle).join('') + ELLIPSIS).width <= width) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }
  return characters.slice(0, low).join('') + ELLIPSIS;
}

/** A text wrapped at spaces into at most `lines` lines of `width`, the last cut where it overflows. */
function wrapped(ctx: CanvasRenderingContext2D, text: string, width: number, lines: number): string[] {
  const out: string[] = [];
  let current = '';
  const words = text.replace(/\s+/gu, ' ').trim().split(' ');
  for (let index = 0; index < words.length; index++) {
    const candidate = current.length === 0 ? words[index] : `${current} ${words[index]}`;
    if (ctx.measureText(candidate).width <= width || current.length === 0) {
      current = candidate;
      continue;
    }
    out.push(current);
    current = words[index];
    if (out.length === lines - 1) {
      current = words.slice(index).join(' ');
      break;
    }
  }
  if (current.length > 0) {
    out.push(current);
  }
  return out.slice(0, lines).map((line) => fitted(ctx, line, width));
}

function plate(ctx: CanvasRenderingContext2D, x: number, y: number, width: number, height: number): void {
  ctx.save();
  ctx.globalAlpha = PLATE_ALPHA;
  ctx.fillStyle = PLATE;
  ctx.fillRect(Math.round(x), Math.round(y), Math.round(width), Math.round(height));
  ctx.restore();
}

/**
 * Where on a frame the captions are drawn, and how they are laid out there.
 *
 * <b>Sized from the frame's shorter side</b>, so a caption covers the same share of a small GIF as of
 * a full-size video, and a tall frame for a phone gets text the size a wide one of the same width
 * would — not text twice as tall because the frame is.
 *
 * <b>A frame taller than it is wide stacks the clock under the title</b>: side by side, on a frame
 * that narrow, each would leave the other a sliver.
 *
 * <b>A vertical frame for short-video apps (9:16, or taller) keeps its captions out of what those
 * apps draw over a video</b> — the bar along the top, the description and buttons along the bottom,
 * the column of buttons down the right. The bands are the ones the apps themselves ask creators to
 * keep text out of, rounded to the most cautious of them (Reels, TikTok, Shorts): about an eighth at
 * the top, a little over a fifth at the bottom, an eighth on the right. A caption drawn there would be
 * covered the moment the file is posted.
 */
export interface MovieCaptionArea {
  top: number;
  bottom: number;
  left: number;
  right: number;
  /** The clock goes under the title rather than beside it. */
  stacked: boolean;
  /** The length every caption size is measured against. */
  base: number;
}

/** The frames short-video apps play full screen: 9:16, or taller. */
const SHORT_VIDEO_TALLNESS = 1.7;

export function movieCaptionArea(width: number, height: number): MovieCaptionArea {
  const base = Math.min(width, height);
  const margin = Math.max(4, Math.round(base * 0.025));
  if (height / width >= SHORT_VIDEO_TALLNESS) {
    return {
      top: Math.round(height * 0.12),
      bottom: Math.round(height * 0.22),
      left: Math.max(margin, Math.round(width * 0.06)),
      right: Math.max(margin, Math.round(width * 0.14)),
      stacked: true,
      base,
    };
  }
  return { top: margin, bottom: 0, left: margin, right: margin, stacked: height > width, base };
}

/**
 * Draws the captions over a frame that already holds the model.
 *
 * Laid out in {@link movieCaptionArea}: the title at the top left with the legend under it, the clock
 * at the top right (under the title on a tall frame), the note centred at the bottom, and the progress
 * bar along the bottom of the area. Each text sits on a translucent plate so it reads over a bright
 * model and a black background alike.
 */
export function drawMovieCaptions(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  captions: MovieCaptions,
): void {
  const area = movieCaptionArea(width, height);
  const size = Number.isFinite(captions.size) && captions.size > 0 ? captions.size : 1;
  const fontPx = Math.max(8, Math.round(area.base * 0.042 * size));
  const pad = Math.max(2, Math.round(fontPx * 0.35));
  const margin = Math.max(4, Math.round(area.base * 0.025));
  const lineHeight = Math.round(fontPx * 1.3);
  const barHeight = captions.progress === null ? 0 : Math.max(2, Math.round(area.base * 0.008 * size));
  const innerWidth = width - area.left - area.right;
  const floor = height - area.bottom;

  ctx.save();
  ctx.textBaseline = 'middle';
  ctx.textAlign = 'left';

  let top = area.top;
  let clockWidth = 0;
  let clockText: string | null = null;
  if (captions.clock !== null && captions.clock.length > 0) {
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    const room = (area.stacked ? innerWidth : innerWidth / 2) - 2 * pad;
    const withSpeed = captions.clockSpeed ?? null;
    // The figure is all or nothing: it is written only where the clock and it both fit whole.
    clockText =
      withSpeed !== null && withSpeed.length > 0 && ctx.measureText(withSpeed).width <= room
        ? withSpeed
        : fitted(ctx, captions.clock, room);
    clockWidth = ctx.measureText(clockText).width + 2 * pad;
  }
  const drawClock = (x: number, y: number) => {
    if (clockText === null) return;
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    plate(ctx, x, y, clockWidth, lineHeight);
    ctx.fillStyle = INK;
    ctx.fillText(clockText, x + pad, y + lineHeight / 2);
  };
  if (!area.stacked) {
    drawClock(width - area.right - clockWidth, area.top);
  }

  if (captions.title !== null && captions.title.length > 0) {
    ctx.font = `bold ${fontPx}px ${FONT_FAMILY}`;
    const beside = !area.stacked && clockWidth > 0 ? clockWidth + margin : 0;
    const room = innerWidth - beside - 2 * pad;
    const text = fitted(ctx, captions.title, Math.max(room, fontPx));
    const plateWidth = ctx.measureText(text).width + 2 * pad;
    plate(ctx, area.left, top, plateWidth, lineHeight);
    ctx.fillStyle = INK;
    ctx.fillText(text, area.left + pad, top + lineHeight / 2);
    top += lineHeight + Math.round(margin / 2);
  }
  if (area.stacked && clockText !== null) {
    drawClock(area.left, top);
    top += lineHeight + Math.round(margin / 2);
  }

  if (captions.legend.length > 0) {
    const legendPx = Math.max(8, Math.round(fontPx * 0.85));
    const legendLine = Math.round(legendPx * 1.35);
    const swatch = Math.round(legendPx * 0.7);
    ctx.font = `${legendPx}px ${FONT_FAMILY}`;
    const labelRoom = (area.stacked ? innerWidth : innerWidth / 2) - 3 * pad - swatch;
    // As many entries as fit above the note's area; a legend never runs off the frame.
    const limit = area.bottom > 0 ? area.top + (floor - area.top) * 0.6 : height * 0.6;
    const room = Math.max(0, Math.floor((limit - top) / legendLine));
    const rows = legendRows(captions.legend, room, captions.legendMore);
    const labels = rows.map((row) => fitted(ctx, row.label, labelRoom));
    if (rows.length > 0) {
      const plateWidth =
        Math.max(...labels.map((label) => ctx.measureText(label).width)) + swatch + 3 * pad;
      plate(ctx, area.left, top, plateWidth, rows.length * legendLine + pad);
      rows.forEach((row, index) => {
        const middle = top + pad / 2 + index * legendLine + legendLine / 2;
        if (row.color !== null) {
          ctx.fillStyle = row.color;
          ctx.fillRect(area.left + pad, Math.round(middle - swatch / 2), swatch, swatch);
        }
        ctx.fillStyle = INK;
        ctx.fillText(labels[index], area.left + 2 * pad + swatch, middle);
      });
    }
  }

  if (captions.note !== null && captions.note.length > 0) {
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    const lines = wrapped(ctx, captions.note, (area.bottom > 0 ? innerWidth * 0.9 : width * 0.8) - 2 * pad, NOTE_LINES);
    const blockWidth = Math.max(...lines.map((line) => ctx.measureText(line).width)) + 2 * pad;
    const blockHeight = lines.length * lineHeight;
    const centre = area.left + innerWidth / 2;
    const x = centre - blockWidth / 2;
    const y = floor - (area.bottom > 0 ? 0 : margin) - barHeight - blockHeight;
    plate(ctx, x, y, blockWidth, blockHeight);
    ctx.fillStyle = INK;
    ctx.textAlign = 'center';
    lines.forEach((line, index) => {
      ctx.fillText(line, centre, y + index * lineHeight + lineHeight / 2);
    });
    ctx.textAlign = 'left';
  }

  if (captions.progress !== null) {
    const progress = Math.min(1, Math.max(0, Number.isFinite(captions.progress) ? captions.progress : 0));
    // Along the bottom edge; on a short-video frame, along the bottom of what the app leaves visible.
    const barLeft = area.bottom > 0 ? area.left : 0;
    const barWidth = area.bottom > 0 ? innerWidth : width;
    ctx.fillStyle = PROGRESS_TRACK;
    ctx.fillRect(barLeft, floor - barHeight, barWidth, barHeight);
    ctx.fillStyle = PROGRESS_FILL;
    ctx.fillRect(barLeft, floor - barHeight, Math.round(barWidth * progress), barHeight);
  }

  ctx.restore();
}

/** An hour, past which an elapsed clock counts hours and minutes rather than minutes and seconds. */
const HOUR_MS = 3_600_000;

/**
 * What the clock caption says at a point of the timeline: the moment being replayed, in the
 * reader's language and time zone, or — trips played side by side — how long they have been under
 * way.
 *
 * Time under way is written hours:minutes when the movie's clock runs to an hour or more, and
 * minutes:seconds when all of it is under an hour: a forty-minute trip counted in whole minutes
 * showed forty values over the whole movie and stood still between them, as if the replay had
 * stopped. The unit is chosen by how far the clock will run, not by where it is, so one movie's
 * clock never changes unit on the way.
 */
export function movieClockText(clock: MovieClock, t: TFunction, language: string): string {
  if (clock.kind === 'calendar') {
    return new Date(clock.at).toLocaleString(language, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
  // Counted in the smaller unit of the pair — seconds under an hour, minutes otherwise — and
  // written as that count over sixty, and what is left of it.
  const small = Math.max(0, Math.floor(clock.ms / (clock.totalMs < HOUR_MS ? 1000 : 60_000)));
  const time = `${Math.floor(small / 60)}:${String(small % 60).padStart(2, '0')}`;
  return t('caveview.movie.clockElapsed', { time });
}

/**
 * A time-lapse factor as the clock caption writes it — `240` of "×240" — or null when the movie runs
 * at about the pace things happened at, where the figure would say nothing.
 *
 * <b>Rounded to what a reader can use.</b> The figure answers "how much faster than life is this?",
 * and nobody reads 237 differently from 240: from a hundred up it is given to the nearest ten, from
 * ten up as a whole number, below that to one decimal — and below one, where one decimal would turn
 * a third into 0.3 and a twentieth into nothing at all, to two figures that mean something. Rounded
 * first and written second, so the cut between the forms is made on the number shown: 99.7 is
 * written 100, never 99.7 rounded as a small number.
 *
 * Within a twentieth of life speed nothing is written. "×1" on a movie that plays as it happened is
 * a caption about the absence of something.
 *
 * Written in the reader's language, since the decimal mark is not the same in all of them.
 */
export function movieSpeedText(factor: number | null, language: string): string | null {
  if (factor === null || !Number.isFinite(factor) || factor <= 0 || (factor >= 0.95 && factor <= 1.05)) {
    return null;
  }
  const whole = Math.round(factor);
  if (whole >= 100) {
    return (Math.round(factor / 10) * 10).toLocaleString(language, { maximumFractionDigits: 0 });
  }
  if (Math.round(factor * 10) / 10 >= 10) {
    return whole.toLocaleString(language, { maximumFractionDigits: 0 });
  }
  if (factor >= 1) {
    return factor.toLocaleString(language, { maximumFractionDigits: 1 });
  }
  return factor.toLocaleString(language, { maximumSignificantDigits: 2 });
}

/**
 * The clock caption with the time-lapse factor after it — "… · ×240" — or null when there is no
 * factor to write. The factor is never drawn by itself: it qualifies the clock, and beside nothing
 * it is a bare number, so the only form it has is the clock's own text with it appended.
 */
export function movieClockWithSpeed(
  clock: string,
  factor: number | null,
  t: TFunction,
  language: string,
): string | null {
  const speed = movieSpeedText(factor, language);
  return speed === null ? null : t('caveview.movie.clockSpeed', { clock, factor: speed });
}

/**
 * The title a movie is given when the reader wrote none.
 *
 * One trip is called by its own title. Several are called by where and when: the place, and the
 * days the trips span. Their titles strung together would run off the frame by the third trip,
 * and would say less than the place and the days do. A lone trip with a blank title falls back to
 * the same place-and-days form.
 *
 * @param place the cave's name, or the model's when the cave's is not known.
 * @param days the days the trips span as the reader writes them, or null when none is dated.
 */
export function movieAutoTitle(tripTitles: readonly string[], place: string, days: string | null): string {
  if (tripTitles.length === 1 && tripTitles[0].trim().length > 0) {
    return tripTitles[0].trim();
  }
  return [place.trim(), days?.trim() ?? '']
    .filter((part) => part.length > 0)
    .join(' · ');
}

/**
 * The title a movie carries: the reader's own words when they wrote any, else the automatic one.
 * Null when the title caption is off.
 */
export function movieTitle(
  captions: Pick<MovieSettings['captions'], 'title' | 'titleText'>,
  autoTitle: string,
): string | null {
  if (!captions.title) {
    return null;
  }
  const own = captions.titleText.trim();
  return own.length > 0 ? own : autoTitle;
}

/**
 * The captions of one frame, with what the reader switched off left out — the one composition the
 * export and the preview both draw, so the preview shows the captions the file will carry.
 *
 * The frame is named by where it stands on the movie's own timeline, and the clock and the
 * time-lapse factor are both read from that timeline here: a caller that worked the factor out for
 * itself could caption a file recorded along one timeline with the rate of another.
 */
export function movieCaptionsAt(
  settings: MovieSettings,
  title: string | null,
  party: Pick<MovieParty, 'legend' | 'note'>,
  timeline: Pick<MovieTimeline, 'length' | 'clock'>,
  frame: Pick<MovieFrame, 'position' | 'progress'>,
  words: { t: TFunction; language: string },
): MovieCaptions {
  const { captions } = settings;
  const { progress } = frame;
  const clock = movieClockText(timeline.clock(frame.position), words.t, words.language);
  return {
    title,
    clock: captions.clock ? clock : null,
    // Kept apart from the clock until it is drawn: whether the figure is written depends on the
    // room the frame has for it, which only the drawing knows.
    clockSpeed:
      captions.clock && captions.speed
        ? movieClockWithSpeed(clock, movieSpeedFactor(timeline, settings), words.t, words.language)
        : null,
    legend: captions.legend ? party.legend : [],
    legendMore: (hidden) => words.t('caveview.movie.legendMore', { count: hidden }),
    progress: captions.progress ? progress : null,
    // Already null unless the note caption is on: the party leaves it out itself.
    note: party.note,
    size: captions.size,
  };
}
