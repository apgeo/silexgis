// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  cogColourExpression,
  cogDrawing,
  cogSourceInfo,
  drawnBands,
  isNodata,
  sampleTypeRange,
  scaledByte,
  writeBandColour,
  type CogFileFacts,
  type CogOptions,
} from './cogScaling.ts';

const options = (overrides: Partial<CogOptions> = {}): CogOptions => ({
  bands: null,
  nodata: null,
  min: null,
  max: null,
  projection: 'EPSG:3857',
  projectionDefinition: null,
  ...overrides,
});

const facts = (overrides: Partial<CogFileFacts> = {}): CogFileFacts => ({
  samplesPerPixel: 1,
  sampleFormat: 1,
  bitsPerSample: 16,
  statistics: {},
  nodata: null,
  hasMask: false,
  ...overrides,
});

describe('the bands a GeoTIFF entry draws', () => {
  it('are the ones it names, in its order', () => {
    expect(drawnBands({ bands: [3, 1] }, 3)).toEqual([3, 1]);
  });

  it('are the file’s own, up to four, when it names none', () => {
    expect(drawnBands({ bands: null }, 1)).toEqual([1]);
    expect(drawnBands({ bands: [] }, 3)).toEqual([1, 2, 3]);
    expect(drawnBands({ bands: null }, 6)).toEqual([1, 2, 3, 4]);
  });

  it('refuse a band the file does not have, rather than drawing grey', () => {
    expect(drawnBands({ bands: [1, 4] }, 3)).toBe('cog.band_missing');
  });
});

describe('the range a GeoTIFF is stretched across', () => {
  it('is the entry’s when it states one, whatever the file says', () => {
    const drawing = cogDrawing(options({ min: 10, max: 20 }), facts({ statistics: { min: 0, max: 900 } }));
    expect(drawing).toMatchObject({ min: 10, max: 20 });
  });

  it('falls back on the file’s statistics, then on the sample type, end by end', () => {
    expect(cogDrawing(options(), facts({ statistics: { min: 100, max: 900 } }))).toMatchObject({ min: 100, max: 900 });
    expect(cogDrawing(options(), facts({ statistics: { max: 900 } }))).toMatchObject({ min: 0, max: 900 });
    expect(cogDrawing(options(), facts())).toMatchObject({ min: 0, max: 65535 });
  });

  it('assumes the sample types’ ranges the flat map’s reader assumes', () => {
    expect(sampleTypeRange(1, 8)).toEqual({ min: 0, max: 255 });
    expect(sampleTypeRange(1, 32)).toEqual({ min: 0, max: 4294967295 });
    expect(sampleTypeRange(2, 16)).toEqual({ min: -32768, max: 32767 });
    expect(sampleTypeRange(3, 32)).toEqual({ min: 1.2e-38, max: 3.4e38 });
    expect(sampleTypeRange(3, 64)).toEqual({ min: 0, max: 255 });
  });

  it('stretches to a byte by truncating, clamped at both ends', () => {
    expect(scaledByte(0, 0, 1000)).toBe(0);
    expect(scaledByte(500, 0, 1000)).toBe(127); // 127.5, truncated as a byte array stores it
    expect(scaledByte(1000, 0, 1000)).toBe(255);
    expect(scaledByte(-5, 0, 1000)).toBe(0);
    expect(scaledByte(5000, 0, 1000)).toBe(255);
    expect(scaledByte(Number.NaN, 0, 1000)).toBe(0);
  });
});

describe('what is see-through', () => {
  it('is the entry’s nodata over the file’s, and either makes the file transparent somewhere', () => {
    expect(cogDrawing(options({ nodata: 7 }), facts({ nodata: 0 }))).toMatchObject({ nodata: 7, transparency: true });
    expect(cogDrawing(options(), facts({ nodata: 0 }))).toMatchObject({ nodata: 0, transparency: true });
    expect(cogDrawing(options(), facts())).toMatchObject({ nodata: undefined, transparency: false });
  });

  it('includes a mask, which makes the file transparent even without a nodata value', () => {
    expect(cogDrawing(options(), facts({ hasMask: true }))).toMatchObject({ nodata: undefined, transparency: true });
  });

  it('matches NaN to a NaN nodata, and nothing to no nodata', () => {
    expect(isNodata(Number.NaN, Number.NaN)).toBe(true);
    expect(isNodata(3, Number.NaN)).toBe(false);
    expect(isNodata(0, 0)).toBe(true);
    expect(isNodata(0, undefined)).toBe(false);
  });
});

describe('one pixel’s colour from its bands', () => {
  const colour = (bytes: number[]) => {
    const out = new Uint8ClampedArray(4);
    writeBandColour(bytes, bytes.length, out, 0);
    return [...out];
  };

  it('is grey for one band, grey with alpha for two', () => {
    expect(colour([90])).toEqual([90, 90, 90, 255]);
    expect(colour([90, 40])).toEqual([90, 90, 90, 40]);
  });

  it('is red, green and blue for three, with alpha for four', () => {
    expect(colour([1, 2, 3])).toEqual([1, 2, 3, 255]);
    expect(colour([1, 2, 3, 4])).toEqual([1, 2, 3, 4]);
  });
});

describe('what the flat map hands its GeoTIFF reader', () => {
  it('carries what the entry states and leaves the rest to the reader’s same fallbacks', () => {
    expect(cogSourceInfo('/f.tif', options())).toEqual({ url: '/f.tif' });
    expect(cogSourceInfo('/f.tif', options({ bands: [2], nodata: 0, min: 1, max: 9 }))).toEqual({
      url: '/f.tif',
      bands: [2],
      nodata: 0,
      min: 1,
      max: 9,
    });
  });

  it('spells the colour rule out, the two-band case included', () => {
    expect(cogColourExpression(1, false)).toEqual(['array', ['band', 1], ['band', 1], ['band', 1], 1]);
    // Two bands and the reader's own alpha band: grey, with the second band times the alpha band.
    expect(cogColourExpression(2, true)).toEqual([
      'array',
      ['band', 1],
      ['band', 1],
      ['band', 1],
      ['*', ['band', 2], ['band', 3]],
    ]);
    expect(cogColourExpression(3, true)).toEqual(['array', ['band', 1], ['band', 2], ['band', 3], ['band', 4]]);
    expect(cogColourExpression(4, false)).toEqual(['array', ['band', 1], ['band', 2], ['band', 3], ['band', 4]]);
  });
});
