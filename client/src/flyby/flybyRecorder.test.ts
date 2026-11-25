// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { MemoryMovieSink } from '../movie/encode/memoryMovieSink.ts';
import type { MovieEncoder, MovieEncoderOptions } from '../movie/encode/movieEncoder.ts';
import { MovieSinkMismatch } from '../movie/encode/movieSink.ts';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { FLYBY_VERSION, type FlybyDocument } from './flybyDocument.ts';
import type { FlybyMovieOutput } from './flybyMovie.ts';
import {
  flybyCaptureSize,
  flybySampleFrames,
  isFlybyRecordingAbort,
  recordFlyby,
  recordFlybyToSink,
  type FlybyFrameCanvas,
  type FlybyRecorderStage,
  type FlybyRecording,
  type FlybyRecordingProgress,
} from './flybyRecorder.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

const cave: FlybyResolvedSubject = {
  centre: { lon: 25, lat: 45.5, height: 900 },
  radius: 200,
  halfHeight: 100,
  coarse: false,
};

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

/** Everything the recorder did, in the order it did it. */
type Log = string[];

/** A scene that writes down what is asked of it, with a camera, a capture session and credits. */
function fakeStage(log: Log, options: { settles?: (call: number) => boolean; captureThrowsAt?: number } = {}) {
  let camera = HOME;
  let settleCalls = 0;
  let captures = 0;
  let capturing: { width: number; height: number } | null = null;
  const surface = { width: 0, height: 0 } as HTMLCanvasElement;
  const stage: FlybyRecorderStage & { camera(): Scene3DCameraState; capturing(): typeof capturing } = {
    setCamera: (next) => {
      camera = next;
      log.push('place');
    },
    groundHeight: () => 0,
    refreshData: () => log.push('refresh'),
    settle: async () => {
      log.push('settle');
      settleCalls += 1;
      return options.settles?.(settleCalls) ?? true;
    },
    surfaceSize: () => ({ width: 640, height: 360 }),
    beginCapture: (size) => {
      log.push(`begin ${size.width}x${size.height}`);
      capturing = size;
      surface.width = size.width;
      surface.height = size.height;
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
    credits: () => ['<a href="https://example.org">Tiles © Example</a>'],
    remember: () => {
      const found = camera;
      log.push('remember');
      return () => {
        camera = found;
        log.push('put back');
      };
    },
    camera: () => camera,
    capturing: () => capturing,
  };
  return stage;
}

/** A 2D canvas that writes down what is drawn on it. */
function fakeCanvas(log: Log) {
  return (width: number, height: number): FlybyFrameCanvas => {
    const canvas = { width, height } as HTMLCanvasElement;
    const context = {
      drawImage: (_: unknown, x: number, y: number, w: number, h: number) => log.push(`draw ${x},${y},${w}x${h}`),
      getImageData: () => ({ width, height, data: new Uint8ClampedArray(4) }) as unknown as ImageData,
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

/** An encoder that writes down what it is given; a GIF one wants palette samples first. */
function fakeEncoder(log: Log, options: { samplesWanted?: number; mismatch?: boolean } = {}) {
  const opened: MovieEncoderOptions[] = [];
  let closed = 0;
  const open = async (format: FlybyMovieOutput['format'], encoderOptions: MovieEncoderOptions): Promise<MovieEncoder> => {
    opened.push(encoderOptions);
    log.push(`open ${format}`);
    return {
      format,
      samplesWanted: options.samplesWanted ?? 0,
      prime: async (samples) => {
        log.push(`prime ${samples.length}`);
      },
      addFrame: async (_, index) => {
        log.push(`frame ${index}`);
      },
      finish: async () => new Blob(['movie'], { type: 'video/webm' }),
      ...(encoderOptions.sink
        ? {
            finishInSink: async () => {
              if (options.mismatch) {
                throw new MovieSinkMismatch('a keyframe nobody asked for');
              }
              await encoderOptions.sink!.write(0, new Uint8Array([1, 2, 3]));
            },
          }
        : {}),
      close: () => {
        closed += 1;
        log.push('close');
      },
    };
  };
  return { open, opened, closed: () => closed };
}

const OUTPUT: FlybyMovieOutput = { format: 'webm', width: 1280, height: 720, fps: 4, quality: 'high', level: 'quick' };

function recording(log: Log, overrides: Partial<FlybyRecording> = {}): FlybyRecording & { stage: ReturnType<typeof fakeStage> } {
  return {
    timeline,
    stage: fakeStage(log),
    output: OUTPUT,
    openEncoder: fakeEncoder(log).open,
    createCanvas: fakeCanvas(log),
    nextTask: async () => undefined,
    now: (() => {
      let clock = 0;
      return () => (clock += 100);
    })(),
    ...overrides,
  } as FlybyRecording & { stage: ReturnType<typeof fakeStage> };
}

describe('the size a frame of a flyby is drawn at', () => {
  it('is the movie\'s own when the scene\'s element is at least as large', () => {
    expect(flybyCaptureSize({ width: 1280, height: 720 }, { width: 1600, height: 900 }, 'quick')).toEqual({ width: 1280, height: 720 });
  });

  it('is no larger than the element shows it, unless the movie is to be drawn larger than the screen', () => {
    expect(flybyCaptureSize({ width: 1280, height: 720 }, { width: 640, height: 360 }, 'quick')).toEqual({ width: 640, height: 360 });
    expect(flybyCaptureSize({ width: 1280, height: 720 }, { width: 640, height: 360 }, 'fine')).toEqual({ width: 640, height: 360 });
    expect(flybyCaptureSize({ width: 1280, height: 720 }, { width: 640, height: 360 }, 'fineLarge')).toEqual({ width: 1280, height: 720 });
  });
});

describe('the palette samples of a GIF', () => {
  it('are spread evenly from the first frame to the last', () => {
    expect(flybySampleFrames(9, 3)).toEqual([0, 4, 8]);
    expect(flybySampleFrames(1, 16)).toEqual([0]);
    expect(flybySampleFrames(10, 0)).toEqual([]);
  });
});

describe('recording a flyby', () => {
  it('takes one frame for each of its moments, each placed at its own time on the flyby\'s clock', async () => {
    const log: Log = [];
    const placed: Scene3DCameraState[] = [];
    const asked = recording(log, { holdEndS: 0.5 });
    const setCamera = asked.stage.setCamera;
    asked.stage.setCamera = (camera) => {
      placed.push(camera);
      setCamera(camera);
    };
    const { file, report } = await recordFlyby(asked);
    // Two seconds at four frames a second, and half a second held at the end.
    expect(log.filter((entry) => entry.startsWith('frame '))).toEqual(
      Array.from({ length: 10 }, (_, index) => `frame ${index}`),
    );
    // Eight moments of the flight; the held frames are of the end and place nothing new.
    const times = [0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
    expect(placed.slice(0, 9)).toEqual(times.map((t) => flybyCameraAt(timeline, t, () => 0)));
    expect(report).toEqual({ frames: 10, unsettledFrames: 0, seconds: expect.any(Number) });
    expect(await file.text()).toBe('movie');
  });

  it('films a stretch alone, from its first moment to its last', async () => {
    const log: Log = [];
    const placed: Scene3DCameraState[] = [];
    const asked = recording(log, { stretch: { from: 0.5, to: 1.5 } });
    const setCamera = asked.stage.setCamera;
    asked.stage.setCamera = (camera) => {
      placed.push(camera);
      setCamera(camera);
    };
    const { report } = await recordFlyby(asked);
    expect(report.frames).toBe(4);
    expect(placed.slice(0, 4)).toEqual([0.5, 0.75, 1, 1.25].map((t) => flybyCameraAt(timeline, t, () => 0)));
  });

  it('places, waits, captures, draws the caller\'s layer, then the credits, then hands the frame over — in that order', async () => {
    const log: Log = [];
    await recordFlyby(
      recording(log, {
        output: { ...OUTPUT, level: 'fine', fps: 1 },
        compose: (_context, t) => log.push(`compose ${t}`),
      }),
    );
    const first = log.indexOf('frame 0');
    const frame0 = log.slice(log.indexOf('begin 640x360') + 1, first + 1);
    expect(frame0).toEqual([
      'place',
      'refresh',
      'settle',
      'capture',
      'draw 0,0,1280x720',
      'compose 0',
      'credits Tiles © Example',
      'frame 0',
    ]);
    const second = log.slice(first + 1, log.indexOf('frame 1') + 1);
    // The camera has moved on by a second of the flight: the scene is asked for what it now shows.
    expect(second).toEqual([
      'place',
      'settle',
      'capture',
      'draw 0,0,1280x720',
      'compose 1',
      'credits Tiles © Example',
      'frame 1',
    ]);
  });

  it('waits for nothing when quick', async () => {
    const log: Log = [];
    await recordFlyby(recording(log));
    expect(log).not.toContain('settle');
  });

  it('counts the frames whose wait for the scene ran out', async () => {
    const log: Log = [];
    const asked = recording(log, { output: { ...OUTPUT, level: 'fine', fps: 2 } });
    asked.stage = fakeStage(log, { settles: (call) => call % 2 === 0 });
    const { report } = await recordFlyby(asked);
    expect(report).toMatchObject({ frames: 4, unsettledFrames: 2 });
  });

  it('draws the scene at the movie\'s own size, larger than the element, when asked to', async () => {
    const log: Log = [];
    await recordFlyby(recording(log, { output: { ...OUTPUT, level: 'fineLarge' } }));
    expect(log).toContain('begin 1280x720');
  });

  it('samples a GIF\'s palette from frames across the whole flyby before the first frame', async () => {
    const log: Log = [];
    const progress: FlybyRecordingProgress[] = [];
    await recordFlyby(
      recording(log, {
        output: { ...OUTPUT, format: 'gif', fps: 2 },
        openEncoder: fakeEncoder(log, { samplesWanted: 3 }).open,
        onProgress: (step) => progress.push(step),
      }),
    );
    expect(log.indexOf('prime 3')).toBeLessThan(log.indexOf('frame 0'));
    expect(progress.map(({ stage, step, steps }) => `${stage} ${step}/${steps}`)).toEqual([
      'sampling 1/3',
      'sampling 2/3',
      'sampling 3/3',
      'rendering 1/4',
      'rendering 2/4',
      'rendering 3/4',
      'rendering 4/4',
      'finishing 4/4',
    ]);
    expect(progress.map((step) => step.done)).toEqual([1, 2, 3, 4, 5, 6, 7, 7]);
    // Reckoned from the pace so far, and nothing left once it is all done.
    expect(progress[0].secondsLeft).toBeGreaterThan(0);
    expect(progress.at(-1)!.secondsLeft).toBe(0);
  });

  it('hands the scene back as it was found, the surface included, when it is done', async () => {
    const log: Log = [];
    const asked = recording(log);
    await recordFlyby(asked);
    expect(asked.stage.camera()).toEqual(HOME);
    expect(asked.stage.capturing()).toBeNull();
    // The camera before the session ends, so the last frame drawn at the element's size is the view.
    expect(log.slice(-3)).toEqual(['close', 'put back', 'end']);
  });

  it('stops at any frame when cancelled, closing the encoder and handing the scene back', async () => {
    const log: Log = [];
    const controller = new AbortController();
    const encoder = fakeEncoder(log);
    const asked = recording(log, {
      signal: controller.signal,
      openEncoder: encoder.open,
      onProgress: (step) => {
        if (step.step === 3) {
          controller.abort();
        }
      },
    });
    const outcome = await recordFlyby(asked).catch((error: unknown) => error);
    expect(isFlybyRecordingAbort(outcome)).toBe(true);
    expect(log.filter((entry) => entry.startsWith('frame '))).toHaveLength(3);
    expect(encoder.closed()).toBe(1);
    expect(asked.stage.camera()).toEqual(HOME);
    expect(asked.stage.capturing()).toBeNull();
  });

  it('ends with the scene handed back when a frame cannot be taken', async () => {
    const log: Log = [];
    const asked = recording(log);
    asked.stage = fakeStage(log, { captureThrowsAt: 2 });
    await expect(recordFlyby(asked)).rejects.toThrow(/changed size/);
    expect(asked.stage.camera()).toEqual(HOME);
    expect(asked.stage.capturing()).toBeNull();
    expect(log).toContain('close');
  });

  it('leaves the scene untouched when the encoder cannot be opened', async () => {
    const log: Log = [];
    const asked = recording(log, {
      openEncoder: async () => {
        throw new Error('WEBM encoder, open: not supported');
      },
    });
    await expect(recordFlyby(asked)).rejects.toThrow(/not supported/);
    expect(log).toEqual([]);
  });
});

describe('recording a flyby straight into a file', () => {
  it('writes it into the sink and closes it', async () => {
    const log: Log = [];
    const sink = new MemoryMovieSink();
    const { file } = await recordFlybyToSink(recording(log), sink);
    expect(file).toBeNull();
    expect(sink.state).toBe('closed');
    expect([...sink.bytes()]).toEqual([1, 2, 3]);
  });

  it('leaves nothing half-written when cancelled', async () => {
    const log: Log = [];
    const sink = new MemoryMovieSink();
    const controller = new AbortController();
    const outcome = await recordFlybyToSink(
      recording(log, { signal: controller.signal, onProgress: () => controller.abort() }),
      sink,
    ).catch((error: unknown) => error);
    expect(isFlybyRecordingAbort(outcome)).toBe(true);
    expect(sink.state).toBe('aborted');
  });

  it('makes the movie in memory when the file could not be written as it was encoded', async () => {
    const log: Log = [];
    const sink = new MemoryMovieSink();
    const { file } = await recordFlybyToSink(recording(log, { openEncoder: fakeEncoder(log, { mismatch: true }).open }), sink);
    expect(sink.state).toBe('aborted');
    expect(await file!.text()).toBe('movie');
  });
});
