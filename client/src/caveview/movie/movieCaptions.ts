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
 * Draws the captions over a frame that already holds the model.
 *
 * Laid out against the frame's height, so a caption covers the same share of a small GIF as of a
 * full-size video: the title at the top left with the legend under it, the clock at the top right,
 * the note centred at the bottom, and the progress bar along the bottom edge. Each text sits on a
 * translucent plate so it reads over a bright model and a black background alike.
 */
export function drawMovieCaptions(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  captions: MovieCaptions,
): void {
  const size = Number.isFinite(captions.size) && captions.size > 0 ? captions.size : 1;
  const fontPx = Math.max(8, Math.round(height * 0.042 * size));
  const pad = Math.max(2, Math.round(fontPx * 0.35));
  const margin = Math.max(4, Math.round(height * 0.025));
  const lineHeight = Math.round(fontPx * 1.3);
  const barHeight = captions.progress === null ? 0 : Math.max(2, Math.round(height * 0.008 * size));

  ctx.save();
  ctx.textBaseline = 'middle';
  ctx.textAlign = 'left';

  let clockWidth = 0;
  if (captions.clock !== null && captions.clock.length > 0) {
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    const text = fitted(ctx, captions.clock, width / 2 - margin - 2 * pad);
    clockWidth = ctx.measureText(text).width + 2 * pad;
    const x = width - margin - clockWidth;
    plate(ctx, x, margin, clockWidth, lineHeight);
    ctx.fillStyle = INK;
    ctx.fillText(text, x + pad, margin + lineHeight / 2);
  }

  let top = margin;
  if (captions.title !== null && captions.title.length > 0) {
    ctx.font = `bold ${fontPx}px ${FONT_FAMILY}`;
    const room = width - 2 * margin - (clockWidth > 0 ? clockWidth + margin : 0) - 2 * pad;
    const text = fitted(ctx, captions.title, Math.max(room, fontPx));
    const plateWidth = ctx.measureText(text).width + 2 * pad;
    plate(ctx, margin, top, plateWidth, lineHeight);
    ctx.fillStyle = INK;
    ctx.fillText(text, margin + pad, top + lineHeight / 2);
    top += lineHeight + Math.round(margin / 2);
  }

  if (captions.legend.length > 0) {
    const legendPx = Math.max(8, Math.round(fontPx * 0.85));
    const legendLine = Math.round(legendPx * 1.35);
    const swatch = Math.round(legendPx * 0.7);
    ctx.font = `${legendPx}px ${FONT_FAMILY}`;
    const labelRoom = width / 2 - margin - 3 * pad - swatch;
    // As many entries as fit above the note's area; a legend never runs off the frame.
    const room = Math.max(0, Math.floor((height * 0.6 - top) / legendLine));
    const rows = legendRows(captions.legend, room, captions.legendMore);
    const labels = rows.map((row) => fitted(ctx, row.label, labelRoom));
    if (rows.length > 0) {
      const plateWidth =
        Math.max(...labels.map((label) => ctx.measureText(label).width)) + swatch + 3 * pad;
      plate(ctx, margin, top, plateWidth, rows.length * legendLine + pad);
      rows.forEach((row, index) => {
        const middle = top + pad / 2 + index * legendLine + legendLine / 2;
        if (row.color !== null) {
          ctx.fillStyle = row.color;
          ctx.fillRect(margin + pad, Math.round(middle - swatch / 2), swatch, swatch);
        }
        ctx.fillStyle = INK;
        ctx.fillText(labels[index], margin + 2 * pad + swatch, middle);
      });
    }
  }

  if (captions.note !== null && captions.note.length > 0) {
    ctx.font = `${fontPx}px ${FONT_FAMILY}`;
    const lines = wrapped(ctx, captions.note, width * 0.8 - 2 * pad, NOTE_LINES);
    const blockWidth = Math.max(...lines.map((line) => ctx.measureText(line).width)) + 2 * pad;
    const blockHeight = lines.length * lineHeight;
    const x = (width - blockWidth) / 2;
    const y = height - margin - barHeight - blockHeight;
    plate(ctx, x, y, blockWidth, blockHeight);
    ctx.fillStyle = INK;
    ctx.textAlign = 'center';
    lines.forEach((line, index) => {
      ctx.fillText(line, width / 2, y + index * lineHeight + lineHeight / 2);
    });
    ctx.textAlign = 'left';
  }

  if (captions.progress !== null) {
    const progress = Math.min(1, Math.max(0, Number.isFinite(captions.progress) ? captions.progress : 0));
    ctx.fillStyle = PROGRESS_TRACK;
    ctx.fillRect(0, height - barHeight, width, barHeight);
    ctx.fillStyle = PROGRESS_FILL;
    ctx.fillRect(0, height - barHeight, Math.round(width * progress), barHeight);
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
 * The clock caption with the time-lapse factor after it, when there is one to write: "… · ×240".
 * The factor is never drawn by itself — it qualifies the clock, and beside nothing it is a bare
 * number — so this takes the clock's own text and hands it back unchanged when no factor applies.
 */
export function movieClockWithSpeed(clock: string, factor: number | null, t: TFunction, language: string): string {
  const speed = movieSpeedText(factor, language);
  return speed === null ? clock : t('caveview.movie.clockSpeed', { clock, factor: speed });
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
    clock: !captions.clock
      ? null
      : captions.speed
        ? movieClockWithSpeed(clock, movieSpeedFactor(timeline, settings), words.t, words.language)
        : clock,
    legend: captions.legend ? party.legend : [],
    legendMore: (hidden) => words.t('caveview.movie.legendMore', { count: hidden }),
    progress: captions.progress ? progress : null,
    // Already null unless the note caption is on: the party leaves it out itself.
    note: party.note,
    size: captions.size,
  };
}
