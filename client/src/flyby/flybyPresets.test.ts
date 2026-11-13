// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { eyeLookingAt, metersBetween, type Camera3DEye } from '../scene3d/camera3d.ts';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { flybyHoldsPlace, flybyProblems, normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';
import { buildPreset, viewsOfDocument, type FlybyCapturedView } from './flybyPresets.ts';
import { resolveSubject, type FlybyResolvedSubject, type FlybyWorld } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt, flybyKnotAt, type FlybyTimeline } from './flybyTimeline.ts';

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
