// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  FLYBY_BUNDLE_COLOUR,
  FLYBY_BUNDLE_NUMBER,
  FLYBY_BUNDLE_TINT,
  flybyBundleLookOnFrames,
  flybyBundleLookTracks,
  flybyBundleTrack,
  flybyBundleTrackAt,
  type FlybyBundleTintKey,
  type FlybyBundleTrackKey,
} from './flybyBundleChanges.ts';
import { FLYBY_VERSION, type FlybyChange, type FlybyDocument, type FlybyLook } from './flybyDocument.ts';
import { easeFlyby, flybyLookAt, flybyLookPlan, flybyReliefAt, type FlybyLookBase } from './flybyLook.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import { lightTheVisited } from './flybyRecipes.ts';

// What a flyby changes about the look while it plays, written as keys for a renderer that plays
// keys: first the writing of one value's keys, on values made up here, then each thing a flyby
// can change. Everything is invented — two caves that are only ids, a clock of 25 frames a second.

const number = (value: number) => [value];

/**
 * The keys of a value given as a function of the frame. Where the value jumps, `after` says what
 * it is just after a frame and `before` what it was just before one.
 */
function keysOf(
  at: (frame: number) => number,
  breaks: number[],
  last: number,
  after: Record<number, number> = {},
  before: Record<number, number> = {},
): FlybyBundleTrackKey[] {
  return flybyBundleTrack(
    (frame, side) => number(side === 1 && frame in after ? after[frame] : side === -1 && frame in before ? before[frame] : at(frame)),
    breaks,
    last,
    FLYBY_BUNDLE_NUMBER,
  );
}

describe('the keys of one value', () => {
  it('are one key for a value that never changes', () => {
    expect(keysOf(() => 0.4, [], 100)).toEqual([{ frame: 0, value: [0.4] }]);
    expect(keysOf(() => 0.4, [20, 60], 100)).toEqual([{ frame: 0, value: [0.4] }]);
  });

  it('are two keys and the word "smooth" for a change eased as a flyby eases', () => {
    const eased = (frame: number) => (frame <= 20 ? 1 : frame >= 60 ? 3 : 1 + 2 * easeFlyby((frame - 20) / 40));
    expect(keysOf(eased, [20, 60], 100)).toEqual([
      { frame: 0, value: [1] },
      { frame: 20, value: [1] },
      { frame: 60, value: [3], easing: 'smooth' },
    ]);
  });

  it('are two keys and no word for a change that runs evenly', () => {
    const even = (frame: number) => (frame <= 20 ? 1 : frame >= 60 ? 3 : 1 + (2 * (frame - 20)) / 40);
    expect(keysOf(even, [20, 60], 100)).toEqual([
      { frame: 0, value: [1] },
      { frame: 20, value: [1] },
      { frame: 60, value: [3] },
    ]);
  });

  it('are one key and the word "constant" for a cut, on the frame that first shows the new value', () => {
    const cut = (frame: number) => (frame < 30 ? 0 : 1);
    expect(keysOf(cut, [30], 100, {}, { 30: 0 })).toEqual([
      { frame: 0, value: [0] },
      { frame: 30, value: [1], easing: 'constant' },
    ]);
  });

  it('are a key a frame where two keys cannot say the curve', () => {
    // A curve that is neither the flyby's easing nor a straight line.
    const odd = (frame: number) => (frame <= 10 ? 0 : frame >= 15 ? 1 : ((frame - 10) / 5) ** 3);
    const keys = keysOf(odd, [10, 15], 20);
    expect(keys.map((key) => key.frame)).toEqual([0, 10, 11, 12, 13, 14, 15]);
    expect(keys.every((key) => key.easing === undefined)).toBe(true);
    for (let frame = 0; frame <= 20; frame += 1) {
      expect(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(odd(frame), 9);
    }
  });

  it('put a jump that follows a frame on that frame: the value before it gives way one frame early', () => {
    // 0.6 up to and at frame 10; just after it the value is 0, and from there it eases up to 0.85.
    const taken = (frame: number) => (frame <= 10 ? 0.6 : frame >= 30 ? 0.85 : 0.85 * easeFlyby((frame - 10) / 20));
    const keys = keysOf(taken, [10, 30], 40, { 10: 0 });
    expect(keys).toEqual([
      { frame: 0, value: [0.6] },
      { frame: 10, value: [0], easing: 'constant' },
      { frame: 30, value: [0.85], easing: 'smooth' },
    ]);
    // Right at every frame but the one the jump stands on.
    const off = Array.from({ length: 41 }, (_, frame) => frame).filter(
      (frame) => Math.abs(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0] - taken(frame)) > 1e-6,
    );
    expect(off).toEqual([10]);
  });

  it('put a jump that falls on the last frame of a curve one frame late: the curve keeps its end', () => {
    // Eases down to 0 at frame 30 — and at frame 30 itself the value is back at 0.6.
    const handed = (frame: number) => (frame <= 10 ? 0.85 : frame >= 30 ? 0.6 : 0.85 * (1 - easeFlyby((frame - 10) / 20)));
    const keys = keysOf(handed, [10, 30], 40, {}, { 30: 0 });
    expect(keys).toEqual([
      { frame: 0, value: [0.85] },
      { frame: 10, value: [0.85] },
      { frame: 30, value: [0], easing: 'smooth' },
      { frame: 31, value: [0.6], easing: 'constant' },
    ]);
    const off = Array.from({ length: 41 }, (_, frame) => frame).filter(
      (frame) => Math.abs(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0] - handed(frame)) > 1e-6,
    );
    expect(off).toEqual([30]);
  });

  it('write a curve the clip starts or stops in the middle of frame by frame, and are right at each', () => {
    // Under way since before the first frame, and not over by the last.
    const early = (frame: number) => (frame >= 8 ? 2 : 1 + easeFlyby((frame + 12) / 20));
    const started = keysOf(early, [8], 20);
    expect(started.map((key) => key.frame)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
    const late = (frame: number) => (frame <= 15 ? 1 : 1 + easeFlyby((frame - 15) / 20));
    const stopped = keysOf(late, [15], 20);
    expect(stopped.map((key) => key.frame)).toEqual([0, 15, 16, 17, 18, 19, 20]);
    for (let frame = 0; frame <= 20; frame += 1) {
      expect(flybyBundleTrackAt(started, frame, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(early(frame), 9);
      expect(flybyBundleTrackAt(stopped, frame, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(late(frame), 9);
    }
  });

  it('show a cut that falls on the clip\'s very last frame', () => {
    const cut = (frame: number) => (frame < 20 ? 0 : 1);
    expect(keysOf(cut, [], 20, {}, { 20: 0 })).toEqual([
      { frame: 0, value: [0] },
      { frame: 20, value: [1], easing: 'constant' },
    ]);
  });

  it('are one key for a clip of one frame', () => {
    expect(keysOf(() => 2, [], 0)).toEqual([{ frame: 0, value: [2] }]);
  });
});

describe('a track played back', () => {
  const keys: FlybyBundleTrackKey[] = [
    { frame: 10, value: [1] },
    { frame: 20, value: [3], easing: 'smooth' },
    { frame: 30, value: [5] },
    { frame: 40, value: [0], easing: 'constant' },
  ];

  it('holds its first value before its first key and its last after its last', () => {
    expect(flybyBundleTrackAt(keys, 0, FLYBY_BUNDLE_NUMBER)).toEqual([1]);
    expect(flybyBundleTrackAt(keys, 99, FLYBY_BUNDLE_NUMBER)).toEqual([0]);
  });

  it('moves to a key as that key says: eased, evenly, or not until its frame', () => {
    // A fifth of the way: 3x² − 2x³ of the distance.
    expect(flybyBundleTrackAt(keys, 12, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(1 + 2 * (3 * 0.04 - 2 * 0.008), 12);
    expect(flybyBundleTrackAt(keys, 15, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(2, 12);
    expect(flybyBundleTrackAt(keys, 25, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(4, 12);
    expect(flybyBundleTrackAt(keys, 39, FLYBY_BUNDLE_NUMBER)).toEqual([5]);
    expect(flybyBundleTrackAt(keys, 40, FLYBY_BUNDLE_NUMBER)).toEqual([0]);
  });

  it('mixes two colours as light, which is not how a screen\'s numbers mix', () => {
    const half = FLYBY_BUNDLE_COLOUR.mix([0, 0, 0], [255, 255, 255], 0.5);
    // Half the light of white is a far lighter grey than 128.
    expect(half[0]).toBeCloseTo(187.5, 0);
    expect(FLYBY_BUNDLE_COLOUR.same([10, 20, 30], [11, 19, 30])).toBe(true);
    expect(FLYBY_BUNDLE_COLOUR.same([10, 20, 30], [12, 20, 30])).toBe(false);
  });

  it('takes two tints of no strength for one, whatever their colours', () => {
    expect(FLYBY_BUNDLE_TINT.same([255, 0, 0, 0], [0, 0, 255, 0])).toBe(true);
    expect(FLYBY_BUNDLE_TINT.same([255, 0, 0, 0.5], [0, 0, 255, 0.5])).toBe(false);
    expect(FLYBY_BUNDLE_TINT.same([255, 0, 0, 0.5], [255, 0, 0, 0.5])).toBe(true);
  });
});

// ---- a flyby's look ----

const FPS = 25;
const CAVES = ['cave-a', 'cave-b'];

/** A change as a test writes one: without the name the document gives it. */
type Unnamed<T> = T extends unknown ? Omit<T, 'id'> : never;
type Change = Unnamed<FlybyChange>;

/** Two sections of ten seconds: `one` from 0, `two` from 10. */
function flyby(changes: Change[], look: FlybyLook = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: 'Invented',
    speed: 1,
    loop: false,
    subjects: CAVES.map((caveId) => ({ kind: 'cave', caveId })),
    start: { subject: 0, pose: { heading: 0, pitch: -30, range: 5, lift: 0, clearance: null } },
    sections: [
      { id: 'one', kind: 'orbit', subject: 0, seconds: 10, to: { headingBy: 90 } },
      { id: 'two', kind: 'orbit', subject: 1, seconds: 10, to: { headingBy: 90 } },
    ],
    look,
    changes: changes.map((change, index) => ({ ...change, id: `c${index}` }) as FlybyChange),
  };
}

function viewer(extra: Partial<FlybyLookBase> = {}): FlybyLookBase {
  return {
    backgroundOpacity: 1,
    surface: 'overlay',
    layers: { walls: { visible: true, opacity: 1 }, centerlines: { visible: true, opacity: 1 } },
    ...extra,
  };
}

function tracks(document: FlybyDocument, base = viewer(), stretch?: { from: number; to: number }, holdEndS = 0) {
  return flybyBundleLookTracks(flybyLookPlan(document), base, flybyFrameSchedule(20, FPS, stretch, holdEndS), CAVES);
}

describe('a relief that changes', () => {
  it('is the factor before, the factor after and the flyby\'s own easing between them', () => {
    const made = tracks(flyby([{ kind: 'relief', factor: 3, section: 'one', at: 2, seconds: 4 }], { relief: 1.5 }));
    expect(made.changes).toEqual({
      relief: [
        { frame: 0, value: 1.5 },
        { frame: 50, value: 1.5 },
        { frame: 150, value: 3, easing: 'smooth' },
      ],
    });
    expect(made.reliefAt(0)).toBe(1.5);
    expect(made.reliefAt(100)).toBe(2.25);
    expect(made.reliefAt(499)).toBe(3);
  });

  it('reads, at every frame, what the flyby draws at that frame', () => {
    const document = flyby(
      [
        { kind: 'relief', factor: 3, section: 'one', at: 2, seconds: 4 },
        { kind: 'relief', factor: null, section: 'two', at: 1, seconds: 2 },
      ],
      { relief: 1.5 },
    );
    const plan = flybyLookPlan(document);
    const made = tracks(document);
    for (let frame = 0; frame < 500; frame += 1) {
      expect(made.reliefAt(frame), `frame ${frame}`).toBeCloseTo(flybyReliefAt(plan, undefined, frame / FPS), 6);
    }
    // Back to the factor the flyby opened at, which is what "null" asks for.
    expect(made.changes?.relief?.at(-1)).toEqual({ frame: 325, value: 1.5, easing: 'smooth' });
  });

  it('begins and ends on a frame: a change between two frames is moved to the nearer, by half a frame at most', () => {
    // 2.03 s is frame 50.75, and 4.47 s later frame 162.5: moved to 51 and 163 (half-way goes up).
    const document = flyby([{ kind: 'relief', factor: 2, section: 'one', at: 2.03, seconds: 4.47 }]);
    const made = tracks(document);
    expect(made.changes?.relief).toEqual([
      { frame: 0, value: 1 },
      { frame: 51, value: 1 },
      { frame: 163, value: 2, easing: 'smooth' },
    ]);
    const moved = flybyBundleLookOnFrames(flybyLookPlan(document), { from: 0, fps: FPS });
    expect(moved.changes[0].start).toBeCloseTo(51 / FPS, 12);
    expect(moved.changes[0].end).toBeCloseTo(163 / FPS, 12);
    // What the keys give is what the flyby draws within half a frame of the same moment.
    const plan = flybyLookPlan(document);
    for (let frame = 0; frame < 500; frame += 1) {
      const near = [-0.5, 0, 0.5].map((shift) => flybyReliefAt(plan, undefined, (frame + shift) / FPS));
      expect(made.reliefAt(frame), `frame ${frame}`).toBeGreaterThanOrEqual(Math.min(...near) - 1e-6);
      expect(made.reliefAt(frame), `frame ${frame}`).toBeLessThanOrEqual(Math.max(...near) + 1e-6);
    }
  });

  it('is the viewer\'s own factor throughout where the view lets no flyby stretch the relief', () => {
    const document = flyby([{ kind: 'relief', factor: 3, section: 'one', at: 2, seconds: 4 }], { relief: 1.5 });
    const made = flybyBundleLookTracks(flybyLookPlan(document), viewer({ relief: 2 }), flybyFrameSchedule(20, FPS), CAVES, false);
    expect(made.changes).toBeNull();
    expect(made.reliefAt(100)).toBe(2);
  });
});

describe('a sky that changes', () => {
  it('is a cut where the change has no length', () => {
    const made = tracks(flyby([{ kind: 'sky', colour: '#204060', section: 'one', at: 4, seconds: 0 }]));
    expect(made.sky).toBeUndefined();
    expect(made.changes).toEqual({
      sky: [
        { frame: 0, colour: '#000000' },
        { frame: 100, colour: '#204060', easing: 'constant' },
      ],
    });
  });

  it('is written frame by frame where one colour crosses into another, as the screen mixes them', () => {
    const document = flyby([{ kind: 'sky', colour: '#ffffff', section: 'one', at: 4, seconds: 2 }], { sky: '#000000' });
    const sky = tracks(document).changes?.sky ?? [];
    // Black still on the frame after the change begins: the first step is too small for a screen.
    expect(sky.map((key) => key.frame)).toEqual([0, ...Array.from({ length: 50 }, (_, k) => 101 + k)]);
    // Half-way the screen shows the middle grey of its own numbers, and so does the track.
    expect(sky.find((key) => key.frame === 125)?.colour).toBe('#808080');
    expect(sky.at(-1)).toEqual({ frame: 150, colour: '#ffffff' });
    const plan = flybyLookPlan(document);
    for (const key of sky) {
      expect(key.colour, `frame ${key.frame}`).toBe(flybyLookAt(plan, viewer(), key.frame / FPS).sky ?? '#000000');
    }
  });

  it('is the one colour of a flyby that only names a sky', () => {
    const made = tracks(flyby([], { sky: '#1b2a3a' }));
    expect(made.sky).toBe('#1b2a3a');
    expect(made.changes).toBeNull();
  });
});

describe('the colour over the walls', () => {
  it('is one key for a tint that stands from the first frame to the last', () => {
    const made = tracks(flyby([], { walls: { colour: '#3a3f4a', strength: 0.6 } }));
    expect(made.changes).toEqual({ walls: { tint: [{ frame: 0, colour: '#3a3f4a', strength: 0.6 }] } });
  });

  it('rises in strength along the flyby\'s easing when the walls had their own colours', () => {
    const made = tracks(flyby([{ kind: 'walls', colour: '#aa5500', strength: 0.5, section: 'one', at: 2, seconds: 2 }]));
    expect(made.changes?.walls?.tint).toEqual([
      { frame: 0, colour: '#000000', strength: 0 },
      { frame: 50, colour: '#aa5500', strength: 0 },
      { frame: 100, colour: '#aa5500', strength: 0.5, easing: 'smooth' },
    ]);
  });

  it('goes from one colour to another frame by frame, each the colour the screen shows', () => {
    const document = flyby([{ kind: 'walls', colour: '#0000ff', strength: 0.9, section: 'one', at: 2, seconds: 1 }], {
      walls: { colour: '#ff0000', strength: 0.3 },
    });
    const tint = tracks(document).changes?.walls?.tint ?? [];
    expect(tint.map((key) => key.frame)).toEqual([0, ...Array.from({ length: 26 }, (_, k) => 50 + k)]);
    const plan = flybyLookPlan(document);
    for (const key of tint) {
      const shown = flybyLookAt(plan, viewer(), key.frame / FPS).walls!;
      expect(key.colour, `frame ${key.frame}`).toBe(shown.colour);
      expect(key.strength, `frame ${key.frame}`).toBeCloseTo(shown.strength, 6);
    }
  });

  it('is nothing at all for walls in their own colours', () => {
    expect(tracks(flyby([])).changes).toBeNull();
  });
});

/** The frames at which a cave's track says something other than the flyby draws there. */
function framesOff(keys: readonly FlybyBundleTintKey[], drawn: (t: number) => number[]): number[] {
  const played: FlybyBundleTrackKey[] = keys.map((key) => ({
    frame: key.frame,
    value: [...[1, 3, 5].map((at) => Number.parseInt(key.colour.slice(at, at + 2), 16)), key.strength],
    ...(key.easing ? { easing: key.easing } : {}),
  }));
  const off: number[] = [];
  for (let frame = 0; frame < 500; frame += 1) {
    if (!FLYBY_BUNDLE_TINT.same(flybyBundleTrackAt(played, frame, FLYBY_BUNDLE_TINT), drawn(frame / FPS))) {
      off.push(frame);
    }
  }
  return off;
}

const channels = (colour: string) => [1, 3, 5].map((at) => Number.parseInt(colour.slice(at, at + 2), 16));

describe('one cave in a colour of its own', () => {
  // "Light the visited, dim the others", written out: every cave dimmed, the first lit while the
  // camera is with it.
  const lit: Change[] = [
    { kind: 'cave', subject: 0, colour: '#ffcc00', strength: 0.85, section: 'one', at: 2, seconds: 1 },
    { kind: 'cave', subject: 0, colour: '#ffcc00', strength: 0, section: 'one', at: 8, seconds: 1 },
  ];
  const dimmed: FlybyLook = { walls: { colour: '#3a3f4a', strength: 0.6 }, lines: '#3a3f4a' };

  it('takes its walls over from the colour of every cave, and hands them back', () => {
    const made = tracks(flyby(lit, dimmed));
    expect(made.changes?.walls?.tint).toEqual([{ frame: 0, colour: '#3a3f4a', strength: 0.6 }]);
    expect(made.lineColour).toBe('#3a3f4a');
    // Only the cave that is coloured apart is named.
    expect(made.changes?.caves?.map((cave) => cave.caveId)).toEqual(['cave-a']);
    expect(made.changes?.caves?.[0].walls).toEqual([
      { frame: 0, colour: '#3a3f4a', strength: 0.6 },
      { frame: 50, colour: '#ffcc00', strength: 0, easing: 'constant' },
      { frame: 75, colour: '#ffcc00', strength: 0.85, easing: 'smooth' },
      { frame: 200, colour: '#ffcc00', strength: 0.85 },
      { frame: 225, colour: '#ffcc00', strength: 0, easing: 'smooth' },
      { frame: 226, colour: '#3a3f4a', strength: 0.6, easing: 'constant' },
    ]);
  });

  it('has its lines in its colour for as long as its walls have any of it', () => {
    const made = tracks(flyby(lit, dimmed));
    expect(made.changes?.caves?.[0].lines).toEqual([
      { frame: 0, colour: '#3a3f4a', strength: 1 },
      { frame: 50, colour: '#ffcc00', strength: 1, easing: 'constant' },
      { frame: 225, colour: '#3a3f4a', strength: 1, easing: 'constant' },
    ]);
  });

  it('says what the flyby draws at every frame but the ones a hand-over stands on', () => {
    const document = flyby(lit, dimmed);
    const plan = flybyLookPlan(document);
    const made = tracks(document);
    const walls = (t: number) => {
      const state = flybyLookAt(plan, viewer(), t);
      const own = state.caves?.['cave-a'];
      const tint = own && own.strength > 0 ? own : state.walls!;
      return [...channels(tint.colour), tint.strength];
    };
    const lines = (t: number) => {
      const state = flybyLookAt(plan, viewer(), t);
      const own = state.caves?.['cave-a'];
      return [...channels(own && own.strength > 0 ? own.colour : state.lines!), 1];
    };
    // The screen goes from the common colour to the cave's own between frames 50 and 51, and back
    // between 224 and 225; the track gives each jump a frame.
    expect(framesOff(made.changes!.caves![0].walls!, walls)).toEqual([50, 225]);
    expect(framesOff(made.changes!.caves![0].lines!, lines)).toEqual([50]);
  });

  it('is what the recipe that lights each cave in turn writes', () => {
    const recipe = lightTheVisited(flyby([]), { lit: '#ffcc00', dim: '#3a3f4a', dimStrength: 0.6 });
    const document = { ...flyby([]), look: recipe.look, changes: recipe.changes };
    const made = tracks(document);
    expect(made.changes?.caves?.map((cave) => cave.caveId)).toEqual(CAVES);
    // Each stop: up over a second and a half, down again over the last second and a half.
    const second = made.changes?.caves?.[1].walls ?? [];
    expect(second.slice(0, 4).map((key) => [key.frame, key.strength, key.easing])).toEqual([
      [0, 0.6, undefined],
      [250, 0, 'constant'],
      // 37.5 frames, to the nearer frame.
      [288, 0.85, 'smooth'],
      [463, 0.85, undefined],
    ]);
    // The last cave dims as the flyby ends, and the clip has no frame at the very end of a flyby:
    // a curve the clip stops short of is written frame by frame.
    expect(second.slice(4).map((key) => key.frame)).toEqual(Array.from({ length: 36 }, (_, k) => 464 + k));
    expect(second.at(-1)?.strength).toBeGreaterThan(0);
    // The first cave is lit from the flyby's first moment, so the clip opens on its hand-over;
    // it dims in full, and is given back to the common colour a frame after.
    expect(made.changes?.caves?.[0].walls?.map((key) => [key.frame, key.colour, key.strength, key.easing])).toEqual([
      [0, '#ffcc00', 0, undefined],
      [38, '#ffcc00', 0.85, 'smooth'],
      [213, '#ffcc00', 0.85, undefined],
      [250, '#ffcc00', 0, 'smooth'],
      [251, '#3a3f4a', 0.6, 'constant'],
    ]);
  });

  it('stands in for a common colour that is no colour at all, and the other way round', () => {
    // No common tint: the cave alone has one.
    const alone = tracks(flyby(lit));
    expect(alone.changes?.walls).toBeUndefined();
    expect(alone.changes?.caves?.[0].walls?.map((key) => key.strength)).toEqual([0, 0, 0.85, 0.85, 0]);
    expect(alone.changes?.caves?.[0].lines?.map((key) => [key.frame, key.strength])).toEqual([
      [0, 0],
      [50, 1],
      [225, 0],
    ]);
  });
});

describe('the colour of the lines', () => {
  it('is cut to one colour and back to the colours by depth', () => {
    const made = tracks(
      flyby([
        { kind: 'lines', colour: '#00ff88', section: 'one', at: 2, seconds: 3 },
        { kind: 'lines', colour: null, section: 'two', at: 0, seconds: 0 },
      ]),
    );
    expect(made.lineColour).toBeUndefined();
    expect(made.changes).toEqual({
      lines: {
        tint: [
          { frame: 0, colour: '#000000', strength: 0 },
          { frame: 50, colour: '#00ff88', strength: 1, easing: 'constant' },
          { frame: 250, colour: '#000000', strength: 0, easing: 'constant' },
        ],
      },
    });
  });

  it('is said by the look alone where it is one colour throughout', () => {
    const made = tracks(flyby([], { lines: '#00ff88' }));
    expect(made.lineColour).toBe('#00ff88');
    expect(made.changes).toBeNull();
  });
});

describe('a layer shown, hidden and faded', () => {
  it('fades the lines out and is hidden when they have gone; fades them in from the frame they come', () => {
    const made = tracks(
      flyby([
        { kind: 'layer', layer: 'centerlines', visible: false, section: 'one', at: 2, seconds: 2 },
        { kind: 'layer', layer: 'centerlines', visible: true, section: 'two', at: 0, seconds: 1 },
      ]),
    );
    expect(made.lines).toBe(true);
    expect(made.changes).toEqual({
      lines: {
        shown: [
          { frame: 100, shown: false },
          { frame: 250, shown: true },
        ],
        opacity: [
          { frame: 0, value: 1 },
          { frame: 50, value: 1 },
          { frame: 100, value: 0, easing: 'smooth' },
          { frame: 250, value: 0 },
          { frame: 275, value: 1, easing: 'smooth' },
        ],
      },
    });
  });

  it('brings in walls that were hidden when the clip began', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    const made = tracks(flyby([{ kind: 'layer', layer: 'walls', visible: true, section: 'one', at: 4, seconds: 2 }]), base);
    expect(made.walls).toBe(true);
    expect(made.changes?.walls).toEqual({
      shown: [{ frame: 100, shown: true }],
      opacity: [
        { frame: 0, value: 0 },
        { frame: 100, value: 0 },
        { frame: 150, value: 1, easing: 'smooth' },
      ],
    });
  });

  it('cuts a layer without touching how much of it there is', () => {
    const made = tracks(
      flyby([
        { kind: 'layer', layer: 'walls', visible: false, section: 'one', at: 4, seconds: 0 },
        { kind: 'layer', layer: 'walls', visible: true, section: 'two', at: 0, seconds: 0 },
      ]),
    );
    expect(made.changes).toEqual({
      walls: {
        shown: [
          { frame: 100, shown: false },
          { frame: 250, shown: true },
        ],
      },
    });
  });

  it('carries the strength a layer is drawn at, and its change', () => {
    const still = tracks(flyby([], { layers: { centerlines: { opacity: 0.4 } } }));
    expect(still.changes).toEqual({ lines: { opacity: [{ frame: 0, value: 0.4 }] } });
    const moving = tracks(
      flyby([{ kind: 'layerOpacity', layer: 'centerlines', opacity: 0.25, section: 'one', at: 2, seconds: 2 }], {
        layers: { centerlines: { opacity: 0.75 } },
      }),
    );
    expect(moving.changes?.lines?.opacity).toEqual([
      { frame: 0, value: 0.75 },
      { frame: 50, value: 0.75 },
      { frame: 100, value: 0.25, easing: 'smooth' },
    ]);
  });

  it('writes a fade and a change of strength that run together frame by frame, and is right at each', () => {
    const document = flyby([
      { kind: 'layer', layer: 'centerlines', visible: false, section: 'one', at: 2, seconds: 2 },
      { kind: 'layerOpacity', layer: 'centerlines', opacity: 0.5, section: 'one', at: 3, seconds: 2 },
    ]);
    const opacity = tracks(document).changes?.lines?.opacity ?? [];
    const plan = flybyLookPlan(document);
    // The fade alone for a second, then both: one curve cannot say the product of two.
    expect(opacity.length).toBeGreaterThan(40);
    for (const key of opacity.filter((key) => key.frame < 100)) {
      expect(key.value, `frame ${key.frame}`).toBeCloseTo(flybyLookAt(plan, viewer(), key.frame / FPS).layers.centerlines.opacity, 6);
    }
  });

  it('says that neither is ever shown where both are hidden throughout', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    base.layers.centerlines = { visible: false, opacity: 1 };
    const made = tracks(flyby([]), base);
    expect(made.walls).toBe(false);
    expect(made.lines).toBe(false);
    expect(made.changes).toBeNull();
  });
});

describe('a clip of part of a flyby', () => {
  const document = flyby([{ kind: 'relief', factor: 3, section: 'one', at: 2, seconds: 4 }], { relief: 1 });

  it('counts its frames from its own first, and opens at what the flyby has reached', () => {
    const made = tracks(document, viewer(), { from: 4, to: 12 });
    // Half-way up the ramp at its first frame.
    expect(made.reliefAt(0)).toBe(2);
    const relief = made.changes?.relief ?? [];
    // In the middle of a curve from the first frame on: a key a frame until the curve ends.
    expect(relief.map((key) => key.frame)).toEqual(Array.from({ length: 51 }, (_, k) => k));
    expect(relief.at(-1)).toEqual({ frame: 50, value: 3 });
  });

  it('holds its last moment for the frames held at its end', () => {
    // Two seconds flown — stopping half-way up the ramp — and one held.
    const made = tracks(document, viewer(), { from: 2, to: 4 }, 1);
    expect(made.reliefAt(49)).toBeLessThan(2);
    expect(made.reliefAt(50)).toBe(2);
    expect(made.reliefAt(74)).toBe(2);
    expect(made.changes?.relief?.at(-1)?.frame).toBe(50);
  });
});
