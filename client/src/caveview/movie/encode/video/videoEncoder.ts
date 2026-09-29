// SPDX-License-Identifier: AGPL-3.0-or-later
import { avcDecoderConfig } from '../mp4/avcConfig.ts';
import { muxMp4 } from '../mp4/mp4Muxer.ts';
import {
  MOVIE_MIME,
  movieEncoderError,
  type MovieEncoder,
  type MovieEncoderOptions,
  type MovieQuality,
} from '../movieFormats.ts';
import { muxWebm } from '../webm/webmMuxer.ts';
import type { EncodedVideoFrame } from './encodedFrame.ts';

/**
 * WebM and MP4 through the browser's own video encoder (WebCodecs), with this application's own
 * writers around its output.
 *
 * Every frame is stamped with the time its index says — index / fps — never with the moment it was
 * rendered, so a slow render makes a slow export and never an uneven movie. The browser encodes
 * off this thread; the frames it hands back are kept (compressed) until `finish` writes the file.
 *
 * Which codec is used is asked, not assumed: an encoder that exists may still support nothing
 * (WebKit's test build does exactly that), and H.264 support differs between builds.
 */

export type VideoMovieFormat = 'webm' | 'mp4';

/** Bits per pixel per frame for each quality; the bitrate is width · height · fps · this. */
export const VIDEO_BITS_PER_PIXEL: Record<MovieQuality, number> = { low: 0.05, medium: 0.1, high: 0.2 };

/** A keyframe every this many seconds, so a player can seek anywhere without decoding far. */
export const VIDEO_KEYFRAME_SECONDS = 2;

// Frames allowed to wait inside the browser's encoder before addFrame stops resolving at once.
const MAX_ENCODE_QUEUE = 4;
// Where the encoder has no dequeue event, how often its queue is looked at; where it has one, the
// same timer is only a safety net in case an event is missed.
const QUEUE_POLL_MS = 5;
const QUEUE_SAFETY_MS = 100;

const MAX_FPS = 120;

// The H.264 levels a frame size and rate are checked against: the level's number (ten times its
// name), the most macroblocks in a frame, the most macroblocks a second. The lowest offered is
// 3.1, the level measured working in every browser this was checked in.
const H264_LEVELS: readonly [number, number, number][] = [
  [31, 3600, 108000],
  [32, 5120, 216000],
  [40, 8192, 245760],
  [42, 8704, 522240],
  [50, 22080, 589824],
  [51, 36864, 983040],
  [52, 36864, 2073600],
];

/** The lowest H.264 level that holds this size at this rate, or null when none does. */
export function h264Level(width: number, height: number, fps: number): number | null {
  const mbWide = Math.ceil(width / 16);
  const mbHigh = Math.ceil(height / 16);
  const frameMbs = mbWide * mbHigh;
  for (const [level, maxFrame, maxRate] of H264_LEVELS) {
    // A level also bounds each side, at the square root of eight frames' worth of macroblocks.
    const maxSide = Math.sqrt(8 * maxFrame);
    if (frameMbs <= maxFrame && frameMbs * fps <= maxRate && mbWide <= maxSide && mbHigh <= maxSide) return level;
  }
  return null;
}

/**
 * The codecs tried for a format, best first. WebM: VP9, else VP8. MP4: H.264 Baseline — the
 * profile without B-frames, so frames come back in the order they are shown and the MP4 needs no
 * reordering table — at the level the size needs, first as plain and then as constrained Baseline.
 */
export function videoCodecCandidates(format: VideoMovieFormat, width: number, height: number, fps: number): string[] {
  if (format === 'webm') return ['vp09.00.10.08', 'vp8'];
  const level = h264Level(width, height, fps);
  if (level === null) return [];
  const ll = level.toString(16).padStart(2, '0');
  return [`avc1.4200${ll}`, `avc1.42e0${ll}`];
}

export function videoBitrate(width: number, height: number, fps: number, quality: MovieQuality): number {
  return Math.round(width * height * fps * VIDEO_BITS_PER_PIXEL[quality]);
}

export function videoEncoderConfig(
  codec: string,
  width: number,
  height: number,
  fps: number,
  quality: MovieQuality,
): VideoEncoderConfig {
  return {
    codec,
    width,
    height,
    bitrate: videoBitrate(width, height, fps, quality),
    framerate: fps,
    latencyMode: 'quality',
    ...(codec.startsWith('avc1.') ? { avc: { format: 'avc' as const } } : {}),
  };
}

/** Why a size or rate cannot be encoded at all, whatever the browser; null when it can be. */
export function videoSizeProblem(width: number, height: number, fps: number): string | null {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 2 || height < 2) {
    return `a frame of ${width}×${height} is not a video size`;
  }
  // The colour planes are half the size of the brightness plane in each direction.
  if (width % 2 !== 0 || height % 2 !== 0) return `a video frame's sides must be even, not ${width}×${height}`;
  if (!Number.isFinite(fps) || fps <= 0 || fps > MAX_FPS) return `${fps} frames a second is not a video frame rate`;
  return null;
}

/** The first codec this browser says it can encode the format with at this size and rate, or null. */
export async function chooseVideoCodec(
  format: VideoMovieFormat,
  width: number,
  height: number,
  fps: number,
  quality: MovieQuality = 'medium',
): Promise<string | null> {
  if (typeof VideoEncoder === 'undefined' || typeof VideoFrame === 'undefined') return null;
  if (videoSizeProblem(width, height, fps)) return null;
  for (const codec of videoCodecCandidates(format, width, height, fps)) {
    try {
      const answer = await VideoEncoder.isConfigSupported(videoEncoderConfig(codec, width, height, fps, quality));
      if (answer.supported) return codec;
    } catch {
      // A configuration the browser cannot even parse is one it does not support.
    }
  }
  return null;
}

export async function openVideoEncoder(format: VideoMovieFormat, options: MovieEncoderOptions): Promise<VideoMovieEncoder> {
  const { width, height, fps, quality } = options;
  if (!VIDEO_BITS_PER_PIXEL[quality]) throw movieEncoderError(format, 'open', `unknown quality '${quality}'`);
  const problem = videoSizeProblem(width, height, fps);
  if (problem) throw movieEncoderError(format, 'open', problem);
  const codec = await chooseVideoCodec(format, width, height, fps, quality);
  if (!codec) {
    throw movieEncoderError(format, 'open', `this browser cannot encode it at ${width}×${height}, ${fps} frames a second`);
  }
  try {
    return new VideoMovieEncoder(format, codec, options);
  } catch (e) {
    throw movieEncoderError(format, 'open', e);
  }
}

export class VideoMovieEncoder implements MovieEncoder {
  readonly format: VideoMovieFormat;
  readonly samplesWanted = 0;
  readonly codec: string;

  private readonly encoder: VideoEncoder;
  private readonly width: number;
  private readonly height: number;
  private readonly fps: number;
  private readonly keyInterval: number;
  private frames: EncodedVideoFrame[] = [];
  private description: Uint8Array | null = null;
  private nextIndex = 0;
  private failure: Error | null = null;
  private closed = false;
  private wakers = new Set<() => void>();

  constructor(format: VideoMovieFormat, codec: string, options: MovieEncoderOptions) {
    this.format = format;
    this.codec = codec;
    this.width = options.width;
    this.height = options.height;
    this.fps = options.fps;
    this.keyInterval = Math.max(1, Math.round(VIDEO_KEYFRAME_SECONDS * options.fps));
    this.encoder = new VideoEncoder({
      output: (chunk, metadata) => this.take(chunk, metadata),
      error: (e) => this.fail('encode', e),
    });
    this.encoder.configure(videoEncoderConfig(codec, options.width, options.height, options.fps, options.quality));
  }

  /** Frames the browser has handed back so far. */
  get encodedFrames(): number {
    return this.frames.length;
  }

  async prime(): Promise<void> {
    // A video encoder needs no palette; priming is only for encoders that ask for samples.
  }

  addFrame(source: HTMLCanvasElement | OffscreenCanvas, index: number): Promise<void> {
    if (this.failure) return Promise.reject(this.failure);
    let frame: VideoFrame | null = null;
    try {
      if (this.closed) throw new Error('the encoder is closed');
      if (index !== this.nextIndex) throw new Error(`frame ${index} was given where frame ${this.nextIndex} was expected`);
      const timestamp = this.timeOf(index);
      frame = new VideoFrame(source, { timestamp, duration: this.timeOf(index + 1) - timestamp });
      this.encoder.encode(frame, { keyFrame: index % this.keyInterval === 0 });
      this.nextIndex++;
    } catch (e) {
      return Promise.reject(movieEncoderError(this.format, 'addFrame', e));
    } finally {
      // The encoder keeps its own reference to what it was given; this one is released at once so
      // no frame's pixels outlive the call.
      frame?.close();
    }
    return this.room();
  }

  async finish(): Promise<Blob> {
    if (this.closed) throw movieEncoderError(this.format, 'finish', 'the encoder is closed');
    if (this.failure) throw this.failure;
    if (this.nextIndex === 0) throw movieEncoderError(this.format, 'finish', 'no frames were added');
    try {
      await this.encoder.flush();
    } catch (e) {
      if (this.closed) throw movieEncoderError(this.format, 'finish', 'the encoder was closed');
      throw this.failure ?? movieEncoderError(this.format, 'finish', e);
    }
    if (this.closed) throw movieEncoderError(this.format, 'finish', 'the encoder was closed');
    if (this.failure) throw this.failure;
    let parts: Uint8Array[];
    try {
      if (this.frames.length !== this.nextIndex) {
        throw new Error(`${this.nextIndex} frames were added but ${this.frames.length} came back from the encoder`);
      }
      parts = this.mux();
    } catch (e) {
      const error = movieEncoderError(this.format, 'write', e);
      this.close();
      throw error;
    }
    const blob = new Blob(parts as BlobPart[], { type: MOVIE_MIME[this.format] });
    this.close();
    return blob;
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    if (this.encoder.state !== 'closed') {
      try {
        this.encoder.close();
      } catch {
        // Already closed by an error the browser reported; nothing is left to release.
      }
    }
    this.frames = [];
    // Closing mid-run is how an export is cancelled, and cancelling is not an error: whoever is
    // waiting for room simply stops waiting.
    this.wakeAll();
  }

  private timeOf(index: number): number {
    return Math.round((index * 1e6) / this.fps);
  }

  private mux(): Uint8Array[] {
    if (this.format === 'webm') {
      const codecId = this.codec.startsWith('vp09') ? 'V_VP9' : 'V_VP8';
      return muxWebm({ codecId, width: this.width, height: this.height, fps: this.fps }, this.frames);
    }
    const avcC = avcDecoderConfig(this.description, this.frames[0]?.data ?? null);
    if (!avcC) throw new Error('the H.264 encoder reported no decoder configuration');
    return muxMp4({ width: this.width, height: this.height, fps: this.fps, avcC }, this.frames);
  }

  private take(chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata): void {
    if (this.closed || this.failure) return;
    try {
      const data = new Uint8Array(chunk.byteLength);
      chunk.copyTo(data);
      const own = Math.round(1e6 / this.fps);
      this.frames.push({ data, timestamp: chunk.timestamp, duration: chunk.duration ?? own, key: chunk.type === 'key' });
      const description = metadata?.decoderConfig?.description;
      if (description) this.description = copyBytes(description);
    } catch (e) {
      this.fail('encode', e);
    }
  }

  private fail(stage: string, e: unknown): void {
    if (this.closed) return;
    this.failure ??= movieEncoderError(this.format, stage, e);
    if (this.encoder.state !== 'closed') {
      try {
        this.encoder.close();
      } catch {
        // Nothing more to release.
      }
    }
    this.wakeAll();
  }

  private wakeAll(): void {
    const wakers = [...this.wakers];
    this.wakers.clear();
    for (const wake of wakers) wake();
  }

  // Resolves once the browser's queue has room again, or the encoder closed; rejects if it failed.
  private async room(): Promise<void> {
    while (!this.closed && !this.failure && this.encoder.encodeQueueSize > MAX_ENCODE_QUEUE) {
      await this.dequeued();
    }
    if (this.failure && !this.closed) throw this.failure;
  }

  private dequeued(): Promise<void> {
    return new Promise<void>((resolve) => {
      const hasEvent = 'ondequeue' in this.encoder;
      const done = () => {
        clearTimeout(timer);
        if (hasEvent) this.encoder.removeEventListener('dequeue', done);
        this.wakers.delete(done);
        resolve();
      };
      const timer = setTimeout(done, hasEvent ? QUEUE_SAFETY_MS : QUEUE_POLL_MS);
      if (hasEvent) this.encoder.addEventListener('dequeue', done);
      this.wakers.add(done);
    });
  }
}

function copyBytes(source: AllowSharedBufferSource): Uint8Array {
  const view = ArrayBuffer.isView(source)
    ? new Uint8Array(source.buffer, source.byteOffset, source.byteLength)
    : new Uint8Array(source);
  return new Uint8Array(view);
}
