// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { metersBetween, type Camera3DEye } from '../scene3d/camera3d.ts';
import { FLYBY_VERSION, type FlybyDocument, type FlybySection } from './flybyDocument.ts';
import { buildPreset, DEFAULT_FLYBY_PRESET_OPTIONS } from './flybyPresets.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import {
  compileFlyby,
  flybyCameraAt,
  flybyKnotAt,
  flybySectionAt,
  FlybySubjectMissing,
} from './flybyTimeline.ts';

const cave: FlybyResolvedSubject = {
  centre: { lon: 25, lat: 45.5, height: 900 },
  radius: 200,
  halfHeight: 100,
  coarse: false,
};
const other: FlybyResolvedSubject = {
  centre: { lon: 25.05, lat: 45.52, height: 1100 },
  radius: 300,
  halfHeight: 60,
  coarse: false,
};

const flat = () => 0;

function flyby(sections: FlybySection[], overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [
      { kind: 'cave', caveId: 'a' },
      { kind: 'cave', caveId: 'b' },
    ],
    start: { subject: 0, pose: { heading: 0, pitch: -30, range: 5, lift: 0, clearance: null } },
    sections,
    ...overrides,
  };
}

function eyeOf(camera: { longitude: number; latitude: number; height: number }): Camera3DEye {
  return { lon: camera.longitude, lat: camera.latitude, height: camera.height };
}

describe('the path a flyby describes', () => {
  it('opens on the starting pose and puts each section on the clock at its own speed', () => {
    const document = flyby(
      [
        { id: 'a', kind: 'approach', subject: 0, seconds: 6, to: { range: 2 } },
        { id: 'b', kind: 'orbit', subject: 0, seconds: 20, speed: 2, to: { headingBy: 360 } },
      ],
      { speed: 2 },
    );
    const timeline = compileFlyby(document, [cave, other]);
    expect(timeline.spans).toEqual([
      { id: 'a', start: 0, end: 3 },
      { id: 'b', start: 3, end: 8 },
    ]);
    expect(timeline.length).toBe(8);
    const opening = flybyKnotAt(timeline, 0);
    expect(opening.distance).toBeCloseTo(1000);
    expect(opening.heading).toBe(0);
    expect(opening.pitch).toBe(-30);
    // The eye stands a thousand metres from the cave's middle, to its south, looking north.
    const camera = flybyCameraAt(timeline, 0, flat);
    expect(metersBetween(eyeOf(camera), cave.centre)).toBeCloseTo(1000, 0);
    expect(camera.latitude).toBeLessThan(cave.centre.lat);
    expect(flybySectionAt(timeline, 1)).toBe(0);
    expect(flybySectionAt(timeline, 3)).toBe(1);
    expect(flybySectionAt(timeline, 99)).toBe(1);
  });

  it('gives the same camera for the same moment, whatever was asked before', () => {
    const timeline = compileFlyby(buildPreset('tour', flyby([]).subjects), [cave, other]);
    const first = flybyCameraAt(timeline, 17.3, flat);
    flybyCameraAt(timeline, 40, flat);
    flybyCameraAt(timeline, 2, flat);
    expect(flybyCameraAt(timeline, 17.3, flat)).toEqual(first);
  });

  it('flies on through the joins between sections, and comes to rest at the end', () => {
    const timeline = compileFlyby(buildPreset('aroundCave', [{ kind: 'cave', caveId: 'a' }]), [cave]);
    const step = 1 / 30;
    const speeds: number[] = [];
    for (let t = step; t <= timeline.length; t += step) {
      speeds.push(metersBetween(eyeOf(flybyCameraAt(timeline, t, flat)), eyeOf(flybyCameraAt(timeline, t - step, flat))));
    }
    // No frame-to-frame jump anywhere: the largest step is a small multiple of the typical one.
    const sorted = [...speeds].sort((a, b) => a - b);
    const median = sorted[Math.floor(sorted.length / 2)];
    expect(Math.max(...speeds)).toBeLessThan(median * 12);
    // Moving at the join between the approach and the orbit, still at the very end.
    const join = Math.round(timeline.spans[0].end / step);
    expect(speeds[join]).toBeGreaterThan(median * 0.2);
    expect(speeds[speeds.length - 1]).toBeLessThan(median * 0.2);
  });

  it('turns the way and as far as it was asked, at an even rate', () => {
    const document = flyby([{ id: 'o', kind: 'orbit', subject: 0, seconds: 40, to: { headingBy: -720 } }]);
    const timeline = compileFlyby(document, [cave, other]);
    let previous = flybyKnotAt(timeline, 0).heading;
    const steps: number[] = [];
    for (let t = 1; t <= 40; t += 1) {
      const heading = flybyKnotAt(timeline, t).heading;
      expect(heading).toBeLessThan(previous);
      steps.push(previous - heading);
      previous = heading;
    }
    expect(previous).toBeCloseTo(-720);
    // The middle of the turn is flown at the asked rate, not hurried through.
    const middle = steps.slice(10, 30);
    expect(Math.max(...middle)).toBeLessThan(18 * 1.25);
    expect(Math.min(...middle)).toBeGreaterThan(18 * 0.75);
  });

  it('closes in by equal proportions over equal times', () => {
    const document = flyby([{ id: 'a', kind: 'approach', subject: 0, seconds: 10, to: { range: 0.05 } }]);
    const timeline = compileFlyby(document, [cave, other]);
    // Half-way through, the distance is the geometric middle of its ends, not their average.
    expect(flybyKnotAt(timeline, 5).distance).toBeCloseTo(Math.sqrt(1000 * 10), 0);
  });

  it('does not swing past straight down when the tilt turns round', () => {
    const document = flyby([
      { id: 'a', kind: 'tilt', subject: 0, seconds: 4, to: { pitch: -89 } },
      { id: 'b', kind: 'tilt', subject: 0, seconds: 4, to: { pitch: -20 } },
    ]);
    const timeline = compileFlyby(document, [cave, other]);
    for (let t = 0; t <= 8; t += 0.1) {
      expect(flybyKnotAt(timeline, t).pitch).toBeGreaterThanOrEqual(-89.0001);
    }
  });

  it('hops to another subject over the top, standing back far enough to see both', () => {
    const document = flyby([{ id: 'h', kind: 'hop', subject: 1, seconds: 8, to: { range: 3 } }]);
    const timeline = compileFlyby(document, [cave, other]);
    const separation = metersBetween(cave.centre, other.centre);
    const top = flybyKnotAt(timeline, 4);
    expect(top.distance).toBeGreaterThanOrEqual(separation * 0.9 - 1);
    expect(top.pitch).toBeLessThanOrEqual(-40);
    // And arrives looking at the second subject from three of its radii.
    const end = flybyKnotAt(timeline, 8);
    expect(end.distance).toBeCloseTo(900);
    const camera = flybyCameraAt(timeline, 8, flat);
    expect(metersBetween(eyeOf(camera), other.centre)).toBeCloseTo(900, 0);
  });

  it('keeps the eye above the ground until the limit is lifted, and then descends rather than drops', () => {
    const ground = () => 1000;
    const document = flyby(
      [
        { id: 'o', kind: 'orbit', subject: 0, seconds: 10, to: { headingBy: 90 } },
        { id: 'd', kind: 'dive', subject: 0, seconds: 10, to: { range: 0.5, lift: -1, clearance: null } },
      ],
      { start: { subject: 0, pose: { heading: 0, pitch: -5, range: 2, lift: 0, clearance: 80 } } },
    );
    const timeline = compileFlyby(document, [cave, other]);
    // The cave's middle is at 900 m under ground at 1000 m: a nearly level look at it would stand
    // the eye inside the hillside, so it is held 80 m above the ground and tilts down instead.
    for (const t of [0, 5, 10]) {
      const camera = flybyCameraAt(timeline, t, ground);
      expect(camera.height).toBeCloseTo(1080);
      expect(camera.pitch).toBeLessThan(-5);
    }
    const heights = [10, 12, 14, 16, 18, 20].map((t) => flybyCameraAt(timeline, t, ground).height);
    for (let i = 1; i < heights.length; i += 1) {
      expect(heights[i]).toBeLessThan(heights[i - 1]);
    }
    // No drop on the first frame after the limit starts to lift.
    expect(1080 - flybyCameraAt(timeline, 10 + 1 / 30, ground).height).toBeLessThan(1);
    // At the bottom the eye is underground, looking at the cave's lowest point.
    expect(heights[heights.length - 1]).toBeLessThan(1000);
  });

  it('looks at a subject raised by its own height, not by its width', () => {
    const document = flyby([], {
      start: { subject: 0, pose: { heading: 0, pitch: -90, range: 1, lift: 1, clearance: null } },
      sections: [{ id: 'h', kind: 'hold', subject: 0, seconds: 1, to: {} }],
    });
    const timeline = compileFlyby(document, [cave, other]);
    // Straight above the top of the cave (900 + 100), one radius (200 m) up.
    expect(flybyCameraAt(timeline, 0, flat).height).toBeCloseTo(1200);
  });

  it('looks at the places on a cave when told to, and at the ends of its middle when it has none', () => {
    const withPlaces: FlybyResolvedSubject = {
      ...cave,
      top: { lon: 25.001, lat: 45.501, height: 1000 },
      bottom: { lon: 24.998, lat: 45.499, height: 800 },
    };
    const document = flyby([{ id: 'd', kind: 'dive', subject: 0, seconds: 4, to: { aim: 'bottom', pitch: -90, range: 0.5 } }], {
      start: { subject: 0, pose: { aim: 'top', heading: 0, pitch: -90, range: 0.5, lift: 0, clearance: null } },
    });
    // Straight above its highest place, then straight above its deepest: not above its middle.
    const placed = compileFlyby(document, [withPlaces, other]);
    const opening = flybyCameraAt(placed, 0, flat);
    expect(opening.longitude).toBeCloseTo(25.001, 5);
    expect(opening.latitude).toBeCloseTo(45.501, 5);
    expect(opening.height).toBeCloseTo(1100);
    const end = flybyCameraAt(placed, 4, flat);
    expect(end.longitude).toBeCloseTo(24.998, 5);
    expect(end.latitude).toBeCloseTo(45.499, 5);
    expect(end.height).toBeCloseTo(900);
    // The aim is carried on by a section that does not name one.
    const carried = compileFlyby(
      { ...document, sections: [...document.sections, { id: 'h', kind: 'hold', subject: 0, seconds: 2, to: {} }] },
      [withPlaces, other],
    );
    expect(flybyCameraAt(carried, 6, flat).longitude).toBeCloseTo(24.998, 5);

    // A subject that is only a box: the top and the bottom of its middle.
    const boxed = compileFlyby(document, [cave, other]);
    expect(flybyCameraAt(boxed, 0, flat).longitude).toBeCloseTo(25, 5);
    expect(flybyCameraAt(boxed, 0, flat).height).toBeCloseTo(1000 + 100);
    expect(flybyCameraAt(boxed, 4, flat).height).toBeCloseTo(800 + 100);
  });

  it('flies around every subject as one when asked for all', () => {
    const document = flyby([{ id: 'o', kind: 'orbit', subject: 'all', seconds: 10, to: { headingBy: 180 } }], {
      start: { subject: 'all', pose: { heading: 0, pitch: -45, range: 2, lift: 0, clearance: null } },
    });
    const timeline = compileFlyby(document, [cave, other]);
    const separation = metersBetween(cave.centre, other.centre);
    expect(flybyKnotAt(timeline, 0).distance).toBeGreaterThan(separation);
  });

  it('refuses a document whose subject was not found', () => {
    const document = flyby([{ id: 'h', kind: 'hop', subject: 1, seconds: 8, to: {} }]);
    expect(() => compileFlyby(document, [cave, undefined])).toThrow(FlybySubjectMissing);
    expect(() => compileFlyby(document, [undefined, other])).toThrow(FlybySubjectMissing);
  });

  it('builds every preset into a path with no moment undefined', () => {
    const subjects = flyby([]).subjects;
    for (const preset of ['aroundCave', 'aroundArea', 'aroundSeveral', 'tour', 'planToProfile'] as const) {
      const timeline = compileFlyby(
        buildPreset(preset, subjects, { ...DEFAULT_FLYBY_PRESET_OPTIONS, underground: true }),
        [cave, other],
      );
      expect(timeline.length).toBeGreaterThan(10);
      for (let t = 0; t <= timeline.length; t += 0.5) {
        const camera = flybyCameraAt(timeline, t, flat);
        for (const value of Object.values(camera)) {
          expect(Number.isFinite(value)).toBe(true);
        }
        expect(camera.pitch).toBeGreaterThanOrEqual(-90);
      }
    }
  });
});
