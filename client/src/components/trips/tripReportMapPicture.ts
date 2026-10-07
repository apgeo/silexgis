// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../../api/hooks.ts';
import { tripPalette as palette } from '../../map/markerPalette.ts';
import { creditLines } from '../../scene3d/sceneImage3d.ts';
import type { ReportMapShapeKind, TripReportMapContent } from './tripReportMap.ts';
import {
  paintTripReportMap,
  type PaintedMap,
  type ReportMapPainter,
} from './tripReportMapCanvas.ts';

// From what is to be drawn to the finished picture a write-up is downloaded with.
//
// The drawing of the map is somebody else's job (the painter, which needs a real browser). This
// decides what happens around it, and all of it runs under a test:
//
//  - the background is tried once and never trusted. If its tiles did not all arrive, did not
//    arrive in time, or arrived in a way the browser will not let be copied out, the map is drawn
//    again on a plain ground. A picture is never handed over with holes in it, and never held
//    back for the sake of a background;
//  - whichever happened is written on the picture, under the map: the credit the background's
//    source asks for, or the reason there is no background. A document that leaves with a bare
//    map and no word about why reads as a fault of whoever sent it;
//  - what the marks mean is written there too, because the picture travels without the page.
//
// The picture is always the same size, whatever screen it was made on.

/** The map itself, in pixels. Placed about fifteen centimetres wide, this prints at 200 to the inch. */
export const REPORT_MAP_WIDTH = 1200;
export const REPORT_MAP_HEIGHT = 800;

/** How long a background's tiles are waited for. Past this the document matters more than they do. */
const TILE_WAIT_MS = 12_000;

/** Why a picture has no background. Each has its own sentence, because each has its own remedy. */
export type BackgroundLeftOut =
  /** The installation offers no source a document may copy. Nothing was asked of anybody. */
  | 'none-offered'
  /** The source was asked and some of its tiles did not come. */
  | 'not-delivered'
  /** The source was asked and did not finish answering in time. */
  | 'timed-out'
  /** The tiles came, and the browser would not let the finished picture be read back. */
  | 'not-copyable';

export type ReportMapBackground =
  | { drawn: true; attribution: string }
  | { drawn: false; reason: BackgroundLeftOut };

export interface TripReportMapPicture {
  /** The picture, as a PNG. */
  blob: Blob;
  background: ReportMapBackground;
}

/** The words written onto the picture, already in the language of whoever is downloading. */
export interface ReportMapWords {
  /** What each kind of mark means. */
  legend: Record<ReportMapShapeKind, string>;
  /** The line crediting the background, given the credit its source asks for. */
  background: (attribution: string) => string;
  /** The line that stands in the background's place, by why there is none. */
  noBackground: Record<BackgroundLeftOut, string>;
}

// ---------------------------------------------------------------------------
// The scale bar
// ---------------------------------------------------------------------------

export interface ScaleBar {
  /** How long the bar is on the picture. */
  pixels: number;
  /** What that length is on the ground, worded: "200 m", "5 km". */
  label: string;
}

/**
 * The longest round distance — 1, 2 or 5 times a power of ten metres — whose bar fits in the
 * room given, or null when nothing can be said.
 *
 * It matters most where there is no background: three marks on a plain ground say nothing at all
 * about how far apart they are until something says how long a centimetre is.
 */
export function scaleBar(metresPerPixel: number, roomPixels: number): ScaleBar | null {
  if (!Number.isFinite(metresPerPixel) || metresPerPixel <= 0 || roomPixels <= 0) {
    return null;
  }
  const most = metresPerPixel * roomPixels;
  const magnitude = 10 ** Math.floor(Math.log10(most));
  const step = [5, 2, 1].find((multiple) => multiple * magnitude <= most);
  if (!step) {
    return null;
  }
  const metres = step * magnitude;
  return {
    pixels: Math.round(metres / metresPerPixel),
    label: metres >= 1000 ? `${metres / 1000} km` : `${metres} m`,
  };
}

// ---------------------------------------------------------------------------
// Laying the picture out
// ---------------------------------------------------------------------------

const STRIP_PADDING = 20;
const LEGEND_FONT = '22px sans-serif';
const LEGEND_LINE = 36;
const CREDIT_FONT = '19px sans-serif';
const CREDIT_LINE = 28;
const MARK = 22;
const MARK_GAP = 10;
const ENTRY_GAP = 40;
const INK = '#1f1f1f';
const QUIET_INK = '#555555';

const LEGEND_ORDER: readonly ReportMapShapeKind[] = ['sketch', 'meeting', 'cave'];

function context2d(canvas: HTMLCanvasElement): CanvasRenderingContext2D {
  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('This browser cannot draw onto a 2D canvas.');
  }
  return context;
}

/** Breaks text into lines no wider than the room there is, at word boundaries. */
function wrapWords(context: CanvasRenderingContext2D, text: string, room: number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(' ')) {
    const candidate = line ? `${line} ${word}` : word;
    if (line && context.measureText(candidate).width > room) {
      lines.push(line);
      line = word;
    } else {
      line = candidate;
    }
  }
  if (line) {
    lines.push(line);
  }
  return lines;
}

/** How a shape is drawn on the map, as far as its mark in the legend has to follow it. */
type MarkForm = 'point' | 'line' | 'area';

/** A point, a line or an area, read off the stored type and its Multi- forms alike. */
function formOf(geometryType: string): MarkForm {
  if (geometryType.endsWith('LineString')) {
    return 'line';
  }
  return geometryType.endsWith('Polygon') ? 'area' : 'point';
}

/**
 * One mark of the legend, drawn the way the map draws that shape.
 *
 * A sketch is a point, a line or an area, and its mark follows it: a legend showing a dot beside
 * "where the trip worked" would send a reader looking for a dot on a map that has a track on it.
 */
function drawMark(
  context: CanvasRenderingContext2D,
  kind: ReportMapShapeKind,
  form: MarkForm,
  left: number,
  middle: number,
): void {
  const centre = left + MARK / 2;
  if (kind === 'cave') {
    context.fillStyle = palette.derived;
    context.fillRect(left + 2, middle - MARK / 2 + 2, MARK - 4, MARK - 4);
    return;
  }
  if (kind === 'meeting') {
    context.beginPath();
    context.arc(centre, middle, MARK / 2 - 2, 0, Math.PI * 2);
    context.fillStyle = palette.meetingCentre;
    context.fill();
    context.lineWidth = 4;
    context.strokeStyle = palette.meeting;
    context.stroke();
    return;
  }
  if (form === 'line') {
    context.beginPath();
    context.moveTo(left, middle);
    context.lineTo(left + MARK, middle);
    context.lineWidth = 5;
    context.strokeStyle = palette.sketch;
    context.stroke();
    return;
  }
  if (form === 'area') {
    context.fillStyle = palette.sketchFill;
    context.fillRect(left + 2, middle - MARK / 2 + 2, MARK - 4, MARK - 4);
    context.beginPath();
    context.moveTo(left + 2, middle - MARK / 2 + 2);
    context.lineTo(left + MARK - 2, middle - MARK / 2 + 2);
    context.lineTo(left + MARK - 2, middle + MARK / 2 - 2);
    context.lineTo(left + 2, middle + MARK / 2 - 2);
    context.lineTo(left + 2, middle - MARK / 2 + 2);
    context.lineWidth = 3;
    context.strokeStyle = palette.sketch;
    context.stroke();
    return;
  }
  context.beginPath();
  context.arc(centre, middle, MARK / 2 - 2, 0, Math.PI * 2);
  context.fillStyle = palette.sketch;
  context.fill();
}

interface LegendEntry {
  kind: ReportMapShapeKind;
  form: MarkForm;
  text: string;
  width: number;
}

/** The legend's entries arranged in rows that fit the picture's width. */
function legendRows(
  context: CanvasRenderingContext2D,
  content: TripReportMapContent,
  words: ReportMapWords,
  room: number,
): LegendEntry[][] {
  context.font = LEGEND_FONT;
  const rows: LegendEntry[][] = [];
  let row: LegendEntry[] = [];
  let used = 0;
  for (const kind of LEGEND_ORDER) {
    // One entry for each kind of mark that is on the map, and none for a kind that is not: an
    // entry for a mark that is not there sends a reader looking for it.
    const drawn = content.shapes.find((shape) => shape.kind === kind);
    if (!drawn) {
      continue;
    }
    const form = formOf(drawn.geometry.type);
    const text = words.legend[kind];
    const width = MARK + MARK_GAP + context.measureText(text).width;
    if (row.length > 0 && used + ENTRY_GAP + width > room) {
      rows.push(row);
      row = [];
      used = 0;
    }
    used += (row.length > 0 ? ENTRY_GAP : 0) + width;
    row.push({ kind, form, text, width });
  }
  if (row.length > 0) {
    rows.push(row);
  }
  return rows;
}

function drawScaleBar(context: CanvasRenderingContext2D, painted: PaintedMap, height: number): void {
  const bar = scaleBar(painted.metresPerPixel, 240);
  if (!bar) {
    return;
  }
  context.font = CREDIT_FONT;
  const labelWidth = context.measureText(bar.label).width;
  const boxWidth = Math.max(bar.pixels, labelWidth) + 24;
  const left = 20;
  const top = height - 20 - 58;

  // A light plate under it, so the bar reads over a dark background as well as a plain one.
  context.fillStyle = 'rgba(255, 255, 255, 0.85)';
  context.fillRect(left, top, boxWidth, 58);

  context.fillStyle = INK;
  context.textBaseline = 'middle';
  context.fillText(bar.label, left + 12, top + 18);

  const y = top + 42;
  context.strokeStyle = INK;
  context.lineWidth = 3;
  context.beginPath();
  context.moveTo(left + 12, y - 7);
  context.lineTo(left + 12, y);
  context.lineTo(left + 12 + bar.pixels, y);
  context.lineTo(left + 12 + bar.pixels, y - 7);
  context.stroke();
}

/**
 * The finished picture: the map, a scale bar on it, and under it a strip saying what the marks
 * mean and what the background is — or why there is none.
 *
 * The strip is under the map rather than over its bottom edge, so nothing drawn is covered and
 * the credit cannot be cropped off without cropping into the map it credits.
 */
function compose(
  painted: PaintedMap,
  content: TripReportMapContent,
  words: ReportMapWords,
  backgroundLine: string,
): HTMLCanvasElement {
  const width = REPORT_MAP_WIDTH;
  const room = width - STRIP_PADDING * 2;

  // Measured first, because the picture's height depends on how the words break.
  const measure = context2d(document.createElement('canvas'));
  const rows = legendRows(measure, content, words, room);
  measure.font = CREDIT_FONT;
  const creditLinesWrapped = wrapWords(measure, backgroundLine, room);
  const stripHeight =
    STRIP_PADDING * 2 + rows.length * LEGEND_LINE + creditLinesWrapped.length * CREDIT_LINE;

  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = REPORT_MAP_HEIGHT + stripHeight;
  const context = context2d(canvas);

  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.drawImage(painted.canvas, 0, 0);
  drawScaleBar(context, painted, REPORT_MAP_HEIGHT);

  // A hairline between the map and its strip, so a plain ground does not run into the words.
  context.fillStyle = '#d0d0d0';
  context.fillRect(0, REPORT_MAP_HEIGHT, width, 1);

  let y = REPORT_MAP_HEIGHT + STRIP_PADDING;
  context.textBaseline = 'middle';
  for (const row of rows) {
    let x = STRIP_PADDING;
    for (const entry of row) {
      drawMark(context, entry.kind, entry.form, x, y + LEGEND_LINE / 2);
      context.font = LEGEND_FONT;
      context.fillStyle = INK;
      context.fillText(entry.text, x + MARK + MARK_GAP, y + LEGEND_LINE / 2);
      x += entry.width + ENTRY_GAP;
    }
    y += LEGEND_LINE;
  }

  context.font = CREDIT_FONT;
  context.fillStyle = QUIET_INK;
  for (const line of creditLinesWrapped) {
    context.fillText(line, STRIP_PADDING, y + CREDIT_LINE / 2);
    y += CREDIT_LINE;
  }

  return canvas;
}

/** The canvas as a PNG, or the reason the browser gave for refusing. */
function encode(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise<Blob>((resolve, reject) => {
    try {
      canvas.toBlob((blob) => {
        if (blob) {
          resolve(blob);
        } else {
          reject(new Error('The picture could not be written as a PNG.'));
        }
      }, 'image/png');
    } catch (error) {
      // A canvas holding anything the browser fetched without leave to copy refuses here,
      // synchronously, rather than through the callback. Passed on exactly as it was thrown:
      // which refusal it is can only be told from the thing itself.
      reject(error);
    }
  });
}

/** Whether the browser refused to hand a picture over because of where part of it came from. */
function refusedAsUncopyable(error: unknown): boolean {
  return (error as { name?: unknown } | null)?.name === 'SecurityError';
}

/**
 * Makes the picture a write-up is downloaded with.
 *
 * `basemap` is the one background the installation lets a document copy, or null when it offers
 * none; choosing it is not this function's business. Throws only when no picture at all can be
 * made — the caller then downloads the document without one and says so.
 */
export async function makeTripReportMapPicture(
  content: TripReportMapContent,
  basemap: MapLayerInfo | null,
  words: ReportMapWords,
  paint: ReportMapPainter = paintTripReportMap,
): Promise<TripReportMapPicture> {
  const size = { width: REPORT_MAP_WIDTH, height: REPORT_MAP_HEIGHT, timeoutMs: TILE_WAIT_MS };
  let reason: BackgroundLeftOut = 'none-offered';

  if (basemap) {
    const attribution = creditLines([basemap.attribution ?? ''])[0] ?? '';
    const painted = await paint(content, basemap, size);
    if (painted.background === 'drawn') {
      try {
        const blob = await encode(compose(painted, content, words, words.background(attribution)));
        return { blob, background: { drawn: true, attribution } };
      } catch (error) {
        if (!refusedAsUncopyable(error)) {
          throw error;
        }
        reason = 'not-copyable';
      }
    } else {
      reason = painted.background === 'timed-out' ? 'timed-out' : 'not-delivered';
    }
  }

  // Drawn again from nothing rather than by taking the background off the first drawing: a map
  // that was half tiled, or that the browser will not let be read, is not one to build on.
  const plain = await paint(content, null, size);
  const blob = await encode(compose(plain, content, words, words.noBackground[reason]));
  return { blob, background: { drawn: false, reason } };
}
