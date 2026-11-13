// SPDX-License-Identifier: AGPL-3.0-or-later
import { openMovieEncoder, type MovieEncoder } from '../movie/encode/movieEncoder.ts';
import { isMovieSinkMismatch, type MovieSink } from '../movie/encode/movieSink.ts';
import { creditLines, drawCreditStrip } from '../scene3d/sceneImage3d.ts';
import type { FlybyFrameSchedule, FlybyMovieLevel, FlybyMovieOutput, FlybyStretch } from './flybyMovie.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import { placeFlybyMoment, type FlybyStage } from './flybyPlayer.ts';
import type { FlybyTimeline } from './flybyTimeline.ts';

// Writing a flyby to a movie file, one frame at a time.
//
// Each frame is a function of its number. Its moment on the flyby's clock is read off the frame
// schedule, never off a clock on the wall, and the scene is put at that moment through the very
// call the player uses — so a frame shows what the screen showed at that moment, and a frame that
// took ten seconds to draw makes a slow export, never an uneven movie.
//
// The scene is somebody's view, and it is handed back as it was found however the export ends:
// finished, failed or cancelled. Cancelling is not a failure: it rejects with an `AbortError`.

/** The scene, as far as making a movie of it goes; a test supplies a plain object. */
export type FlybyRecorderStage = Parameters<typeof placeFlybyMoment>[1] &
  Pick<FlybyStage, 'refreshData'> & {
    /**
     * Resolves true once the scene has finished drawing what its camera now shows and loading the
     * data for it, or false when `timeoutMs` passes first.
     */
    settle(timeoutMs: number): Promise<boolean>;
    /** The size of the element the scene is drawn into, in pixels of the page. */
    surfaceSize(): { width: number; height: number };
    beginCapture(size: { width: number; height: number }): void;
    /** Draws a frame now; to be read before anything is awaited. */
    captureFrame(): HTMLCanvasElement;
    endCapture(): void;
    /** The credits of everything drawn now, as the scene configures them. */
    credits(): string[];
    /**
     * Notes how the scene stands now — where its camera is, and whatever else placing a moment
     * changes — and answers what puts it back so.
     */
    remember(): () => void;
  };

/** What a frame is composed on; a test supplies a recording stand-in. */
export interface FlybyFrameCanvas {
  canvas: HTMLCanvasElement;
  context: CanvasRenderingContext2D;
}

export interface FlybyRecordingProgress {
  /** Taking the frames a GIF's palette is made from, then the movie's frames, then closing the file. */
  stage: 'sampling' | 'rendering' | 'finishing';
  /** Samples and frames together: the whole of the work, for a bar. */
  done: number;
  total: number;
  /** The stage's own count, which is what a person is told: a movie of 20 frames is 20 frames. */
  step: number;
  steps: number;
  /** Seconds the rest is reckoned to take at the pace so far; null until there is a pace. */
  secondsLeft: number | null;
}

/** What an export says when it is over. */
export interface FlybyRecordingReport {
  frames: number;
  /** Frames taken after their wait for the scene to finish loading had run out. */
  unsettledFrames: number;
  /** How long the export took. */
  seconds: number;
}

export interface FlybyRecording {
  timeline: FlybyTimeline;
  stage: FlybyRecorderStage;
  output: FlybyMovieOutput;
  /** The part of the flyby filmed; left out, all of it. */
  stretch?: Partial<FlybyStretch>;
  /** Seconds the last moment is held for. */
  holdEndS?: number;
  /**
   * Draws over each frame, after the scene and before the credits: `t` is the frame's moment on
   * the flyby's clock, so whatever is drawn is a function of it as the frame is.
   */
  compose?(context: CanvasRenderingContext2D, t: number, width: number, height: number): void;
  /** How long one frame of a careful level waits for the scene, at most. */
  settleMs?: number;
  signal?: AbortSignal;
  onProgress?(progress: FlybyRecordingProgress): void;
  /** Seams for tests; the defaults are the real encoder, a detached canvas, a task yield and the page's clock. */
  openEncoder?: typeof openMovieEncoder;
  createCanvas?(width: number, height: number, willReadFrequently: boolean): FlybyFrameCanvas;
  nextTask?(): Promise<void>;
  now?(): number;
}

/** How long a frame taken carefully waits for the scene at most, when the caller does not say. */
export const FLYBY_FRAME_SETTLE_MILLISECONDS = 15_000;
/** How far the flyby's clock moves before the scene is asked to load what is newly in view. */
const REFRESH_SECONDS = 1.5;

export function isFlybyRecordingAbort(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError';
}

function cancelled(): DOMException {
  return new DOMException('The movie export was cancelled.', 'AbortError');
}

/** Yields through a message rather than a timer, which a background tab holds back to once a second. */
function nextTask(): Promise<void> {
  return new Promise((resolve) => {
    const channel = new MessageChannel();
    channel.port1.onmessage = () => {
      channel.port1.close();
      resolve();
    };
    channel.port2.postMessage(null);
  });
}

/** A canvas of its own to compose a movie's frames on, on no page. */
export function detachedCanvas(width: number, height: number, willReadFrequently: boolean): FlybyFrameCanvas {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d', { willReadFrequently });
  if (context === null) {
    throw new Error('A movie frame could not be composed: the browser gave no 2D canvas.');
  }
  return { canvas, context };
}

/** Which frames a GIF's palette is sampled from: `wanted` of them, spread evenly from first to last. */
export function flybySampleFrames(count: number, wanted: number): number[] {
  if (wanted <= 0 || count <= 0) {
    return [];
  }
  if (wanted === 1 || count === 1) {
    return [0];
  }
  return Array.from({ length: wanted }, (_, index) => Math.round((index * (count - 1)) / (wanted - 1)));
}

/**
 * The size the scene is drawn at for a frame of `movie`, from an element of `surface` pixels of the
 * page that has the movie's shape.
 *
 * The movie's own size whenever the element is at least as large, or the level asks for it. Below
 * that, the element's own size: the scene is drawn no larger than the screen shows it, and the
 * frame is scaled up to the movie's size when it is composed.
 */
export function flybyCaptureSize(
  movie: { width: number; height: number },
  surface: { width: number; height: number },
  level: FlybyMovieLevel,
): { width: number; height: number } {
  if (level === 'fineLarge' || (surface.width >= movie.width && surface.height >= movie.height)) {
    return { width: movie.width, height: movie.height };
  }
  return { width: Math.round(surface.width), height: Math.round(surface.height) };
}

/**
 * What draws a movie's frame for moment `t` onto `frame`: the scene as it is drawn now, then the
 * caller's drawing over it, then the credits.
 *
 * Every way of making a movie of a flyby composes its frames through this one function, so a frame
 * taken one by one and a frame recorded live hold the same things in the same places. The scene is
 * drawn and read in the same task — the surface is not kept, and a later read finds it blank — so
 * whoever calls this has placed the moment and does not await anything between that and the call.
 */
export function createFlybyFrameComposer(
  stage: Pick<FlybyRecorderStage, 'captureFrame' | 'credits'>,
  frame: FlybyFrameCanvas,
  compose?: FlybyRecording['compose'],
): (t: number) => void {
  const { width, height } = frame.canvas;
  let creditsKey: string | null = null;
  let lines: string[] = [];
  return (t) => {
    const { context } = frame;
    const surface = stage.captureFrame();
    context.drawImage(surface, 0, 0, width, height);
    if (compose) {
      // Whatever the caller's drawing leaves set — a transparency, a shadow — is not the credits'.
      context.save();
      try {
        compose(context, t, width, height);
      } finally {
        context.restore();
      }
    }
    const credits = stage.credits();
    const key = credits.join('\n');
    if (key !== creditsKey) {
      creditsKey = key;
      lines = creditLines(credits);
    }
    // Always, whatever the caller drew: the map tiles' licences ask for their credit on every
    // picture of them.
    drawCreditStrip(context, lines, width, height);
  };
}

/** Records the movie and answers its file with the report. */
export async function recordFlyby(recording: FlybyRecording): Promise<{ file: Blob; report: FlybyRecordingReport }> {
  const { file, report } = await record(recording, null);
  if (file === null) {
    throw new Error('The movie was recorded without a file to answer.');
  }
  return { file, report };
}

/**
 * Records the movie into `sink` as it is encoded, where {@link recordFlyby} holds it in memory.
 *
 * Answers a null file once the whole file is in the sink and the sink is closed. On every other way
 * out the sink has been aborted before the promise settles, so nothing half-written is left. Where
 * the file could not be written as it was encoded with the bytes it would have had in memory, or
 * the encoder takes no sink, the movie is made in memory instead and its file answered.
 */
export async function recordFlybyToSink(
  recording: FlybyRecording,
  sink: MovieSink,
): Promise<{ file: Blob | null; report: FlybyRecordingReport }> {
  let result: { file: Blob | null; report: FlybyRecordingReport };
  try {
    result = await record(recording, sink);
    if (result.file === null) {
      // The last moment a cancel can still take the file back.
      if (recording.signal?.aborted) {
        throw cancelled();
      }
      await sink.close();
      return result;
    }
  } catch (error) {
    await discard(sink);
    if (!isMovieSinkMismatch(error) || recording.signal?.aborted) {
      throw error;
    }
    return record(recording, null);
  }
  await discard(sink);
  return result;
}

async function discard(sink: MovieSink): Promise<void> {
  try {
    await sink.abort();
  } catch {
    // Nothing to add to what the export itself reports.
  }
}

async function record(
  recording: FlybyRecording,
  sink: MovieSink | null,
): Promise<{ file: Blob | null; report: FlybyRecordingReport }> {
  const {
    timeline,
    stage,
    output,
    compose,
    signal,
    onProgress,
    settleMs = FLYBY_FRAME_SETTLE_MILLISECONDS,
    openEncoder = openMovieEncoder,
    createCanvas = detachedCanvas,
    nextTask: yieldTask = nextTask,
    now = () => performance.now(),
  } = recording;
  const check = () => {
    if (signal?.aborted) {
      throw cancelled();
    }
  };
  check();
  const startedAt = now();
  const { width, height } = output;
  const schedule: FlybyFrameSchedule = flybyFrameSchedule(
    timeline.length,
    output.fps,
    recording.stretch,
    recording.holdEndS ?? 0,
  );
  const careful = output.level !== 'quick';

  // Opened before the scene is touched, so an encoder this browser cannot open leaves the view
  // exactly as it was.
  const encoder: MovieEncoder = await openEncoder(output.format, {
    width,
    height,
    fps: output.fps,
    quality: output.quality,
    ...(sink ? { sink, frameCount: schedule.count } : {}),
  });

  let putBack: (() => void) | null = null;
  let capturing = false;
  /** Closes the encoder and hands the scene back, trying every step whatever an earlier one did. */
  const handBack = (): unknown[] => {
    const failures: unknown[] = [];
    const attempt = (step: () => void) => {
      try {
        step();
      } catch (error) {
        failures.push(error);
      }
    };
    attempt(() => encoder.close());
    // While the session is still open, so the one frame drawn when it ends is the view as it was.
    if (putBack) {
      attempt(putBack);
    }
    if (capturing) {
      attempt(() => stage.endCapture());
    }
    return failures;
  };

  let unsettledFrames = 0;
  let file: Blob | null;
  try {
    check();
    putBack = stage.remember();
    stage.beginCapture(flybyCaptureSize(output, stage.surfaceSize(), output.level));
    capturing = true;

    const frame = createCanvas(width, height, encoder.samplesWanted > 0);
    const composeFrame = createFlybyFrameComposer(stage, frame, compose);
    let lastRefresh = Number.NEGATIVE_INFINITY;
    let placed: number | null = null;

    /**
     * Puts the scene at moment `t` and waits for it as the level asks; answers whether the wait
     * ran out. Only the moment the scene is already at is not placed again.
     */
    const stageMoment = async (t: number, waitAnyway: boolean): Promise<boolean> => {
      if (placed !== t) {
        placeFlybyMoment(timeline, stage, t);
        if (Math.abs(t - lastRefresh) >= REFRESH_SECONDS) {
          lastRefresh = t;
          stage.refreshData();
        }
      }
      let ranOut = false;
      if (careful && (placed !== t || waitAnyway)) {
        ranOut = !(await stage.settle(settleMs));
        check();
      }
      placed = t;
      return ranOut;
    };

    const samples = flybySampleFrames(schedule.count, encoder.samplesWanted);
    const total = samples.length + schedule.count;
    let done = 0;
    const report = (stageName: FlybyRecordingProgress['stage'], step: number, steps: number) => {
      const elapsed = (now() - startedAt) / 1000;
      onProgress?.({
        stage: stageName,
        done,
        total,
        step,
        steps,
        secondsLeft: done > 0 && total > done ? (elapsed / done) * (total - done) : done >= total ? 0 : null,
      });
    };

    if (samples.length > 0) {
      const taken: ImageData[] = [];
      for (const index of samples) {
        const t = schedule.at(index);
        await stageMoment(t, false);
        composeFrame(t);
        taken.push(frame.context.getImageData(0, 0, width, height));
        done += 1;
        report('sampling', taken.length, samples.length);
        await yieldTask();
        check();
      }
      await encoder.prime(taken);
      check();
    }

    for (let index = 0; index < schedule.count; index += 1) {
      const t = schedule.at(index);
      // The first frame waits even where a palette sample left the scene at its moment: the sample
      // was taken from another place, and what loaded since then is not this frame's concern.
      if (await stageMoment(t, index === 0)) {
        unsettledFrames += 1;
      }
      composeFrame(t);
      await encoder.addFrame(frame.canvas, index);
      done += 1;
      report('rendering', index + 1, schedule.count);
      await yieldTask();
      check();
    }

    report('finishing', schedule.count, schedule.count);
    if (sink && encoder.finishInSink) {
      await encoder.finishInSink();
      file = null;
    } else {
      file = await encoder.finish();
    }
    check();
  } catch (error) {
    // The export's own failure is the one the viewer is owed; a step of handing back that also
    // failed is not reported over it.
    handBack();
    throw error;
  }
  const failures = handBack();
  if (failures.length > 0) {
    throw failures[0];
  }
  return {
    file,
    report: { frames: schedule.count, unsettledFrames, seconds: (now() - startedAt) / 1000 },
  };
}
