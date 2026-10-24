// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../api/hooks.ts';

// How a cloud-optimised GeoTIFF from the layer catalogue turns into colour: which bands are drawn,
// which value range they are stretched across, which samples are see-through, and how one to four
// bands become one colour. The flat map hands these rules to OpenLayers' GeoTIFF source and WebGL
// tile layer; the 3D scene has no such reader and applies them itself, tile by tile. Both read them
// from here, so a hillshade cannot be stretched one way on the map and another on the globe.
//
// The fallbacks are OpenLayers' own (its `normalize` behaviour for a GeoTIFF source): a range the
// entry does not state comes from the file's GDAL statistics, else from the sample type's range.

export type CogOptions = NonNullable<MapLayerInfo['cog']>;

/** The most bands one colour is made of: red, green, blue and alpha. */
const MAX_DRAWN_BANDS = 4;

/** What a GeoTIFF says about itself that the drawing rules depend on. */
export interface CogFileFacts {
  /** Samples per pixel of the data as read — three for a file converted to RGB on reading. */
  samplesPerPixel: number;
  /** TIFF SampleFormat of the data as read: 1 unsigned, 2 signed, 3 floating point. */
  sampleFormat: number;
  /** Bits per sample of the data as read. */
  bitsPerSample: number;
  /** STATISTICS_MINIMUM / STATISTICS_MAXIMUM GDAL wrote for the full-resolution image, if it did. */
  statistics: { min?: number; max?: number };
  /** The file's own nodata value (its GDAL_NODATA tag), or null. */
  nodata: number | null;
  /** True when the file carries a transparency mask beside its images. */
  hasMask: boolean;
}

/** The rules for one file, resolved once from the entry and the file and then applied per sample. */
export interface CogDrawing {
  /** The file's bands to read, numbered from 1, in drawing order (1 to 4 of them). */
  bands: number[];
  /** Every band is stretched across this one range. */
  min: number;
  max: number;
  /** A pixel whose every drawn sample equals this is see-through. Undefined: none is. */
  nodata: number | undefined;
  /** Whether see-through pixels exist at all: a nodata value, or a mask. */
  transparency: boolean;
}

/** Why a file cannot be drawn by the entry's rules; a stable code the 3D layer list words. */
export type CogDrawingProblem = 'cog.band_missing';

/**
 * The bands an entry draws: the ones it names, or the file's own up to four. A named band the file
 * does not have is a problem rather than a silently grey picture.
 */
export function drawnBands(
  options: Pick<CogOptions, 'bands'>,
  samplesPerPixel: number,
): number[] | CogDrawingProblem {
  if (options.bands && options.bands.length > 0) {
    return options.bands.some((band) => band > samplesPerPixel) ? 'cog.band_missing' : [...options.bands];
  }
  return Array.from({ length: Math.min(samplesPerPixel, MAX_DRAWN_BANDS) }, (_, index) => index + 1);
}

/**
 * The range of a sample type, as OpenLayers assumes it when nothing better is known. Its odd
 * corners are kept on purpose — a 32-bit float from 1.2e-38, a 64-bit one as if it were a byte —
 * because matching the flat map matters more here than improving on it.
 */
export function sampleTypeRange(sampleFormat: number, bitsPerSample: number): { min: number; max: number } {
  if (sampleFormat === 3) {
    return bitsPerSample === 32 ? { min: 1.2e-38, max: 3.4e38 } : { min: 0, max: 255 };
  }
  if (sampleFormat === 2) {
    switch (bitsPerSample) {
      case 8:
        return { min: -128, max: 127 };
      case 16:
        return { min: -32768, max: 32767 };
      case 32:
        return { min: -2147483648, max: 2147483647 };
      default:
        return { min: 0, max: 255 };
    }
  }
  switch (bitsPerSample) {
    case 16:
      return { min: 0, max: 65535 };
    case 32:
      return { min: 0, max: 4294967295 };
    default:
      return { min: 0, max: 255 };
  }
}

/** Resolves the entry's rules against what the file says about itself. */
export function cogDrawing(options: CogOptions, facts: CogFileFacts): CogDrawing | CogDrawingProblem {
  const bands = drawnBands(options, facts.samplesPerPixel);
  if (typeof bands === 'string') {
    return bands;
  }
  const typeRange = sampleTypeRange(facts.sampleFormat, facts.bitsPerSample);
  // The catalogue publishes both ends or neither; each end falls back on its own, as OpenLayers'.
  const min = options.min ?? facts.statistics.min ?? typeRange.min;
  const max = options.max ?? facts.statistics.max ?? typeRange.max;
  const nodata = options.nodata ?? facts.nodata ?? undefined;
  return { bands, min, max, nodata, transparency: nodata !== undefined || facts.hasMask };
}

/**
 * One sample stretched to a byte: `(v − min) / (max − min)` of 255, clamped, and truncated as the
 * byte array OpenLayers writes it into truncates.
 */
export function scaledByte(value: number, min: number, max: number): number {
  const scaled = (255 * (value - min)) / (max - min);
  if (!(scaled > 0)) {
    return 0;
  }
  return scaled >= 255 ? 255 : Math.trunc(scaled);
}

/** Whether a sample is the nodata value, NaN standing for itself. */
export function isNodata(value: number, nodata: number | undefined): boolean {
  if (nodata === undefined) {
    return false;
  }
  return Number.isNaN(nodata) ? Number.isNaN(value) : value === nodata;
}

/**
 * One pixel's colour from its drawn, already-stretched bands: one band is grey, two are grey with
 * alpha, three are red, green and blue, four add alpha. Written into `out` at `offset` as RGBA bytes.
 */
export function writeBandColour(bytes: ArrayLike<number>, count: number, out: Uint8ClampedArray, offset: number): void {
  if (count >= 3) {
    out[offset] = bytes[0]!;
    out[offset + 1] = bytes[1]!;
    out[offset + 2] = bytes[2]!;
    out[offset + 3] = count >= 4 ? bytes[3]! : 255;
    return;
  }
  out[offset] = bytes[0]!;
  out[offset + 1] = bytes[0]!;
  out[offset + 2] = bytes[0]!;
  out[offset + 3] = count === 2 ? bytes[1]! : 255;
}

/**
 * What the flat map hands OpenLayers' GeoTIFF source for an entry: the same bands, nodata and
 * range as above, left out where the entry states nothing so the source applies the same
 * fallbacks to the file itself.
 */
export function cogSourceInfo(url: string, options: CogOptions): {
  url: string;
  bands?: number[];
  nodata?: number;
  min?: number;
  max?: number;
} {
  return {
    url,
    ...(options.bands && options.bands.length > 0 ? { bands: [...options.bands] } : {}),
    ...(options.nodata !== null ? { nodata: options.nodata } : {}),
    ...(options.min !== null && options.max !== null ? { min: options.min, max: options.max } : {}),
  };
}

/**
 * The colour rule above as an OpenLayers WebGL style expression, for a source drawing `bandCount`
 * bands, with or without the alpha band OpenLayers appends when the file has see-through pixels.
 *
 * Needed for one case its default gets wrong: two bands plus that alpha band are three, which it
 * draws as red, green and blue rather than as grey with alpha. Every other count is written out
 * too, so the flat map states the rule rather than relying on which cases the default happens to
 * match.
 */
export function cogColourExpression(bandCount: number, transparency: boolean): unknown[] {
  const band = (n: number) => ['band', n];
  const alphaBand = bandCount + 1;
  const ownAlpha = bandCount === 2 ? band(2) : bandCount >= 4 ? band(4) : 1;
  const alpha = transparency
    ? ownAlpha === 1
      ? band(alphaBand)
      : ['*', ownAlpha, band(alphaBand)]
    : ownAlpha;
  return bandCount >= 3
    ? ['array', band(1), band(2), band(3), alpha]
    : ['array', band(1), band(1), band(1), alpha];
}
