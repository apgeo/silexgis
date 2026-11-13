// SPDX-License-Identifier: AGPL-3.0-or-later
import { flybyLength, flybySectionStarts, flybyTimeIn, type FlybyCaption, type FlybyDocument } from './flybyDocument.ts';

// What a flyby writes over the scene: a title card at the start, captions during sections, an end
// card, and the picture rising out of black and sinking back into it.
//
// Two halves. What is shown at moment `t` is a function of the document and of `t`, like
// everything else about a flyby. How it is drawn is one function onto a plain 2D canvas, used by
// the live view — a canvas laid over the scene — and by whatever makes a movie of the flyby, for
// every frame: the same function, so the file says on screen exactly what the screen said. Every
// size is a share of the frame, never a number of pixels, because the same flyby is drawn on a
// laptop window and into a 4K file.

/** A card placed on the flyby's clock. */
export interface FlybyPlacedCard {
  text: string;
  subtitle?: string;
  start: number;
  end: number;
}

export interface FlybyPlacedCaption {
  text: string;
  start: number;
  end: number;
}

/** A document's texts, placed on its clock, in real seconds. */
export interface FlybyTextPlan {
  length: number;
  title?: FlybyPlacedCard;
  end?: FlybyPlacedCard;
  captions: FlybyPlacedCaption[];
  fadeIn: number;
  fadeOut: number;
  /** Whether a north arrow is drawn, and the line of text in the corner when there is one. */
  northArrow: boolean;
  corner?: string;
  /**
   * The camera's compass heading at a moment, for the north arrow: given by whoever built the plan
   * from the flyby's path. Left out, the arrow points up.
   */
  heading?(t: number): number;
  /** Whether the picture says in a corner how much the relief is stretched, when it is. */
  reliefMark: boolean;
  /**
   * The factor the relief is drawn at, at a moment: given by whoever plays the flyby, who alone
   * knows the factor of the scene it plays in. Left out, true height throughout, and no mark.
   */
  relief?(t: number): number;
  /** The mark in the reader's words, for a factor already rounded to a tenth; left out, `×2`. */
  reliefWords?(factor: number): string;
}

export interface FlybyShownCard {
  text: string;
  subtitle?: string;
  /** 0–1. */
  alpha: number;
}

/** What is over the scene at one moment. */
export interface FlybyTextState {
  /** How far the picture is darkened towards black, 0–1. */
  dim: number;
  title?: FlybyShownCard;
  end?: FlybyShownCard;
  caption?: { text: string; alpha: number };
  /** The camera's heading, degrees, when a north arrow is drawn. */
  north?: number;
  corner?: string;
  /** The words saying the relief is stretched, while it is drawn other than true. */
  relief?: string;
}

/** A card or caption takes this long to appear and to go, at most, in seconds. */
const CARD_FADE_SECONDS = 0.8;
const CAPTION_FADE_SECONDS = 0.5;

/**
 * The texts of a document placed on its clock. `found` are captions worked out when the flyby was
 * prepared — a cave's name over the section that pauses at it — placed with the document's own.
 */
export function flybyTextPlan(document: FlybyDocument, found: readonly FlybyCaption[] = []): FlybyTextPlan {
  const length = flybyLength(document);
  const texts = document.texts ?? {};
  const starts = flybySectionStarts(document);
  const plan: FlybyTextPlan = {
    length,
    captions: [],
    fadeIn: Math.min(texts.fadeIn ?? 0, length),
    fadeOut: Math.min(texts.fadeOut ?? 0, length),
    northArrow: texts.northArrow === true,
    ...(texts.corner ? { corner: texts.corner } : {}),
    reliefMark: texts.reliefMark !== false,
  };
  if (texts.title) {
    plan.title = {
      text: texts.title.text,
      ...(texts.title.subtitle ? { subtitle: texts.title.subtitle } : {}),
      start: 0,
      end: Math.min(texts.title.seconds, length),
    };
  }
  if (texts.end) {
    plan.end = {
      text: texts.end.text,
      ...(texts.end.subtitle ? { subtitle: texts.end.subtitle } : {}),
      start: Math.max(0, length - texts.end.seconds),
      end: length,
    };
  }
  for (const caption of [...(texts.captions ?? []), ...found]) {
    const time = flybyTimeIn(document, starts, caption);
    if (time) {
      plan.captions.push({ text: caption.text, start: time.start, end: Math.min(time.end, length) });
    }
  }
  plan.captions.sort((a, b) => a.start - b.start);
  return plan;
}

/** True when the plan writes nothing and darkens nothing: then no overlay is needed. */
export function flybyTextPlanIsEmpty(plan: FlybyTextPlan): boolean {
  return (
    !plan.title &&
    !plan.end &&
    plan.captions.length === 0 &&
    plan.fadeIn === 0 &&
    plan.fadeOut === 0 &&
    !plan.northArrow &&
    !plan.corner &&
    !(plan.reliefMark && plan.relief)
  );
}

function eased(p: number): number {
  const s = Math.min(1, Math.max(0, p));
  return s * s * (3 - 2 * s);
}

/** How strongly something shown from `start` to `end` is drawn at `t`, fading in and out over `fade`. */
function presence(start: number, end: number, t: number, fadeIn: number, fadeOut: number): number {
  if (t < start || t > end) {
    return 0;
  }
  const span = end - start;
  const rise = Math.min(fadeIn, span / 4);
  const fall = Math.min(fadeOut, span / 4);
  const appearing = rise > 0 ? eased((t - start) / rise) : 1;
  const going = fall > 0 ? eased((end - t) / fall) : 1;
  return Math.min(appearing, going);
}

/** What is over the scene at moment `t`. */
export function flybyTextsAt(plan: FlybyTextPlan, t: number): FlybyTextState {
  const clamped = Math.min(Math.max(t, 0), plan.length);
  const state: FlybyTextState = { dim: 0 };
  if (plan.fadeIn > 0 && clamped < plan.fadeIn) {
    state.dim = Math.max(state.dim, 1 - eased(clamped / plan.fadeIn));
  }
  if (plan.fadeOut > 0 && clamped > plan.length - plan.fadeOut) {
    state.dim = Math.max(state.dim, eased((clamped - (plan.length - plan.fadeOut)) / plan.fadeOut));
  }
  // The title is there from the first frame — a recording opens on it — and goes before it ends;
  // the end card comes and then stays to the last frame, which is the picture a recording ends on.
  if (plan.title) {
    const alpha = presence(plan.title.start, plan.title.end, clamped, 0, CARD_FADE_SECONDS);
    if (alpha > 0) {
      state.title = { text: plan.title.text, ...(plan.title.subtitle ? { subtitle: plan.title.subtitle } : {}), alpha };
    }
  }
  if (plan.end) {
    const alpha = presence(plan.end.start, plan.end.end, clamped, CARD_FADE_SECONDS, 0);
    if (alpha > 0) {
      state.end = { text: plan.end.text, ...(plan.end.subtitle ? { subtitle: plan.end.subtitle } : {}), alpha };
    }
  }
  if (plan.northArrow) {
    // Rounded to a tenth of a degree, so a camera at rest does not redraw the arrow every frame.
    state.north = Math.round((plan.heading?.(clamped) ?? 0) * 10) / 10;
  }
  if (plan.corner) {
    state.corner = plan.corner;
  }
  if (plan.reliefMark && plan.relief) {
    // To the tenth a person chooses the factor in, which is also what keeps the mark from being
    // redrawn on every frame of a ramp, and from saying "×1" for a factor a hair from true.
    const factor = Math.round(plan.relief(clamped) * 10) / 10;
    if (factor !== 1) {
      state.relief = plan.reliefWords?.(factor) ?? `×${factor}`;
    }
  }
  // One caption at a time: the latest one that has begun.
  for (const caption of plan.captions) {
    const alpha = presence(caption.start, caption.end, clamped, CAPTION_FADE_SECONDS, CAPTION_FADE_SECONDS);
    if (alpha > 0) {
      state.caption = { text: caption.text, alpha };
    }
  }
  return state;
}

/** Whether two moments show the same thing, so a canvas showing one need not be drawn again for the other. */
export function sameFlybyTexts(a: FlybyTextState | undefined, b: FlybyTextState | undefined): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

/** Empties a canvas that holds nothing but texts, before the next moment's are drawn on it. */
export function clearFlybyTexts(context: Pick<CanvasRenderingContext2D, 'clearRect'>, width: number, height: number): void {
  context.clearRect(0, 0, width, height);
}

/** The parts of a 2D canvas the texts are drawn with; a test hands in a recorder. */
export type FlybyTextCanvas = Pick<
  CanvasRenderingContext2D,
  | 'save'
  | 'restore'
  | 'fillRect'
  | 'fillText'
  | 'measureText'
  | 'beginPath'
  | 'moveTo'
  | 'arcTo'
  | 'closePath'
  | 'fill'
  | 'font'
  | 'fillStyle'
  | 'globalAlpha'
  | 'textAlign'
  | 'textBaseline'
  | 'shadowColor'
  | 'shadowBlur'
  | 'translate'
  | 'rotate'
  | 'lineTo'
  | 'arc'
  | 'stroke'
  | 'strokeStyle'
  | 'lineWidth'
>;

const FONT_FAMILY = 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

/** Splits text into lines no wider than `width`, at spaces; a word longer than a line is a line of its own. */
function wrap(context: FlybyTextCanvas, text: string, width: number, maxLines: number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(/\s+/).filter((part) => part.length > 0)) {
    const candidate = line ? `${line} ${word}` : word;
    if (line && context.measureText(candidate).width > width) {
      lines.push(line);
      line = word;
    } else {
      line = candidate;
    }
  }
  if (line) {
    lines.push(line);
  }
  if (lines.length > maxLines) {
    const kept = lines.slice(0, maxLines);
    kept[maxLines - 1] = `${kept[maxLines - 1]}…`;
    return kept;
  }
  return lines;
}

function card(context: FlybyTextCanvas, shown: FlybyShownCard, width: number, unit: number, centreY: number) {
  const titleSize = unit * 0.075;
  const subtitleSize = unit * 0.04;
  context.save();
  context.globalAlpha = shown.alpha;
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.fillStyle = '#ffffff';
  // A soft shadow rather than a box: a card is read over open ground and over black alike.
  context.shadowColor = 'rgba(0, 0, 0, 0.7)';
  context.shadowBlur = unit * 0.015;
  context.font = `600 ${titleSize}px ${FONT_FAMILY}`;
  const titleLines = wrap(context, shown.text, width * 0.84, 3);
  context.font = `400 ${subtitleSize}px ${FONT_FAMILY}`;
  const subtitleLines = shown.subtitle ? wrap(context, shown.subtitle, width * 0.84, 2) : [];
  const gap = subtitleLines.length > 0 ? unit * 0.03 : 0;
  const total = titleLines.length * titleSize * 1.2 + gap + subtitleLines.length * subtitleSize * 1.3;
  let y = centreY - total / 2;
  context.font = `600 ${titleSize}px ${FONT_FAMILY}`;
  for (const line of titleLines) {
    context.fillText(line, width / 2, y + (titleSize * 1.2) / 2);
    y += titleSize * 1.2;
  }
  y += gap;
  context.font = `400 ${subtitleSize}px ${FONT_FAMILY}`;
  for (const line of subtitleLines) {
    context.fillText(line, width / 2, y + (subtitleSize * 1.3) / 2);
    y += subtitleSize * 1.3;
  }
  context.restore();
}

function roundedBox(context: FlybyTextCanvas, x: number, y: number, w: number, h: number, r: number) {
  context.beginPath();
  context.moveTo(x + r, y);
  context.arcTo(x + w, y, x + w, y + h, r);
  context.arcTo(x + w, y + h, x, y + h, r);
  context.arcTo(x, y + h, x, y, r);
  context.arcTo(x, y, x + w, y, r);
  context.closePath();
  context.fill();
}

function caption(context: FlybyTextCanvas, text: string, alpha: number, width: number, height: number, unit: number) {
  const size = unit * 0.042;
  const lineHeight = size * 1.3;
  const pad = unit * 0.02;
  context.save();
  context.globalAlpha = alpha;
  context.font = `500 ${size}px ${FONT_FAMILY}`;
  const lines = wrap(context, text, width * 0.8, 3);
  const textWidth = Math.max(...lines.map((line) => context.measureText(line).width));
  const boxWidth = textWidth + pad * 2;
  const boxHeight = lines.length * lineHeight + pad * 1.4;
  // Above the bottom edge by enough to clear the map credits a recording carries there.
  const bottom = height * 0.88;
  const top = bottom - boxHeight;
  context.fillStyle = 'rgba(0, 0, 0, 0.55)';
  roundedBox(context, (width - boxWidth) / 2, top, boxWidth, boxHeight, pad * 0.6);
  context.fillStyle = '#ffffff';
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  lines.forEach((line, index) => {
    context.fillText(line, width / 2, top + pad * 0.7 + lineHeight * (index + 0.5));
  });
  context.restore();
}

/**
 * A north arrow in the top right corner. The picture's up is the way the camera faces, so north is
 * turned from up by the camera's heading, the other way.
 */
function northArrow(context: FlybyTextCanvas, heading: number, width: number, unit: number) {
  const radius = unit * 0.045;
  const margin = unit * 0.03;
  context.save();
  context.translate(width - margin - radius, margin + radius);
  context.globalAlpha = 0.9;
  context.fillStyle = 'rgba(0, 0, 0, 0.45)';
  context.beginPath();
  context.arc(0, 0, radius, 0, Math.PI * 2);
  context.fill();
  context.rotate((-heading * Math.PI) / 180);
  // A narrow arrow, its point white and its tail grey, and the N beyond its point.
  context.fillStyle = '#ffffff';
  context.beginPath();
  context.moveTo(0, -radius * 0.62);
  context.lineTo(radius * 0.26, radius * 0.1);
  context.lineTo(-radius * 0.26, radius * 0.1);
  context.closePath();
  context.fill();
  context.fillStyle = 'rgba(255, 255, 255, 0.45)';
  context.beginPath();
  context.moveTo(radius * 0.26, radius * 0.1);
  context.lineTo(0, radius * 0.62);
  context.lineTo(-radius * 0.26, radius * 0.1);
  context.closePath();
  context.fill();
  context.fillStyle = '#ffffff';
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.font = `700 ${radius * 0.42}px ${FONT_FAMILY}`;
  context.fillText('N', 0, -radius * 0.82);
  context.restore();
}

/** A line of text in the top left corner, for the whole of the flyby. */
function cornerText(context: FlybyTextCanvas, text: string, width: number, unit: number) {
  const size = unit * 0.032;
  const margin = unit * 0.03;
  context.save();
  context.globalAlpha = 0.95;
  context.fillStyle = '#ffffff';
  context.shadowColor = 'rgba(0, 0, 0, 0.8)';
  context.shadowBlur = unit * 0.01;
  context.textAlign = 'left';
  context.textBaseline = 'top';
  context.font = `500 ${size}px ${FONT_FAMILY}`;
  const [line] = wrap(context, text, width * 0.6, 1);
  context.fillText(line ?? '', margin, margin);
  context.restore();
}

/** The relief's stretch, small in the top left corner — under the corner's own line when there is one. */
function reliefMark(context: FlybyTextCanvas, text: string, width: number, unit: number, below: boolean) {
  const size = unit * 0.026;
  const margin = unit * 0.03;
  context.save();
  context.globalAlpha = 0.9;
  context.fillStyle = '#ffffff';
  context.shadowColor = 'rgba(0, 0, 0, 0.8)';
  context.shadowBlur = unit * 0.01;
  context.textAlign = 'left';
  context.textBaseline = 'top';
  context.font = `500 ${size}px ${FONT_FAMILY}`;
  const [line] = wrap(context, text, width * 0.4, 1);
  context.fillText(line ?? '', margin, margin + (below ? unit * 0.032 * 1.35 : 0));
  context.restore();
}

/**
 * Draws what is over the scene at one moment onto a canvas `width` by `height`, **over whatever the
 * canvas already holds**. A movie's frame is the scene first and the texts over it, on one canvas,
 * so nothing here may wipe it; a canvas that holds only texts is cleared by whoever owns it, with
 * {@link clearFlybyTexts}.
 *
 * Nothing here is a fixed number of pixels: every size is a share of the frame's smaller side, so
 * a title fills the same part of a phone-shaped movie as of a wide one.
 */
export function drawFlybyTexts(context: FlybyTextCanvas, state: FlybyTextState, width: number, height: number): void {
  const unit = Math.min(width, height);
  // The furniture of the frame first, so a fade to black takes it with the picture.
  if (state.north !== undefined) {
    northArrow(context, state.north, width, unit);
  }
  if (state.corner) {
    cornerText(context, state.corner, width, unit);
  }
  if (state.relief) {
    reliefMark(context, state.relief, width, unit, Boolean(state.corner));
  }
  if (state.dim > 0) {
    context.save();
    context.globalAlpha = 1;
    context.fillStyle = `rgba(0, 0, 0, ${Math.min(1, state.dim)})`;
    context.fillRect(0, 0, width, height);
    context.restore();
  }
  if (state.title) {
    card(context, state.title, width, unit, height * 0.45);
  }
  if (state.end) {
    card(context, state.end, width, unit, height * 0.45);
  }
  if (state.caption) {
    caption(context, state.caption.text, state.caption.alpha, width, height, unit);
  }
}
