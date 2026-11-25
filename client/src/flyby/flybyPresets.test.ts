// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { eyeLookingAt, metersBetween, movedBy, type Camera3DEye } from '../scene3d/camera3d.ts';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { flybyHoldsPlace, flybyProblems, normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';
import { buildPreset, DEFAULT_FLYBY_PRESET_OPTIONS, viewsOfDocument, type FlybyCapturedView } from './flybyPresets.ts';
import { resolveSubject, type FlybyResolvedSubject, type FlybyWorld } from './flybySubjects.ts';
import { flybyTextsAt } from './flybyTexts.ts';
import { compileFlyby, flybyCameraAt, flybyKnotAt, flybyLineFallbacks, type FlybyTimeline } from './flybyTimeline.ts';

// The ready-made flights added on top of the first five, proved the way the path arithmetic is
// proved: the camera is where the flight says at the moments that matter, it never jumps between
// two frames, it comes to rest at the end, and the same moment always gives the same picture.

const flat = () => 0;

function eyeOf(camera: Scene3DCameraState): Camera3DEye {
  return { lon: camera.longitude, lat: camera.latitude, height: camera.height };
}

const world = (ground = 0): FlybyWorld => ({
  caveInView: () => undefined,
  caveFacts: async () => undefined,
  featureBounds: async () => undefined,
  groundHeight: () => ground,
  viewTarget: () => undefined,
});

async function resolveAll(document: FlybyDocument, on: FlybyWorld): Promise<(FlybyResolvedSubject | undefined)[]> {
  return Promise.all(document.subjects.map((subject) => resolveSubject(subject, on)));
}

/** Eye steps between frames a thirtieth of a second apart, over the whole flight. */
function frameSteps(timeline: FlybyTimeline, ground: (lon: number, lat: number) => number = flat): number[] {
  const step = 1 / 30;
  const steps: number[] = [];
  let previous = eyeOf(flybyCameraAt(timeline, 0, ground));
  for (let t = step; t <= timeline.length + 1e-9; t += step) {
    const eye = eyeOf(flybyCameraAt(timeline, t, ground));
    steps.push(metersBetween(previous, eye));
    previous = eye;
  }
  return steps;
}

/** No frame-to-frame jump anywhere: the largest step is a small multiple of the typical one. */
function expectNoJump(steps: number[]) {
  const moving = steps.filter((step) => step > 1e-6).sort((a, b) => a - b);
  const median = moving[Math.floor(moving.length / 2)];
  expect(Math.max(...steps)).toBeLessThan(median * 12);
}

describe('a flight through views its author captured', () => {
  const views: FlybyCapturedView[] = [
    // Over a ridge, looking north-west-ish at a point on the ground.
    { target: { longitude: 25, latitude: 45.5, height: 1200 }, distance: 1500, heading: 350, pitch: -35, underground: false, seconds: 6 },
    // Across the valley, having turned the short way through north.
    { target: { longitude: 25.02, latitude: 45.51, height: 1100 }, distance: 900, heading: 10, pitch: -25, underground: false, seconds: 8 },
    // Inside a cave, close to a wall.
    { target: { longitude: 25.021, latitude: 45.5105, height: 950 }, distance: 12, heading: 200, pitch: -10, underground: true, seconds: 5 },
  ];
  const document = buildPreset('myViews', [], undefined, 'Views', views);

  it('keeps the views as the places they looked at, and says the flyby holds places', () => {
    expect(document.subjects).toHaveLength(3);
    expect(document.subjects[2]).toEqual({ kind: 'place', longitude: 25.021, latitude: 45.5105, height: 950, radiusM: 12 });
    expect(flybyHoldsPlace(document)).toBe(true);
    expect(flybyProblems(document)).toEqual([]);
    // Written to a file and read back, it is the same flight, and its views can be edited again.
    const read = normalizeFlybyDocument(JSON.parse(JSON.stringify(document)));
    expect(read).toEqual(document);
    expect(viewsOfDocument(read!)).toEqual(views.map((view, index) => (index === 0 ? { ...view, seconds: 6 } : view)));
  });

  it('stands the camera exactly where each view was taken, at the moment the flight reaches it', async () => {
    const timeline = compileFlyby(document, await resolveAll(document, world(0)));
    expect(timeline.length).toBe(13);
    const moments = [0, 8, 13];
    views.forEach((view, index) => {
      const camera = flybyCameraAt(timeline, moments[index], flat);
      const eye = eyeLookingAt({ lon: view.target.longitude, lat: view.target.latitude, height: view.target.height }, view.heading, view.pitch, view.distance);
      expect(metersBetween(eyeOf(camera), eye)).toBeLessThan(0.5);
      expect(camera.heading).toBeCloseTo(view.heading, 6);
      expect(camera.pitch).toBeCloseTo(view.pitch, 6);
    });
  });

  it('flies from one view to the next as one curve, the short way round, and comes to rest on the last', async () => {
    const timeline = compileFlyby(document, await resolveAll(document, world(0)));
    expectNoJump(frameSteps(timeline));
    // From 350° to 10° is twenty degrees through north, never three hundred and forty the other way.
    for (let t = 0; t <= 8; t += 0.25) {
      const heading = flybyKnotAt(timeline, t).heading;
      expect(heading).toBeGreaterThanOrEqual(349.999);
      expect(heading).toBeLessThanOrEqual(370.001);
    }
    const steps = frameSteps(timeline);
    const median = [...steps].sort((a, b) => a - b)[Math.floor(steps.length / 2)];
    expect(steps[steps.length - 1]).toBeLessThan(median * 0.2);
    // Every moment is a function of its time alone.
    const again = compileFlyby(document, await resolveAll(document, world(0)));
    expect(flybyCameraAt(again, 9.7, flat)).toEqual(flybyCameraAt(timeline, 9.7, flat));
  });

  it('keeps above the ground between views taken above it, and may go under it for a view taken there', async () => {
    const ground = () => 1150;
    const timeline = compileFlyby(document, await resolveAll(document, world(1150)));
    for (let t = 0; t <= 8; t += 0.5) {
      expect(flybyCameraAt(timeline, t, ground).height).toBeGreaterThanOrEqual(1150 + 10 - 1e-6);
    }
    expect(flybyCameraAt(timeline, 13, ground).height).toBeLessThan(1150);
  });

  it('holds a single view still, and builds nothing from no views', () => {
    const one = buildPreset('myViews', [], undefined, '', views.slice(0, 1));
    expect(one.sections.map((section) => section.kind)).toEqual(['hold']);
    expect(flybyProblems(buildPreset('myViews', [], undefined, '', []))).toContainEqual({ code: 'noSections' });
  });
});

describe('a descent down a cave\'s survey', () => {
  // The same invented survey as the route tests', a little longer: down a passage that turns.
  const stations: [number, number, number][] = [
    [25, 45.5, 1000],
    [25.0004, 45.5002, 985],
    [25.0008, 45.5006, 960],
    [25.0011, 45.5011, 930],
    [25.0016, 45.5013, 900],
    [25.0022, 45.5012, 870],
    [25.0026, 45.5008, 840],
    [25.0027, 45.5003, 820],
  ];
  const route = stations.map(([lon, lat, height]) => ({ lon, lat, height }));
  const cave: FlybyResolvedSubject = {
    centre: { lon: 25.00135, lat: 45.50065, height: 910 },
    radius: 150,
    halfHeight: 90,
    top: route[0],
    bottom: route.at(-1)!,
    coarse: false,
    route,
  };
  const document = buildPreset('descent', [{ kind: 'cave', caveId: 'a' }]);
  const timeline = compileFlyby(document, [cave]);
  const stretch = timeline.stretches[0];

  /** How far a point is from the drawn route, in metres. */
  function offRoute(point: Camera3DEye): number {
    let nearest = Number.POSITIVE_INFINITY;
    for (let i = 1; i < route.length; i += 1) {
      for (let k = 0; k <= 50; k += 1) {
        const p = k / 50;
        const on = {
          lon: route[i - 1].lon + (route[i].lon - route[i - 1].lon) * p,
          lat: route[i - 1].lat + (route[i].lat - route[i - 1].lat) * p,
          height: route[i - 1].height + (route[i].height - route[i - 1].height) * p,
        };
        nearest = Math.min(nearest, metersBetween(on, point));
      }
    }
    return nearest;
  }

  it('is built as approach, descent, a moment at the bottom, and back up', () => {
    expect(document.sections.map((section) => section.kind)).toEqual(['approach', 'descent', 'hold', 'rise']);
    expect(flybyProblems(document)).toEqual([]);
    // Nothing of the survey is in the document: the way down is found when it is prepared.
    expect(JSON.stringify(document)).not.toContain('45.5');
    expect(flybyHoldsPlace(document)).toBe(false);
  });

  it('follows the survey from its highest point to its deepest, a little behind and above the point followed', () => {
    expect(timeline.stretches).toHaveLength(1);
    const span = timeline.spans[1];
    // A fifth of the section flies to where the line begins.
    expect(stretch.start).toBeCloseTo(span.start + 6);
    expect(stretch.end).toBeCloseTo(span.end);
    for (let t = stretch.start; t <= stretch.end; t += 0.5) {
      const knot = flybyKnotAt(timeline, t);
      const point = movedBy(timeline.origin, knot.target);
      // Smoothed over about the camera's distance, never far off the passage.
      expect(offRoute(point)).toBeLessThan(stretch.distance);
      const camera = flybyCameraAt(timeline, t, () => 1005);
      // Above the point followed, and behind it: the eye stands against the way it faces.
      expect(camera.height).toBeGreaterThan(point.height);
      const east = (point.lon - camera.longitude) * Math.cos((point.lat * Math.PI) / 180);
      const north = point.lat - camera.latitude;
      const facing = ((Math.atan2(east, north) * 180) / Math.PI + 360) % 360;
      const turn = Math.abs(((facing - camera.heading + 540) % 360) - 180);
      expect(turn).toBeLessThan(1);
    }
    // At its two ends it is at the two ends of the route.
    const first = flybyCameraAt(timeline, stretch.start, () => 1005);
    const last = flybyCameraAt(timeline, stretch.end, () => 1005);
    expect(metersBetween(eyeOf(first), eyeLookingAt(route[0], first.heading, -30, stretch.distance))).toBeLessThan(0.5);
    expect(metersBetween(eyeOf(last), eyeLookingAt(route.at(-1)!, last.heading, -30, stretch.distance))).toBeLessThan(0.5);
  });

  it('looks along the passage: where the passage turns east, the camera faces east', () => {
    // The passage between the fifth and the seventh station runs east and then south-east.
    const middle = flybyKnotAt(timeline, stretch.start + (stretch.end - stretch.start) * 0.72);
    const facing = ((middle.heading % 360) + 360) % 360;
    expect(facing).toBeGreaterThan(60);
    expect(facing).toBeLessThan(170);
  });

  it('joins the flight before and after it with no jump, at rest at both joins, and rests at the end', () => {
    expectNoJump(frameSteps(timeline, () => 1005));
    const steps = frameSteps(timeline, () => 1005);
    const median = [...steps].sort((a, b) => a - b)[Math.floor(steps.length / 2)];
    const near = (t: number) =>
      metersBetween(eyeOf(flybyCameraAt(timeline, t, () => 1005)), eyeOf(flybyCameraAt(timeline, t + 1 / 30, () => 1005)));
    for (const join of [stretch.start, stretch.end]) {
      expect(near(join - 1 / 30)).toBeLessThan(median * 0.2);
      expect(near(join)).toBeLessThan(median * 0.2);
    }
    // The knot on either side of a join and the line agree on where the camera is.
    for (const join of [stretch.start, stretch.end]) {
      const before = flybyKnotAt(timeline, join - 1e-6);
      const after = flybyKnotAt(timeline, join + 1e-6);
      expect(Math.hypot(before.target.east - after.target.east, before.target.north - after.target.north, before.target.up - after.target.up)).toBeLessThan(0.01);
      expect(Math.abs(before.heading - after.heading)).toBeLessThan(0.01);
    }
    // The heading never swings faster than a slow pan.
    for (let t = stretch.start; t < stretch.end; t += 1 / 30) {
      const turn = Math.abs(flybyKnotAt(timeline, t + 1 / 30).heading - flybyKnotAt(timeline, t).heading);
      expect(turn).toBeLessThan(4);
    }
    expect(flybyCameraAt(compileFlyby(document, [cave]), 21.3, () => 1005)).toEqual(flybyCameraAt(timeline, 21.3, () => 1005));
  });

  it('dives straight down instead when the survey gives no way down, and says which section did', () => {
    const unjoined = { ...cave, route: undefined };
    const diving = compileFlyby(document, [unjoined]);
    expect(diving.stretches).toEqual([]);
    expect(flybyLineFallbacks(document, [unjoined])).toEqual(['descent-2']);
    expect(flybyLineFallbacks(document, [cave])).toEqual([]);
    // At the end of the section the camera looks at the deepest point from close by.
    const end = flybyCameraAt(diving, diving.spans[1].end, () => 1005);
    expect(metersBetween(eyeOf(end), eyeLookingAt(cave.bottom!, end.heading, -30, 0.15 * 150))).toBeLessThan(0.5);
  });
});

describe('a flight along an imported file\'s track', () => {
  // An invented walk of about four kilometres, wandering a little as a GPS track does.
  const track = Array.from({ length: 120 }, (_, i) => ({
    longitude: 25 + i * 0.0004 + 0.00003 * Math.sin(i * 1.7),
    latitude: 45.5 + 0.004 * Math.sin(i / 25) + 0.00002 * Math.cos(i * 2.3),
  }));
  const ground = (longitude: number, latitude: number) => 900 + 150 * Math.sin((longitude - 25) * 120) + 80 * Math.cos((latitude - 45.5) * 300);
  const withFile: FlybyWorld = {
    ...world(0),
    groundHeight: ground,
    fileLines: async () => ({
      bounds: [25, 45.496, 25.048, 45.504],
      lines: [track, [track[0], track[1]]],
    }),
  };
  const document = buildPreset('alongLine', [{ kind: 'file', geofileId: 'walk' }]);

  it('is about the file by its id alone, and is built as along the line and back up', async () => {
    expect(document.sections.map((section) => section.kind)).toEqual(['follow', 'rise']);
    expect(document.subjects).toEqual([{ kind: 'file', geofileId: 'walk' }]);
    expect(flybyHoldsPlace(document)).toBe(false);
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))).toEqual(document);
  });

  it('follows the track at a height above the ground, looking ahead along it', async () => {
    const resolved = await resolveAll(document, withFile);
    expect(resolved[0]?.route).toHaveLength(120);
    const timeline = compileFlyby(document, resolved);
    const stretch = timeline.stretches[0];
    expect(stretch.distance).toBe(250);
    for (let t = stretch.start + 0.5; t < stretch.end; t += 1) {
      const knot = flybyKnotAt(timeline, t);
      const followed = movedBy(timeline.origin, knot.target);
      // On the track, give or take the smoothing of its wander.
      const nearest = Math.min(...track.map((point) => metersBetween({ lon: point.longitude, lat: point.latitude, height: 0 }, { ...followed, height: 0 })));
      expect(nearest).toBeLessThan(40);
      const camera = flybyCameraAt(timeline, t, ground);
      // Above the ground under it by at least the clearance asked for.
      expect(camera.height - ground(camera.longitude, camera.latitude)).toBeGreaterThanOrEqual(60 - 1e-6);
      // Facing where the track goes next: a point a little further along is ahead of the camera.
      const ahead = movedBy(timeline.origin, flybyKnotAt(timeline, Math.min(t + 2, stretch.end)).target);
      const east = (ahead.lon - camera.longitude) * Math.cos((ahead.lat * Math.PI) / 180);
      const north = ahead.lat - camera.latitude;
      const bearing = ((Math.atan2(east, north) * 180) / Math.PI + 360) % 360;
      expect(Math.abs(((bearing - camera.heading + 540) % 360) - 180)).toBeLessThan(25);
    }
  });

  it('flies the whole of it as one curve with no jump, and rests at the end', async () => {
    const timeline = compileFlyby(document, await resolveAll(document, withFile));
    const steps = frameSteps(timeline, ground);
    expectNoJump(steps);
    const median = [...steps].sort((a, b) => a - b)[Math.floor(steps.length / 2)];
    expect(steps[steps.length - 1]).toBeLessThan(median * 0.2);
    const again = compileFlyby(document, await resolveAll(document, withFile));
    expect(flybyCameraAt(again, 31.4, ground)).toEqual(flybyCameraAt(timeline, 31.4, ground));
  });

  it('flies straight to a file that holds no line, and says which section could not follow one', async () => {
    const pointsOnly: FlybyWorld = { ...withFile, fileLines: async () => ({ bounds: [25, 45.496, 25.048, 45.504], lines: [] }) };
    const resolved = await resolveAll(document, pointsOnly);
    expect(resolved[0]?.route).toBeUndefined();
    expect(compileFlyby(document, resolved).stretches).toEqual([]);
    expect(flybyLineFallbacks(document, resolved)).toEqual(['follow-1']);
  });
});

// ---- from far away: the camera's real direction on the real (ellipsoidal) Earth ----

const WGS84_A = 6_378_137;
const WGS84_F = 1 / 298.257223563;
const WGS84_E2 = WGS84_F * (2 - WGS84_F);
const radians = (degrees: number) => (degrees * Math.PI) / 180;

/** A point as the scene's engine places it: earth-centred metres. */
function earthCentred(lon: number, lat: number, height: number): [number, number, number] {
  const phi = radians(lat);
  const lambda = radians(lon);
  const n = WGS84_A / Math.sqrt(1 - WGS84_E2 * Math.sin(phi) ** 2);
  return [
    (n + height) * Math.cos(phi) * Math.cos(lambda),
    (n + height) * Math.cos(phi) * Math.sin(lambda),
    (n * (1 - WGS84_E2) + height) * Math.sin(phi),
  ];
}

/**
 * The angle, in degrees, between where a camera points — its heading and pitch taken in the frame
 * of the ground under the eye, as the engine takes them — and the point it is meant to look at.
 * Nothing here is the flat patch of ground the path is worked out on: it is the check of it.
 */
function offCentre(camera: Scene3DCameraState, looked: Camera3DEye): number {
  const phi = radians(camera.latitude);
  const lambda = radians(camera.longitude);
  const east = [-Math.sin(lambda), Math.cos(lambda), 0];
  const north = [-Math.sin(phi) * Math.cos(lambda), -Math.sin(phi) * Math.sin(lambda), Math.cos(phi)];
  const up = [Math.cos(phi) * Math.cos(lambda), Math.cos(phi) * Math.sin(lambda), Math.sin(phi)];
  const h = radians(camera.heading);
  const p = radians(camera.pitch);
  const look = [0, 1, 2].map((i) => east[i] * Math.sin(h) * Math.cos(p) + north[i] * Math.cos(h) * Math.cos(p) + up[i] * Math.sin(p));
  const eye = earthCentred(camera.longitude, camera.latitude, camera.height);
  const point = earthCentred(looked.lon, looked.lat, looked.height);
  const toward = [0, 1, 2].map((i) => point[i] - eye[i]);
  const length = Math.hypot(...toward);
  const cos = (look[0] * toward[0] + look[1] * toward[1] + look[2] * toward[2]) / length;
  return (Math.acos(Math.min(1, cos)) * 180) / Math.PI;
}

describe('a flight from far away', () => {
  const small: FlybyResolvedSubject = { centre: { lon: 25.3, lat: 45.45, height: 1100 }, radius: 200, halfHeight: 60, coarse: false };
  const massif: FlybyResolvedSubject = { centre: { lon: 23.1, lat: 46.6, height: 1200 }, radius: 8000, halfHeight: 400, coarse: false };
  const cases = [
    { name: 'a cave', preset: 'aroundCave' as const, subject: small, subjects: [{ kind: 'cave' as const, caveId: 'a' }] },
    { name: 'a massif', preset: 'aroundArea' as const, subject: massif, subjects: [{ kind: 'feature' as const, featureId: 'm' }] },
  ];

  for (const { name, preset, subject, subjects } of cases) {
    it(`keeps ${name} in the middle of the picture at every distance, from the scale of the country down`, () => {
      const document = buildPreset(preset, subjects, { ...DEFAULT_FLYBY_PRESET_OPTIONS, fromFar: true });
      expect(document.sections[0].kind).toBe('fromFar');
      expect(flybyProblems(document)).toEqual([]);
      const timeline = compileFlyby(document, [subject]);
      const far = timeline.spans[0];
      const opening = flybyCameraAt(timeline, 0, flat);
      expect(opening.height - subject.centre.height).toBeGreaterThan(340_000);
      let worst = 0;
      for (let t = 0; t <= far.end + 4; t += 0.05) {
        const knot = flybyKnotAt(timeline, t);
        const camera = flybyCameraAt(timeline, t, flat);
        const looked = movedBy(timeline.origin, knot.target);
        worst = Math.max(worst, offCentre(camera, looked));
        // Straight down, or as near as makes no difference, for as long as it is far.
        if (knot.distance > 60_000) {
          expect(camera.pitch).toBeLessThan(-89.5);
        }
      }
      // A tenth of a degree is a few pixels in the middle of a picture sixty degrees wide.
      expect(worst).toBeLessThan(0.15);
    });
  }

  it('comes down all the way without climbing back, and on into the flight as it was', () => {
    const plain = buildPreset('aroundCave', cases[0].subjects);
    const document = buildPreset('aroundCave', cases[0].subjects, { ...DEFAULT_FLYBY_PRESET_OPTIONS, fromFar: true });
    expect(document.sections.slice(1)).toEqual(plain.sections);
    const timeline = compileFlyby(document, [small]);
    const plainTimeline = compileFlyby(plain, [small]);
    // Past the approach — which now flies on from the descent instead of starting from still — the
    // flight is the one without it, fourteen seconds later.
    for (const t of [7, 15, 25]) {
      expect(flybyCameraAt(timeline, t + 14, flat).longitude).toBeCloseTo(flybyCameraAt(plainTimeline, t, flat).longitude, 9);
    }
    // Closer at every moment of the descent than at the one before.
    for (let t = 0.1; t <= 14; t += 0.1) {
      expect(flybyKnotAt(timeline, t).distance).toBeLessThan(flybyKnotAt(timeline, t - 0.1).distance);
    }
    // Its speed falls by hundreds of times on the way down, so it is held to change gently from
    // each frame to the next rather than to stay near a typical speed: no lurch anywhere.
    // (Past its first half second, where it sets off from still and every frame is a whole
    // multiple of the first; and up to the circle, which slows to a stop the same way at its end.)
    const steps = frameSteps(timeline);
    for (let i = 15; i < 30 * 30; i += 1) {
      if (steps[i] > 0.05 && steps[i - 1] > 0.05) {
        expect(steps[i] / steps[i - 1]).toBeLessThan(1.3);
        expect(steps[i - 1] / steps[i]).toBeLessThan(1.3);
      }
    }
  });
});

describe('a tour that stops at each cave', () => {
  const caves = [
    { kind: 'cave' as const, caveId: 'a' },
    { kind: 'cave' as const, caveId: 'b' },
  ];
  const resolved: FlybyResolvedSubject[] = [
    { centre: { lon: 25, lat: 45.5, height: 900 }, radius: 200, halfHeight: 50, coarse: false, about: { name: 'Peștera A', lengthM: 1200, depthM: 85 } },
    { centre: { lon: 25.05, lat: 45.52, height: 1000 }, radius: 300, halfHeight: 60, coarse: false, about: { name: 'Peștera B' } },
  ];

  it('stops nowhere and says nothing unless asked', () => {
    const plain = buildPreset('tour', caves);
    expect(plain.sections.some((section) => section.say !== undefined)).toBe(false);
    expect(plain.sections.filter((section) => section.kind === 'hold')).toEqual([]);
    expect(compileFlyby(plain, resolved).texts.captions).toEqual([]);
  });

  it('holds still over each cave for the time given, the circle slowing into it', () => {
    const document = buildPreset('tour', caves, { ...DEFAULT_FLYBY_PRESET_OPTIONS, pauseSeconds: 5 });
    expect(document.sections.map((section) => section.kind)).toEqual(['approach', 'orbit', 'hold', 'hop', 'orbit', 'hold', 'rise']);
    const timeline = compileFlyby(document, resolved);
    const pause = timeline.spans[2];
    expect(pause.end - pause.start).toBe(5);
    const held = flybyCameraAt(timeline, pause.start, flat);
    expect(flybyCameraAt(timeline, pause.start + 2.5, flat)).toEqual({ ...held });
    expect(flybyCameraAt(timeline, pause.end, flat)).toEqual({ ...held });
    expectNoJump(frameSteps(timeline));
  });

  it('writes the cave\'s name, and its size when asked, over the pause — from what was read, not from the document', () => {
    const named = buildPreset('tour', caves, { ...DEFAULT_FLYBY_PRESET_OPTIONS, pauseSeconds: 5, pauseSay: 'nameAndSize' });
    // The document says what to say, never the name itself.
    expect(JSON.stringify(named)).not.toContain('Peștera');
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(named)))).toEqual(named);
    const words = (about: { name?: string; lengthM?: number; depthM?: number }, say: string) =>
      about.name && (say === 'name' ? about.name : `${about.name} (${about.lengthM ?? '?'} m, ${about.depthM ?? '?'} m)`);
    const timeline = compileFlyby(named, resolved, words);
    const pause = timeline.spans[2];
    expect(timeline.texts.captions).toEqual([
      { text: 'Peștera A (1200 m, 85 m)', start: pause.start, end: pause.end },
      { text: 'Peștera B (? m, ? m)', start: timeline.spans[5].start, end: timeline.spans[5].end },
    ]);
    expect(flybyTextsAt(timeline.texts, pause.start + 2.5).caption?.text).toBe('Peștera A (1200 m, 85 m)');
    // A cave whose name could not be read says nothing.
    const unnamed = compileFlyby(named, [{ ...resolved[0], about: undefined }, resolved[1]], words);
    expect(unnamed.texts.captions.map((caption) => caption.text)).toEqual(['Peștera B (? m, ? m)']);
  });
});

describe('the north arrow of a flight', () => {
  it('points where north is in the picture at every moment, and is kept with the flyby', () => {
    const document = { ...buildPreset('aroundCave', [{ kind: 'cave', caveId: 'a' }]), texts: { northArrow: true, corner: 'Club' } };
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))).toEqual(document);
    const timeline = compileFlyby(document, [
      { centre: { lon: 25, lat: 45.5, height: 900 }, radius: 200, halfHeight: 50, coarse: false },
    ]);
    for (const t of [0, 3, 9.5, 17, timeline.length]) {
      const north = flybyTextsAt(timeline.texts, t).north!;
      expect(Math.abs(north - flybyCameraAt(timeline, t, flat).heading)).toBeLessThanOrEqual(0.05);
    }
  });
});
