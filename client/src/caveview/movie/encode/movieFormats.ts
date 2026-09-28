// SPDX-License-Identifier: AGPL-3.0-or-later

// The shapes every movie encoder shares. They live apart from the facade so that each encoder can
// implement them without importing the facade that opens it.

export type MovieFormat = 'gif' | 'webm' | 'mp4';
export type MovieQuality = 'low' | 'medium' | 'high';

export const MOVIE_MIME: Record<MovieFormat, string> = {
  gif: 'image/gif',
  webm: 'video/webm',
  mp4: 'video/mp4',
};

export const MOVIE_EXTENSION: Record<MovieFormat, string> = {
  gif: 'gif',
  webm: 'webm',
  mp4: 'mp4',
};

/**
 * The frame rates a GIF can hold exactly. A GIF delay is a whole number of centiseconds, so only
 * rates whose frame lasts a whole number of them keep every frame the same length; faster than 25
 * is left out because browsers slow down very short delays.
 */
export const GIF_FRAME_RATES: readonly number[] = [10, 12.5, 20, 25];

export interface MovieFormatSupport {
  format: MovieFormat;
  supported: boolean;
  /** The codec a video format would be encoded with; null for GIF and for an unsupported format. */
  codec: string | null;
}

export interface MovieEncoderOptions {
  width: number;
  height: number;
  fps: number;
  quality: MovieQuality;
  /** '#rrggbb' colours a GIF must reproduce exactly (marker, plate and caption colours). */
  reservedColors?: readonly string[];
}

export interface MovieEncoder {
  readonly format: MovieFormat;
  /** Frames the encoder wants to see before the first addFrame (a GIF palette); 0 for video. */
  readonly samplesWanted: number;
  /** Called once with samplesWanted frames (RGBA, width×height) before addFrame, iff samplesWanted > 0. */
  prime(samples: readonly ImageData[]): Promise<void>;
  /** Reads `source` synchronously within the call (so the caller may redraw it as soon as the call
   *  returns); the promise is back-pressure only. Frames arrive in order, index 0,1,2…, each lasting
   *  1/fps. A frame identical to the previous one may be merged into it (longer delay/duration). */
  addFrame(source: HTMLCanvasElement | OffscreenCanvas, index: number): Promise<void>;
  finish(): Promise<Blob>;
  /** Releases workers / encoders. Idempotent; safe mid-run (that is how cancel works). */
  close(): void;
}

/** An encoder failure, saying which format and which stage it happened in. */
export function movieEncoderError(format: MovieFormat, stage: string, detail: unknown): Error {
  // A browser's DOMException carries its text in `message` too, though it is not always an Error.
  const message =
    detail instanceof Error
      ? detail.message
      : typeof (detail as { message?: unknown } | null)?.message === 'string'
        ? (detail as { message: string }).message
        : String(detail);
  return new Error(`${format.toUpperCase()} encoder, ${stage}: ${message}`, { cause: detail });
}
