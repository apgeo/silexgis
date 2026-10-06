// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { creditLines, imageWithCredits, sceneImageFileName } from './sceneImage3d.ts';

/** One thing the fake context was asked to draw, with the fill in force when it was asked. */
interface Drawn {
  op: 'drawImage' | 'fillRect' | 'fillText';
  args: unknown[];
  fillStyle: string;
  font: string;
}

/**
 * A 2D context that records what it was asked to draw. The test runner has no canvas, and what
 * matters here is where things were put and in what order, not the pixels that resulted. Text is
 * measured at ten pixels a character, so a test can say exactly where a line has to break.
 */
class FakeContext {
  readonly drawn: Drawn[] = [];
  fillStyle = '';
  font = '';
  textBaseline = '';

  private record(op: Drawn['op'], args: unknown[]) {
    this.drawn.push({ op, args, fillStyle: this.fillStyle, font: this.font });
  }
  drawImage(...args: unknown[]) {
    this.record('drawImage', args);
  }
  fillRect(...args: unknown[]) {
    this.record('fillRect', args);
  }
  fillText(...args: unknown[]) {
    this.record('fillText', args);
  }
  measureText(text: string) {
    return { width: text.length * 10 };
  }
}

let context: FakeContext;
let canvases: HTMLCanvasElement[];
let bitmap: { width: number; height: number; close: ReturnType<typeof vi.fn> };
let written: Blob | null;

beforeEach(() => {
  context = new FakeContext();
  canvases = [];
  bitmap = { width: 800, height: 600, close: vi.fn() };
  written = new Blob(['composited'], { type: 'image/png' });
  vi.stubGlobal('createImageBitmap', vi.fn(async () => bitmap));
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (
    this: HTMLCanvasElement,
  ) {
    canvases.push(this);
    return context as unknown as CanvasRenderingContext2D;
  } as never);
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(((
    callback: BlobCallback,
    type?: string,
  ) => {
    callback(written && type === 'image/png' ? written : null);
  }) as never);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const frame = new Blob(['frame'], { type: 'image/png' });

describe('sceneImageFileName', () => {
  it('names a picture by the viewer’s own clock, to the second', () => {
    expect(sceneImageFileName(new Date(2026, 9, 6, 14, 5, 9))).toBe(
      'silexgis-3d-20261006-140509.png',
    );
  });

  it('pads every part, so names sort in the order the pictures were taken', () => {
    expect(sceneImageFileName(new Date(2027, 0, 2, 3, 4, 5))).toBe('silexgis-3d-20270102-030405.png');
  });
});

describe('creditLines', () => {
  it('reads a credit configured with a link as the words a reader would see', () => {
    expect(
      creditLines(['© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors']),
    ).toEqual(['© OpenStreetMap contributors']);
  });

  it('drops blanks and says a source once however many layers name it', () => {
    expect(creditLines(['© Copernicus', '   ', '© Copernicus', '', '© ANCPI'])).toEqual([
      '© Copernicus',
      '© ANCPI',
    ]);
  });
});

describe('imageWithCredits', () => {
  it('draws the frame at its own size and writes the credits over a strip along the bottom edge', async () => {
    const image = await imageWithCredits(frame, ['© OpenStreetMap contributors', '© Copernicus']);

    expect(image).toBe(written);
    expect(canvases[0].width).toBe(800);
    expect(canvases[0].height).toBe(600);

    const [picture, strip, text] = context.drawn;
    expect(picture).toMatchObject({ op: 'drawImage', args: [bitmap, 0, 0] });

    // The strip runs the full width and ends exactly at the bottom edge, and it lets the ground
    // under it show through.
    expect(strip.op).toBe('fillRect');
    const [left, top, width, height] = strip.args as number[];
    expect(left).toBe(0);
    expect(width).toBe(800);
    expect(top + height).toBe(600);
    expect(strip.fillStyle).toBe('rgba(0, 0, 0, 0.55)');

    // Both credits, in the order given, inside the strip and in a colour that reads on it.
    expect(text.op).toBe('fillText');
    expect(text.args[0]).toBe('© OpenStreetMap contributors · © Copernicus');
    expect(text.args[2] as number).toBeGreaterThan(top);
    expect(text.args[2] as number).toBeLessThan(600);
    expect(text.fillStyle).toBe('#ffffff');
    expect(context.drawn).toHaveLength(3);
  });

  it('breaks credits too long for the picture onto further lines and grows the strip to hold them', async () => {
    // Eight hundred pixels at ten a character is under eighty characters a line.
    const long = [
      'Imagery by a provider with a long name and a longer licence statement',
      'Elevation model derived from several national programmes and one global one',
    ];

    await imageWithCredits(frame, long);

    const strip = context.drawn.find((item) => item.op === 'fillRect')!;
    const texts = context.drawn.filter((item) => item.op === 'fillText');
    expect(texts.length).toBeGreaterThan(1);
    for (const text of texts) {
      expect((text.args[0] as string).length * 10).toBeLessThanOrEqual(800);
    }
    // Nothing was dropped to make it fit.
    expect(texts.map((text) => text.args[0]).join(' ')).toBe(long.join(' · '));
    // Each line sits lower than the last, and all of them inside a strip that still ends at the edge.
    const [, top, , height] = strip.args as number[];
    const baselines = texts.map((text) => text.args[2] as number);
    expect([...baselines].sort((a, b) => a - b)).toEqual(baselines);
    expect(baselines[0]).toBeGreaterThan(top);
    expect(top + height).toBe(600);
  });

  it('sets the type larger on a picture with more pixels, where the same type would be a hairline', async () => {
    await imageWithCredits(frame, ['© Copernicus']);
    const small = context.drawn.find((item) => item.op === 'fillText')!.font;

    context = new FakeContext();
    bitmap = { width: 3840, height: 2160, close: vi.fn() };
    await imageWithCredits(frame, ['© Copernicus']);
    const large = context.drawn.find((item) => item.op === 'fillText')!.font;

    expect(Number.parseInt(large, 10)).toBeGreaterThan(Number.parseInt(small, 10));
  });

  it('hands the frame back untouched when there is no credit to carry', async () => {
    expect(await imageWithCredits(frame, [])).toBe(frame);
    expect(await imageWithCredits(frame, ['  ', '<b></b>'])).toBe(frame);
    expect(canvases).toEqual([]);
  });

  it('lets go of the decoded frame whether or not the picture could be written', async () => {
    await imageWithCredits(frame, ['© Copernicus']);
    expect(bitmap.close).toHaveBeenCalledTimes(1);

    written = null;
    await expect(imageWithCredits(frame, ['© Copernicus'])).rejects.toThrow(/PNG/);
    expect(bitmap.close).toHaveBeenCalledTimes(2);
  });
});
