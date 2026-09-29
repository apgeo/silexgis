// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import type {
  CaveViewCaptureFrameOptions,
  CaveViewLabelText,
  CaveViewLayerGetter,
  CaveViewLiveMarker,
  CaveViewLiveMarkerOptions,
  CaveViewRef,
  CaveViewTrail,
  CaveViewTrailOptions,
} from '../loadCaveView.ts';
import type { MovieEncoder, MovieEncoderOptions } from './encode/movieEncoder.ts';
import type { MovieTripData } from './movieParty.ts';
import { isMovieAbort, movieSampleFrames, recordMovie, type MovieRecording } from './movieRecorder.ts';
import { DEFAULT_MOVIE_SETTINGS, type MovieSettings } from './movieSettings.ts';
import { buildMovieTimeline } from './movieTimeline.ts';

const MODEL = 'model-1';
const ARMED = '2026-09-12T08:00:00Z';
const CLOSED = '2026-09-12T12:00:00Z';
const CONSTANTS = {
  CAMERA_PERSPECTIVE: 1,
  CAMERA_ORTHOGRAPHIC: 2,
  SHADING_DEPTH: 9,
  SHADING_DEPTH_CURSOR: 11,
};

// ---- a trip ------------------------------------------------------------------------------------

function tracking(caverIds: string[]): TrackingState {
  return {
    state: 'closed',
    surveyModelId: MODEL,
    surveyModelMissing: false,
    referenceStationName: null,
    depthFilter: [],
    armedAt: ARMED,
    closedAt: CLOSED,
    positionsWithheld: false,
    publishesRealNames: true,
    publishedAt: null,
    publishedUntil: null,
    teams: [],
    participants: caverIds.map(
      (caverId) =>
        ({
          caverId,
          teamId: null,
          lastKind: null,
          lastRecordedAt: null,
          positionRecordedAt: null,
          stationName: null,
          depthM: null,
          positionSurveyModelId: null,
          label: null,
          in: false,
          out: false,
          publishedAs: null,
        }) as TrackingState['participants'][number],
    ),
  };
}

let sequence = 0;
const report = (caverId: string, stationName: string, recordedAt: string): TrackingEvent =>
  ({
    id: `event-${sequence++}`,
    caverId,
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName,
    depthEnteredM: null,
    note: null,
    recordedAt,
  }) as TrackingEvent;

const NAMES: Record<string, string> = { ana: 'Ana Popescu', bogdan: 'Bogdan Ionescu' };
const EVENTS = [
  report('ana', 'p.1', ARMED),
  report('bogdan', 'p.1', ARMED),
  report('ana', 'p.2', '2026-09-12T10:00:00Z'),
  report('bogdan', 'p.3', '2026-09-12T11:00:00Z'),
].reverse();

const TRIP: MovieTripData = {
  tripLogId: 'trip-1',
  title: 'Trip one',
  tracking: tracking(['ana', 'bogdan']),
  events: EVENTS,
  nameOf: (id) => NAMES[id] ?? '?',
};

const TIMELINE = buildMovieTimeline(
  [{ tripLogId: 'trip-1', window: { from: Date.parse(ARMED), to: Date.parse(CLOSED) }, moments: [] }],
  { mode: 'calendar', quietGapMs: null },
)!;

/** 2 s of replay and 1 s held at 10 fps: 30 frames. */
function settings(overrides: Partial<MovieSettings> = {}, cavers: Partial<MovieSettings['cavers']> = {}): MovieSettings {
  return {
    ...DEFAULT_MOVIE_SETTINGS,
    format: 'webm',
    size: '320x180',
    fps: 10,
    durationS: 2,
    holdEndS: 1,
    ...overrides,
    cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, ...cavers },
  };
}

// ---- a viewer ----------------------------------------------------------------------------------

interface Captured extends Omit<CaveViewCaptureFrameOptions, 'into'> {
  drawnInto: boolean;
  labelSize: number | null;
  labels: boolean;
  markers: Record<string, string>;
}

/** Enough of the viewer to record against, keeping what it was told and what it holds. */
function fakeViewer() {
  const markers = new Map<string, CaveViewLiveMarker>();
  const trails = new Map<string, CaveViewTrail & { color?: string }>();
  const log: string[] = [];
  const captured: Captured[] = [];
  /** Each move, with how many frames had been captured when it was made. */
  const moves: { id: string; duration: number | undefined; at: number }[] = [];
  let angles = { azimuth: 0.3, polar: 0.7 };
  let capturing = false;
  const layers = {
    model: false,
    legs: true,
    stations: false,
    stationLabels: false,
    stationComments: false,
    entrances: true,
    entrance_dots: true,
    splays: false,
    walls: true,
    scraps: true,
    duplicateLegs: false,
    surfaceLegs: true,
    traces: false,
    warnings: false,
    box: false,
    // The preview shows a grid the movie does not have.
    grid: true,
  };
  const has = Object.fromEntries(
    Object.keys(layers).map((key) => [`has${key[0].toUpperCase()}${key.slice(1)}`, true]),
  ) as Record<CaveViewLayerGetter, boolean>;
  const viewer = {
    ...layers,
    ...has,
    HUD: false,
    fog: false,
    shadingMode: 0 as number | undefined,
    cameraType: CONSTANTS.CAMERA_PERSPECTIVE,
    linewidth: 0,
    zScale: 0.5 as number | undefined,
    hasRealTerrain: null as boolean | null,
    liveMarkerLabels: true,
    liveMarkerLabelSize: 12 as number | null,
    liveMarkerLabelBacking: true,
    beginCapture: vi.fn((options: { width: number; height: number }) => {
      log.push('beginCapture');
      capturing = true;
      return { width: options.width, height: options.height };
    }),
    captureFrame: vi.fn((options: CaveViewCaptureFrameOptions = {}) => {
      if (!capturing) throw new Error('not capturing');
      if (options.azimuth !== undefined) angles = { ...angles, azimuth: options.azimuth };
      if (options.polar !== undefined) angles = { ...angles, polar: options.polar };
      const { into, ...rest } = options;
      captured.push({
        ...rest,
        drawnInto: into !== undefined,
        labelSize: viewer.liveMarkerLabelSize,
        labels: viewer.liveMarkerLabels,
        markers: Object.fromEntries([...markers].map(([id, marker]) => [id, String(marker.ref)])),
      });
      return { canvas: document.createElement('canvas'), azimuth: angles.azimuth, polar: angles.polar, moving: false };
    }),
    endCapture: vi.fn(() => {
      log.push('endCapture');
      capturing = false;
    }),
    get capturing() {
      return capturing;
    },
    getCameraAngles: () => ({ ...angles }),
    setCameraAngles: vi.fn((next: { azimuth?: number; polar?: number }) => {
      angles = { azimuth: next.azimuth ?? angles.azimuth, polar: next.polar ?? angles.polar };
    }),
    addLiveMarker: vi.fn((id: string, ref: CaveViewRef, options: CaveViewLiveMarkerOptions = {}) => {
      const marker: CaveViewLiveMarker = { id, ref, label: options.label ?? '', color: options.color, resolved: true };
      if (options.sublabel !== undefined) marker.sublabel = options.sublabel;
      markers.set(id, marker);
      return { ...marker };
    }),
    moveLiveMarker: vi.fn((id: string, ref: CaveViewRef, options: CaveViewLiveMarkerOptions = {}) => {
      const marker = markers.get(id);
      if (marker === undefined) return null;
      moves.push({ id, duration: options.duration, at: captured.length });
      marker.ref = ref;
      if (options.label !== undefined) marker.label = options.label;
      if (options.color !== undefined) marker.color = options.color;
      return { ...marker };
    }),
    removeLiveMarker: vi.fn((id: string) => markers.delete(id)),
    getLiveMarkers: () => [...markers.values()].map((marker) => ({ ...marker })),
    setLiveMarkerClusterLabel: vi.fn(
      (_label: ((markers: readonly CaveViewLiveMarker[]) => CaveViewLabelText | null) | null) => {},
    ),
    addTrail: vi.fn((id: string, refs: readonly CaveViewRef[], options: CaveViewTrailOptions = {}) => {
      const trail = { id, resolved: true, points: [], gaps: [], lengthM: refs.length, progress: 1, visible: options.visible ?? true, color: options.color };
      trails.set(id, trail);
      return trail;
    }),
    updateTrail: vi.fn((id: string, _refs: readonly CaveViewRef[] | null, options: CaveViewTrailOptions = {}) => {
      const trail = trails.get(id);
      if (trail === undefined) return null;
      if (options.visible !== undefined) trail.visible = options.visible;
      if (options.color !== undefined) trail.color = options.color;
      return trail;
    }),
    removeTrail: vi.fn((id: string) => trails.delete(id)),
    getTrails: () => [...trails.values()].map((trail) => ({ ...trail })),
  };
  return { viewer, markers, trails, log, captured, moves };
}

/** What a viewer is showing, for comparing before and after a recording. */
function scene(fake: ReturnType<typeof fakeViewer>) {
  const { viewer } = fake;
  return {
    markers: viewer.getLiveMarkers(),
    trails: viewer.getTrails(),
    camera: viewer.getCameraAngles(),
    labels: [viewer.liveMarkerLabels, viewer.liveMarkerLabelSize, viewer.liveMarkerLabelBacking],
    grid: viewer.grid,
    stations: viewer.stations,
    capturing: viewer.capturing,
  };
}

/** A viewer already showing a preview: one marker at another station, and one trail. */
function previewViewer() {
  const fake = fakeViewer();
  fake.viewer.addLiveMarker('trip-1:ana', 'p.9', { label: 'Ana', color: '#123456' });
  fake.viewer.addLiveMarker('someone-else', 'p.4', { label: 'X', sublabel: 'y', color: '#654321' });
  fake.viewer.addTrail('preview-trail', ['p.1', 'p.9'], { color: '#abcdef' });
  vi.clearAllMocks();
  return fake;
}

// ---- an encoder --------------------------------------------------------------------------------

function fakeEncoder(samplesWanted = 0, failAt: number | null = null) {
  const calls: string[] = [];
  const encoder: MovieEncoder = {
    format: 'webm',
    samplesWanted,
    prime: vi.fn(async (samples: readonly ImageData[]) => {
      calls.push(`prime:${samples.length}`);
    }),
    addFrame: vi.fn(async (_source: HTMLCanvasElement | OffscreenCanvas, index: number) => {
      if (index === failAt) {
        throw new Error('WEBM encoder, encode: the codec gave up');
      }
      calls.push(`frame:${index}`);
    }),
    finish: vi.fn(async () => {
      calls.push('finish');
      return new Blob(['movie'], { type: 'video/webm' });
    }),
    close: vi.fn(() => {
      calls.push('close');
    }),
  };
  const opened: MovieEncoderOptions[] = [];
  const openEncoder = vi.fn(async (_format: string, options: MovieEncoderOptions) => {
    opened.push(options);
    return encoder;
  }) as unknown as NonNullable<MovieRecording['openEncoder']>;
  return { encoder, calls, opened, openEncoder };
}

/** The test environment's 2D stub, with the text drawing the captions need. */
function canvas(width: number, height: number): HTMLCanvasElement {
  const element = document.createElement('canvas');
  element.width = width;
  element.height = height;
  const context = element.getContext('2d') as CanvasRenderingContext2D;
  Object.assign(context, { fillText() {}, globalAlpha: 1, textBaseline: 'alphabetic', textAlign: 'start' });
  element.getContext = (() => context) as unknown as HTMLCanvasElement['getContext'];
  return element;
}

function recording(
  fake: ReturnType<typeof fakeViewer>,
  encoder: ReturnType<typeof fakeEncoder>,
  overrides: Partial<MovieRecording> = {},
): MovieRecording {
  return {
    viewer: fake.viewer,
    constants: CONSTANTS as MovieRecording['constants'],
    settings: settings(),
    trips: [TRIP],
    timeline: TIMELINE,
    surveyModelId: MODEL,
    title: 'Trip one',
    words: { t: i18n.t, language: 'en', today: 'never' },
    clusterLabelAfter: null,
    openEncoder: encoder.openEncoder,
    createCanvas: canvas,
    nextTask: () => Promise.resolve(),
    ...overrides,
  };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('movieSampleFrames', () => {
  it('spreads the samples evenly from the first frame to the last', () => {
    expect(movieSampleFrames(30, 4)).toEqual([0, 10, 19, 29]);
    expect(movieSampleFrames(30, 1)).toEqual([0]);
    expect(movieSampleFrames(30, 0)).toEqual([]);
  });
});

describe('recordMovie', () => {
  it('renders every frame once, in order, turning the camera and advancing the markers by the schedule', async () => {
    const fake = fakeViewer();
    const encoder = fakeEncoder();
    const progress: string[] = [];
    const file = await recordMovie(
      recording(fake, encoder, { onProgress: ({ stage, done, total }) => progress.push(`${stage}:${done}/${total}`) }),
    );

    expect(await file.text()).toBe('movie');
    expect(encoder.opened).toEqual([
      expect.objectContaining({ width: 320, height: 180, fps: 10, quality: DEFAULT_MOVIE_SETTINGS.quality }),
    ]);
    expect(encoder.calls).toEqual([...Array.from({ length: 30 }, (_, index) => `frame:${index}`), 'finish', 'close']);
    expect(progress.at(-2)).toBe('rendering:30/30');
    expect(progress.at(-1)).toBe('finishing:30/30');

    const step = (6 * Math.PI) / 180 / 10;
    const azimuths = fake.captured.map((frame) => frame.azimuth!);
    azimuths.slice(0, 20).forEach((azimuth, index) => expect(azimuth).toBeCloseTo(0.3 + index * step, 12));
    // The still frames at the end hold the last angle of the replay.
    azimuths.slice(20).forEach((azimuth) => expect(azimuth).toBeCloseTo(0.3 + 19 * step, 12));
    expect(new Set(fake.captured.map((frame) => frame.polar))).toEqual(new Set([0.7]));
    expect(fake.captured.map((frame) => frame.advance)).toEqual([0, ...Array.from({ length: 29 }, () => 100)]);
  });

  it('starts from the view a turn under way is going to, not from halfway through it', async () => {
    const fake = fakeViewer();
    const encoder = fakeEncoder();
    // A turn to the starting view is still under way: until it is settled the camera stands
    // between the two views, and settling it brings it to the one it was turning to.
    const setAngles = fake.viewer.setCameraAngles;
    let turning = true;
    fake.viewer.setCameraAngles = vi.fn((next: { azimuth?: number; polar?: number }) => {
      if (turning && next.azimuth === undefined && next.polar === undefined) {
        turning = false;
        setAngles({ azimuth: 1.2, polar: Math.PI / 2 });
        return;
      }
      setAngles(next);
    });
    await recordMovie(recording(fake, encoder));

    expect(fake.captured[0].azimuth).toBeCloseTo(1.2, 12);
    expect(new Set(fake.captured.map((frame) => frame.polar))).toEqual(new Set([Math.PI / 2]));
    // And the preview is handed back at that view, not tilted between two.
    expect(fake.viewer.getCameraAngles()).toEqual({ azimuth: 1.2, polar: Math.PI / 2 });
  });

  it('slides the markers over the transition time, and places them at once on the first frame', async () => {
    const fake = fakeViewer();
    await recordMovie(recording(fake, fakeEncoder(), { settings: settings({}, { transitionS: 1.5 }) }));
    // Ana moves at 10:00 and Bogdan at 11:00, both after the first frame.
    expect(fake.moves.length).toBeGreaterThanOrEqual(2);
    for (const move of fake.moves) {
      expect(move.duration).toBe(1500);
    }
    expect(fake.captured[0].markers).toEqual({ 'trip-1:ana': 'p.1', 'trip-1:bogdan': 'p.1' });
    expect(fake.captured[29].markers).toEqual({ 'trip-1:ana': 'p.2', 'trip-1:bogdan': 'p.3' });
  });

  it('samples a palette from frames spread over the movie before the first frame, then starts from the first', async () => {
    const fake = fakeViewer();
    const encoder = fakeEncoder(4);
    await recordMovie(recording(fake, encoder));

    expect(encoder.calls[0]).toBe('prime:4');
    expect(encoder.calls[1]).toBe('frame:0');
    const step = (6 * Math.PI) / 180 / 10;
    const sampled = fake.captured.slice(0, 4);
    // Frames 0, 10, 19 and 29 — the last a still frame, at the last angle of the replay.
    [0, 10, 19, 19].forEach((turn, index) => expect(sampled[index].azimuth!).toBeCloseTo(0.3 + turn * step, 12));
    expect(sampled.map((frame) => frame.advance)).toEqual([0, 0, 0, 0]);
    // The last sample stands the party where the movie ends; the first frame puts it back at the start.
    expect(sampled[3].markers).toEqual({ 'trip-1:ana': 'p.2', 'trip-1:bogdan': 'p.3' });
    expect(fake.captured[4].markers).toEqual({ 'trip-1:ana': 'p.1', 'trip-1:bogdan': 'p.1' });
    expect(fake.captured[4].advance).toBe(0);
    // Every move made while sampling, and the one back to the start, was instant.
    const early = fake.moves.filter((move) => move.at <= 4);
    expect(early.length).toBeGreaterThan(0);
    expect(early.every((move) => move.duration === 0)).toBe(true);
    expect(fake.captured).toHaveLength(4 + 30);
    expect(fake.captured.every((frame) => frame.drawnInto)).toBe(true);
  });

  it('reports each stage by its own count, and the whole of the work for the bar', async () => {
    const progress: string[] = [];
    await recordMovie(
      recording(fakeViewer(), fakeEncoder(4), {
        onProgress: ({ stage, done, total, step, steps }) => progress.push(`${stage} ${step}/${steps} ${done}/${total}`),
      }),
    );

    expect(progress.slice(0, 4)).toEqual(['sampling 1/4 1/34', 'sampling 2/4 2/34', 'sampling 3/4 3/34', 'sampling 4/4 4/34']);
    // The first frame of the movie is its first, however many samples were taken before it.
    expect(progress[4]).toBe('rendering 1/30 5/34');
    expect(progress.at(-2)).toBe('rendering 30/30 34/34');
    expect(progress.at(-1)).toBe('finishing 30/30 34/34');
  });

  it('draws labels at the frame’s own size while capturing, and none at all when they are off', async () => {
    const sized = fakeViewer();
    await recordMovie(recording(sized, fakeEncoder(), { settings: settings({}, { labelSize: 21 }) }));
    expect(new Set(sized.captured.map((frame) => frame.labelSize))).toEqual(new Set([21]));
    expect(new Set(sized.captured.map((frame) => frame.labels))).toEqual(new Set([true]));

    const off = fakeViewer();
    await recordMovie(recording(off, fakeEncoder(), { settings: settings({}, { labels: 'off' }) }));
    expect(new Set(off.captured.map((frame) => frame.labels))).toEqual(new Set([false]));
    expect(off.viewer.liveMarkerLabels).toBe(true);
  });

  it('hands the preview back exactly as it found it', async () => {
    const fake = previewViewer();
    const before = scene(fake);
    const clusterLabelAfter = () => 'preview';
    await recordMovie(recording(fake, fakeEncoder(4), { settings: settings({}, { trails: true }), clusterLabelAfter }));

    expect(scene(fake)).toEqual(before);
    expect(fake.viewer.setLiveMarkerClusterLabel).toHaveBeenLastCalledWith(clusterLabelAfter);
    expect(fake.log).toEqual(['beginCapture', 'endCapture']);
    // The movie's own trails were drawn, and are gone.
    expect(fake.viewer.addTrail).toHaveBeenCalled();
    expect([...fake.trails.keys()]).toEqual(['preview-trail']);
    // During the recording the preview's trail was hidden, and the movie's view was on.
    expect(fake.viewer.updateTrail).toHaveBeenCalledWith('preview-trail', null, { visible: false });
  });

  it('is the same recording every time from the same inputs', async () => {
    const first = previewViewer();
    const second = previewViewer();
    const one = fakeEncoder(4);
    const two = fakeEncoder(4);
    await recordMovie(recording(first, one));
    await recordMovie(recording(second, two));
    expect(second.captured).toEqual(first.captured);
    expect(two.calls).toEqual(one.calls);
  });

  it('stops when cancelled, puts the preview back, and rejects with an abort nothing logs', async () => {
    const errors = vi.spyOn(console, 'error');
    const fake = previewViewer();
    const before = scene(fake);
    const encoder = fakeEncoder();
    const controller = new AbortController();
    const outcome = recordMovie(
      recording(fake, encoder, {
        signal: controller.signal,
        onProgress: ({ done }) => {
          if (done === 5) controller.abort();
        },
      }),
    ).catch((error: unknown) => error);

    const error = await outcome;
    expect(isMovieAbort(error)).toBe(true);
    expect(encoder.calls).toEqual(['frame:0', 'frame:1', 'frame:2', 'frame:3', 'frame:4', 'close']);
    expect(scene(fake)).toEqual(before);
    expect(errors).not.toHaveBeenCalled();
  });

  it('does not start when already cancelled, and touches nothing', async () => {
    const fake = previewViewer();
    const encoder = fakeEncoder();
    const controller = new AbortController();
    controller.abort();
    const error = await recordMovie(recording(fake, encoder, { signal: controller.signal })).catch((e: unknown) => e);
    expect(isMovieAbort(error)).toBe(true);
    expect(encoder.openEncoder).not.toHaveBeenCalled();
    expect(fake.viewer.beginCapture).not.toHaveBeenCalled();
  });

  it('passes an encoder’s failure on as it is, not as a cancel, after putting the preview back', async () => {
    const fake = previewViewer();
    const before = scene(fake);
    const encoder = fakeEncoder(0, 3);
    const error = await recordMovie(recording(fake, encoder)).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(Error);
    expect((error as Error).message).toBe('WEBM encoder, encode: the codec gave up');
    expect(isMovieAbort(error)).toBe(false);
    expect(encoder.calls.at(-1)).toBe('close');
    expect(scene(fake)).toEqual(before);
  });

  it('stays a cancel when putting back fails on a viewer being taken down, and reports it otherwise', async () => {
    const takenDown = previewViewer();
    const controller = new AbortController();
    takenDown.viewer.endCapture.mockImplementation(() => {
      throw new Error('disposed');
    });
    const cancelled = await recordMovie(
      recording(takenDown, fakeEncoder(), {
        signal: controller.signal,
        onProgress: ({ done }) => {
          if (done === 2) controller.abort();
        },
      }),
    ).catch((e: unknown) => e);
    expect(isMovieAbort(cancelled)).toBe(true);
    // Everything after the failing step was still put back.
    expect(takenDown.viewer.liveMarkerLabelSize).toBe(12);

    const finished = previewViewer();
    finished.viewer.endCapture.mockImplementation(() => {
      throw new Error('disposed');
    });
    const failed = await recordMovie(recording(finished, fakeEncoder())).catch((e: unknown) => e);
    expect((failed as Error).message).toBe('disposed');
  });

  it('passes the viewer’s refusal to capture on, leaving the preview as it was', async () => {
    const fake = previewViewer();
    const before = scene(fake);
    fake.viewer.beginCapture.mockImplementation(() => {
      throw new Error('the container is not the shape of the frame');
    });
    const encoder = fakeEncoder();
    const error = await recordMovie(recording(fake, encoder)).catch((e: unknown) => e);
    expect((error as Error).message).toBe('the container is not the shape of the frame');
    expect(fake.viewer.endCapture).not.toHaveBeenCalled();
    expect(encoder.calls).toEqual(['close']);
    expect(scene(fake)).toEqual(before);
  });
});
