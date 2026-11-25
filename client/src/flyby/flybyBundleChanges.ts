// SPDX-License-Identifier: AGPL-3.0-or-later
import { flybyChangeMember, type FlybyChange, type FlybyColour, type FlybyLayerKey } from './flybyDocument.ts';
import {
  flybyLayerPartsAt,
  flybyLookAt,
  flybyReliefAt,
  type FlybyLookBase,
  type FlybyLookPlan,
  type FlybyLookState,
  type FlybyTimedChange,
} from './flybyLook.ts';
import type { FlybyFrameSchedule } from './flybyMovie.ts';

// What a flyby changes about the look while it plays, as a render bundle is told it.
//
// A bundle's scene is built once and then played: whatever changes during the clip has to be
// written down beforehand as keys — "at this frame the relief is 2" — with a word for how the
// value gets from one key to the next. The renderer knows three such words: held and then jumped
// ("constant"), evenly ("linear"), and eased in and out along 3x² − 2x³ ("smooth"), which is the
// very curve a flyby's changes are eased by. So a change of the flyby is two keys and that word,
// wherever two keys can say it — and what a renderer's keys can say is this:
//
// - A key may lie after the clip's last frame: the clip then ends on the way to it. So a change
//   the clip stops in the middle of is still its two keys.
// - A key may lie before the clip's first frame, at a frame below 0: the clip then begins on the
//   way from it. So a change the clip begins in the middle of is its two keys as well, and a
//   layer that is half faded on the first frame a fade that began before it.
// - Up to three keys may stand on one frame: the first is where the value was going, the second,
//   which jumps, what it is on that frame and from it on, and a third, a jump too, the value the
//   way to the next key starts from, which no frame shows. So a move may end in a jump on the
//   very frame the screen jumps on, and the next move may start from a value the screen never
//   showed.
// - A colour on its way to a key is mixed as a screen mixes two colours, which is how the flyby
//   mixes them. So a colour crossing into another is its two keys too.
// - A layer that is shown or hidden may take a while over it, eased; the renderer multiplies that
//   by the layer's own strength. So a fade is one number on the key that shows or hides.
//
// What is left for a key a frame, joined by straight lines — right at every frame whatever the
// curve was — is a move that is stopped part-way: a clip held still on its last moment in the
// middle of a change. A renderer's curve can begin before the clip and end after it, but it
// cannot be told to stand still half-way.
//
// Two things are decided here and nowhere else:
//
// - A change begins and ends on a frame of the clip. Its moments are moved to the nearest frame
//   before anything is worked out, which shifts it by at most half a frame — and the relief the
//   camera's heights are multiplied by is read from the same keys the caves are stretched by, so
//   the two cannot part company by that half frame.
// - A frame shows what the screen shows at that frame's very moment. Where a cave's own colour
//   takes over from the colour of every cave the screen jumps: at the moment the cave's change
//   begins it still has the colour of every cave, and a moment later its own, at no strength yet.
//   The keys say exactly that — on the frame of the change the old colour, and beside it, as
//   the third key of that frame, the new colour at no strength, which the rise starts from and
//   no frame shows.
//
// Nothing here knows the scene or the server: it is arithmetic on the flyby's look.

/** How a value gets to a key from the key before it; left out, evenly. */
export type FlybyBundleEasing = 'smooth' | 'constant';

/** One key of a track: at `frame` of the clip, counted from 0, the value is `value`. */
export interface FlybyBundleTrackKey {
  frame: number;
  value: number[];
  easing?: FlybyBundleEasing;
}

/** What two values of one kind have to do with each other. */
export interface FlybyBundleTrackSpace {
  /** Whether two values draw the same picture. */
  same(a: readonly number[], b: readonly number[]): boolean;
  /** The value `p` of the way from one to the other, as a renderer works it out between two keys. */
  mix(a: readonly number[], b: readonly number[], p: number): number[];
}

const smoothstep = (p: number) => p * p * (3 - 2 * p);
const lerp = (a: number, b: number, p: number) => a + (b - a) * p;

/** How far two numbers may stand apart and be one value: what a key is written to. */
const NUMBER_TOLERANCE = 1e-6;
/** How far two colours may: one step of the 256 a channel has on a screen. */
const CHANNEL_TOLERANCE = 1;

/** Two colours mixed as a screen mixes them: each channel straight from one number to the other. */
const mixChannels = (a: readonly number[], b: readonly number[], p: number) => [0, 1, 2].map((i) => lerp(a[i], b[i], p));
const sameChannels = (a: readonly number[], b: readonly number[]) =>
  [0, 1, 2].every((i) => Math.abs(a[i] - b[i]) <= CHANNEL_TOLERANCE);

/** A plain number: a factor, a strength. */
export const FLYBY_BUNDLE_NUMBER: FlybyBundleTrackSpace = {
  same: (a, b) => Math.abs(a[0] - b[0]) <= NUMBER_TOLERANCE,
  mix: (a, b, p) => [lerp(a[0], b[0], p)],
};

/** A colour, as the three channels a screen is given, 0–255. */
export const FLYBY_BUNDLE_COLOUR: FlybyBundleTrackSpace = { same: sameChannels, mix: mixChannels };

/**
 * A colour laid over something by a strength: three channels and the strength, 0–1. At no strength
 * the colour is not seen, so two tints of no strength are the same whatever their colours.
 */
export const FLYBY_BUNDLE_TINT: FlybyBundleTrackSpace = {
  same: (a, b) =>
    (a[3] <= NUMBER_TOLERANCE && b[3] <= NUMBER_TOLERANCE) ||
    (Math.abs(a[3] - b[3]) <= NUMBER_TOLERANCE && sameChannels(a, b)),
  mix: (a, b, p) => [...mixChannels(a, b, p), lerp(a[3], b[3], p)],
};

/**
 * The value a track has at a frame: what a renderer playing its keys shows there. Before its first
 * key a track has that key's value — a first key at a frame below 0 is one the clip begins on the
 * way from. Of the keys on one frame the second is what that frame shows, and the last the value
 * the way to the next key starts from: the second again, unless a third says otherwise.
 */
export function flybyBundleTrackAt(keys: readonly FlybyBundleTrackKey[], frame: number, space: FlybyBundleTrackSpace): number[] {
  // The last key on the last frame that has any, at this frame or before it.
  let last = -1;
  while (last + 1 < keys.length && keys[last + 1].frame <= frame) {
    last += 1;
  }
  if (last < 0) {
    return [...keys[0].value];
  }
  const before = keys[last];
  if (before.frame === frame) {
    let first = last;
    while (first > 0 && keys[first - 1].frame === frame) {
      first -= 1;
    }
    return [...keys[Math.min(first + 1, last)].value];
  }
  const key = keys[last + 1];
  if (!key || key.easing === 'constant') {
    return [...before.value];
  }
  const share = (frame - before.frame) / (key.frame - before.frame);
  return space.mix(before.value, key.value, key.easing === 'smooth' ? smoothstep(share) : share);
}

/** The very same numbers: two values may draw one picture and still be two starts for a curve. */
const equal = (a: readonly number[], b: readonly number[]) => a.every((part, i) => Math.abs(part - b[i]) <= 1e-9);

/**
 * The keys of one value over a clip.
 *
 * `sample(frame, side)` is the value at a frame — at its very moment (`0`), which is what the frame
 * shows, or just before (`-1`) or just after it (`1`), which differ only where the value jumps.
 * `breaks` are the frames at which something that moves the value begins or ends; between two of
 * them the value holds or moves in one piece, and each piece is written with as few keys as say it
 * exactly. A break after `last` is where the piece the clip ends in really ends, and one before 0
 * where the piece it begins in really began — `sample` is then asked about those frames too. Either
 * is left out by whoever does not want the piece said by its real ends: a clip held still before
 * the end of a piece leaves that end out.
 */
export function flybyBundleTrack(
  sample: (frame: number, side: -1 | 0 | 1) => number[],
  breaks: readonly number[],
  last: number,
  space: FlybyBundleTrackSpace,
): FlybyBundleTrackKey[] {
  const keys: FlybyBundleTrackKey[] = [{ frame: 0, value: sample(0, 0) }];
  const standing = () => keys[keys.length - 1];
  /** How many keys stand on the frame of the last one. */
  const onStanding = () => {
    let count = 1;
    while (count < keys.length && keys[keys.length - 1 - count].frame === standing().frame) {
      count += 1;
    }
    return count;
  };
  const push = (frame: number, value: number[], easing?: FlybyBundleEasing) => {
    keys.push(easing ? { frame, value, easing } : { frame, value });
  };
  /** From `frame` on the value is `value`, wherever it was going before. */
  const jump = (frame: number, value: number[]) => {
    const key = standing();
    if (key.frame === frame && (keys.length === 1 || key.easing === 'constant' || keys[keys.length - 2].frame === frame)) {
      // The key of this frame is the clip's first, or a jump already: it is to here instead.
      key.value = value;
    } else {
      push(frame, value, 'constant');
    }
  };
  /** A key on `frame` with exactly `value`, for a curve to start from; the frame already shows that picture. */
  const startAt = (frame: number, value: number[]) => {
    const key = standing();
    if (key.frame < frame) {
      push(frame, value, equal(key.value, value) ? undefined : 'constant');
    } else if (!equal(key.value, value)) {
      jump(frame, value);
    }
  };
  /**
   * `frame` shows `shown`, and the way on starts from `origin`, which no frame shows: three keys
   * on the frame — where the value was going, what the frame shows, and where the way on starts.
   */
  const startBeside = (frame: number, shown: number[], origin: number[]) => {
    if (standing().frame < frame) {
      push(frame, shown);
    }
    if (onStanding() === 1) {
      push(frame, shown, 'constant');
    }
    if (onStanding() === 2) {
      standing().value = shown;
      push(frame, origin, 'constant');
    } else {
      standing().value = origin;
    }
  };

  const whole = [...new Set(breaks.filter((frame) => Number.isInteger(frame)))].sort((a, b) => a - b);
  // Where the piece the clip begins in really began, when that is before the clip.
  const began = whole.includes(0) ? undefined : whole.filter((frame) => frame < 0).pop();
  const ends = [...new Set([0, last, ...whole.filter((frame) => frame > 0 && frame < last)])].sort((a, b) => a - b);
  // Where the piece the clip ends in really ends, when that is after the clip.
  const beyond = whole.includes(last) ? undefined : whole.find((frame) => frame > last);
  if (beyond !== undefined && last > 0) {
    ends[ends.length - 1] = beyond;
  }

  for (let n = 0; n + 1 < ends.length; n += 1) {
    const b = ends[n + 1];
    // The first piece from its real start, where that says it: tried first, and from the clip's
    // first frame where it does not.
    for (const a of n === 0 && began !== undefined ? [began, 0] : [ends[n]]) {
      const from = sample(a, 1);
      const to = sample(b, -1);
      // The frames of the piece that are seen: from the clip's first, up to the piece's end or the clip's.
      const seen: number[] = [];
      for (let frame = Math.max(a + 1, 0); frame <= Math.min(b - 1, last); frame += 1) {
        seen.push(frame);
      }
      const inner = seen.map((frame) => sample(frame, 0));
      const held = space.same(to, from) && inner.every((value) => space.same(value, from));
      const share = (k: number) => (seen[k] - a) / (b - a);
      const smooth = inner.length > 0 && inner.every((value, k) => space.same(value, space.mix(from, to, smoothstep(share(k)))));
      const even = inner.every((value, k) => space.same(value, space.mix(from, to, share(k))));

      if (a < 0) {
        // A piece the clip begins in the middle of: its two keys, the first before the clip —
        // where two keys say every frame of it that is seen.
        if (held || !(smooth || even)) {
          continue;
        }
        keys[0] = { frame: a, value: from };
        push(b, to, smooth ? 'smooth' : undefined);
      } else if (!space.same(sample(a, 0), from)) {
        // The screen jumps a moment after this frame: the frame itself still shows what was, and
        // what moves from here starts from a value the frame does not show. Held, that is a
        // jump on the next frame; moving, the start stands beside what the frame shows, as the
        // third key of the frame — or, where two keys cannot say the move or it is over in fewer
        // frames than that takes keys, it is written frame by frame from the frame after.
        if (held) {
          if (a + 1 <= last) {
            jump(a + 1, from);
          }
        } else if (inner.length > 3 && (smooth || even)) {
          startBeside(a, sample(a, 0), from);
          push(b, to, smooth ? 'smooth' : undefined);
        } else {
          inner.forEach((value, k) => (k === 0 ? jump(a + 1, value) : push(a + 1 + k, value)));
        }
      } else if (!held) {
        startAt(a, from);
        if (smooth) {
          push(b, to, 'smooth');
        } else if (even) {
          push(b, to);
        } else {
          inner.forEach((value, k) => push(a + 1 + k, value));
        }
      }

      // The frame the piece ends on shows what the screen shows there. A curve's own end is where
      // the value was going: what the frame shows instead stands beside it on that frame, as a jump.
      if (b <= last) {
        const shown = sample(b, 0);
        if (!space.same(flybyBundleTrackAt(keys, b, space), shown)) {
          if (!held && standing().frame === b - 1 && space.same(shown, to)) {
            // The last straight piece of a curve written frame by frame.
            push(b, to);
          } else {
            jump(b, shown);
          }
        }
      }
      break;
    }
  }

  // A key between two keys of the very value it has says nothing. The very value, and not one
  // that draws the same picture: a key that is a shade off its neighbours is where a slow change
  // starts from. And not one of three on a frame: there each says something by where it stands.
  const onFrame = new Map<number, number>();
  for (const key of keys) {
    onFrame.set(key.frame, (onFrame.get(key.frame) ?? 0) + 1);
  }
  return keys.filter((key, index) => {
    const before = keys[index - 1];
    const after = keys[index + 1];
    return !before || !after || (onFrame.get(key.frame) ?? 0) > 2 || !equal(before.value, key.value) || !equal(key.value, after.value);
  });
}

// ---- the flyby's look, on the clip's frames ----

/** A number at a frame of the clip. */
export interface FlybyBundleNumberKey {
  frame: number;
  value: number;
  easing?: FlybyBundleEasing;
}

/** A colour at a frame of the clip. */
export interface FlybyBundleColourKey {
  frame: number;
  colour: FlybyColour;
  easing?: FlybyBundleEasing;
}

/** A colour laid over something by `strength`, 0–1, at a frame of the clip. */
export interface FlybyBundleTintKey {
  frame: number;
  colour: FlybyColour;
  strength: number;
  easing?: FlybyBundleEasing;
}

/** From this frame on a layer is shown, or hidden; each says the opposite of the one before it. */
export interface FlybyBundleShownKey {
  frame: number;
  shown: boolean;
  /**
   * How many frames, from this one on, the layer takes to appear, or to go before it is hidden,
   * eased as a flyby eases; left out, it is cut.
   */
  fadeFrames?: number;
}

/** What changes of one layer of the scene. A member left out does not change, and is as a bare scene has it. */
export interface FlybyBundleLayerChanges {
  /** When it is shown and hidden, and how long it takes over it. Before the first key it is the other way round. */
  shown?: FlybyBundleShownKey[];
  /** How much of it is there, 0–1, besides its coming and going. One key: that much throughout. */
  opacity?: FlybyBundleNumberKey[];
  /**
   * For walls, the colour mixed into their own by `strength`. For survey lines, the one colour
   * they are drawn in (strength 1) or their colours by depth (strength 0).
   */
  tint?: FlybyBundleTintKey[];
}

/** One cave whose colours go their own way: what stands in for the colours of every cave. */
export interface FlybyBundleCaveChanges {
  caveId: string;
  walls?: FlybyBundleTintKey[];
  lines?: FlybyBundleTintKey[];
}

/** Everything a flyby changes about the look during a clip that a bundle carries. */
export interface FlybyBundleChanges {
  /** How many times taller than true the scene is drawn; left out, one factor throughout. */
  relief?: FlybyBundleNumberKey[];
  /** What stands behind everything; no sky is black. Left out, one colour throughout. */
  sky?: FlybyBundleColourKey[];
  walls?: FlybyBundleLayerChanges;
  lines?: FlybyBundleLayerChanges;
  caves?: FlybyBundleCaveChanges[];
  /** How much of the daylight is there, 0–1; left out, all of it throughout. */
  daylight?: FlybyBundleNumberKey[];
}

/** The look of a clip as a bundle is told it. */
export interface FlybyBundleLookTracks {
  /** Whether walls are shown at any frame of the clip. */
  walls: boolean;
  /** Whether survey lines are. */
  lines: boolean;
  /** The relief at each frame of the clip, as the keys give it. */
  reliefAt(frame: number): number;
  /** What stands behind everything at the clip's first frame; left out, black. */
  sky?: FlybyColour;
  /** The one colour every survey line has at the clip's first frame; left out, their colours by depth. */
  lineColour?: FlybyColour;
  /** Null where nothing changes and nothing stands other than a bare scene has it. */
  changes: FlybyBundleChanges | null;
  /**
   * The other layers asked about, by key: whether each is shown at any frame of the clip, when it
   * is shown and hidden, and how much of it is there besides — left out where that is all of it.
   */
  layers: Record<FlybyLayerKey, FlybyBundleLayerShown>;
}

/** How one layer of the scene comes and goes over a clip. */
export interface FlybyBundleLayerShown {
  ever: boolean;
  shown: FlybyBundleShownKey[];
  opacity?: FlybyBundleNumberKey[];
}

/**
 * How near to a moment "just before" and "just after" it are, as a share of the change that ends or
 * begins there: near enough that a value which does not jump is the same number to a millionth,
 * and far enough that the change is not yet over, or has begun.
 */
const BESIDE = 1e-7;

/** How close two moments are when they are one: far less than a frame. */
const SAME_MOMENT = 1e-9;

const BLACK: FlybyColour = '#000000';

function hex(channels: readonly number[]): FlybyColour {
  return `#${[0, 1, 2].map((i) => Math.min(255, Math.max(0, Math.round(channels[i]))).toString(16).padStart(2, '0')).join('')}`;
}

function channelsOf(colour: FlybyColour): number[] {
  return [1, 3, 5].map((at) => Number.parseInt(colour.slice(at, at + 2), 16));
}

const rounded = (value: number) => Math.round(value * 1e6) / 1e6;

function numberKeys(keys: readonly FlybyBundleTrackKey[]): FlybyBundleNumberKey[] {
  return keys.map((key) => ({ frame: key.frame, value: rounded(key.value[0]), ...(key.easing ? { easing: key.easing } : {}) }));
}

function colourKeys(keys: readonly FlybyBundleTrackKey[]): FlybyBundleColourKey[] {
  return keys.map((key) => ({ frame: key.frame, colour: hex(key.value), ...(key.easing ? { easing: key.easing } : {}) }));
}

function tintKeys(keys: readonly FlybyBundleTrackKey[]): FlybyBundleTintKey[] {
  return keys.map((key) => ({
    frame: key.frame,
    colour: hex(key.value),
    strength: rounded(Math.min(1, Math.max(0, key.value[3]))),
    ...(key.easing ? { easing: key.easing } : {}),
  }));
}

/**
 * The flyby's look with every change moved onto the frames of a clip: each begins and ends at the
 * frame nearest to where the flyby has it.
 */
export function flybyBundleLookOnFrames(plan: FlybyLookPlan, schedule: Pick<FlybyFrameSchedule, 'from' | 'fps'>): FlybyLookPlan {
  const onFrame = (t: number) => schedule.from + Math.round((t - schedule.from) * schedule.fps) / schedule.fps;
  return { ...plan, changes: plan.changes.map((timed) => ({ change: timed.change, start: onFrame(timed.start), end: onFrame(timed.end) })) };
}

/**
 * The look as it is just before moment `t` (`side` −1) or just after it (1): the plan with the
 * changes that end or begin exactly at `t` nudged so that, asked about `t` itself, they are not
 * quite over, or have just begun. Asking about a moment a little earlier or later instead would
 * be near for one change and not for another: a moment's breadth that leaves a long change
 * unfinished is past the end of a short one.
 */
function beside(plan: FlybyLookPlan, t: number, side: -1 | 1): FlybyLookPlan {
  const at = (moment: number) => Math.abs(moment - t) <= SAME_MOMENT;
  let moved = false;
  const changes = plan.changes.map((timed): FlybyTimedChange => {
    const length = timed.end - timed.start;
    if (side < 0 && at(timed.start)) {
      // Not begun: it starts later.
      moved = true;
      return { change: timed.change, start: timed.start + 1, end: timed.end + 1 };
    }
    if (side < 0 && at(timed.end) && length > 0) {
      // Not quite over: it ends a little later.
      moved = true;
      return { change: timed.change, start: timed.start, end: timed.end + length * BESIDE };
    }
    if (side > 0 && at(timed.start) && length > 0) {
      // Just begun: it started a little earlier.
      moved = true;
      return { change: timed.change, start: timed.start - length * BESIDE, end: timed.end };
    }
    return timed;
  });
  return moved ? { ...plan, changes } : plan;
}

/**
 * What a flyby changes about the look over a clip, as keys on the clip's frames.
 *
 * `caves` are the caves the bundle names; one of them has tracks of its own only where the flyby
 * colours it apart. `relief` is false where the view does not let a flyby stretch the relief: the
 * viewer's own factor then stands throughout. `others` are layers of the scene besides the walls and
 * the lines — the entrances, a layer of features, the photographs — whose coming and going is
 * wanted as keys too.
 */
export function flybyBundleLookTracks(
  look: FlybyLookPlan,
  base: FlybyLookBase,
  schedule: FlybyFrameSchedule,
  caves: readonly string[],
  relief = true,
  others: readonly FlybyLayerKey[] = [],
): FlybyBundleLookTracks {
  const plan = flybyBundleLookOnFrames(look, schedule);
  const last = Math.max(0, schedule.count - 1);
  // The frames held at the end all show the moment the flight stops at, and nothing comes after
  // that moment: whatever the look is there, it is for every one of them.
  const stops = schedule.held > 0 ? schedule.moving : Number.POSITIVE_INFINITY;
  const momentOf = (frame: number) => schedule.from + Math.min(frame, stops) / schedule.fps;
  /** The plan to ask about a frame's moment: as it is, or as it is to one side of that moment. */
  const planAt = (frame: number, side: -1 | 0 | 1): FlybyLookPlan =>
    side === 0 || frame > stops || (frame === stops && side > 0) ? plan : beside(plan, momentOf(frame), side);
  // Every track asks about the same moments, and the look of a moment is worked out once.
  const states = new Map<string, FlybyLookState>();
  const stateAt = (frame: number, side: -1 | 0 | 1): FlybyLookState => {
    const asked = planAt(frame, side);
    const key = `${Math.min(frame, stops)}:${asked === plan ? 0 : side}`;
    let state = states.get(key);
    if (!state) {
      state = flybyLookAt(asked, base, momentOf(frame));
      states.set(key, state);
    }
    return state;
  };
  const frameOf = (t: number) => Math.round((t - schedule.from) * schedule.fps);
  // Where a value can start or stop moving: at the ends of the changes that move it, and at the
  // frame the flight stops on, after which every frame is held. A change the clip ends in the
  // middle of ends after the clip — unless the clip is held still before that — and one it
  // begins in the middle of began before it.
  const breaksOf = (...members: string[]) =>
    [
      ...plan.changes
        .filter((timed) => members.includes(flybyChangeMember(timed.change)))
        .flatMap((timed) => [frameOf(timed.start), frameOf(timed.end)]),
      ...(schedule.held > 0 ? [schedule.moving] : []),
    ].filter((frame) => frame <= stops);

  // ---- relief ----
  const reliefKeys = relief
    ? flybyBundleTrack(
        (frame, side) => [flybyReliefAt(planAt(frame, side), base.relief, momentOf(frame))],
        breaksOf('relief'),
        last,
        FLYBY_BUNDLE_NUMBER,
      )
    : [{ frame: 0, value: [base.relief ?? 1] }];

  // ---- sky ----
  const skyKeys = flybyBundleTrack((frame, side) => channelsOf(stateAt(frame, side).sky ?? BLACK), breaksOf('sky'), last, FLYBY_BUNDLE_COLOUR);

  // ---- the layers a bundle draws: the walls, the lines, and whatever else is asked about ----
  const layer = (key: FlybyLayerKey): FlybyBundleLayerShown => {
    const member = `layer:${key}`;
    const first = flybyLayerPartsAt(plan, base, key, momentOf(0)).visible;

    // Each change that shows the layer or hides it, as the renderer is told it: a key on the
    // frame it begins on, with the frames it takes — the renderer fades and eases it, and hides
    // a layer that goes when it has gone. That says a fade the clip has whole, ends in the
    // middle of, or begins in the middle of: its key is then on a frame before the clip's first.
    // One the clip is held still in the middle of is `folded`: its key is a cut, and how far it
    // has got is multiplied into the layer's strength.
    const shown: FlybyBundleShownKey[] = [];
    const folded = new Set<FlybyChange>();
    let on = flybyLayerPartsAt(plan, base, key, Number.NEGATIVE_INFINITY).visible;
    for (const timed of plan.changes) {
      if (timed.change.kind !== 'layer' || flybyChangeMember(timed.change) !== member || timed.change.visible === on) {
        continue;
      }
      on = timed.change.visible;
      const start = frameOf(timed.start);
      const end = frameOf(timed.end);
      if (end <= 0 || start > Math.min(last, stops)) {
        // Over by the clip's first frame, or not begun by the last moment it shows: not of this clip.
        continue;
      }
      if (end > start && end > stops) {
        folded.add(timed.change);
        if (on && start >= 0) {
          shown.push({ frame: start, shown: true });
        } else if (!on && end <= Math.min(last, stops)) {
          shown.push({ frame: end, shown: false });
        }
      } else {
        shown.push({ frame: start, shown: on, ...(end > start ? { fadeFrames: end - start } : {}) });
      }
    }

    // How much of the layer is there besides its coming and going: its own strength — and how
    // far a folded fade has got, from the moment it begins until the next change of the layer.
    const strength = (frame: number, side: -1 | 0 | 1): number[] => {
      const asked = planAt(frame, side);
      const t = momentOf(frame);
      const parts = flybyLayerPartsAt(asked, base, key, t);
      const begun = asked.changes.filter((timed) => flybyChangeMember(timed.change) === member && timed.start <= t);
      const latest = begun[begun.length - 1];
      return [latest && folded.has(latest.change) ? parts.strength * parts.fade : parts.strength];
    };
    const folds = plan.changes
      .filter((timed) => folded.has(timed.change))
      .flatMap((timed) => [frameOf(timed.start), frameOf(timed.end)])
      .filter((frame) => frame > 0 && frame <= stops);
    const opacity = numberKeys(flybyBundleTrack(strength, [...breaksOf(`layerOpacity:${key}`), ...folds], last, FLYBY_BUNDLE_NUMBER));
    return {
      ever: first || shown.some((said) => said.shown),
      shown,
      opacity: opacity.length > 1 || opacity[0].value < 1 ? opacity : undefined,
    };
  };
  const walls = layer('walls');
  const lines = layer('centerlines');

  // ---- colours: of every cave, and of the caves coloured apart ----
  const subjectsOf = (caveId: string) =>
    Object.entries(plan.caveOf)
      .filter(([, id]) => id === caveId)
      .map(([subject]) => `cave:${subject}`);
  const none = [0, 0, 0, 0];
  const wallsTintOf = (state: FlybyLookState, caveId?: string): number[] => {
    const own = caveId === undefined ? undefined : state.caves?.[caveId];
    const tint = own && own.strength > 0 ? own : state.walls;
    return tint && tint.strength > 0 ? [...channelsOf(tint.colour), tint.strength] : none;
  };
  const linesTintOf = (state: FlybyLookState, caveId?: string): number[] => {
    const own = caveId === undefined ? undefined : state.caves?.[caveId];
    const colour = own && own.strength > 0 ? own.colour : state.lines;
    return colour ? [...channelsOf(colour), 1] : none;
  };
  const tinted = (of: (state: FlybyLookState, caveId?: string) => number[], members: string[], caveId?: string) => {
    const keys = tintKeys(
      flybyBundleTrack((frame, side) => of(stateAt(frame, side), caveId), breaksOf(...members, ...(caveId ? subjectsOf(caveId) : [])), last, FLYBY_BUNDLE_TINT),
    );
    return keys.some((key) => key.strength > 0) ? keys : undefined;
  };
  const wallsTint = tinted(wallsTintOf, ['walls']);
  const linesTint = tinted(linesTintOf, ['lines']);
  const apart: FlybyBundleCaveChanges[] = [];
  for (const caveId of caves) {
    if (subjectsOf(caveId).length === 0) {
      continue;
    }
    const ownWalls = tinted(wallsTintOf, ['walls'], caveId);
    const ownLines = tinted(linesTintOf, ['lines'], caveId);
    const differs = (own: FlybyBundleTintKey[] | undefined, common: FlybyBundleTintKey[] | undefined) =>
      JSON.stringify(own ?? null) !== JSON.stringify(common ?? null);
    if (differs(ownWalls, wallsTint) || differs(ownLines, linesTint)) {
      // A cave's own track stands in for the common one whole, so it is said even where it is
      // "no colour at all" while every other cave has one.
      apart.push({
        caveId,
        ...(differs(ownWalls, wallsTint) ? { walls: ownWalls ?? [{ frame: 0, colour: BLACK, strength: 0 }] } : {}),
        ...(differs(ownLines, linesTint) ? { lines: ownLines ?? [{ frame: 0, colour: BLACK, strength: 0 }] } : {}),
      });
    }
  }

  const layerChanges = (ever: boolean, shown: FlybyBundleShownKey[], opacity?: FlybyBundleNumberKey[], tint?: FlybyBundleTintKey[]) => {
    const said: FlybyBundleLayerChanges = {
      ...(shown.length > 0 ? { shown } : {}),
      ...(opacity ? { opacity } : {}),
      ...(tint ? { tint } : {}),
    };
    return ever && Object.keys(said).length > 0 ? said : undefined;
  };
  const wallsSaid = layerChanges(walls.ever, walls.shown, walls.opacity, wallsTint);
  // One colour for the lines throughout is the colour of the first frame, which the look itself
  // says; a track is for a colour that changes.
  const linesSaid = layerChanges(lines.ever, lines.shown, lines.opacity, linesTint && linesTint.length > 1 ? linesTint : undefined);
  const changes: FlybyBundleChanges = {
    ...(reliefKeys.length > 1 ? { relief: numberKeys(reliefKeys) } : {}),
    ...(skyKeys.length > 1 ? { sky: colourKeys(skyKeys) } : {}),
    ...(wallsSaid ? { walls: wallsSaid } : {}),
    ...(linesSaid ? { lines: linesSaid } : {}),
    ...(apart.length > 0 ? { caves: apart } : {}),
  };

  // What the first frame shows: a first key before the clip is where the value was, not what it is.
  const sky = hex(flybyBundleTrackAt(skyKeys, 0, FLYBY_BUNDLE_COLOUR));
  const linesFirst = linesTint
    ? flybyBundleTrackAt(
        linesTint.map((key) => ({ frame: key.frame, value: [...channelsOf(key.colour), key.strength], easing: key.easing })),
        0,
        FLYBY_BUNDLE_TINT,
      )
    : undefined;
  return {
    walls: walls.ever,
    lines: lines.ever,
    layers: Object.fromEntries([...new Set(others)].map((key) => [key, layer(key)])),
    reliefAt: (frame) => rounded(flybyBundleTrackAt(reliefKeys, Math.min(Math.max(frame, 0), last), FLYBY_BUNDLE_NUMBER)[0]),
    ...(sky !== BLACK ? { sky } : {}),
    ...(linesFirst && linesFirst[3] > 0 ? { lineColour: hex(linesFirst) } : {}),
    changes: Object.keys(changes).length > 0 ? changes : null,
  };
}

// ---- the daylight ----

/** How long the daylight takes to go when the camera goes under the ground, and to come back, in seconds. */
export const FLYBY_BUNDLE_DAYLIGHT_FADE_S = 1;

/**
 * How much of the daylight a render has over a clip, from whether the camera of each frame is
 * under the ground: the sun and the sky of a bundle are that strong.
 *
 * The rule, which is stated to whoever makes a bundle and in the bundle's own notes:
 *
 * - The camera is underground at a frame when its eye is below the ground of the scene the flyby
 *   was played in, at the place the eye stands over. The ground is the scene's own — whatever
 *   terrain it had loaded, or its flat surface — and it counts whether or not the flyby cuts it
 *   open above a cave, because a bundle holds no ground to cut.
 * - A dip under the ground, or a rise out of it, that lasts less than the fade and has the clip
 *   going on at both ends of it is no change of place: a camera skimming a slope does not flicker
 *   the sun.
 * - The daylight goes, eased, over the fade from the frame the camera goes under, and is back,
 *   eased, on the frame it comes out — having risen over the fade before that. A stay underground
 *   shorter than two fades is half going and half coming back. A clip that begins underground
 *   begins dark, and one that ends there stays dark.
 *
 * Undefined when the camera is never underground: the daylight is then whole throughout and
 * nothing needs saying.
 */
export function flybyBundleDaylight(underground: readonly boolean[], fps: number): FlybyBundleNumberKey[] | undefined {
  const fade = Math.max(1, Math.round(fps * FLYBY_BUNDLE_DAYLIGHT_FADE_S));
  // Runs of frames on one side of the ground: the first frame, the frame after the last, and the side.
  let runs: { from: number; to: number; under: boolean }[] = [];
  underground.forEach((under, frame) => {
    const run = runs[runs.length - 1];
    if (run && run.under === under) {
      run.to = frame + 1;
    } else {
      runs.push({ from: frame, to: frame + 1, under });
    }
  });
  // The shortest run that is too short to count joins its neighbours, until none is left.
  for (;;) {
    const inside = runs.slice(1, -1).filter((run) => run.to - run.from < fade);
    if (inside.length === 0) {
      break;
    }
    const shortest = inside.reduce((a, b) => (b.to - b.from < a.to - a.from ? b : a));
    const at = runs.indexOf(shortest);
    runs = [...runs.slice(0, at - 1), { from: runs[at - 1].from, to: runs[at + 1].to, under: runs[at - 1].under }, ...runs.slice(at + 2)];
  }
  if (!runs.some((run) => run.under)) {
    return undefined;
  }

  const keys: FlybyBundleNumberKey[] = [{ frame: 0, value: runs[0].under ? 0 : 1 }];
  const put = (frame: number, value: number, easing?: FlybyBundleEasing) => {
    const key = keys[keys.length - 1];
    if (key.frame === frame && key.value === value) {
      return;
    }
    keys.push(easing ? { frame, value, easing } : { frame, value });
  };
  for (const run of runs.filter((each) => each.under)) {
    const goes = run.from > 0;
    const comes = run.to < underground.length;
    const takes = goes && comes ? Math.min(fade, Math.floor((run.to - run.from) / 2)) : Math.min(fade, run.to - run.from);
    if (goes) {
      put(run.from, 1);
      // Past the clip's last frame where the clip ends first: it ends on the way to the dark.
      put(run.from + (comes ? takes : fade), 0, 'smooth');
    }
    if (comes) {
      put(run.to - takes, 0);
      put(run.to, 1, 'smooth');
    }
  }
  return keys;
}
