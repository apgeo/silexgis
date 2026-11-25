// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  FLYBY_BUNDLE_COLOUR,
  FLYBY_BUNDLE_NUMBER,
  FLYBY_BUNDLE_TINT,
  flybyBundleDaylight,
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

/** A number as a key writes it: to a millionth. */
const rounded = (value: number) => Math.round(value * 1e6) / 1e6;

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

  it('write a curve that starts a moment after a frame frame by frame: the frame itself keeps the value before', () => {
    // 0.6 up to and at frame 10; just after it the value is 0, and from there it eases up to 0.85.
    const taken = (frame: number) => (frame <= 10 ? 0.6 : frame >= 30 ? 0.85 : 0.85 * easeFlyby((frame - 10) / 20));
    const keys = keysOf(taken, [10, 30], 40, { 10: 0 });
    // The curve starts at frame 10 from a value frame 10 does not show: no two keys say that.
    expect(keys.map((key) => key.frame)).toEqual([0, ...Array.from({ length: 20 }, (_, k) => 11 + k)]);
    expect(keys[1]).toEqual({ frame: 11, value: [taken(11)], easing: 'constant' });
    expect(keys.slice(2).every((key) => key.easing === undefined)).toBe(true);
    // Right at every frame, the one the jump follows included.
    const off = Array.from({ length: 41 }, (_, frame) => frame).filter(
      (frame) => Math.abs(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0] - taken(frame)) > 1e-6,
    );
    expect(off).toEqual([]);
  });

  it('put a jump a frame after the frame it follows, where the value then holds', () => {
    const switched = (frame: number) => (frame <= 10 ? 0.6 : 1);
    expect(keysOf(switched, [10], 40, { 10: 1 })).toEqual([
      { frame: 0, value: [0.6] },
      { frame: 11, value: [1], easing: 'constant' },
    ]);
  });

  it('put a jump that falls on the last frame of a curve on that frame, beside the curve\'s end', () => {
    // Eases down to 0 at frame 30 — and at frame 30 itself the value is back at 0.6.
    const handed = (frame: number) => (frame <= 10 ? 0.85 : frame >= 30 ? 0.6 : 0.85 * (1 - easeFlyby((frame - 10) / 20)));
    const keys = keysOf(handed, [10, 30], 40, {}, { 30: 0 });
    // Two keys on frame 30: where the curve was going, and what the frame shows.
    expect(keys).toEqual([
      { frame: 0, value: [0.85] },
      { frame: 10, value: [0.85] },
      { frame: 30, value: [0], easing: 'smooth' },
      { frame: 30, value: [0.6], easing: 'constant' },
    ]);
    const off = Array.from({ length: 41 }, (_, frame) => frame).filter(
      (frame) => Math.abs(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0] - handed(frame)) > 1e-6,
    );
    expect(off).toEqual([]);
  });

  it('give a curve the clip stops in the middle of its real end, after the clip\'s last frame', () => {
    const late = (frame: number) => (frame <= 15 ? 1 : frame >= 35 ? 2 : 1 + easeFlyby((frame - 15) / 20));
    const keys = keysOf(late, [15, 35], 20);
    expect(keys).toEqual([
      { frame: 0, value: [1] },
      { frame: 15, value: [1] },
      { frame: 35, value: [2], easing: 'smooth' },
    ]);
    for (let frame = 0; frame <= 20; frame += 1) {
      expect(flybyBundleTrackAt(keys, frame, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(late(frame), 9);
    }
    // One frame past the last is enough: a change that ends as the flyby does.
    const ending = (frame: number) => (frame <= 15 ? 1 : 1 + easeFlyby((frame - 15) / 6));
    expect(keysOf(ending, [15, 21], 20).at(-1)).toEqual({ frame: 21, value: [2], easing: 'smooth' });
  });

  it('write a curve the clip starts in the middle of frame by frame, and are right at each', () => {
    // Under way since before the first frame.
    const early = (frame: number) => (frame >= 8 ? 2 : 1 + easeFlyby((frame + 12) / 20));
    const started = keysOf(early, [8], 20);
    expect(started.map((key) => key.frame)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
    // And one whose end nobody names — a clip held still before it — up to the last frame seen.
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

  it('gives the later of two keys on one frame from that frame on, and moves towards the earlier before it', () => {
    const paired: FlybyBundleTrackKey[] = [
      { frame: 0, value: [1] },
      { frame: 10, value: [0], easing: 'smooth' },
      { frame: 10, value: [0.6], easing: 'constant' },
      { frame: 20, value: [1] },
    ];
    expect(flybyBundleTrackAt(paired, 5, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(0.5, 12);
    expect(flybyBundleTrackAt(paired, 9, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(1 - easeFlyby(0.9), 12);
    expect(flybyBundleTrackAt(paired, 10, FLYBY_BUNDLE_NUMBER)).toEqual([0.6]);
    expect(flybyBundleTrackAt(paired, 15, FLYBY_BUNDLE_NUMBER)[0]).toBeCloseTo(0.8, 12);
  });

  it('mixes two colours as a screen mixes them: straight between the numbers a screen is given', () => {
    const half = FLYBY_BUNDLE_COLOUR.mix([0, 0, 0], [255, 255, 255], 0.5);
    // The middle grey of a screen's own numbers, not the far lighter grey of half the light.
    expect(half[0]).toBeCloseTo(127.5, 9);
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

  it('is two keys where one colour crosses into another: a renderer told to mixes them as the screen does', () => {
    const document = flyby([{ kind: 'sky', colour: '#ffffff', section: 'one', at: 4, seconds: 2 }], { sky: '#000000' });
    const sky = tracks(document).changes?.sky ?? [];
    expect(sky).toEqual([
      { frame: 0, colour: '#000000' },
      { frame: 100, colour: '#000000' },
      { frame: 150, colour: '#ffffff', easing: 'smooth' },
    ]);
    // Played back, every frame is the colour the screen shows, to the step a screen's channel has.
    const played: FlybyBundleTrackKey[] = sky.map((key) => ({ frame: key.frame, value: channels(key.colour), ...(key.easing ? { easing: key.easing } : {}) }));
    const plan = flybyLookPlan(document);
    for (let frame = 0; frame < 500; frame += 1) {
      const shown = channels(flybyLookAt(plan, viewer(), frame / FPS).sky ?? '#000000');
      const said = flybyBundleTrackAt(played, frame, FLYBY_BUNDLE_COLOUR);
      expect(Math.max(...said.map((channel, i) => Math.abs(channel - shown[i]))), `frame ${frame}`).toBeLessThanOrEqual(0.5 + 1e-9);
    }
    // Half-way the screen shows the middle grey of its own numbers.
    expect(flybyBundleTrackAt(played, 125, FLYBY_BUNDLE_COLOUR)[0]).toBeCloseTo(127.5, 9);
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
      // The colour is there before any of it shows: nothing crosses from black into it.
      { frame: 50, colour: '#aa5500', strength: 0, easing: 'constant' },
      { frame: 100, colour: '#aa5500', strength: 0.5, easing: 'smooth' },
    ]);
  });

  it('goes from one colour to another in two keys, every frame the colour the screen shows', () => {
    const document = flyby([{ kind: 'walls', colour: '#0000ff', strength: 0.9, section: 'one', at: 2, seconds: 1 }], {
      walls: { colour: '#ff0000', strength: 0.3 },
    });
    const tint = tracks(document).changes?.walls?.tint ?? [];
    expect(tint).toEqual([
      { frame: 0, colour: '#ff0000', strength: 0.3 },
      { frame: 50, colour: '#ff0000', strength: 0.3 },
      { frame: 75, colour: '#0000ff', strength: 0.9, easing: 'smooth' },
    ]);
    const plan = flybyLookPlan(document);
    expect(
      framesOff(tint, (t) => {
        const shown = flybyLookAt(plan, viewer(), t).walls!;
        return [...channels(shown.colour), shown.strength];
      }),
    ).toEqual([]);
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
    const walls = made.changes?.caves?.[0].walls ?? [];
    // Taking over: frame 50 still shows the colour of every cave, and the cave's own rises from
    // the frame after — a curve whose start no frame shows, written frame by frame.
    expect(walls.slice(0, 3)).toEqual([
      { frame: 0, colour: '#3a3f4a', strength: 0.6 },
      { frame: 51, colour: '#ffcc00', strength: rounded(0.85 * easeFlyby(1 / 25)), easing: 'constant' },
      { frame: 52, colour: '#ffcc00', strength: rounded(0.85 * easeFlyby(2 / 25)) },
    ]);
    expect(walls.slice(1, 26).map((key) => key.frame)).toEqual(Array.from({ length: 25 }, (_, k) => 51 + k));
    // Handing back: two keys on frame 225 — where the cave's own colour was going, and the
    // colour of every cave, which is what that frame shows.
    expect(walls.slice(25)).toEqual([
      { frame: 75, colour: '#ffcc00', strength: 0.85 },
      { frame: 200, colour: '#ffcc00', strength: 0.85 },
      { frame: 225, colour: '#ffcc00', strength: 0, easing: 'smooth' },
      { frame: 225, colour: '#3a3f4a', strength: 0.6, easing: 'constant' },
    ]);
  });

  it('has its lines in its colour for as long as its walls have any of it', () => {
    const made = tracks(flyby(lit, dimmed));
    expect(made.changes?.caves?.[0].lines).toEqual([
      { frame: 0, colour: '#3a3f4a', strength: 1 },
      // From the first frame the walls have any of its colour: the frame after its change begins.
      { frame: 51, colour: '#ffcc00', strength: 1, easing: 'constant' },
      { frame: 225, colour: '#3a3f4a', strength: 1, easing: 'constant' },
    ]);
  });

  it('says what the flyby draws at every frame, the frames it changes hands on among them', () => {
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
    // between 224 and 225 — and so do the keys.
    expect(framesOff(made.changes!.caves![0].walls!, walls)).toEqual([]);
    expect(framesOff(made.changes!.caves![0].lines!, lines)).toEqual([]);
  });

  it('is what the recipe that lights each cave in turn writes', () => {
    const recipe = lightTheVisited(flyby([]), { lit: '#ffcc00', dim: '#3a3f4a', dimStrength: 0.6 });
    const document = { ...flyby([]), look: recipe.look, changes: recipe.changes };
    const made = tracks(document);
    expect(made.changes?.caves?.map((cave) => cave.caveId)).toEqual(CAVES);
    // The second cave: lit over a second and a half from frame 250, frame by frame from the frame
    // after its change begins (37.5 frames, to the nearer frame) —
    const second = made.changes?.caves?.[1].walls ?? [];
    expect(second.slice(0, 2).map((key) => [key.frame, key.strength > 0, key.easing])).toEqual([
      [0, true, undefined],
      [251, true, 'constant'],
    ]);
    expect(second.slice(1, 39).map((key) => key.frame)).toEqual(Array.from({ length: 38 }, (_, k) => 251 + k));
    // — and dimmed as the flyby ends, which is one frame past the clip's last: the real end of
    // the change is its key, and the clip stops on the way to it.
    expect(second.slice(38).map((key) => [key.frame, key.strength, key.easing])).toEqual([
      [288, 0.85, undefined],
      [463, 0.85, undefined],
      [500, 0, 'smooth'],
    ]);
    // The first cave's change begins at the flyby's first moment, when it still has the colour of
    // every cave; it dims in full, and frame 250 shows the common colour again.
    const first = made.changes?.caves?.[0].walls ?? [];
    expect(first.slice(0, 2).map((key) => [key.frame, key.colour, key.easing])).toEqual([
      [0, '#3a3f4a', undefined],
      [1, '#ffcc00', 'constant'],
    ]);
    expect(first.slice(-4).map((key) => [key.frame, key.colour, key.strength, key.easing])).toEqual([
      [38, '#ffcc00', 0.85, undefined],
      [213, '#ffcc00', 0.85, undefined],
      [250, '#ffcc00', 0, 'smooth'],
      [250, '#3a3f4a', 0.6, 'constant'],
    ]);
  });

  it('stands in for a common colour that is no colour at all, and the other way round', () => {
    // No common tint: the cave alone has one.
    const alone = tracks(flyby(lit));
    expect(alone.changes?.walls).toBeUndefined();
    expect(alone.changes?.caves?.[0].walls?.map((key) => key.strength)).toEqual([0, 0, 0.85, 0.85, 0]);
    expect(alone.changes?.caves?.[0].walls?.map((key) => key.frame)).toEqual([0, 50, 75, 200, 225]);
    expect(alone.changes?.caves?.[0].lines?.map((key) => [key.frame, key.strength])).toEqual([
      [0, 0],
      [51, 1],
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
  it('fades the lines out and in: a key on the frame the fade begins, with the frames it takes', () => {
    const made = tracks(
      flyby([
        { kind: 'layer', layer: 'centerlines', visible: false, section: 'one', at: 2, seconds: 2 },
        { kind: 'layer', layer: 'centerlines', visible: true, section: 'two', at: 0, seconds: 1 },
      ]),
    );
    expect(made.lines).toBe(true);
    // Nothing is said of how much of them there is: that does not change.
    expect(made.changes).toEqual({
      lines: {
        shown: [
          { frame: 50, shown: false, fadeFrames: 50 },
          { frame: 250, shown: true, fadeFrames: 25 },
        ],
      },
    });
  });

  it('brings in walls that were hidden when the clip began', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    const made = tracks(flyby([{ kind: 'layer', layer: 'walls', visible: true, section: 'one', at: 4, seconds: 2 }]), base);
    expect(made.walls).toBe(true);
    expect(made.changes?.walls).toEqual({ shown: [{ frame: 100, shown: true, fadeFrames: 50 }] });
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

  it('keeps a fade and a change of strength that run together apart: the renderer multiplies them', () => {
    const document = flyby([
      { kind: 'layer', layer: 'centerlines', visible: false, section: 'one', at: 2, seconds: 2 },
      { kind: 'layerOpacity', layer: 'centerlines', opacity: 0.5, section: 'one', at: 3, seconds: 2 },
    ]);
    expect(tracks(document).changes?.lines).toEqual({
      shown: [{ frame: 50, shown: false, fadeFrames: 50 }],
      opacity: [
        { frame: 0, value: 1 },
        { frame: 75, value: 1 },
        { frame: 125, value: 0.5, easing: 'smooth' },
      ],
    });
  });

  it('gives a fade the clip ends in the middle of its whole length, past the clip\'s last frame', () => {
    const document = flyby([{ kind: 'layer', layer: 'walls', visible: false, section: 'one', at: 2, seconds: 2 }]);
    // Three seconds of it: the walls are half gone when the clip ends.
    const made = tracks(document, viewer(), { from: 0, to: 3 });
    expect(made.changes?.walls).toEqual({ shown: [{ frame: 50, shown: false, fadeFrames: 50 }] });
  });

  it('writes a fade the clip begins in the middle of into the strength, frame by frame', () => {
    const document = flyby([{ kind: 'layer', layer: 'walls', visible: false, section: 'one', at: 2, seconds: 2 }]);
    const plan = flybyLookPlan(document);
    // From the third second: half gone at the first frame, gone at frame 25.
    const made = tracks(document, viewer(), { from: 3, to: 8 });
    expect(made.walls).toBe(true);
    expect(made.changes?.walls?.shown).toEqual([{ frame: 25, shown: false }]);
    const opacity = made.changes?.walls?.opacity ?? [];
    expect(opacity.map((key) => key.frame)).toEqual(Array.from({ length: 26 }, (_, k) => k));
    for (const key of opacity.slice(0, 25)) {
      expect(key.value, `frame ${key.frame}`).toBeCloseTo(flybyLookAt(plan, viewer(), 3 + key.frame / FPS).layers.walls.opacity, 6);
    }
    expect(opacity.at(-1)).toEqual({ frame: 25, value: 0 });
  });

  it('writes a fade the clip is held still in the middle of into the strength, and hides nothing', () => {
    const document = flyby([{ kind: 'layer', layer: 'walls', visible: false, section: 'one', at: 2, seconds: 2 }]);
    const plan = flybyLookPlan(document);
    // Three seconds flown and one held: the walls stand half gone for the last second.
    const made = tracks(document, viewer(), { from: 0, to: 3 }, 1);
    expect(made.changes?.walls?.shown).toBeUndefined();
    const opacity = made.changes?.walls?.opacity ?? [];
    expect(opacity.map((key) => key.frame)).toEqual([0, ...Array.from({ length: 26 }, (_, k) => 50 + k)]);
    expect(opacity.at(-1)?.value).toBeCloseTo(flybyLookAt(plan, viewer(), 3).layers.walls.opacity, 6);
    expect(opacity.at(-1)?.value).toBeCloseTo(0.5, 6);
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

  it('ends in the middle of a change on the way to that change\'s own end', () => {
    // The first four seconds: half-way up the ramp at the last frame, the ramp's end fifty frames on.
    const made = tracks(document, viewer(), { from: 0, to: 4 });
    expect(made.changes?.relief).toEqual([
      { frame: 0, value: 1 },
      { frame: 50, value: 1 },
      { frame: 150, value: 3, easing: 'smooth' },
    ]);
    const plan = flybyLookPlan(document);
    for (let frame = 0; frame < 100; frame += 1) {
      expect(made.reliefAt(frame), `frame ${frame}`).toBeCloseTo(flybyReliefAt(plan, undefined, frame / FPS), 6);
    }
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

describe('the daylight of a clip', () => {
  /** A camera that is under the ground from frame `from` up to, and not on, frame `to`. */
  const under = (count: number, ...stays: [number, number][]) =>
    Array.from({ length: count }, (_, frame) => stays.some(([from, to]) => frame >= from && frame < to));

  it('is whole, and says nothing, for a camera that never goes under the ground', () => {
    expect(flybyBundleDaylight(under(100), FPS)).toBeUndefined();
    expect(flybyBundleDaylight([], FPS)).toBeUndefined();
  });

  it('goes over a second from the frame the camera goes under, and is back on the frame it comes out', () => {
    expect(flybyBundleDaylight(under(450, [100, 300]), FPS)).toEqual([
      { frame: 0, value: 1 },
      { frame: 100, value: 1 },
      { frame: 125, value: 0, easing: 'smooth' },
      { frame: 275, value: 0 },
      { frame: 300, value: 1, easing: 'smooth' },
    ]);
  });

  it('begins dark for a clip that begins underground, and stays dark for one that ends there', () => {
    expect(flybyBundleDaylight(under(200, [0, 80]), FPS)).toEqual([
      { frame: 0, value: 0 },
      { frame: 55, value: 0 },
      { frame: 80, value: 1, easing: 'smooth' },
    ]);
    expect(flybyBundleDaylight(under(200, [120, 200]), FPS)).toEqual([
      { frame: 0, value: 1 },
      { frame: 120, value: 1 },
      { frame: 145, value: 0, easing: 'smooth' },
    ]);
    expect(flybyBundleDaylight(under(50, [0, 50]), FPS)).toEqual([{ frame: 0, value: 0 }]);
  });

  it('ends on the way to the dark where the clip ends less than a second after the camera goes under', () => {
    // Ten frames under at the end: the fade's real end is past the clip's last frame.
    expect(flybyBundleDaylight(under(100, [90, 100]), FPS)).toEqual([
      { frame: 0, value: 1 },
      { frame: 90, value: 1 },
      { frame: 115, value: 0, easing: 'smooth' },
    ]);
  });

  it('is half going and half coming back for a stay shorter than two fades', () => {
    expect(flybyBundleDaylight(under(200, [100, 130]), FPS)).toEqual([
      { frame: 0, value: 1 },
      { frame: 100, value: 1 },
      { frame: 115, value: 0, easing: 'smooth' },
      { frame: 130, value: 1, easing: 'smooth' },
    ]);
  });

  it('takes no notice of a dip, or of a rise, shorter than the fade', () => {
    // A camera skimming a slope: under for ten frames.
    expect(flybyBundleDaylight(under(200, [100, 110]), FPS)).toBeUndefined();
    // Out for ten frames in the middle of a long stay underground: it stays dark.
    expect(flybyBundleDaylight(under(300, [50, 150], [160, 250]), FPS)).toEqual([
      { frame: 0, value: 1 },
      { frame: 50, value: 1 },
      { frame: 75, value: 0, easing: 'smooth' },
      { frame: 225, value: 0 },
      { frame: 250, value: 1, easing: 'smooth' },
    ]);
  });
});
