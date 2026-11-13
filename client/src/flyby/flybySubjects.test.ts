// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { metersBetween } from '../scene3d/camera3d.ts';
import { FLYBY_VERSION, flybyProblems, type FlybyDocument, type FlybySubject } from './flybyDocument.ts';
import {
  buildPreset,
  DEFAULT_FLYBY_PRESET_OPTIONS,
  presetFits,
  presetFor,
} from './flybyPresets.ts';
import {
  boundsRadius,
  reorderSubjects,
  resolveSubject,
  shortestRoute,
  subjectFor,
  subjectsTogether,
  type FlybyResolvedSubject,
  type FlybyWorld,
} from './flybySubjects.ts';

function world(overrides: Partial<FlybyWorld> = {}): FlybyWorld {
  return {
    caveInView: () => undefined,
    caveFacts: async () => undefined,
    featureBounds: async () => undefined,
    groundHeight: () => 1000,
    viewTarget: () => undefined,
    ...overrides,
  };
}

function at(lon: number, lat: number, radius = 100): FlybyResolvedSubject {
  return { centre: { lon, lat, height: 0 }, radius, halfHeight: 10, coarse: false };
}

describe('finding what a flyby is about', () => {
  it('sizes a cave from what is drawn of it', async () => {
    const subject = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({
        // About 786 m east to west and 1113 m south to north at this latitude.
        caveInView: () => ({ caveId: 'c', bounds: [25, 45, 25.01, 45.01], top: 950, bottom: 650 }),
      }),
    );
    expect(subject?.coarse).toBe(false);
    expect(subject?.centre.lon).toBeCloseTo(25.005);
    expect(subject?.centre.lat).toBeCloseTo(45.005);
    expect(subject?.centre.height).toBe(800);
    expect(subject?.halfHeight).toBe(150);
    // As far as its furthest corner: half the diagonal of its box on the ground, and half its depth.
    expect(subject?.radius).toBeCloseTo(Math.hypot(boundsRadius([25, 45, 25.01, 45.01]), 150));
    expect(subject?.radius).toBeGreaterThan(600);
  });

  it('knows where on a cave its highest and its lowest drawn points are', async () => {
    const subject = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({
        caveInView: () => ({
          caveId: 'c',
          bounds: [25, 45, 25.01, 45.01],
          top: 950,
          bottom: 650,
          topAt: { longitude: 25.001, latitude: 45.002, height: 950 },
          bottomAt: { longitude: 25.009, latitude: 45.008, height: 650 },
        }),
      }),
    );
    // The deepest place is not under the middle of the box, and that is the point of knowing it.
    expect(subject?.top).toEqual({ lon: 25.001, lat: 45.002, height: 950 });
    expect(subject?.bottom).toEqual({ lon: 25.009, lat: 45.008, height: 650 });
  });

  it('takes a cave drawn without heights as lying on the ground, and as only roughly known', async () => {
    const subject = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({ caveInView: () => ({ caveId: 'c', bounds: [25, 45, 25.001, 45.001] }) }),
    );
    expect(subject?.centre.height).toBe(1000);
    expect(subject?.coarse).toBe(true);
  });

  it('guesses a cave nothing is drawn of from its description, and says it is a guess', async () => {
    const subject = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({ caveFacts: async () => ({ longitude: 25, latitude: 45, depthM: 400, lengthM: 2000 }) }),
    );
    expect(subject?.centre).toEqual({ lon: 25, lat: 45, height: 800 });
    expect(subject?.halfHeight).toBe(200);
    // A quarter of its length along the ground, half its depth down.
    expect(subject?.radius).toBeCloseTo(Math.hypot(500, 200));
    expect(subject?.coarse).toBe(true);
  });

  it('never circles anything from closer than a sensible distance', async () => {
    // Known only as a point: stood well back from, since there is only the ground to show.
    const rough = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({ caveFacts: async () => ({ longitude: 25, latitude: 45 }) }),
    );
    expect(rough?.radius).toBe(150);
    expect(rough?.halfHeight).toBeGreaterThan(0);
    // A few metres of surveyed passage: closer, but never on top of it.
    const short = await resolveSubject(
      { kind: 'cave', caveId: 'c' },
      world({ caveInView: () => ({ caveId: 'c', bounds: [25, 45, 25.00001, 45.00001], top: 900, bottom: 898 }) }),
    );
    expect(short?.radius).toBe(40);
  });

  it('answers nothing for a cave this reader cannot read', async () => {
    expect(await resolveSubject({ kind: 'cave', caveId: 'c' }, world())).toBeUndefined();
    expect(await resolveSubject({ kind: 'feature', featureId: 'f' }, world())).toBeUndefined();
    expect(await resolveSubject({ kind: 'view' }, world())).toBeUndefined();
  });

  it('sizes an area from its box and from the ground inside it', async () => {
    const subject = await resolveSubject(
      { kind: 'feature', featureId: 'f' },
      world({
        featureBounds: async () => [25, 45, 25.1, 45.1],
        // A slope rising to the north: 1000 m at the south edge, 2000 m at the north.
        groundHeight: (_longitude, latitude) => 1000 + (latitude - 45) * 10_000,
      }),
    );
    expect(subject?.centre.height).toBeCloseTo(1500);
    expect(subject?.halfHeight).toBeCloseTo(500);
    expect(subject?.radius).toBeGreaterThan(5000);
  });

  it('takes "what the view shows" from where the view is centred and how far back the eye is', async () => {
    const subject = await resolveSubject(
      { kind: 'view' },
      world({ viewTarget: () => ({ target: { lon: 25, lat: 45, height: 700 }, distance: 2000 }) }),
    );
    expect(subject?.centre).toEqual({ lon: 25, lat: 45, height: 700 });
    expect(subject?.radius).toBe(700);
  });

  it('puts a pinned place on the ground', async () => {
    const subject = await resolveSubject({ kind: 'place', longitude: 25, latitude: 45, radiusM: 300 }, world());
    expect(subject).toMatchObject({ centre: { lon: 25, lat: 45, height: 1000 }, radius: 300, coarse: false });
  });

  it('holds every subject in one ball when they are taken together', () => {
    const a = at(25, 45);
    const b = at(25.02, 45);
    const together = subjectsTogether([a, b])!;
    expect(together.centre.lon).toBeCloseTo(25.01);
    // Half the distance between the two, and the radius of one of them.
    expect(together.radius).toBeCloseTo(metersBetween(together.centre, a.centre) + 100);
    expect(together.radius).toBeGreaterThan(880);
    expect(subjectsTogether([a])).toBe(a);
    expect(subjectsTogether([])).toBeUndefined();
    expect(subjectFor('all', [a, undefined, b])).toEqual(together);
    expect(subjectFor(1, [a, undefined, b])).toBeUndefined();
  });

  it('orders a tour so the camera always goes on to the nearest cave', () => {
    const subjects = [at(25, 45), at(25.3, 45), at(25.1, 45), at(25.2, 45)];
    expect(shortestRoute(subjects)).toEqual([0, 2, 3, 1]);
    expect(shortestRoute([])).toEqual([]);
  });

  it('moves every reference with its subject when the order changes', () => {
    const subjects: FlybySubject[] = [
      { kind: 'cave', caveId: 'a' },
      { kind: 'cave', caveId: 'b' },
      { kind: 'cave', caveId: 'c' },
    ];
    const document: FlybyDocument = {
      version: FLYBY_VERSION,
      name: '',
      speed: 1,
      loop: false,
      subjects,
      start: { subject: 0, pose: { heading: 0, pitch: -20, range: 5, lift: 0, clearance: null } },
      sections: [
        { id: 'x', kind: 'hop', subject: 1, seconds: 5, to: {} },
        { id: 'y', kind: 'hop', subject: 2, seconds: 5, to: {} },
        { id: 'z', kind: 'rise', subject: 'all', seconds: 5, to: {} },
      ],
    };
    const moved = reorderSubjects(document, [0, 2, 1]);
    expect(moved.subjects.map((subject) => (subject.kind === 'cave' ? subject.caveId : ''))).toEqual(['a', 'c', 'b']);
    // The hop that went to cave b still goes to cave b, now second in the list.
    expect(moved.sections.map((section) => section.subject)).toEqual([2, 1, 'all']);
  });
});

describe('the ready-made flybys', () => {
  const one: FlybySubject[] = [{ kind: 'cave', caveId: 'a' }];
  const three: FlybySubject[] = [...one, { kind: 'cave', caveId: 'b' }, { kind: 'feature', featureId: 'f' }];

  it('are each a document that can be played', () => {
    for (const preset of ['aroundCave', 'aroundArea', 'aroundSeveral', 'tour', 'planToProfile'] as const) {
      for (const underground of [false, true]) {
        const document = buildPreset(preset, three, { ...DEFAULT_FLYBY_PRESET_OPTIONS, underground }, 'Name');
        expect(flybyProblems(document)).toEqual([]);
        expect(document.preset).toBe(preset);
        expect(document.name).toBe('Name');
        expect(new Set(document.sections.map((section) => section.id)).size).toBe(document.sections.length);
      }
    }
  });

  it('are offered only for subjects they can be built from', () => {
    expect(presetFits('tour', one)).toBe(false);
    expect(presetFits('tour', three)).toBe(true);
    expect(presetFits('aroundCave', one)).toBe(true);
    expect(presetFits('aroundCave', [])).toBe(false);
    expect(presetFor(one)).toBe('aroundCave');
    expect(presetFor([{ kind: 'view' }])).toBe('aroundArea');
    expect(presetFor(three)).toBe('tour');
  });

  it('turn as many times, as fast and the way they were asked', () => {
    const document = buildPreset('aroundCave', one, {
      turns: 2,
      clockwise: false,
      underground: false,
      secondsPerTurn: 30,
    });
    const orbit = document.sections.find((section) => section.kind === 'orbit')!;
    expect(orbit.to.headingBy).toBe(-720);
    expect(orbit.seconds).toBe(60);
  });

  it('go underground only when asked, lifting the limit above ground and putting it back', () => {
    const above = buildPreset('aroundCave', one);
    expect(above.sections.some((section) => section.kind === 'dive')).toBe(false);
    expect(above.sections.every((section) => section.to.clearance !== null)).toBe(true);
    const below = buildPreset('aroundCave', one, { ...DEFAULT_FLYBY_PRESET_OPTIONS, underground: true });
    const kinds = below.sections.map((section) => section.kind);
    expect(kinds).toEqual(['approach', 'orbit', 'dive', 'dive', 'rise']);
    expect(below.sections[2].to.clearance).toBeNull();
    expect(below.sections[4].to.clearance).toBeGreaterThan(0);
    // Down the cave itself — in at its highest place, on to its deepest — and back to its middle.
    expect(below.sections.slice(2).map((section) => section.to.aim)).toEqual(['top', 'bottom', 'centre']);
  });

  it('visit every subject of a tour in order and end on all of them', () => {
    const document = buildPreset('tour', three);
    const visited = document.sections.filter((section) => section.kind === 'orbit').map((section) => section.subject);
    expect(visited).toEqual([0, 1, 2]);
    expect(document.sections.filter((section) => section.kind === 'hop').map((section) => section.subject)).toEqual([1, 2]);
    expect(document.sections[document.sections.length - 1].subject).toBe('all');
  });
});
