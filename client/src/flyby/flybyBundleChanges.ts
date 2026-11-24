// SPDX-License-Identifier: AGPL-3.0-or-later
import { flybyChangeMember, type FlybyColour } from './flybyDocument.ts';
import { flybyLookAt, flybyReliefAt, type FlybyLookBase, type FlybyLookPlan, type FlybyLookState } from './flybyLook.ts';
import type { FlybyFrameSchedule } from './flybyMovie.ts';

// What a flyby changes about the look while it plays, as a render bundle is told it.
//
// A bundle's scene is built once and then played: whatever changes during the clip has to be
// written down beforehand as keys — "at this frame the relief is 2" — with a word for how the
// value gets from one key to the next. The renderer knows three such words: held and then jumped
// ("constant"), evenly ("linear"), and eased in and out along 3x² − 2x³ ("smooth"), which is the
// very curve a flyby's changes are eased by. So a change of the flyby is two keys and that word,
// wherever two keys can say it; and where they cannot — two things moving one value at once, a
// clip that starts in the middle of a change, a colour crossing into another (mixed here as the
// screen mixes it, which is not how a renderer mixes two colours) — the value is written down
// frame by frame, joined by straight lines, which is right at every frame whatever the curve was.
//
// Two things are decided here and nowhere else:
//
// - A change begins and ends on a frame of the clip. Its moments are moved to the nearest frame
//   before anything is worked out, which shifts it by at most half a frame — and the relief the
//   camera's heights are multiplied by is read from the same keys the caves are stretched by, so
//   the two cannot part company by that half frame.
// - A value that jumps keeps one frame to itself. Where a cave's own colour takes over from the
//   colour of every cave, the screen goes from one to the other between two frames; a track has
//   one value a frame, so the jump stands on the frame the change starts at, or on the frame
//   after the one it ends at.
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

/** A channel of a screen colour, 0–255, as the light it stands for, 0–1; and back. */
const toLight = (channel: number) => {
  const c = channel / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};
const toChannel = (light: number) => 255 * (light <= 0.0031308 ? light * 12.92 : 1.055 * light ** (1 / 2.4) - 0.055);

/** A renderer mixes two colours as light, not as the numbers a screen is given. */
const mixChannels = (a: readonly number[], b: readonly number[], p: number) =>
  [0, 1, 2].map((i) => toChannel(lerp(toLight(a[i]), toLight(b[i]), p)));
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

/** The value a track has at a frame: what a renderer playing its keys shows there. */
export function flybyBundleTrackAt(keys: readonly FlybyBundleTrackKey[], frame: number, space: FlybyBundleTrackSpace): number[] {
  let before = keys[0];
  for (const key of keys) {
    if (key.frame > frame) {
      if (key.easing === 'constant' || before === key) {
        return [...before.value];
      }
      const share = (frame - before.frame) / (key.frame - before.frame);
      return space.mix(before.value, key.value, key.easing === 'smooth' ? smoothstep(share) : share);
    }
    before = key;
  }
  return [...before.value];
}

/**
 * The keys of one value over a clip.
 *
 * `sample(frame, side)` is the value at a frame of the clip — at its very moment (`0`), or just
 * before (`-1`) or just after it (`1`), which differ only where the value jumps. `breaks` are the
 * frames at which something that moves the value begins or ends; between two of them the value
 * holds or moves in one piece, and each piece is written with as few keys as say it exactly.
 */
export function flybyBundleTrack(
  sample: (frame: number, side: -1 | 0 | 1) => number[],
  breaks: readonly number[],
  last: number,
  space: FlybyBundleTrackSpace,
): FlybyBundleTrackKey[] {
  const keys: FlybyBundleTrackKey[] = [{ frame: 0, value: sample(0, last > 0 ? 1 : 0) }];
  const put = (frame: number, value: number[], easing?: FlybyBundleEasing) => {
    keys.push(easing ? { frame, value, easing } : { frame, value });
  };
  const ends = [...new Set([0, last, ...breaks.filter((frame) => Number.isInteger(frame) && frame > 0 && frame < last)])].sort(
    (a, b) => a - b,
  );

  for (let n = 0; n + 1 < ends.length; n += 1) {
    const a = ends[n];
    const b = ends[n + 1];
    const from = sample(a, 1);
    const to = sample(b, -1);
    const inner: number[][] = [];
    for (let frame = a + 1; frame < b; frame += 1) {
      inner.push(sample(frame, 0));
    }
    const held = space.same(to, from) && inner.every((value) => space.same(value, from));
    const standing = keys[keys.length - 1];

    if (standing.frame === a && !space.same(standing.value, from)) {
      // The piece before this one came to its end on this very frame, with another value: the
      // jump to this piece's start waits for the next frame, and from there the piece is written
      // frame by frame — a curve has to start at a key, and its key's frame is taken.
      if (held) {
        put(a + 1, from, 'constant');
        continue;
      }
      inner.forEach((value, k) => put(a + 1 + k, value, k === 0 ? 'constant' : undefined));
      put(b, to, inner.length === 0 ? 'constant' : undefined);
      continue;
    }
    if (standing.frame < a && !space.same(standing.value, from)) {
      put(a, from, 'constant');
    }
    if (held) {
      continue;
    }
    if (keys[keys.length - 1].frame < a) {
      put(a, from);
    }
    const share = (k: number) => (k + 1) / (b - a);
    if (inner.length > 0 && inner.every((value, k) => space.same(value, space.mix(from, to, smoothstep(share(k)))))) {
      put(b, to, 'smooth');
    } else if (inner.every((value, k) => space.same(value, space.mix(from, to, share(k))))) {
      put(b, to);
    } else {
      inner.forEach((value, k) => put(a + 1 + k, value));
      put(b, to);
    }
  }

  // The clip's last frame itself, where something happens on exactly it.
  const end = sample(last, 0);
  const standing = keys[keys.length - 1];
  if (!space.same(flybyBundleTrackAt(keys, last, space), end)) {
    if (standing.frame < last) {
      put(last, end, 'constant');
    } else {
      standing.value = end;
    }
  }

  // A key between two keys of the very value it has says nothing. The very value, and not one
  // that draws the same picture: a key that is a shade off its neighbours is where a slow change
  // starts from.
  const equal = (a: readonly number[], b: readonly number[]) => a.every((part, i) => Math.abs(part - b[i]) <= 1e-9);
  return keys.filter((key, index) => {
    const before = keys[index - 1];
    const after = keys[index + 1];
    return !before || !after || !equal(before.value, key.value) || !equal(key.value, after.value);
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
}

/** What changes of one layer of the scene. A member left out does not change, and is as a bare scene has it. */
export interface FlybyBundleLayerChanges {
  /** When it is shown and hidden. Before the first key it is the other way round. */
  shown?: FlybyBundleShownKey[];
  /** How much of it is there, 0–1. One key: that much throughout. */
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
}

/** A moment's breadth, in seconds: far less than a frame, and enough to stand on one side of a jump. */
const BESIDE = 1e-7;

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
 * What a flyby changes about the look over a clip, as keys on the clip's frames.
 *
 * `caves` are the caves the bundle names; one of them has tracks of its own only where the flyby
 * colours it apart. `relief` is false where the view does not let a flyby stretch the relief: the
 * viewer's own factor then stands throughout.
 */
export function flybyBundleLookTracks(
  look: FlybyLookPlan,
  base: FlybyLookBase,
  schedule: FlybyFrameSchedule,
  caves: readonly string[],
  relief = true,
): FlybyBundleLookTracks {
  const plan = flybyBundleLookOnFrames(look, schedule);
  const last = Math.max(0, schedule.count - 1);
  // The frames held at the end all show the moment the flight stops at, and nothing stands beside
  // that moment: whatever the look is there, it is for every one of them.
  const moment = (frame: number, side: -1 | 0 | 1 = 0) =>
    frame >= schedule.moving ? schedule.from + schedule.moving / schedule.fps : schedule.from + frame / schedule.fps + side * BESIDE;
  // Every track asks about the same moments, and the look of a moment is worked out once.
  const states = new Map<number, FlybyLookState>();
  const stateAt = (frame: number, side: -1 | 0 | 1): FlybyLookState => {
    const at = moment(frame, side);
    let state = states.get(at);
    if (!state) {
      state = flybyLookAt(plan, base, at);
      states.set(at, state);
    }
    return state;
  };
  const frameOf = (t: number) => Math.round((t - schedule.from) * schedule.fps);
  // Where a value can start or stop moving: at the ends of the changes that move it, and at the
  // frame the flight stops on, after which every frame is held.
  const breaksOf = (...members: string[]) =>
    [
      ...plan.changes
        .filter((timed) => members.includes(flybyChangeMember(timed.change)))
        .flatMap((timed) => [frameOf(timed.start), frameOf(timed.end)]),
      schedule.moving,
    ].filter((frame) => frame > 0 && frame < last);

  // ---- relief ----
  const reliefKeys = relief
    ? flybyBundleTrack((frame, side) => [flybyReliefAt(plan, base.relief, moment(frame, side))], breaksOf('relief'), last, FLYBY_BUNDLE_NUMBER)
    : [{ frame: 0, value: [base.relief ?? 1] }];

  // ---- sky ----
  const skyKeys = flybyBundleTrack((frame, side) => channelsOf(stateAt(frame, side).sky ?? BLACK), breaksOf('sky'), last, FLYBY_BUNDLE_COLOUR);

  // ---- the two layers a bundle draws ----
  const layer = (key: 'walls' | 'centerlines') => {
    const shownAt = (state: FlybyLookState) => state.layers[key] ?? base.layers[key] ?? { visible: true, opacity: 1 };
    const breaks = breaksOf(`layer:${key}`, `layerOpacity:${key}`);
    const turns = [...new Set(breaksOf(`layer:${key}`))].sort((a, b) => a - b);
    const first = shownAt(stateAt(0, 0)).visible;
    const shown: FlybyBundleShownKey[] = [];
    let now = first;
    for (const frame of [...turns, ...(last > 0 ? [last] : [])]) {
      const visible = shownAt(stateAt(frame, 0)).visible;
      if (visible !== now && (shown.length === 0 || shown[shown.length - 1].frame < frame)) {
        shown.push({ frame, shown: visible });
        now = visible;
      }
    }
    // How much of the layer is there says something only while it is shown: hidden, it keeps the
    // strength it went with, or — hidden from the start — the one it will come with.
    const strength = (frame: number, side: -1 | 0 | 1): number[] => {
      const state = shownAt(stateAt(frame, side));
      if (state.visible) {
        return [state.opacity];
      }
      const went = [...shown].reverse().find((key) => !key.shown && key.frame <= frame);
      const before = went ? shownAt(stateAt(went.frame, -1)) : undefined;
      if (before?.visible) {
        return [before.opacity];
      }
      const comes = shown.find((key) => key.shown && key.frame >= frame);
      const then = comes ? shownAt(stateAt(comes.frame, 0)) : undefined;
      return [then?.visible ? then.opacity : 1];
    };
    const opacity = numberKeys(flybyBundleTrack(strength, breaks, last, FLYBY_BUNDLE_NUMBER));
    return {
      ever: first || shown.some((key) => key.shown),
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

  const sky = hex(skyKeys[0].value);
  return {
    walls: walls.ever,
    lines: lines.ever,
    reliefAt: (frame) => rounded(flybyBundleTrackAt(reliefKeys, Math.min(Math.max(frame, 0), last), FLYBY_BUNDLE_NUMBER)[0]),
    ...(sky !== BLACK ? { sky } : {}),
    ...(linesTint && linesTint[0].strength > 0 ? { lineColour: linesTint[0].colour } : {}),
    changes: Object.keys(changes).length > 0 ? changes : null,
  };
}
