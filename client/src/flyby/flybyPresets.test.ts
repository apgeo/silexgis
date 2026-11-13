// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { eyeLookingAt, metersBetween, movedBy, type Camera3DEye } from '../scene3d/camera3d.ts';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { flybyHoldsPlace, flybyProblems, normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';
import { buildPreset, viewsOfDocument, type FlybyCapturedView } from './flybyPresets.ts';
import { resolveSubject, type FlybyResolvedSubject, type FlybyWorld } from './flybySubjects.ts';
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
    expect(steps[steps.length - 1]).toBeLessThan(median * 0.05);
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
    expect(stretch.start).toBeCloseTo(span.start + 3);
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
