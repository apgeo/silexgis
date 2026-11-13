// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { FLYBY_VERSION, type FlybyDocument } from './flybyDocument.ts';
import {
  browserLiveCapture,
  flybyLiveTypes,
  recordFlybyLive,
  type FlybyLiveCaptureFactory,
  type FlybyLiveCaptureOptions,
  type FlybyLiveRecording,
  type FlybyLiveStage,
} from './flybyLiveRecorder.ts';
import type { FlybyFrameCanvas } from './flybyRecorder.ts';
import { isFlybyRecordingAbort } from './flybyRecorder.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

// The live recorder against a scene, a display and a recorder that are all plain objects: the
// display's frames come when the test says, at the times it says, so what a slow machine and a
// fast one would each put in the file can be told apart exactly.

const cave: FlybyResolvedSubject = { centre: { lon: 25, lat: 45.5, height: 900 }, radius: 200, halfHeight: 100, coarse: false };

const document: FlybyDocument = {
  version: FLYBY_VERSION,
  name: '',
  speed: 1,
  loop: false,
  subjects: [{ kind: 'cave', caveId: 'a' }],
  start: { subject: 0, pose: { heading: 0, pitch: -30, range: 5, lift: 0, clearance: null } },
  sections: [
    { id: 'a', kind: 'approach', subject: 0, seconds: 1, to: { range: 2 } },
    { id: 'o', kind: 'orbit', subject: 0, seconds: 1, to: { headingBy: 90 } },
  ],
};

/** Two seconds of flight. */
const timeline = compileFlyby(document, [cave]);

const HOME: Scene3DCameraState = { longitude: 20, latitude: 40, height: 5000, heading: 10, pitch: -45, roll: 0 };

type Log = string[];

/** A scene with a capture session, and a display whose frames the test hands out one at a time. */
function fakeStage(log: Log, options: { settle?: () => Promise<boolean>; captureThrowsAt?: number; surface?: { width: number; height: number } } = {}) {
  let camera = HOME;
  let clock = 1000;
  let captures = 0;
  let capturing: { width: number; height: number } | null = null;
  let nextHandle = 1;
  const waiting = new Map<number, () => void>();
  const placed: Scene3DCameraState[] = [];
  const surface = { width: 0, height: 0 } as HTMLCanvasElement;
  const stage: FlybyLiveStage = {
    setCamera: (next) => {
      camera = next;
      placed.push(next);
      log.push('place');
    },
    groundHeight: () => 0,
    refreshData: () => log.push('refresh'),
    settle: options.settle ?? (async () => {
      log.push('settle');
      return true;
    }),
    surfaceSize: () => options.surface ?? { width: 640, height: 360 },
    beginCapture: (size) => {
      log.push(`begin ${size.width}x${size.height}`);
      capturing = size;
    },
    captureFrame: () => {
      captures += 1;
      if (captures === options.captureThrowsAt) {
        throw new Error('The scene changed size while frames were being taken from it.');
      }
      log.push('capture');
      return surface;
    },
    endCapture: () => {
      log.push('end');
      capturing = null;
    },
    credits: () => ['Tiles © Example'],
    remember: () => {
      const found = camera;
      log.push('remember');
      return () => {
        camera = found;
        log.push('put back');
      };
    },
    now: () => clock,
    requestFrame: (callback) => {
      const handle = nextHandle++;
      waiting.set(handle, callback);
      return handle;
    },
    cancelFrame: (handle) => {
      waiting.delete(handle);
    },
  };
  return {
    stage,
    placed,
    camera: () => camera,
    capturing: () => capturing,
    /** Frames the display has been asked for and not yet drawn. */
    pending: () => waiting.size,
    /** The display draws its next frame `ms` after the last one. */
    async frame(ms: number) {
      clock += ms;
      const due = [...waiting.values()];
      waiting.clear();
      for (const callback of due) {
        callback();
      }
      await settled();
    },
  };
}

/** Lets every promise that can move, move. */
async function settled() {
  for (let index = 0; index < 5; index += 1) {
    await Promise.resolve();
  }
}

function fakeCanvas(log: Log) {
  return (width: number, height: number): FlybyFrameCanvas => {
    const canvas = { width, height } as HTMLCanvasElement;
    const context = {
      drawImage: (_: unknown, x: number, y: number, w: number, h: number) => log.push(`draw ${x},${y},${w}x${h}`),
      save: () => undefined,
      restore: () => undefined,
      measureText: (text: string) => ({ width: text.length * 5 }),
      fillRect: () => undefined,
      fillText: (text: string) => log.push(`credits ${text}`),
      font: '',
      fillStyle: '',
      textBaseline: '',
    } as unknown as CanvasRenderingContext2D;
    return { canvas, context };
  };
}

/** A recorder that writes down what it is told, and can be made to fail. */
function fakeCapture(log: Log) {
  const made: { canvas: HTMLCanvasElement; options: FlybyLiveCaptureOptions; fail(error: unknown): void }[] = [];
  const factory: FlybyLiveCaptureFactory = (canvas, options, onError) => {
    made.push({ canvas, options, fail: onError });
    return {
      start: () => log.push('record'),
      finish: async () => {
        log.push('finish');
        return new Blob(['live movie'], { type: options.mimeType });
      },
      discard: () => log.push('discard'),
    };
  };
  return { factory, made };
}

function recording(log: Log, scene: ReturnType<typeof fakeStage>, capture: ReturnType<typeof fakeCapture>, overrides: Partial<FlybyLiveRecording> = {}): FlybyLiveRecording {
  return {
    timeline,
    stage: scene.stage,
    output: { width: 1280, height: 720, fps: 30, quality: 'high' },
    type: { format: 'webm', mimeType: 'video/webm;codecs=vp9' },
    createCapture: capture.factory,
    createCanvas: fakeCanvas(log),
    ...overrides,
  };
}

/** Hands out display frames `ms` apart until the recording is over. */
async function playOut(scene: ReturnType<typeof fakeStage>, done: () => boolean, ms: number) {
  for (let index = 0; index < 1000 && !done(); index += 1) {
    await scene.frame(ms);
  }
}

describe('which recordings this browser can make live', () => {
  const recorder = (supported: string[]) => ({ isTypeSupported: (type: string) => supported.includes(type) });

  it('prefers MP4, then WebM with VP9, then with VP8 — one type for each container', () => {
    expect(
      flybyLiveTypes({ recorder: recorder(['video/mp4;codecs=avc1', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm;codecs=vp8']), canvasStreams: true }),
    ).toEqual([
      { format: 'mp4', mimeType: 'video/mp4;codecs=avc1' },
      { format: 'webm', mimeType: 'video/webm;codecs=vp9' },
    ]);
    expect(flybyLiveTypes({ recorder: recorder(['video/webm;codecs=vp8']), canvasStreams: true })).toEqual([
      { format: 'webm', mimeType: 'video/webm;codecs=vp8' },
    ]);
  });

  it('offers none without the recorder, without a way to film a canvas, or when it writes none of them', () => {
    expect(flybyLiveTypes({ canvasStreams: true })).toEqual([]);
    expect(flybyLiveTypes({ recorder: recorder(['video/webm;codecs=vp8']), canvasStreams: false })).toEqual([]);
    expect(flybyLiveTypes({ recorder: recorder(['video/ogg']), canvasStreams: true })).toEqual([]);
    expect(
      flybyLiveTypes({
        recorder: {
          isTypeSupported: () => {
            throw new Error('unknown type');
          },
        },
        canvasStreams: true,
      }),
    ).toEqual([]);
  });

  it('finds none in a page with no recorder at all', () => {
    // The test page has no MediaRecorder, as a browser without one has not.
    expect(flybyLiveTypes()).toEqual([]);
  });
});

describe('recording a flyby live', () => {
  it('lets the first moment load, then composes a frame on every display frame at the time since it began, to the end and the held end', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    const capture = fakeCapture(log);
    const progress: number[] = [];
    let result: Awaited<ReturnType<typeof recordFlybyLive>> | undefined;
    void recordFlybyLive(
      recording(log, scene, capture, {
        holdEndS: 0.5,
        compose: (_context, t) => log.push(`texts ${t}`),
        onProgress: (step) => progress.push(step.seconds),
      }),
    ).then((answer) => {
      result = answer;
    });
    await settled();
    // Remembered, boxed at the element's own size (it is smaller than the movie), placed at the
    // first moment, let load, composed, and only then filmed.
    expect(log).toEqual([
      'remember',
      'begin 640x360',
      'place',
      'refresh',
      'settle',
      'capture',
      'draw 0,0,1280x720',
      'texts 0',
      'credits Tiles © Example',
      'record',
    ]);
    expect(capture.made[0].canvas).toMatchObject({ width: 1280, height: 720 });
    // The rate asked for is the most the canvas is filmed at; the bitrate is the video encoders'.
    expect(capture.made[0].options).toMatchObject({ mimeType: 'video/webm;codecs=vp9', fps: 30 });
    expect(capture.made[0].options.bitsPerSecond).toBeGreaterThan(0);

    // A machine drawing four frames a second.
    await playOut(scene, () => result !== undefined, 250);
    expect(result).toBeDefined();
    const times = log.filter((entry) => entry.startsWith('texts ')).map((entry) => Number(entry.slice(6)));
    expect(times).toEqual([0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2, 2]);
    // Each frame's camera is its own moment's, by the call the player places moments with.
    expect(scene.placed.slice(1, 9)).toEqual(times.slice(1, 9).map((t) => flybyCameraAt(timeline, t, () => 0)));
    expect(progress.at(-1)).toBe(2.5);
    // Finished after one more display frame, then the scene was handed back.
    expect(log.slice(log.lastIndexOf('credits Tiles © Example') + 1)).toEqual(['finish', 'put back', 'end']);
    expect(await result!.file.text()).toBe('live movie');
    expect(result!.report).toEqual({ frames: 11, seconds: 2.5, fps: 11 / 2.5 });
    expect(scene.camera()).toEqual(HOME);
    expect(scene.capturing()).toBeNull();
    expect(scene.pending()).toBe(0);
  });

  it('on a slow machine leaves out the moments it could not draw in time, and is no longer for it', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    let result: Awaited<ReturnType<typeof recordFlybyLive>> | undefined;
    void recordFlybyLive(recording(log, scene, fakeCapture(log), { compose: (_context, t) => log.push(`texts ${t}`) })).then((answer) => {
      result = answer;
    });
    await settled();
    // One frame every 0.7 s of real time.
    await playOut(scene, () => result !== undefined, 700);
    const times = log.filter((entry) => entry.startsWith('texts ')).map((entry) => Number(entry.slice(6)));
    expect(times).toEqual([0, 0.7, 1.4, 2]);
    expect(result!.report.frames).toBe(4);
    expect(result!.report.seconds).toBeCloseTo(2.1);
  });

  it('films a stretch alone, from its first moment', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    let result: unknown;
    void recordFlybyLive(
      recording(log, scene, fakeCapture(log), { stretch: { from: 0.5, to: 1.5 }, compose: (_context, t) => log.push(`texts ${t}`) }),
    ).then((answer) => {
      result = answer;
    });
    await settled();
    await playOut(scene, () => result !== undefined, 500);
    const times = log.filter((entry) => entry.startsWith('texts ')).map((entry) => Number(entry.slice(6)));
    expect(times).toEqual([0.5, 1, 1.5]);
  });

  it('boxes the scene at the movie\'s size where the element is at least as large', async () => {
    const log: Log = [];
    const scene = fakeStage(log, { surface: { width: 1600, height: 900 } });
    let over = false;
    const running = recordFlybyLive(recording(log, scene, fakeCapture(log))).then(() => {
      over = true;
    });
    await settled();
    expect(log).toContain('begin 1280x720');
    await playOut(scene, () => over, 500);
    await running;
  });

  it('when cancelled, throws the recording away and hands the scene back', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    const controller = new AbortController();
    const running = recordFlybyLive(recording(log, scene, fakeCapture(log), { signal: controller.signal }));
    const outcome = running.then(
      () => 'finished',
      (error: unknown) => error,
    );
    await settled();
    await scene.frame(250);
    await scene.frame(250);
    controller.abort();
    const error = await outcome;
    expect(isFlybyRecordingAbort(error)).toBe(true);
    expect(log).toContain('discard');
    expect(log).not.toContain('finish');
    expect(log.slice(-3)).toEqual(['discard', 'put back', 'end']);
    expect(scene.camera()).toEqual(HOME);
    // Nothing more is drawn for it.
    expect(scene.pending()).toBe(0);
  });

  it('cancelled while the first moment loads, never starts filming', async () => {
    const log: Log = [];
    let release!: (settled: boolean) => void;
    const scene = fakeStage(log, { settle: () => new Promise((resolve) => (release = resolve)) });
    const controller = new AbortController();
    const outcome = recordFlybyLive(recording(log, scene, fakeCapture(log), { signal: controller.signal })).catch((error: unknown) => error);
    await settled();
    controller.abort();
    expect(isFlybyRecordingAbort(await outcome)).toBe(true);
    release(true);
    await settled();
    expect(log).not.toContain('record');
    expect(log.slice(-2)).toEqual(['put back', 'end']);
  });

  it('ends with the scene handed back when the browser\'s recorder fails part-way', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    const capture = fakeCapture(log);
    const outcome = recordFlybyLive(recording(log, scene, capture)).catch((error: unknown) => error);
    await settled();
    await scene.frame(250);
    capture.made[0].fail(new Error('The encoder ran out of memory.'));
    const error = await outcome;
    expect((error as Error).message).toBe('The encoder ran out of memory.');
    expect(log.slice(-3)).toEqual(['discard', 'put back', 'end']);
    expect(scene.camera()).toEqual(HOME);
    expect(scene.pending()).toBe(0);
  });

  it('ends with the scene handed back when a frame cannot be taken', async () => {
    const log: Log = [];
    const scene = fakeStage(log, { captureThrowsAt: 3 });
    const outcome = recordFlybyLive(recording(log, scene, fakeCapture(log))).catch((error: unknown) => error);
    await settled();
    await scene.frame(250);
    await scene.frame(250);
    expect((await outcome as Error).message).toMatch(/changed size/);
    expect(log.slice(-3)).toEqual(['discard', 'put back', 'end']);
    expect(scene.capturing()).toBeNull();
  });

  it('when the scene cannot be boxed, hands it back without having filmed anything', async () => {
    const log: Log = [];
    const scene = fakeStage(log);
    scene.stage.beginCapture = () => {
      throw new Error('The scene is not on the screen.');
    };
    await expect(recordFlybyLive(recording(log, scene, fakeCapture(log)))).rejects.toThrow('not on the screen');
    expect(log).toEqual(['remember', 'put back']);
  });
});

describe('the browser\'s own recorder', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** A MediaRecorder that hands over a chunk on each request and on stop. */
  function stubRecorder() {
    const made: FakeRecorder[] = [];
    class FakeRecorder {
      state: 'inactive' | 'recording' = 'inactive';
      mimeType: string;
      ondataavailable: ((event: { data: Blob }) => void) | null = null;
      onerror: ((event: Event) => void) | null = null;
      onstop: ((event: Event) => void) | null = null;
      sliceMs?: number;
      options: { mimeType: string; videoBitsPerSecond: number };
      constructor(_stream: unknown, options: { mimeType: string; videoBitsPerSecond: number }) {
        this.options = options;
        this.mimeType = options.mimeType;
        made.push(this);
      }
      start(sliceMs: number) {
        this.state = 'recording';
        this.sliceMs = sliceMs;
      }
      stop() {
        this.state = 'inactive';
        this.ondataavailable?.({ data: new Blob(['last']) });
        this.onstop?.(new Event('stop'));
      }
      chunk(text: string) {
        this.ondataavailable?.({ data: new Blob([text]) });
      }
    }
    vi.stubGlobal('MediaRecorder', FakeRecorder);
    const stopped: number[] = [];
    const canvas = {
      captureStream: vi.fn(() => ({ getTracks: () => [{ stop: () => stopped.push(1) }] })),
    } as unknown as HTMLCanvasElement;
    return { made, canvas, stopped };
  }

  const options = { mimeType: 'video/webm;codecs=vp9', fps: 25, bitsPerSecond: 5_000_000 };

  it('films the canvas at the rate asked for as a ceiling, and answers every chunk it handed over as one file', async () => {
    const { made, canvas, stopped } = stubRecorder();
    const capture = browserLiveCapture(canvas, options, () => undefined);
    expect(canvas.captureStream).toHaveBeenCalledWith(25);
    capture.start();
    expect(made[0].options).toEqual({ mimeType: 'video/webm;codecs=vp9', videoBitsPerSecond: 5_000_000 });
    made[0].chunk('first ');
    const file = await capture.finish();
    expect(file.type).toBe('video/webm;codecs=vp9');
    expect(await file.text()).toBe('first last');
    expect(stopped.length).toBeGreaterThan(0);
  });

  it('throws away what it holds when discarded', () => {
    const { made, canvas, stopped } = stubRecorder();
    const capture = browserLiveCapture(canvas, options, () => undefined);
    capture.start();
    made[0].chunk('first');
    capture.discard();
    capture.discard();
    expect(made[0].state).toBe('inactive');
    expect(stopped.length).toBeGreaterThan(0);
  });

  it('says when it fails while recording, and fails the file when it fails while finishing', async () => {
    const { made, canvas } = stubRecorder();
    const errors: unknown[] = [];
    const capture = browserLiveCapture(canvas, options, (error) => errors.push(error));
    capture.start();
    made[0].onerror?.(new Event('error'));
    expect(errors).toHaveLength(1);
    expect((errors[0] as Error).message).toMatch(/recorder stopped with an error/);

    const second = browserLiveCapture(canvas, options, () => undefined);
    second.start();
    // A recorder that fails as it is stopped, rather than stopping.
    made[1].stop = function (this: { onerror: ((event: Event) => void) | null }) {
      this.onerror?.(new Event('error'));
    };
    await expect(second.finish()).rejects.toThrow(/recorder stopped with an error/);
  });
});
