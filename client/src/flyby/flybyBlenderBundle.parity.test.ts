// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { flybyBundleRequest, type FlybyBundleRequest } from './flybyBlenderBundle.ts';
import { FLYBY_BUNDLE_NUMBER, FLYBY_BUNDLE_TINT, flybyBundleTrackAt, type FlybyBundleTrackKey } from './flybyBundleChanges.ts';
import type { Camera3DEye } from '../scene3d/camera3d.ts';
import { FLYBY_VERSION, flybyProblems, type FlybyDocument } from './flybyDocument.ts';
import { flybyLookAt, flybyReliefAt, type FlybyLookBase } from './flybyLook.ts';
import { flybyFrameSchedule, type FlybyFrameSchedule } from './flybyMovie.ts';
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
// And the look, which changes while the flyby plays: the relief each frame is drawn at, the sky,
// the walls and the lines shown and how much of them, and each cave's colours — read here, frame
// by frame, from the function that puts a moment's look on the screen. The server's tests play
// the bundle's keys and set them beside these; Blender is asked what each model has at a frame.
//
// And one thing the screen does not show and a bundle does: the frames at which the camera is
// under the ground, worked out here from the camera's heights and the invented hill. A bundle's
// sun and sky go out there.
//
// And what the scene points out: entrances and features as markers, and photographs at stations.
// A request names a marker by id and says nothing of a photograph but whose cave it is of, so the
// invented places of both are written beside the cameras — with whether each layer is shown, and
// how much of it, at every frame. The server's tests stand the bundle's markers and photographs
// beside those places, frame by frame, and their coming and going beside these rows. The first of
// the two clips also lets its caves appear part by part.
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
// lens narrows on the way and it is drawn at one and a half times its height — and at twice its
// height by the end, the relief growing while the camera flies along the track.
//
// Its look changes as a flyby's does: every cave dimmed and each lit while the camera is with it,
// the lines cut from their colours by depth to one colour and thinned to half, the sky darkening,
// and the walls fading out as the flyby ends — gone at its very end, which is one frame past the
// clip's last. Every change begins and ends on a frame of the clip.

/** Where the invented scene stands. */
const PARITY_ORIGIN: Camera3DEye = { lon: 9.75, lat: 44.35, height: 640 };

/** The two caves' ids, as a request names them. */
const PARITY_CAVES = ['0193a5c0-0000-7000-8000-00000000000a', '0193a5c0-0000-7000-8000-00000000000b'] as const;

const second: Camera3DEye = { lon: 9.7655, lat: 44.3588, height: 590 };

/** A layer the viewer draws from a saved filter, by the key a flyby shows and hides it by. */
const PARITY_FEATURES = 'filter:0193a5c0-0000-7000-8000-0000000000f1';

/**
 * Invented entrances and features: an id as a request names it, and the place the server's test
 * gives it. Two entrances of the first cave, one of the second, and three springs beside them.
 */
const PARITY_MARKERS: { layer: string; kind: 'entrances' | 'features'; id: string; lon: number; lat: number; named?: boolean }[] = [
  { layer: 'entrances', kind: 'entrances', id: '0193a5c0-0000-7000-8000-0000000000e1', lon: 9.7494, lat: 44.3503, named: true },
  { layer: 'entrances', kind: 'entrances', id: '0193a5c0-0000-7000-8000-0000000000e2', lon: 9.7509, lat: 44.3495, named: true },
  { layer: 'entrances', kind: 'entrances', id: '0193a5c0-0000-7000-8000-0000000000e3', lon: 9.7648, lat: 44.3592, named: true },
  { layer: PARITY_FEATURES, kind: 'features', id: '0193a5c0-0000-7000-8000-0000000000d1', lon: 9.7521, lat: 44.3512 },
  { layer: PARITY_FEATURES, kind: 'features', id: '0193a5c0-0000-7000-8000-0000000000d2', lon: 9.7631, lat: 44.3571 },
  { layer: PARITY_FEATURES, kind: 'features', id: '0193a5c0-0000-7000-8000-0000000000d3', lon: 9.7702, lat: 44.3611 },
];

/**
 * Invented stations with a photograph each: the cave, counted from 0, and the station's place and
 * surveyed altitude. One in the first cave; three down the second, which the camera descends.
 */
const PARITY_PHOTOS: [number, number, number, number][] = [
  [0, 9.7501, 44.3499, 640],
  [1, 9.7652, 44.359, 660],
  [1, 9.7659, 44.3586, 590],
  [1, 9.7665, 44.3582, 500],
];

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
  look: { sky: '#1b2a3a', lens: 60, relief: 1.5, walls: { colour: '#3a3f4a', strength: 0.6 } },
  changes: [
    { id: 'zoom', kind: 'lens', degrees: 35, section: 'round', at: 0.5, seconds: 2 },
    { id: 'taller', kind: 'relief', factor: 2, section: 'along', at: 0, seconds: 2 },
    // The first cave lit as the camera comes in, and dimmed again as it finishes its turn.
    { id: 'lit-1', kind: 'cave', subject: 0, colour: '#ffcc00', strength: 0.85, section: 'in', at: 0, seconds: 1 },
    { id: 'dim-1', kind: 'cave', subject: 0, colour: '#ffcc00', strength: 0, section: 'round', at: 3, seconds: 1 },
    // The second, while the camera goes down it.
    { id: 'lit-2', kind: 'cave', subject: 1, colour: '#ffcc00', strength: 0.85, section: 'over', at: 2, seconds: 1 },
    { id: 'dim-2', kind: 'cave', subject: 1, colour: '#ffcc00', strength: 0, section: 'down', at: 2, seconds: 1 },
    { id: 'one-colour', kind: 'lines', colour: '#5c6370', section: 'round', at: 0, seconds: 0 },
    { id: 'thin', kind: 'layerOpacity', layer: 'centerlines', opacity: 0.5, section: 'over', at: 0, seconds: 1 },
    { id: 'dusk', kind: 'sky', colour: '#0b1118', section: 'down', at: 0, seconds: 2 },
    { id: 'walls-go', kind: 'layer', layer: 'walls', visible: false, section: 'along', at: 1.4, seconds: 1.6 },
    // The entrances fade out as the camera leaves for the second cave and are cut back in while it
    // goes down; a layer of features is thinned; the photographs are switched off for the track.
    { id: 'pins-go', kind: 'layer', layer: 'entrances', visible: false, section: 'over', at: 0, seconds: 1 },
    { id: 'pins-back', kind: 'layer', layer: 'entrances', visible: true, section: 'down', at: 1, seconds: 0 },
    { id: 'springs-thin', kind: 'layerOpacity', layer: PARITY_FEATURES, opacity: 0.4, section: 'round', at: 1, seconds: 2 },
    { id: 'photos-go', kind: 'layer', layer: 'photos', visible: false, section: 'along', at: 0.4, seconds: 0 },
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
 * that frame is drawn at —, the way it looks, where the top of its picture points, the angle it
 * sees from left to right, and that relief. Eleven numbers a frame.
 */
function expectedCameras(request: FlybyBundleRequest, reliefs: readonly number[]): number[][] {
  const { width, height } = request.output!;
  return request.frames.map(([longitude, latitude, heightM, heading, pitch, , , lens], index) => {
    const at = axes(longitude, latitude);
    const h = radians(heading);
    const p = radians(pitch);
    const eye = ecef(longitude, latitude, heightM * reliefs[index]);
    const forward = combine(Math.sin(h) * Math.cos(p), Math.cos(h) * Math.cos(p), Math.sin(p), at);
    // With no roll the top of the picture is in the plane of the line of sight and the vertical.
    const up = combine(-Math.sin(h) * Math.sin(p), -Math.cos(h) * Math.sin(p), Math.cos(p), at);
    const across = width >= height ? lens : (2 * Math.atan(Math.tan(radians(lens) / 2) * (width / height)) * 180) / Math.PI;
    return [
      ...eye.map((value) => round(value, 4)),
      ...forward.map((value) => round(value, 10)),
      ...up.map((value) => round(value, 10)),
      round(across, 6),
      round(reliefs[index], 6),
    ];
  });
}

/** Twenty invented points about the two caves: longitude, latitude and true height, for whoever draws them at a frame's relief. */
function expectedPoints(): number[][] {
  return Array.from({ length: 20 }, (_, i) => [
    round(9.75 + 0.0016 * (((i * 7) % 11) - 3), 6),
    round(44.35 + 0.0011 * (((i * 5) % 9) - 2), 6),
    520 + 15 * i,
  ]);
}

// ---- the flyby, sampled as an export samples it ----

const viewer: FlybyLookBase = { backgroundOpacity: 1, surface: 'overlay', lens: 60, relief: 1, layers: {} };
const timeline = compileFlyby(PARITY_FLYBY, PARITY_SUBJECTS, (about) => about.name);

const PARITY_LAYER_COLOURS: Record<string, string> = { entrances: '#146262', [PARITY_FEATURES]: '#722ed1' };

function ask(size: { width: number; height: number }, stretch?: { from: number; to: number }, holdEndS = 0, reveal = false) {
  const schedule = flybyFrameSchedule(timeline.length, 25, stretch, holdEndS);
  const made = flybyBundleRequest({
    name: PARITY_FLYBY.name,
    timeline,
    schedule,
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
    markers: ['entrances', PARITY_FEATURES].map((layer) => ({
      layer,
      kind: layer === 'entrances' ? ('entrances' as const) : ('features' as const),
      colour: PARITY_LAYER_COLOURS[layer],
      markers: PARITY_MARKERS.filter((marker) => marker.layer === layer).map((marker) => ({
        featureId: marker.id,
        groundM: parityGround(marker.lon, marker.lat),
        ...(marker.named ? { named: true } : {}),
      })),
    })),
    reveal: reveal ? 'distance' : null,
  });
  return { ...made, schedule };
}

const CASES = [
  // The whole of it, its caves appearing part by part.
  { name: 'two-cave-flyby.landscape', made: ask({ width: 1280, height: 720 }, undefined, 0, true) },
  // A stretch of it, upright, with the last moment held: the first second is still straight down,
  // and the first cave is lit just as the stretch ends.
  { name: 'two-cave-flyby.portrait', made: ask({ width: 1080, height: 1920 }, { from: 0.4, to: 4 }, 0.2) },
  // A stretch cut out of its middle, which begins while the lines are thinning and the entrances
  // are going, and ends under the ground with the relief on its way up.
  { name: 'two-cave-flyby.cut', made: ask({ width: 1280, height: 720 }, { from: 9.4, to: 15.4 }) },
] as const;

// ---- the look, read from what puts it on the screen ----

/** The relief each frame of a clip is drawn at. */
function expectedReliefs(schedule: FlybyFrameSchedule): number[] {
  return Array.from({ length: schedule.count }, (_, index) => flybyReliefAt(timeline.look, viewer.relief, schedule.at(index)));
}

type LookRow = [string, boolean, number, boolean, number, ...(string | number | null)[]];

/**
 * The look of each frame: the sky, whether the walls are shown and how much of them, the same of
 * the lines, and for each cave the colour over its walls with its strength and the one colour of
 * its lines (null: by depth). A cave's own colour stands over the colour of every cave while it
 * has any strength, as the scene draws it.
 */
function expectedLook(schedule: FlybyFrameSchedule): LookRow[] {
  return Array.from({ length: schedule.count }, (_, index) => {
    const state = flybyLookAt(timeline.look, viewer, schedule.at(index));
    const walls = state.layers.walls ?? { visible: true, opacity: 1 };
    const lines = state.layers.centerlines ?? { visible: true, opacity: 1 };
    const row: LookRow = [state.sky ?? '#000000', walls.visible, round(walls.opacity, 6), lines.visible, round(lines.opacity, 6)];
    for (const caveId of PARITY_CAVES) {
      const own = state.caves?.[caveId];
      const tint = own && own.strength > 0 ? own : state.walls;
      row.push(tint && tint.strength > 0 ? tint.colour : '#000000', round(tint?.strength ?? 0, 6));
      row.push(own && own.strength > 0 ? own.colour : (state.lines ?? null));
    }
    return row;
  });
}

type PointedRow = [boolean, number, boolean, number, boolean];

/**
 * What the scene points out at each frame: whether the entrances are shown and how much of them,
 * the same of the layer of features, and whether the photographs are — which the view switches at
 * once: on while their layer is, off when a change that hides it has run.
 */
function expectedPointed(schedule: FlybyFrameSchedule): PointedRow[] {
  return Array.from({ length: schedule.count }, (_, index) => {
    const state = flybyLookAt(timeline.look, viewer, schedule.at(index));
    const of = (key: string) => state.layers[key] ?? { visible: true, opacity: 1 };
    const entrances = of('entrances');
    const features = of(PARITY_FEATURES);
    return [entrances.visible, round(entrances.opacity, 6), features.visible, round(features.opacity, 6), of('photos').visible];
  });
}

const channels = (colour: string) => [1, 3, 5].map((at) => Number.parseInt(colour.slice(at, at + 2), 16));

type TintKeys = readonly { frame: number; colour: string; strength: number; easing?: string | null }[];

function played(keys: TintKeys): FlybyBundleTrackKey[] {
  return keys.map((key) => ({
    frame: key.frame,
    value: [...channels(key.colour), key.strength],
    ...(key.easing === 'smooth' || key.easing === 'constant' ? { easing: key.easing } : {}),
  }));
}

/**
 * The frames at which the camera is under the ground, as runs: each from its first frame up to, and
 * not including, the frame it is above the ground again. From the heights the request carries and
 * the invented hill — and nothing of the exporter's.
 */
function expectedUnderground(request: FlybyBundleRequest): [number, number][] {
  const runs: [number, number][] = [];
  request.frames.forEach(([longitude, latitude, height], frame) => {
    if (height < parityGround(longitude, latitude)) {
      const run = runs[runs.length - 1];
      if (run && run[1] === frame) {
        run[1] = frame + 1;
      } else {
        runs.push([frame, frame + 1]);
      }
    }
  });
  return runs;
}

/**
 * The frames at which the request's keys say another colour for a cave than the screen shows. A
 * hand-over between a cave's own colour and the colour of every cave used to stand a frame off;
 * now there is none. By cave, for its walls and for its lines.
 */
function handOvers(request: FlybyBundleRequest, look: readonly LookRow[]): { walls: number[]; lines: number[] }[] {
  return PARITY_CAVES.map((caveId, cave) => {
    const apart = request.changes?.caves?.find((entry) => entry.caveId === caveId);
    const walls = played((apart?.walls ?? request.changes?.walls?.tint ?? []) as TintKeys);
    const lines = played(
      (apart?.lines ??
        request.changes?.lines?.tint ??
        (request.look?.lineColour ? [{ frame: 0, colour: request.look.lineColour, strength: 1 }] : [])) as TintKeys,
    );
    const off = { walls: [] as number[], lines: [] as number[] };
    look.forEach((row, frame) => {
      const [wallsColour, wallsStrength, linesColour] = row.slice(5 + cave * 3, 8 + cave * 3) as [string, number, string | null];
      const none = [0, 0, 0, 0];
      const wallsSaid = walls.length > 0 ? flybyBundleTrackAt(walls, frame, FLYBY_BUNDLE_TINT) : none;
      if (!FLYBY_BUNDLE_TINT.same(wallsSaid, [...channels(wallsColour), wallsStrength])) {
        off.walls.push(frame);
      }
      const linesSaid = lines.length > 0 ? flybyBundleTrackAt(lines, frame, FLYBY_BUNDLE_TINT) : none;
      if (!FLYBY_BUNDLE_TINT.same(linesSaid, linesColour ? [...channels(linesColour), 1] : none)) {
        off.lines.push(frame);
      }
    });
    return off;
  });
}

/**
 * How far a recomputed number may stand from the committed one: two of the last digit it is written
 * to. A last digit can differ between machines — the sines and cosines behind it agree only to
 * their last bit — and anything more than that is a different camera.
 */
const REQUEST_FRAME = [2e-10, 2e-10, 2e-4, 2e-7, 2e-7, 2e-7, 2e-3, 2e-5];
const EXPECTED_FRAME = [2e-4, 2e-4, 2e-4, 2e-10, 2e-10, 2e-10, 2e-10, 2e-10, 2e-10, 2e-6, 2e-6];

function tolerance(path: string): number {
  const column = Number(/\[(\d+)\]$/.exec(path)?.[1] ?? 0);
  if (path.startsWith('request.frames[')) {
    return REQUEST_FRAME[column];
  }
  if (path.startsWith('expected.frames[')) {
    return EXPECTED_FRAME[column];
  }
  if (path.startsWith('expected.underground')) {
    return 0;
  }
  if (path.startsWith('expected.look[') || path.startsWith('expected.pointed[') || path.includes('.changes.') || path.includes('.opacity[')) {
    // A strength or a factor, written to a millionth.
    return 2e-6;
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
  // One frame a line, so a change to one frame is one line of a diff; and one key a line.
  const { frames, ...rest } = value;
  const rows = (list: unknown[][]) => list.map((row) => `    ${JSON.stringify(row)}`).join(',\n');
  let tables = '';
  for (const name of ['look', 'pointed']) {
    if (Array.isArray(rest[name])) {
      tables += `,\n  "${name}": [\n${rows(rest[name] as unknown[][])}\n  ]`;
      delete rest[name];
    }
  }
  const head = JSON.stringify(rest, null, 2)
    .replace(/\{\n\s+"(?:frame|featureId)": [^{}]*?\}/g, (key) => key.replace(/\n\s*/g, ' '))
    .replace(/\[\n\s+(?:-?[\d.]+|"[^"]*"),\n[^[\]{}]*?\]/g, (list) => list.replace(/\n\s*/g, ' ').replace('[ ', '[').replace(' ]', ']'));
  return `${head.slice(0, -2)}${tables},\n  "frames": [\n${rows(frames)}\n  ]\n}\n`;
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
    // The lens is carried frame by frame, every change of the look by its keys and what the scene
    // points out as markers: only the ground is left out, and the outlines a layer of features may hold.
    expect(omissions).toEqual(['ground', 'featureOutlines']);
    expect(request.changes?.relief).toEqual([
      { frame: 0, value: 1.5, easing: null },
      { frame: 375, value: 1.5, easing: null },
      { frame: 425, value: 2, easing: 'smooth' },
    ]);

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

  it('changes its look in every way a bundle carries, each change on frames of the clip', () => {
    const { request } = CASES[0].made;
    const changes = request.changes!;
    // The first cave: dimmed with the rest; lit over a second from no strength of its own colour,
    // which no frame shows — the third key of frame 75, which itself still shows the colour of
    // every cave; dimmed again in two keys, with the colour of every cave beside the second of
    // them, on frame 225.
    expect(changes.walls?.tint).toEqual([{ frame: 0, colour: '#3a3f4a', strength: 0.6, easing: null }]);
    const first = changes.caves?.[0].walls?.map((key) => [key.frame, key.strength, key.easing]) ?? [];
    expect(first).toEqual([
      [0, 0.6, null],
      [75, 0.6, null],
      [75, 0.6, 'constant'],
      [75, 0, 'constant'],
      [100, 0.85, 'smooth'],
      [200, 0.85, null],
      [225, 0, 'smooth'],
      [225, 0.6, 'constant'],
    ]);
    // The lines: by depth, then one colour; a cave's own while it is lit.
    expect(changes.lines?.tint?.map((key) => [key.frame, key.strength, key.easing])).toEqual([
      [0, 0, null],
      [125, 1, 'constant'],
    ]);
    expect(changes.lines?.opacity).toEqual([
      { frame: 0, value: 1, easing: null },
      { frame: 225, value: 1, easing: null },
      { frame: 250, value: 0.5, easing: 'smooth' },
    ]);
    // The walls fade out over forty frames from frame 410 and have gone at 450 — one frame past
    // the clip's last, which ends on the way. How much of them there is does not change.
    expect(changes.walls?.shown).toEqual([{ frame: 410, shown: false, fadeFrames: 40 }]);
    expect(changes.walls?.opacity ?? null).toBeNull();
    // The sky crosses from one colour to another in two keys, mixed as the screen mixes them.
    expect(changes.sky).toEqual([
      { frame: 0, colour: '#1b2a3a', easing: null },
      { frame: 300, colour: '#1b2a3a', easing: null },
      { frame: 350, colour: '#0b1118', easing: 'smooth' },
    ]);
    // "Relief ×N" for every tenth the ramp passes.
    expect(request.texts?.cornerCaptions?.map((mark) => mark.text)).toEqual([
      'Relief ×1.5',
      'Relief ×1.6',
      'Relief ×1.7',
      'Relief ×1.8',
      'Relief ×1.9',
      'Relief ×2',
    ]);
  });

  it('begins a stretch cut out of the middle on the way from keys before its first frame', () => {
    const { request, schedule } = CASES[2].made;
    expect(request.frames).toHaveLength(150);
    const changes = request.changes!;
    // The lines are thinning as the stretch begins: the thinning's own two keys, the first ten
    // frames before the clip — and the first frame is part of the way down.
    expect(changes.lines?.opacity).toEqual([
      { frame: -10, value: 1, easing: null },
      { frame: 15, value: 0.5, easing: 'smooth' },
    ]);
    expect(expectedLook(schedule)[0][4]).toBeGreaterThan(0.5);
    expect(expectedLook(schedule)[0][4]).toBeLessThan(1);
    // The entrances are going: a fade that began ten frames before the clip. They are cut back in later.
    const entrances = request.markers?.find((layer) => layer.kind === 'entrances');
    expect(entrances?.shown).toEqual([
      { frame: -10, shown: false, fadeFrames: 25 },
      { frame: 90, shown: true },
    ]);
    expect(entrances?.opacity ?? null).toBeNull();
    // The second cave is lit a second and a half in: from no strength of its own colour, the
    // third key of that frame.
    const second = changes.caves?.find((cave) => cave.caveId === PARITY_CAVES[1])?.walls ?? [];
    expect(second.filter((key) => key.frame === 40).map((key) => [key.strength, key.easing])).toEqual([
      [0.6, null],
      [0.6, 'constant'],
      [0, 'constant'],
    ]);
    // It ends with the relief a fifth of the way through its two seconds, and under the ground:
    // both changes have their real ends after the clip's last frame.
    expect(changes.relief).toEqual([
      { frame: 0, value: 1.5, easing: null },
      { frame: 140, value: 1.5, easing: null },
      { frame: 190, value: 2, easing: 'smooth' },
    ]);
    expect(expectedUnderground(request)).toEqual([[121, 150]]);
    expect(changes.daylight).toEqual([
      { frame: 0, value: 1, easing: null },
      { frame: 121, value: 1, easing: null },
      { frame: 146, value: 0, easing: 'smooth' },
    ]);
    // And every frame's colours are the screen's.
    expect(handOvers(request, expectedLook(schedule))).toEqual([
      { walls: [], lines: [] },
      { walls: [], lines: [] },
    ]);
  });

  it('says of each cave\'s colours what the screen shows at every frame, the frames it changes hands on among them', () => {
    const { request, schedule } = CASES[0].made;
    expect(handOvers(request, expectedLook(schedule))).toEqual([
      // Lit from frame 75 and dimmed at 225: the screen changes hands just after and just at.
      { walls: [], lines: [] },
      { walls: [], lines: [] },
    ]);
    // The stretch ends as the first cave has just been lit, and holds that.
    const portrait = CASES[1].made;
    expect(handOvers(portrait.request, expectedLook(portrait.schedule))).toEqual([
      { walls: [], lines: [] },
      { walls: [], lines: [] },
    ]);
  });

  it('puts the daylight out while the camera is under the invented hill', () => {
    const { request } = CASES[0].made;
    const under = expectedUnderground(request);
    // Once: at the bottom of the second cave's survey, and out again as it turns to the track —
    // a second and a quarter, so the daylight is half of that going and half of it coming back.
    expect(under).toEqual([[356, 388]]);
    expect(request.changes?.daylight).toEqual([
      { frame: 0, value: 1, easing: null },
      { frame: 356, value: 1, easing: null },
      { frame: 372, value: 0, easing: 'smooth' },
      { frame: 388, value: 1, easing: 'smooth' },
    ]);
    // The stretch never leaves the daylight, and says nothing of it.
    expect(expectedUnderground(CASES[1].made.request)).toEqual([]);
    expect(CASES[1].made.request.changes?.daylight ?? null).toBeNull();
  });

  it('points out its entrances, a layer of features and the photographs of its caves, each layer coming and going on frames', () => {
    const { request } = CASES[0].made;
    expect(request.markers?.map((layer) => [layer.kind, layer.colour, layer.markers?.length])).toEqual([
      ['entrances', '#146262', 3],
      ['features', '#722ed1', 3],
    ]);
    // The entrances go over a second as the hop begins (frame 225) and are cut back in a second
    // into the descent (325).
    expect(request.markers?.[0].shown).toEqual([
      { frame: 225, shown: false, fadeFrames: 25 },
      { frame: 325, shown: true },
    ]);
    expect(request.markers?.[0].opacity ?? null).toBeNull();
    // The features are there throughout, thinned over two seconds of the turn.
    expect(request.markers?.[1].shown ?? null).toBeNull();
    expect(request.markers?.[1].opacity).toEqual([
      { frame: 0, value: 1, easing: null },
      { frame: 150, value: 1, easing: null },
      { frame: 200, value: 0.4, easing: 'smooth' },
    ]);
    // The photographs of both caves, switched off at once as the track begins.
    expect(request.photos).toEqual({ caves: [...PARITY_CAVES], shown: [{ frame: 385, shown: false }] });
    // The caves appear from the moment the title has gone to four fifths of the way to the end card.
    expect(request.reveal).toEqual({ by: 'distance', startS: 2, endS: 13.6 });
    expect(CASES[1].made.request.reveal ?? null).toBeNull();
  });

  it('says of each layer of markers what the screen shows at every frame', () => {
    for (const { made } of CASES) {
      const rows = expectedPointed(made.schedule);
      (made.request.markers ?? []).forEach((layer, at) => {
        let on = layer.shown && layer.shown.length > 0 ? !layer.shown[0].shown : true;
        const keys = (layer.opacity ?? [{ frame: 0, value: 1, easing: null }]).map(
          (key): FlybyBundleTrackKey => ({
            frame: key.frame,
            value: [key.value],
            ...(key.easing === 'smooth' || key.easing === 'constant' ? { easing: key.easing } : {}),
          }),
        );
        rows.forEach((row, frame) => {
          // Shown from the frame a key shows it on; one that hides it hides it once its fade has run.
          let fade = 1;
          for (const key of layer.shown ?? []) {
            const takes = key.fadeFrames ?? 0;
            if (key.frame > frame) {
              break;
            }
            const x = takes > 0 ? Math.min(1, (frame - key.frame) / takes) : 1;
            const eased = x * x * (3 - 2 * x);
            on = key.shown || x < 1;
            fade = key.shown ? eased : 1 - eased;
          }
          const [visible, opacity] = [row[at * 2] as boolean, row[at * 2 + 1] as number];
          expect(on, `layer ${at} frame ${frame}`).toBe(visible);
          if (visible) {
            expect(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0] * fade, `layer ${at} frame ${frame}`).toBeCloseTo(opacity, 5);
          }
        });
      });
    }
  });

  it('draws each frame at the relief the request\'s own keys give', () => {
    for (const { made } of CASES) {
      const keys = (made.request.changes?.relief ?? [{ frame: 0, value: made.request.relief, easing: null }]).map(
        (key): FlybyBundleTrackKey => ({
          frame: key.frame,
          value: [key.value],
          ...(key.easing === 'smooth' || key.easing === 'constant' ? { easing: key.easing } : {}),
        }),
      );
      expectedReliefs(made.schedule).forEach((relief, frame) => {
        expect(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0], `frame ${frame}`).toBeCloseTo(relief, 6);
      });
    }
  });
});

describe.each(CASES)('$name', ({ name, made }) => {
  const requestPath = join(CONTRACT, `${name}.request.json`);
  const expectedPath = join(CONTRACT, `${name}.expected.json`);
  const look = expectedLook(made.schedule);
  const expected = {
    about:
      'The camera of each frame of the request beside this file, on the Earth: eye x y z in metres (earth-centred, WGS 84), ' +
      'the unit direction it looks in, the unit direction of the top of its picture, the degrees it sees from left to right, ' +
      'and the relief the frame is drawn at. Twenty invented points (longitude, latitude, true height) to project through it, ' +
      'each at its height times the frame\'s relief. The look of each frame: the sky, the walls shown and how much of them, ' +
      'the lines shown and how much of them, then for each cave of the request the colour over its walls, that colour\'s ' +
      'strength, and the one colour of its lines (null: their colours by depth). And the frames at which the camera is under ' +
      'the ground, as runs from a first frame up to, and not including, the frame it is above the ground again. What the scene ' +
      'points out: the invented place of each marker the request names (its layer counted from 0, its id, longitude, latitude ' +
      'and the true height of the ground there), the invented stations with a photograph (the cave counted from 0, longitude, ' +
      'latitude, surveyed altitude), and for each frame whether the first layer of markers is shown and how much of it, the ' +
      'same of the second, and whether the photographs are shown.',
    width: made.request.output!.width,
    height: made.request.output!.height,
    fps: made.request.output!.fps,
    points: expectedPoints(),
    underground: expectedUnderground(made.request),
    markers: PARITY_MARKERS.map((marker) => [
      marker.layer === 'entrances' ? 0 : 1,
      marker.id,
      marker.lon,
      marker.lat,
      round(parityGround(marker.lon, marker.lat), 3),
    ]),
    photos: PARITY_PHOTOS,
    look,
    pointed: expectedPointed(made.schedule),
    frames: expectedCameras(made.request, expectedReliefs(made.schedule)),
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
