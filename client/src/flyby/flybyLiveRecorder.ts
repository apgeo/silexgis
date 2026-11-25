// SPDX-License-Identifier: AGPL-3.0-or-later
import { videoBitrate } from '../movie/encode/video/videoEncoder.ts';
import type { FlybyMovieOutput, FlybyStretch } from './flybyMovie.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import { placeFlybyMoment, type FlybyStage } from './flybyPlayer.ts';
import {
  createFlybyFrameComposer,
  detachedCanvas,
  FLYBY_FRAME_SETTLE_MILLISECONDS,
  flybyCaptureSize,
  type FlybyFrameCanvas,
  type FlybyRecorderStage,
  type FlybyRecording,
} from './flybyRecorder.ts';
import type { FlybyTimeline } from './flybyTimeline.ts';

// Recording a flyby live, by the browser's own recorder, while it plays in real time.
//
// The frame-exact recorder draws each frame for its own moment and so is never jerky, however slowly
// the machine draws. This is the other way: the clock is the wall's, a frame the machine could not
// draw in time is simply not in the file, and what the file holds is what the screen showed at the
// speed it was drawn. In exchange it takes exactly as long as the flyby.
//
// The scene's own drawing surface cannot simply be filmed: it holds neither the texts (an overlay of
// their own) nor the credits (elements of the page), and it is not kept between frames. So on every
// display frame the moment is placed, the scene drawn and read at once, and composed — scene, texts,
// credits — onto a canvas of the movie's size by the very function the frame-exact recorder composes
// with. It is that canvas the browser records.
//
// The scene is handed back as it was found however the recording ends; cancelling rejects with an
// `AbortError`, as the frame-exact recorder's does.

/** A video container the browser's recorder writes, and the type it is asked for by. */
export interface FlybyLiveType {
  format: 'mp4' | 'webm';
  mimeType: string;
}

/** The types asked of the recorder, in the order they are preferred: MP4, then WebM with VP9, then VP8. */
const LIVE_TYPES: readonly FlybyLiveType[] = [
  { format: 'mp4', mimeType: 'video/mp4;codecs=avc1' },
  { format: 'mp4', mimeType: 'video/mp4' },
  { format: 'webm', mimeType: 'video/webm;codecs=vp9' },
  { format: 'webm', mimeType: 'video/webm;codecs=vp8' },
];

/** What of the browser a live recording needs; the default is the page's own. */
export interface FlybyLiveEnvironment {
  /** The browser's recorder, if it has one. */
  recorder?: { isTypeSupported(type: string): boolean };
  /** Whether a canvas can be filmed as a stream. */
  canvasStreams: boolean;
}

function pageEnvironment(): FlybyLiveEnvironment {
  const recorder = typeof MediaRecorder === 'undefined' ? undefined : MediaRecorder;
  const canvasStreams =
    typeof HTMLCanvasElement !== 'undefined' && typeof HTMLCanvasElement.prototype.captureStream === 'function';
  return { ...(recorder && typeof recorder.isTypeSupported === 'function' ? { recorder } : {}), canvasStreams };
}

/**
 * The containers a live recording can be written as in this browser, the preferred first, with the
 * first type of each the recorder says it writes. Empty where the browser has no recorder, cannot
 * film a canvas, or writes none of them: a live recording is then not offered.
 */
export function flybyLiveTypes(environment: FlybyLiveEnvironment = pageEnvironment()): FlybyLiveType[] {
  const { recorder } = environment;
  if (!recorder || !environment.canvasStreams) {
    return [];
  }
  const found: FlybyLiveType[] = [];
  for (const type of LIVE_TYPES) {
    if (found.some((entry) => entry.format === type.format)) {
      continue;
    }
    let supported = false;
    try {
      supported = recorder.isTypeSupported(type.mimeType);
    } catch {
      // A browser that throws on a type it does not know has not got it.
    }
    if (supported) {
      found.push(type);
    }
  }
  return found;
}

/** What films the composed canvas. */
export interface FlybyLiveCapture {
  start(): void;
  /** Ends the recording; answers the file once the recorder has handed over all it holds. */
  finish(): Promise<Blob>;
  /** Ends the recording and drops whatever it holds. Safe to call more than once, and after `finish`. */
  discard(): void;
}

export interface FlybyLiveCaptureOptions {
  mimeType: string;
  /** The most frames a second the canvas is filmed at. */
  fps: number;
  bitsPerSecond: number;
}

/** Opens what films `canvas`; `onError` is told when it fails on its own, at any time. */
export type FlybyLiveCaptureFactory = (
  canvas: HTMLCanvasElement,
  options: FlybyLiveCaptureOptions,
  onError: (error: unknown) => void,
) => FlybyLiveCapture;

/** How often the browser's recorder hands over what it has recorded, in milliseconds. */
const RECORDER_SLICE_MILLISECONDS = 1000;

/** The browser's own recorder, filming the canvas's stream. */
export const browserLiveCapture: FlybyLiveCaptureFactory = (canvas, options, onError) => {
  // The rate is a ceiling: a canvas is filmed when it has been drawn on, at most this often.
  const stream = canvas.captureStream(options.fps);
  const recorder = new MediaRecorder(stream, { mimeType: options.mimeType, videoBitsPerSecond: options.bitsPerSecond });
  const chunks: Blob[] = [];
  let dropped = false;
  let finishing: Promise<Blob> | null = null;
  let failFinish: ((error: unknown) => void) | null = null;
  const stopStream = () => {
    for (const track of stream.getTracks()) {
      track.stop();
    }
  };
  recorder.ondataavailable = (event) => {
    if (!dropped && event.data.size > 0) {
      chunks.push(event.data);
    }
  };
  recorder.onerror = (event) => {
    const detail = (event as Event & { error?: unknown }).error;
    const error = detail instanceof Error ? detail : new Error("The browser's recorder stopped with an error.");
    stopStream();
    if (failFinish) {
      failFinish(error);
    } else {
      onError(error);
    }
  };
  return {
    start: () => recorder.start(RECORDER_SLICE_MILLISECONDS),
    finish: () =>
      (finishing ??= new Promise<Blob>((resolve, reject) => {
        failFinish = reject;
        recorder.onstop = () => {
          stopStream();
          resolve(new Blob(chunks, { type: recorder.mimeType || options.mimeType }));
        };
        if (recorder.state === 'inactive') {
          recorder.onstop(new Event('stop'));
        } else {
          recorder.stop();
        }
      })),
    discard: () => {
      dropped = true;
      chunks.length = 0;
      recorder.onstop = null;
      if (recorder.state !== 'inactive') {
        recorder.stop();
      }
      stopStream();
    },
  };
};

/** The scene, as far as recording it live goes; a test supplies a plain object. */
export type FlybyLiveStage = FlybyRecorderStage & Pick<FlybyStage, 'now' | 'requestFrame' | 'cancelFrame'>;

export interface FlybyLiveProgress {
  /** Seconds recorded so far, and of how many. */
  seconds: number;
  total: number;
  /** Frames composed so far. */
  frames: number;
}

/** What a live recording says when it is over. */
export interface FlybyLiveReport {
  /** Frames composed while recording: the most the file can hold. */
  frames: number;
  /** How long the recording ran. */
  seconds: number;
  /** Frames composed a second: how fast this machine drew the flyby. */
  fps: number;
}

export interface FlybyLiveRecording {
  timeline: FlybyTimeline;
  stage: FlybyLiveStage;
  /** The file: its size, its rate (a ceiling here) and its quality; its format is `type`'s. */
  output: Pick<FlybyMovieOutput, 'width' | 'height' | 'fps' | 'quality'>;
  type: FlybyLiveType;
  stretch?: Partial<FlybyStretch>;
  /** Seconds the last moment is held for. */
  holdEndS?: number;
  compose?: FlybyRecording['compose'];
  /** How long the first moment waits for the scene to finish loading before the clock starts. */
  settleMs?: number;
  signal?: AbortSignal;
  onProgress?(progress: FlybyLiveProgress): void;
  /** Seams for tests; the defaults are the browser's recorder and a detached canvas. */
  createCapture?: FlybyLiveCaptureFactory;
  createCanvas?(width: number, height: number): FlybyFrameCanvas;
}

/** How long, in real time, a flying camera goes before the scene is asked to load what is newly in view. */
const REFRESH_MILLISECONDS = 1500;

function cancelled(): DOMException {
  return new DOMException('The movie export was cancelled.', 'AbortError');
}

/** Records the flyby live and answers its file with the report. */
export async function recordFlybyLive(recording: FlybyLiveRecording): Promise<{ file: Blob; report: FlybyLiveReport }> {
  const {
    timeline,
    stage,
    output,
    type,
    compose,
    signal,
    onProgress,
    settleMs = FLYBY_FRAME_SETTLE_MILLISECONDS,
    createCapture = browserLiveCapture,
    createCanvas = (width, height) => detachedCanvas(width, height, false),
  } = recording;
  if (signal?.aborted) {
    throw cancelled();
  }
  const { width, height } = output;
  // The stretch is read as the frame-exact recorder reads it, so both film the same part.
  const { from, to } = flybyFrameSchedule(timeline.length, output.fps, recording.stretch);
  const total = to - from + Math.max(0, recording.holdEndS ?? 0);

  // Every way out goes through here: the recording is stopped and the scene given back.
  let failWith!: (error: unknown) => void;
  const failed = new Promise<never>((_, reject) => {
    failWith = reject;
  });
  // Raced against, never awaited alone: a failure nobody is waiting for is not an unhandled one.
  failed.catch(() => undefined);
  const onAbort = () => failWith(cancelled());
  signal?.addEventListener('abort', onAbort, { once: true });

  let putBack: (() => void) | null = null;
  let capturing = false;
  let capture: FlybyLiveCapture | null = null;
  let frameHandle: number | undefined;
  let over = false;
  const handBack = (keepFile: boolean): unknown[] => {
    over = true;
    signal?.removeEventListener('abort', onAbort);
    const failures: unknown[] = [];
    const attempt = (step: () => void) => {
      try {
        step();
      } catch (error) {
        failures.push(error);
      }
    };
    if (frameHandle !== undefined) {
      const handle = frameHandle;
      frameHandle = undefined;
      attempt(() => stage.cancelFrame(handle));
    }
    if (capture && !keepFile) {
      const dropping = capture;
      attempt(() => dropping.discard());
    }
    // While the session is still open, so the one frame drawn when it ends is the view as it was.
    if (putBack) {
      attempt(putBack);
    }
    if (capturing) {
      attempt(() => stage.endCapture());
    }
    return failures;
  };

  let frames = 0;
  /** Seconds from the start of the recording to its last frame. */
  let recorded = 0;
  let file: Blob;
  try {
    putBack = stage.remember();
    // Never larger than the element: this is drawn while it is shown, at the pace of the screen.
    stage.beginCapture(flybyCaptureSize(output, stage.surfaceSize(), 'quick'));
    capturing = true;
    const frame = createCanvas(width, height);
    const composeFrame = createFlybyFrameComposer(stage, frame, compose);

    // The first moment is let load before the clock starts, so the file does not open on ground
    // still arriving; from there on, what is drawn in time is what is filmed.
    placeFlybyMoment(timeline, stage, from);
    stage.refreshData();
    await Promise.race([stage.settle(settleMs), failed]);
    if (signal?.aborted) {
      throw cancelled();
    }
    composeFrame(from);
    frames = 1;
    const options: FlybyLiveCaptureOptions = {
      mimeType: type.mimeType,
      fps: output.fps,
      bitsPerSecond: videoBitrate(width, height, output.fps, output.quality),
    };
    capture = createCapture(frame.canvas, options, (error) => failWith(error));
    capture.start();
    const zeroAt = stage.now();
    let lastRefresh = zeroAt;

    const flown = new Promise<void>((resolve, reject) => {
      const tick = () => {
        frameHandle = undefined;
        if (over) {
          return;
        }
        try {
          const now = stage.now();
          const elapsed = (now - zeroAt) / 1000;
          const t = Math.min(to, from + elapsed);
          placeFlybyMoment(timeline, stage, t);
          if (now - lastRefresh >= REFRESH_MILLISECONDS) {
            lastRefresh = now;
            stage.refreshData();
          }
          // Held to the end too, rather than left standing: a canvas is filmed only when it is
          // drawn on, and a file whose last frame is never followed by another may end on it.
          composeFrame(t);
          frames += 1;
          recorded = elapsed;
          onProgress?.({ seconds: Math.min(elapsed, total), total, frames });
          if (elapsed < total) {
            frameHandle = stage.requestFrame(tick);
            return;
          }
          // One more display frame after the last, so the browser has filmed it before it is told to stop.
          frameHandle = stage.requestFrame(() => {
            frameHandle = undefined;
            resolve();
          });
        } catch (error) {
          reject(error);
        }
      };
      frameHandle = stage.requestFrame(tick);
    });
    await Promise.race([flown, failed]);
    file = await Promise.race([capture.finish(), failed]);
    if (signal?.aborted) {
      throw cancelled();
    }
  } catch (error) {
    // The recording's own failure is the one the viewer is owed; a step of handing back that also
    // failed is not reported over it.
    handBack(false);
    throw error;
  }
  const failures = handBack(true);
  if (failures.length > 0) {
    throw failures[0];
  }
  return { file, report: { frames, seconds: recorded, fps: recorded > 0 ? frames / recorded : frames } };
}
