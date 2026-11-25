// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { flybyBundleRequest, type FlybyBundleRequest } from './flybyBlenderBundle.ts';
import type { Camera3DEye } from '../scene3d/camera3d.ts';
import { FLYBY_VERSION, flybyProblems, type FlybyDocument } from './flybyDocument.ts';
import type { FlybyLookBase } from './flybyLook.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

// The browser's half of a check that runs in three places.
//
// A flyby is flown here and rendered somewhere else, by a program that has never seen this code.
// What has to survive the journey is the camera: where it stands, which way it looks and how wide
// it sees, at every frame. So an invented flyby is sampled as an export samples one, and beside the
// request the export would send, this test writes down — worked out here, from the globe's own
// arithmetic and nothing of the exporter's — where on the Earth the camera of each frame is and
// which way it points. The server's tests build the bundle from the same request and compare its
// camera with these numbers; where a Blender is installed they build the scene in it and compare
// that camera too.
//
// Both files are committed under contract/blender-bundle/v1. A normal run compares against them and
// fails on any difference; SILEXGIS_CONTRACT_RECORD=1 rewrites them, which is the only way they
// change, and a diff in them is a change to what a bundle is made from.

const CONTRACT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'contract', 'blender-bundle', 'v1');
const RECORD = process.env.SILEXGIS_CONTRACT_RECORD === '1';

// ---- the invented flyby ----

// An invented flyby over two invented caves, for checking that a camera sampled here and the
// camera a render bundle gives Blender are the same camera, frame by frame.
//
// Nothing below is a real place or a real survey. The caves are a pair of boxes with made-up ways
// down them, the track is a made-up line, and the ground is a sine hill — a function, so the
// camera's clearance above it is the same number on every machine. The flyby goes through every
// way a section can move the camera: in from far away looking straight down, an approach, a whole
// turn, a hop to the second cave, a descent along its survey and a flight along the track; its
// lens narrows on the way and it is drawn at one and a half times its height.

/** Where the invented scene stands. */
const PARITY_ORIGIN: Camera3DEye = { lon: 9.75, lat: 44.35, height: 640 };

/** The two caves' ids, as a request names them. */
const PARITY_CAVES = ['0193a5c0-0000-7000-8000-00000000000a', '0193a5c0-0000-7000-8000-00000000000b'] as const;

const second: Camera3DEye = { lon: 9.7655, lat: 44.3588, height: 590 };

/** A line of places from one to another, bowed a little to one side, `count` of them. */
function line(from: Camera3DEye, to: Camera3DEye, count: number, bow: number): Camera3DEye[] {
  return Array.from({ length: count }, (_, i) => {
    const p = i / (count - 1);
    const sideways = Math.sin(p * Math.PI) * bow;
    return {
      lon: from.lon + (to.lon - from.lon) * p + sideways,
      lat: from.lat + (to.lat - from.lat) * p - sideways / 2,
      height: from.height + (to.height - from.height) * p,
    };
  });
}

const PARITY_SUBJECTS: FlybyResolvedSubject[] = [
  {
    centre: PARITY_ORIGIN,
    radius: 180,
    halfHeight: 70,
    top: { lon: 9.7494, lat: 44.3503, height: 710 },
    bottom: { lon: 9.7509, lat: 44.3495, height: 570 },
    coarse: false,
  },
  {
    centre: second,
    radius: 260,
    halfHeight: 110,
    top: { lon: 9.7648, lat: 44.3592, height: 700 },
    bottom: { lon: 9.7667, lat: 44.3581, height: 480 },
    coarse: false,
    // The way down it: from its top to its bottom, not in a straight line.
    route: line({ lon: 9.7648, lat: 44.3592, height: 700 }, { lon: 9.7667, lat: 44.3581, height: 480 }, 9, 0.0006),
  },
  {
    // An imported track on the surface, running away to the north-east.
    centre: { lon: 9.775, lat: 44.366, height: 760 },
    radius: 900,
    halfHeight: 40,
    coarse: false,
    route: line({ lon: 9.768, lat: 44.36, height: 720 }, { lon: 9.784, lat: 44.372, height: 800 }, 12, 0.0015),
  },
];

/** A gentle hill: the ground under any place, the same wherever it is asked. */
function parityGround(longitude: number, latitude: number): number {
  return 650 + 120 * Math.sin((longitude - 9.7) * 40) * Math.cos((latitude - 44.3) * 55);
}

const PARITY_FLYBY: FlybyDocument = {
  version: FLYBY_VERSION,
  name: 'Two invented caves',
  speed: 1,
  loop: false,
  subjects: [
    { kind: 'cave', caveId: PARITY_CAVES[0] },
    { kind: 'cave', caveId: PARITY_CAVES[1] },
    { kind: 'file', geofileId: 'invented-track' },
  ],
  start: { subject: 0, pose: { heading: 20, pitch: -89.9, range: 3, rangeM: 350_000, lift: 0, clearance: 30 } },
  sections: [
    { id: 'far', kind: 'fromFar', subject: 0, seconds: 3, to: { heading: 20, pitch: -35, range: 6, lift: 0, clearance: 30 } },
    { id: 'in', kind: 'approach', subject: 0, seconds: 2, to: { range: 2.5, pitch: -28 } },
    { id: 'round', kind: 'orbit', subject: 0, seconds: 4, to: { headingBy: 360 } },
    { id: 'over', kind: 'hop', subject: 1, seconds: 3, to: { heading: 140, pitch: -32, range: 3 }, say: 'name' },
    { id: 'down', kind: 'descent', subject: 1, seconds: 3, to: { rangeM: 25, pitch: -18, clearance: null } },
    { id: 'along', kind: 'follow', subject: 2, seconds: 3, to: { rangeM: 120, pitch: -24, clearance: 20 } },
  ],
  look: { sky: '#1b2a3a', lens: 60, relief: 1.5 },
  changes: [
    { id: 'zoom', kind: 'lens', degrees: 35, section: 'round', at: 0.5, seconds: 2 },
    { id: 'taller', kind: 'relief', factor: 2, section: 'along', at: 0, seconds: 2 },
  ],
  texts: {
    title: { text: 'Two invented caves', subtitle: 'A camera test', seconds: 2 },
    end: { text: 'The end', seconds: 1.5 },
    captions: [{ section: 'round', text: 'Once around', at: 0.5, seconds: 2 }],
    fadeIn: 0.5,
    fadeOut: 0.5,
    northArrow: true,
    corner: 'Invented survey',
  },
};

// ---- the globe, written out here and not borrowed ----

const A = 6_378_137;
const F = 1 / 298.257223563;
const E2 = F * (2 - F);
const radians = (degrees: number) => (degrees * Math.PI) / 180;

type Vector = [number, number, number];

function ecef(longitude: number, latitude: number, height: number): Vector {
  const lambda = radians(longitude);
  const phi = radians(latitude);
  const n = A / Math.sqrt(1 - E2 * Math.sin(phi) ** 2);
  return [
    (n + height) * Math.cos(phi) * Math.cos(lambda),
    (n + height) * Math.cos(phi) * Math.sin(lambda),
    (n * (1 - E2) + height) * Math.sin(phi),
  ];
}

/** East, north and up at a place, each as a direction in earth-centred axes. */
function axes(longitude: number, latitude: number): { east: Vector; north: Vector; up: Vector } {
  const lambda = radians(longitude);
  const phi = radians(latitude);
  return {
    east: [-Math.sin(lambda), Math.cos(lambda), 0],
    north: [-Math.sin(phi) * Math.cos(lambda), -Math.sin(phi) * Math.sin(lambda), Math.cos(phi)],
    up: [Math.cos(phi) * Math.cos(lambda), Math.cos(phi) * Math.sin(lambda), Math.sin(phi)],
  };
}

const combine = (east: number, north: number, up: number, at: ReturnType<typeof axes>): Vector => [
  east * at.east[0] + north * at.north[0] + up * at.up[0],
  east * at.east[1] + north * at.north[1] + up * at.up[1],
  east * at.east[2] + north * at.north[2] + up * at.up[2],
];

const round = (value: number, decimals: number) => Math.round(value * 10 ** decimals) / 10 ** decimals;

/**
 * What each frame's camera is, on the Earth: where the eye stands — at its height times the relief
 * the scene is drawn at —, the way it looks, where the top of its picture points, and the angle it
 * sees from left to right. Ten numbers a frame.
 */
function expectedCameras(request: FlybyBundleRequest): number[][] {
  const { width, height } = request.output!;
  return request.frames.map(([longitude, latitude, heightM, heading, pitch, , , lens]) => {
    const at = axes(longitude, latitude);
    const h = radians(heading);
    const p = radians(pitch);
    const eye = ecef(longitude, latitude, heightM * request.relief);
    const forward = combine(Math.sin(h) * Math.cos(p), Math.cos(h) * Math.cos(p), Math.sin(p), at);
    // With no roll the top of the picture is in the plane of the line of sight and the vertical.
    const up = combine(-Math.sin(h) * Math.sin(p), -Math.cos(h) * Math.sin(p), Math.cos(p), at);
    const across = width >= height ? lens : (2 * Math.atan(Math.tan(radians(lens) / 2) * (width / height)) * 180) / Math.PI;
    return [
      ...eye.map((value) => round(value, 4)),
      ...forward.map((value) => round(value, 10)),
      ...up.map((value) => round(value, 10)),
      round(across, 6),
    ];
  });
}

/** Twenty invented points about the two caves, on the Earth as the scene draws them. */
function expectedPoints(relief: number): number[][] {
  return Array.from({ length: 20 }, (_, i) =>
    ecef(9.75 + 0.0016 * (((i * 7) % 11) - 3), 44.35 + 0.0011 * (((i * 5) % 9) - 2), (520 + 15 * i) * relief).map((value) =>
      round(value, 4),
    ),
  );
}

// ---- the flyby, sampled as an export samples it ----

const viewer: FlybyLookBase = { backgroundOpacity: 1, surface: 'overlay', lens: 60, relief: 1, layers: {} };
const timeline = compileFlyby(PARITY_FLYBY, PARITY_SUBJECTS, (about) => about.name);

function ask(size: { width: number; height: number }, stretch?: { from: number; to: number }, holdEndS = 0) {
  return flybyBundleRequest({
    name: PARITY_FLYBY.name,
    timeline,
    schedule: flybyFrameSchedule(timeline.length, 25, stretch, holdEndS),
    size,
    quality: 'low',
    groundHeight: parityGround,
    base: viewer,
    flybyRelief: true,
    placement: { absolute: true, offsetM: 0 },
    routeCaves: [...PARITY_CAVES],
    cavesInView: [],
    surveyTop: () => undefined,
    groundAt: () => undefined,
    depthBands: [
      { fromMeters: 0, color: '#d9480f' },
      { fromMeters: 50, color: '#8c2f6b' },
    ],
    reliefWords: (factor) => `Relief ×${factor}`,
  });
}

const CASES = [
  { name: 'two-cave-flyby.landscape', made: ask({ width: 1280, height: 720 }) },
  // A stretch of it, upright, with the last moment held: the first second is still straight down.
  { name: 'two-cave-flyby.portrait', made: ask({ width: 1080, height: 1920 }, { from: 0.4, to: 4 }, 0.2) },
] as const;

/**
 * How far a recomputed number may stand from the committed one: two of the last digit it is written
 * to. A last digit can differ between machines — the sines and cosines behind it agree only to
 * their last bit — and anything more than that is a different camera.
 */
const REQUEST_FRAME = [2e-10, 2e-10, 2e-4, 2e-7, 2e-7, 2e-7, 2e-3, 2e-5];
const EXPECTED_FRAME = [2e-4, 2e-4, 2e-4, 2e-10, 2e-10, 2e-10, 2e-10, 2e-10, 2e-10, 2e-6];

function tolerance(path: string): number {
  const column = Number(/\[(\d+)\]$/.exec(path)?.[1] ?? 0);
  if (path.startsWith('request.frames[')) {
    return REQUEST_FRAME[column];
  }
  if (path.startsWith('expected.frames[')) {
    return EXPECTED_FRAME[column];
  }
  if (path.startsWith('expected.points[')) {
    return 2e-4;
  }
  // Everything else is a setting or a time written to a ten-thousandth of a second.
  return 1e-9;
}

function near(actual: unknown, expected: unknown, path: string): void {
  if (typeof expected === 'number' && typeof actual === 'number') {
    expect(Math.abs(actual - expected), `${path}: ${actual} against ${expected}`).toBeLessThanOrEqual(tolerance(path));
    return;
  }
  if (Array.isArray(expected)) {
    expect(Array.isArray(actual) && actual.length === expected.length, `${path}: a list of ${expected.length}`).toBe(true);
    expected.forEach((item, index) => near((actual as unknown[])[index], item, `${path}[${index}]`));
    return;
  }
  if (expected !== null && typeof expected === 'object') {
    expect(actual !== null && typeof actual === 'object', `${path}: an object`).toBe(true);
    expect(Object.keys(actual as object).sort(), path).toEqual(Object.keys(expected).sort());
    for (const [key, value] of Object.entries(expected)) {
      near((actual as Record<string, unknown>)[key], value, `${path}.${key}`);
    }
    return;
  }
  expect(actual, path).toEqual(expected);
}

function compactJson(value: { frames: number[][] } & Record<string, unknown>): string {
  // One frame a line, so a change to one frame is one line of a diff.
  const { frames, ...rest } = value;
  const head = JSON.stringify(rest, null, 2);
  const lines = frames.map((frame) => `    ${JSON.stringify(frame)}`).join(',\n');
  return `${head.slice(0, -2)},\n  "frames": [\n${lines}\n  ]\n}\n`;
}

describe('the invented flyby the camera check is made on', () => {
  it('is a flyby that can be played', () => {
    expect(flybyProblems(PARITY_FLYBY)).toEqual([]);
    expect(timeline.length).toBeCloseTo(18, 9);
  });

  it('goes through every way a section moves the camera, a narrowing lens and a stretched relief', () => {
    const { request, omissions, refusal } = CASES[0].made;
    expect(refusal).toBeNull();
    expect(request.frames).toHaveLength(450);
    expect(request.relief).toBe(1.5);
    // The lens is carried frame by frame; the relief that changes later is said to be left out.
    expect(omissions).toEqual(['ground', 'reliefChanges']);

    const lenses = request.frames.map((frame) => frame[7]);
    expect(Math.max(...lenses)).toBe(60);
    expect(Math.min(...lenses)).toBe(35);
    // Straight down from far away at the start, and under the ground by the end of the descent.
    expect(request.frames[0][4]).toBeLessThan(-89);
    expect(request.frames[0][2]).toBeGreaterThan(300_000);
    const lowest = Math.min(...request.frames.map((frame) => frame[2] - parityGround(frame[0], frame[1])));
    expect(lowest).toBeLessThan(-20);
  });

  it('is sampled through the function that places the camera on the screen', () => {
    const { request } = CASES[0].made;
    for (const index of [0, 1, 74, 75, 137, 300, 449]) {
      const camera = flybyCameraAt(timeline, index / 25, parityGround);
      expect(request.frames[index][0]).toBeCloseTo(camera.longitude, 9);
      expect(request.frames[index][1]).toBeCloseTo(camera.latitude, 9);
      expect(request.frames[index][2]).toBeCloseTo(camera.height, 3);
      expect(request.frames[index][3]).toBeCloseTo(camera.heading, 6);
      expect(request.frames[index][4]).toBeCloseTo(camera.pitch, 6);
    }
  });

  it('takes a stretch frame by frame from where the stretch starts, and holds its end', () => {
    const { request } = CASES[1].made;
    // 3.6 s at 25 a second and a fifth of a second held.
    expect(request.frames).toHaveLength(95);
    const first = flybyCameraAt(timeline, 0.4, parityGround);
    expect(request.frames[0][2]).toBeCloseTo(first.height, 3);
    expect(request.frames[94]).toEqual(request.frames[90]);
    expect(request.texts?.title?.seconds).toBeCloseTo(1.6, 6);
    expect(request.texts?.end).toBeNull();
  });
});

describe.each(CASES)('$name', ({ name, made }) => {
  const requestPath = join(CONTRACT, `${name}.request.json`);
  const expectedPath = join(CONTRACT, `${name}.expected.json`);
  const expected = {
    about:
      'The camera of each frame of the request beside this file, on the Earth: eye x y z in metres (earth-centred, WGS 84), ' +
      'the unit direction it looks in, the unit direction of the top of its picture, and the degrees it sees from left to right. ' +
      'Then twenty invented points, earth-centred, to project through it.',
    relief: made.request.relief,
    width: made.request.output!.width,
    height: made.request.output!.height,
    fps: made.request.output!.fps,
    points: expectedPoints(made.request.relief),
    frames: expectedCameras(made.request),
  };

  it('is the request and the cameras that are committed', () => {
    if (RECORD) {
      mkdirSync(CONTRACT, { recursive: true });
      writeFileSync(requestPath, compactJson(made.request));
      writeFileSync(expectedPath, compactJson(expected));
    }
    expect(existsSync(requestPath), `${requestPath} is missing: run once with SILEXGIS_CONTRACT_RECORD=1`).toBe(true);
    near(made.request, JSON.parse(readFileSync(requestPath, 'utf8')), 'request');
    near(expected, JSON.parse(readFileSync(expectedPath, 'utf8')), 'expected');
  });

  it('has a camera whose two directions are one long and at right angles, at every frame', () => {
    for (const [index, frame] of expected.frames.entries()) {
      const forward = frame.slice(3, 6);
      const up = frame.slice(6, 9);
      expect(Math.hypot(...forward), `frame ${index}`).toBeCloseTo(1, 8);
      expect(Math.hypot(...up), `frame ${index}`).toBeCloseTo(1, 8);
      expect(forward[0] * up[0] + forward[1] * up[1] + forward[2] * up[2], `frame ${index}`).toBeCloseTo(0, 8);
    }
  });
});
