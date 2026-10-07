// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MapLayerInfo } from '../../api/hooks.ts';
import type { TripReportMapContent } from './tripReportMap.ts';
import type { PaintedBackground, PaintedMap, ReportMapPainter } from './tripReportMapCanvas.ts';
import {
  REPORT_MAP_HEIGHT,
  REPORT_MAP_WIDTH,
  makeTripReportMapPicture,
  scaleBar,
  type ReportMapWords,
} from './tripReportMapPicture.ts';

/**
 * A 2D context that records the words it was asked to write. The test runner has no canvas, and
 * what matters here is what the picture says and in which order things happened, not the pixels.
 * Text is measured at ten pixels a character.
 */
class FakeContext {
  readonly written: string[] = [];
  /** How many round marks were drawn: discs and rings, and nothing else is round. */
  rounds = 0;
  fillStyle = '';
  strokeStyle = '';
  font = '';
  lineWidth = 0;
  textBaseline = '';

  fillText(text: string) {
    this.written.push(text);
  }
  measureText(text: string) {
    return { width: text.length * 10 };
  }
  arc() {
    this.rounds += 1;
  }
  fillRect() {}
  drawImage() {}
  beginPath() {}
  moveTo() {}
  lineTo() {}
  fill() {}
  stroke() {}
}

const words: ReportMapWords = {
  legend: { sketch: 'where the trip worked', meeting: 'meeting point', cave: 'cave the trip names' },
  background: (attribution) => `Background map: ${attribution}`,
  noBackground: {
    'none-offered': 'No background: none offered',
    'not-delivered': 'No background: tiles missing',
    'timed-out': 'No background: too slow',
    'not-copyable': 'No background: not copyable',
  },
};

const content: TripReportMapContent = {
  shapes: [
    { kind: 'sketch', geometry: { type: 'Point', coordinates: [25.4, 45.5] } },
    { kind: 'cave', geometry: { type: 'Point', coordinates: [25.41, 45.51] }, label: 'Avenul' },
  ],
};

const basemap = {
  id: 1,
  name: 'Open map',
  layerKind: 'xyz',
  urlTemplate: 'https://tiles.example.invalid/{z}/{x}/{y}.png',
  options: null,
  attribution: '© <a href="https://example.invalid">Open Mappers</a>',
  groupName: null,
  minZoom: 0,
  maxZoom: 19,
  isBase: true,
  isDefault: true,
  sortOrder: 10,
  inDocuments: true,
} as MapLayerInfo;

let contexts: FakeContext[];
/** What each next attempt to write the picture out does: hand back a file, or nothing, or throw. */
let encodes: (() => Blob | null)[];
let composed: HTMLCanvasElement[];

/** A painter that draws nothing and answers how the background is said to have fared. */
function painter(withBasemap: PaintedBackground) {
  const calls: (MapLayerInfo | null)[] = [];
  const paint: ReportMapPainter = async (_content, asked, options) => {
    calls.push(asked);
    const canvas = document.createElement('canvas');
    canvas.width = options.width;
    canvas.height = options.height;
    const painted: PaintedMap = {
      canvas,
      metresPerPixel: 4,
      background: asked ? withBasemap : 'none-asked',
    };
    return painted;
  };
  return { paint, calls };
}

/** Everything the finished picture — the last canvas drawn on — had written on it. */
function writtenOnThePicture(): string[] {
  return contexts[contexts.length - 1].written;
}

beforeEach(() => {
  contexts = [];
  composed = [];
  encodes = [];
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function () {
    const context = new FakeContext();
    contexts.push(context);
    return context as unknown as CanvasRenderingContext2D;
  } as never);
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
    this: HTMLCanvasElement,
    callback: BlobCallback,
  ) {
    composed.push(this);
    const next = encodes.shift();
    callback(next ? next() : new Blob(['picture'], { type: 'image/png' }));
  } as never);
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('the picture a write-up is downloaded with', () => {
  it('is drawn once over the background, and credits the background’s source under the map', async () => {
    const { paint, calls } = painter('drawn');

    const picture = await makeTripReportMapPicture(content, basemap, words, paint);

    expect(calls).toEqual([basemap]);
    // The credit is written as words: on a picture there is nothing to click, and the markup a
    // source's credit is configured with would otherwise be printed across it.
    expect(picture.background).toEqual({ drawn: true, attribution: '© Open Mappers' });
    expect(writtenOnThePicture()).toContain('Background map: © Open Mappers');
    expect(picture.blob.type).toBe('image/png');
  });

  it('says what each mark means, and only for the marks that are on it', async () => {
    const { paint } = painter('drawn');
    await makeTripReportMapPicture(content, basemap, words, paint);

    const written = writtenOnThePicture();
    expect(written).toContain('where the trip worked');
    expect(written).toContain('cave the trip names');
    // This trip states no meeting point, and a legend entry for a mark that is not there would
    // send a reader looking for it.
    expect(written).not.toContain('meeting point');
  });

  /**
   * A sketch is a point, a line or an area, and its mark in the legend is the one on the map. A
   * dot beside "where the trip worked" on a map that shows a track would send a reader looking
   * for a dot.
   */
  it.each([
    ['Point', [25.4, 45.5], 1],
    ['LineString', [[25.4, 45.5], [25.41, 45.51]], 0],
    ['Polygon', [[[25.4, 45.5], [25.41, 45.5], [25.41, 45.51], [25.4, 45.5]]], 0],
    ['MultiLineString', [[[25.4, 45.5], [25.41, 45.51]]], 0],
  ] as const)('marks a sketch that is a %s in the legend the way the map draws it', async (type, coordinates, rounds) => {
    const { paint } = painter('drawn');
    const sketchOnly: TripReportMapContent = {
      shapes: [{ kind: 'sketch', geometry: { type, coordinates } }],
    };

    await makeTripReportMapPicture(sketchOnly, basemap, words, paint);

    expect(contexts[contexts.length - 1].rounds).toBe(rounds);
  });

  it('is the same size whatever it shows: the map, and a strip under it for the words', async () => {
    const { paint } = painter('drawn');
    await makeTripReportMapPicture(content, basemap, words, paint);

    const finished = composed[composed.length - 1];
    expect(finished.width).toBe(REPORT_MAP_WIDTH);
    expect(finished.height).toBeGreaterThan(REPORT_MAP_HEIGHT);
  });

  /**
   * The browser lets a picture be read back only if everything drawn into it was fetched with
   * leave to copy. When it refuses, it refuses at the moment the picture is asked for — after
   * the map looked perfectly well drawn. So the refusal is caught there, the map is drawn again
   * with no background at all, and the picture says why it has none.
   */
  it('is drawn again on a plain ground when the browser will not let the first one be read', async () => {
    const { paint, calls } = painter('drawn');
    // Thrown from the call itself, as a browser throws it — not handed to the callback.
    encodes.push(() => {
      throw new DOMException('Tainted canvases may not be exported.', 'SecurityError');
    });

    const picture = await makeTripReportMapPicture(content, basemap, words, paint);

    expect(calls).toEqual([basemap, null]);
    expect(picture.background).toEqual({ drawn: false, reason: 'not-copyable' });
    const written = writtenOnThePicture();
    expect(written).toContain('No background: not copyable');
    // A credit for a background that is not on the picture would be a false statement on a
    // document that leaves.
    expect(written.join(' ')).not.toContain('Open Mappers');
    expect(picture.blob.size).toBeGreaterThan(0);
  });

  it.each([
    ['tiles-failed', 'not-delivered', 'No background: tiles missing'],
    ['timed-out', 'timed-out', 'No background: too slow'],
  ] as const)(
    'is drawn again on a plain ground when the background came back %s',
    async (fared, reason, line) => {
      const { paint, calls } = painter(fared);

      const picture = await makeTripReportMapPicture(content, basemap, words, paint);

      // The first drawing is never handed over: one with holes in it looks like a whole one.
      expect(calls).toEqual([basemap, null]);
      expect(composed).toHaveLength(1);
      expect(picture.background).toEqual({ drawn: false, reason });
      expect(writtenOnThePicture()).toContain(line);
    },
  );

  it('is drawn on a plain ground from the start when the installation offers no background', async () => {
    const { paint, calls } = painter('drawn');

    const picture = await makeTripReportMapPicture(content, null, words, paint);

    expect(calls).toEqual([null]);
    expect(picture.background).toEqual({ drawn: false, reason: 'none-offered' });
    expect(writtenOnThePicture()).toContain('No background: none offered');
  });

  /**
   * Only the one refusal is answered by drawing again. Anything else that stops a picture being
   * written is not about the background, and drawing without one would not help: it is passed
   * on, and whoever asked downloads the document without a picture and says so.
   */
  it('gives up, rather than drawing again, on a failure that is not about the background', async () => {
    const { paint, calls } = painter('drawn');
    encodes.push(() => null);

    await expect(makeTripReportMapPicture(content, basemap, words, paint)).rejects.toThrow(
      'could not be written',
    );
    expect(calls).toEqual([basemap]);
  });

  it('carries a scale, so that marks on a plain ground say how far apart they are', async () => {
    const { paint } = painter('drawn');
    await makeTripReportMapPicture(content, null, words, paint);

    // Four metres to the pixel and room for 240 pixels: 960 m fits, so the bar says 500 m.
    expect(writtenOnThePicture()).toContain('500 m');
  });
});

describe('scaleBar', () => {
  it('picks the longest round distance that fits the room', () => {
    expect(scaleBar(4, 240)).toEqual({ pixels: 125, label: '500 m' });
    expect(scaleBar(1, 240)).toEqual({ pixels: 200, label: '200 m' });
    expect(scaleBar(0.5, 240)).toEqual({ pixels: 200, label: '100 m' });
  });

  it('words a long distance in kilometres', () => {
    expect(scaleBar(40, 240)).toEqual({ pixels: 125, label: '5 km' });
    expect(scaleBar(10, 240)).toEqual({ pixels: 200, label: '2 km' });
  });

  it('says nothing when there is nothing to measure by', () => {
    expect(scaleBar(0, 240)).toBeNull();
    expect(scaleBar(Number.NaN, 240)).toBeNull();
    expect(scaleBar(4, 0)).toBeNull();
  });
});
