// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  eyeLookingAt,
  movedBy,
  offsetBetween,
  wrapHeading,
  type Camera3DEye,
  type Camera3DOffset,
} from '../scene3d/camera3d.ts';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import {
  sectionLength,
  type FlybyDocument,
  type FlybyFullPose,
  type FlybyPose,
  type FlybySubjectRef,
} from './flybyDocument.ts';
import { flybyLookPlan, type FlybyLookPlan } from './flybyLook.ts';
import { subjectFor, type FlybyResolvedSubject } from './flybySubjects.ts';
import { flybyTextPlan, type FlybyTextPlan } from './flybyTexts.ts';

// A flyby turned into a path: where the camera is at every moment.
//
// The document describes poses around subjects; here each becomes a *knot* in metres — the point
// looked at, how far the eye stands from it, which way it faces and how far it tilts — at a time on
// the flyby's clock. The camera at any moment is read off one smooth curve through all the knots,
// so consecutive sections are flown without stopping at the joins, and the same moment always
// gives the same camera.
//
// Three choices in the arithmetic are deliberate:
//
//   * Distance is interpolated by its logarithm. An approach from twenty kilometres to two hundred
//     metres then closes in equal proportions over equal times, instead of covering nine tenths of
//     the way in the first tenth and crawling the rest.
//   * The heading is kept unwrapped — 370 rather than 10 — so a turn of more than half a circle
//     goes the way it was asked to and not the short way round.
//   * The curve is not allowed to overshoot. A plain smooth curve through uneven knots swings past
//     them: a camera tilting from -30 to -89 and back would dip through straight-down and flip.
//     Where a value turns round at a knot it stands still there, and it never changes faster next
//     to a knot than three times its average rate on either side.

/** The camera's description at one moment, in metres about a fixed origin. */
export interface FlybyKnot {
  /** Seconds from the start of the flyby. */
  t: number;
  /** The point looked at: metres east, north and up from the timeline's origin. */
  target: Camera3DOffset;
  /** Metres from the point looked at to the eye. */
  distance: number;
  /** Compass direction faced, degrees, unwrapped. */
  heading: number;
  /** Degrees below the horizon. */
  pitch: number;
  /** Metres the eye is kept above the ground; null when it may go below. */
  clearance: number | null;
  /** The camera stands still here. */
  rest: boolean;
}

/** A section's place on the clock. */
export interface FlybySpan {
  id: string;
  start: number;
  end: number;
}

/**
 * A stretch of the path that follows a line — down a cave's survey, along a file's track — instead
 * of flying from one knot to the next. Two knots bound it, the camera at rest at each, and between
 * them the camera's moment is read off the line rather than off the curve through the knots.
 */
export interface FlybyStretch {
  start: number;
  end: number;
  /** Points of the line, evenly spaced along it and smoothed, in metres from the timeline's origin. */
  points: Camera3DOffset[];
  /** The compass direction faced at each point, unwrapped: along the line, a little ahead. */
  headings: number[];
  distance: number;
  pitch: number;
  clearance: number | null;
  /** The share of the stretch spent gathering speed at its start, and losing it again at its end. */
  ramp: number;
}

export interface FlybyTimeline {
  /** Seconds. */
  length: number;
  /** The point every knot's target is measured from. */
  origin: Camera3DEye;
  knots: FlybyKnot[];
  spans: FlybySpan[];
  /** The stretches that follow a line, in order. */
  stretches: FlybyStretch[];
  /** What the flyby does to the scene's look, placed on the same clock. */
  look: FlybyLookPlan;
  /** What it writes over the scene, placed on the same clock. */
  texts: FlybyTextPlan;
}

/** Why a document could not be turned into a path. */
export class FlybySubjectMissing extends Error {
  readonly subject: FlybySubjectRef;

  constructor(subject: FlybySubjectRef) {
    super(`flyby: subject ${String(subject)} could not be found`);
    this.name = 'FlybySubjectMissing';
    this.subject = subject;
  }
}

/** How much of a turn one stretch of an orbit may cover before a knot is put in the middle of it. */
const ORBIT_STEP_DEGREES = 90;
/**
 * Coming in from far away, the camera keeps looking straight down until it is this many times as
 * close as where it is going, and no closer than the first of these distances in metres (nor than
 * half as far again as where it is going): only then does it tilt to the pose it is coming to. Seen
 * straight down, the arithmetic of a flat patch of ground about the subject is exact whatever the
 * height; looked at sideways from hundreds of kilometres off, it would miss by the curve of the
 * Earth.
 */
const FAR_TILT_FACTOR = 8;
const FAR_TILT_METRES = 25_000;
/** A hop's highest point stands at least this share of the way between the two subjects back. */
const HOP_DISTANCE_RATIO = 0.9;
/** A hop looks down at least this steeply at its highest point, so the ground travels under it. */
const HOP_PITCH_DEGREES = -40;
const MIN_PITCH = -89.9;
const MAX_PITCH = 60;
/**
 * The share of a section that follows a line spent flying to where the line begins, held between
 * a couple of seconds and a few: long enough to come down from an overview without rushing it.
 */
const LINE_LEAD_SHARE = 0.2;
const LINE_LEAD_SECONDS = { min: 2, max: 8 } as const;
/** Seconds the camera takes to gather speed along a line, and to lose it again at the line's end. */
const LINE_RAMP_SECONDS = 2.5;
/** Most points a line is followed through, however long it is. */
const LINE_MAX_POINTS = 500;

interface Stance {
  subject: FlybySubjectRef;
  pose: FlybyFullPose;
}

function poseAfter(previous: FlybyFullPose, to: FlybyPose): FlybyFullPose {
  return {
    aim: to.aim ?? previous.aim,
    heading: to.heading ?? previous.heading + (to.headingBy ?? 0),
    pitch: to.pitch ?? previous.pitch,
    range: to.range ?? previous.range,
    // A distance in metres holds until a section gives one in radii again.
    ...(to.range === undefined && (to.rangeM ?? previous.rangeM) !== undefined ? { rangeM: to.rangeM ?? previous.rangeM } : {}),
    lift: to.lift ?? previous.lift,
    clearance: to.clearance === undefined ? previous.clearance : to.clearance,
  };
}

function lerp(a: number, b: number, p: number): number {
  return a + (b - a) * p;
}

function knotOf(
  stance: Stance,
  resolved: readonly (FlybyResolvedSubject | undefined)[],
  origin: Camera3DEye,
  t: number,
  rest: boolean,
): FlybyKnot {
  const subject = subjectFor(stance.subject, resolved);
  if (!subject) {
    throw new FlybySubjectMissing(stance.subject);
  }
  // The place looked at: the subject's middle, or its highest or lowest place. A subject with no
  // places of its own answers with the top and the bottom of its middle.
  const raised = (halves: number) => ({ ...subject.centre, height: subject.centre.height + halves * subject.halfHeight });
  const aimed =
    stance.pose.aim === 'top'
      ? (subject.top ?? raised(1))
      : stance.pose.aim === 'bottom'
        ? (subject.bottom ?? raised(-1))
        : subject.centre;
  const centre = offsetBetween(origin, aimed);
  return {
    t,
    target: { east: centre.east, north: centre.north, up: centre.up + stance.pose.lift * subject.halfHeight },
    distance: stance.pose.rangeM ?? stance.pose.range * subject.radius,
    heading: stance.pose.heading,
    pitch: stance.pose.pitch,
    clearance: stance.pose.clearance,
    rest,
  };
}

function between(a: FlybyKnot, b: FlybyKnot, p: number): FlybyKnot {
  return {
    t: lerp(a.t, b.t, p),
    target: {
      east: lerp(a.target.east, b.target.east, p),
      north: lerp(a.target.north, b.target.north, p),
      up: lerp(a.target.up, b.target.up, p),
    },
    distance: Math.exp(lerp(Math.log(a.distance), Math.log(b.distance), p)),
    heading: lerp(a.heading, b.heading, p),
    pitch: lerp(a.pitch, b.pitch, p),
    clearance: b.clearance,
    rest: false,
  };
}

/**
 * The path a document describes, for subjects found as given.
 *
 * Throws {@link FlybySubjectMissing} when a subject the document flies around was not found: a
 * flyby cannot be flown around something that is not there, and a path that quietly skipped it
 * would send the camera on a hop to nowhere.
 */
export function compileFlyby(
  document: FlybyDocument,
  resolved: readonly (FlybyResolvedSubject | undefined)[],
): FlybyTimeline {
  const first = subjectFor(document.start.subject, resolved);
  if (!first) {
    throw new FlybySubjectMissing(document.start.subject);
  }
  const origin = first.centre;
  const knots: FlybyKnot[] = [];
  const spans: FlybySpan[] = [];
  const stretches: FlybyStretch[] = [];
  let stance: Stance = { subject: document.start.subject, pose: document.start.pose };
  let t = 0;
  knots.push(knotOf(stance, resolved, origin, t, true));

  document.sections.forEach((section, index) => {
    const length = sectionLength(document, section);
    const previous = knots[knots.length - 1];
    const next: Stance = { subject: section.subject, pose: poseAfter(stance.pose, section.to) };
    const last = index === document.sections.length - 1;
    const end = knotOf(next, resolved, origin, t + length, last || section.rest === true || section.kind === 'hold');

    const line =
      section.kind === 'descent' || section.kind === 'follow'
        ? lineStretch(subjectFor(next.subject, resolved)?.route, end, previous.heading, origin, t, length)
        : undefined;
    if (line) {
      // Along the line: a short flight to where it begins, the line itself, and at rest at its end
      // — so the stretch joins the curve before and after it with the camera still at both joins.
      const [into, outOf] = line.bounds;
      knots.push(into, outOf);
      stretches.push(line.stretch);
      spans.push({ id: section.id, start: t, end: t + length });
      // What comes next turns from the way the line left the camera facing.
      stance = { ...next, pose: { ...next.pose, heading: outOf.heading } };
      t += length;
      return;
    }

    if (section.kind === 'hop' && next.subject !== stance.subject) {
      // Up and over: half-way, the camera stands back far enough to hold both ends and looks down
      // steeply, so the ground between the two is seen travelling underneath.
      const separation = Math.hypot(
        end.target.east - previous.target.east,
        end.target.north - previous.target.north,
        end.target.up - previous.target.up,
      );
      const middle = between(previous, end, 0.5);
      middle.distance = Math.max(previous.distance, end.distance, separation * HOP_DISTANCE_RATIO);
      middle.pitch = Math.min(previous.pitch, end.pitch, HOP_PITCH_DEGREES);
      // Over open ground the higher of the two limits holds; a hop out of a cave has left it by now.
      middle.clearance = end.clearance ?? previous.clearance;
      knots.push(middle);
    } else if (section.kind === 'fromFar') {
      // Straight down while far, then the tilt. The knot that ends the straight-down part sits on
      // the same even fall of the distance's logarithm as the two ends, so the descent does not
      // change pace there; only the tilt starts.
      const tilt = Math.min(FAR_TILT_FACTOR * end.distance, Math.max(FAR_TILT_METRES, 1.5 * end.distance));
      if (previous.distance > tilt * 1.01 && tilt > end.distance) {
        const share = Math.log(previous.distance / tilt) / Math.log(previous.distance / end.distance);
        const held = between(previous, end, share);
        held.pitch = previous.pitch;
        held.heading = previous.heading;
        held.clearance = previous.clearance;
        knots.push(held);
      }
    } else {
      // A long turn is cut into quarter-turns so it is flown at an even rate: one stretch from 0 to
      // 720 degrees would be free to hurry through its middle.
      const turn = Math.abs(end.heading - previous.heading);
      const pieces = Math.ceil(turn / ORBIT_STEP_DEGREES);
      for (let piece = 1; piece < pieces; piece += 1) {
        knots.push(between(previous, end, piece / pieces));
      }
    }
    knots.push(end);
    spans.push({ id: section.id, start: t, end: t + length });
    stance = next;
    t += length;
  });

  return { length: t, origin, knots, spans, stretches, look: flybyLookPlan(document), texts: flybyTextPlan(document) };
}

// ---- following a line ----

/** The turn from one heading to another the short way round, in degrees. */
function shortTurn(from: number, to: number): number {
  const turn = ((((to - from) % 360) + 540) % 360) - 180;
  return turn === -180 ? 180 : turn;
}

function offsetLerp(a: Camera3DOffset, b: Camera3DOffset, p: number): Camera3DOffset {
  return { east: lerp(a.east, b.east, p), north: lerp(a.north, b.north, p), up: lerp(a.up, b.up, p) };
}

function offsetLength(a: Camera3DOffset, b: Camera3DOffset): number {
  return Math.hypot(b.east - a.east, b.north - a.north, b.up - a.up);
}

/** A line cut into `count` points evenly spaced along it, its two ends kept. */
function resampled(line: readonly Camera3DOffset[], count: number): Camera3DOffset[] {
  const along = [0];
  for (let i = 1; i < line.length; i += 1) {
    along.push(along[i - 1] + offsetLength(line[i - 1], line[i]));
  }
  const total = along[along.length - 1];
  const points: Camera3DOffset[] = [];
  let leg = 1;
  for (let k = 0; k < count; k += 1) {
    const at = (total * k) / (count - 1);
    while (leg < line.length - 1 && along[leg] < at) {
      leg += 1;
    }
    const span = along[leg] - along[leg - 1];
    points.push(offsetLerp(line[leg - 1], line[leg], span > 0 ? (at - along[leg - 1]) / span : 0));
  }
  return points;
}

/**
 * Each value the mean of the `half` on either side of it, the window narrowing towards the ends so
 * the first and the last values stay exactly where they were. Twice over, which is a triangle of
 * weights rather than a box: a corner of the line is rounded off instead of cut.
 */
function smoothed<T>(values: readonly T[], half: number, mean: (picked: readonly T[]) => T): T[] {
  let result = [...values];
  for (let pass = 0; pass < 2; pass += 1) {
    const from = result;
    result = from.map((value, i) => {
      const reach = Math.min(half, i, from.length - 1 - i);
      return reach > 0 ? mean(from.slice(i - reach, i + reach + 1)) : value;
    });
  }
  return result;
}

const meanOffset = (picked: readonly Camera3DOffset[]): Camera3DOffset => ({
  east: picked.reduce((sum, point) => sum + point.east, 0) / picked.length,
  north: picked.reduce((sum, point) => sum + point.north, 0) / picked.length,
  up: picked.reduce((sum, point) => sum + point.up, 0) / picked.length,
});

const meanNumber = (picked: readonly number[]): number => picked.reduce((sum, value) => sum + value, 0) / picked.length;

/**
 * The stretch that follows `route`, with the camera stood off it as `pose` says, and the two knots
 * at rest that bound it; undefined when there is no line to follow, and the section is then flown
 * as an ordinary move to its pose.
 *
 * The line is cut into points evenly spaced along it and smoothed over about the camera's own
 * distance from it, so a survey's zigzag or a track's GPS jitter is not flown leg by leg. The
 * camera faces where the line goes a camera-distance ahead — along the passage, down the path — and
 * where the line goes straight up or down, and so points nowhere on the compass, it keeps the way it
 * was facing.
 */
function lineStretch(
  route: readonly Camera3DEye[] | undefined,
  pose: FlybyKnot,
  facing: number,
  origin: Camera3DEye,
  start: number,
  length: number,
): { stretch: FlybyStretch; bounds: [FlybyKnot, FlybyKnot] } | undefined {
  if (!route || route.length < 2 || !(length > 0)) {
    return undefined;
  }
  const line = route.map((point) => offsetBetween(origin, point));
  let total = 0;
  for (let i = 1; i < line.length; i += 1) {
    total += offsetLength(line[i - 1], line[i]);
  }
  if (!(total > 0.5)) {
    return undefined;
  }
  const count = Math.min(LINE_MAX_POINTS, Math.max(8, Math.ceil(total / Math.max(pose.distance / 3, 0.5)))) + 1;
  const spacing = total / (count - 1);
  const reach = Math.max(1, Math.round(pose.distance / spacing));
  const points = smoothed(resampled(line, count), reach, meanOffset);

  const bearings: (number | undefined)[] = points.map((point, i) => {
    const from = i + reach < count ? point : points[Math.max(0, count - 1 - reach)];
    const to = points[Math.min(i + reach, count - 1)];
    const east = to.east - from.east;
    const north = to.north - from.north;
    return Math.hypot(east, north) > 0.05 * reach * spacing ? (Math.atan2(east, north) * 180) / Math.PI : undefined;
  });
  const firstKnown = bearings.find((bearing) => bearing !== undefined) ?? facing;
  let heading = facing + shortTurn(facing, firstKnown);
  const headings = bearings.map((bearing) => {
    if (bearing !== undefined) {
      heading += shortTurn(heading, bearing);
    }
    return heading;
  });
  const turned = smoothed(headings, reach, meanNumber);

  const lead = Math.min(
    length * 0.4,
    Math.min(LINE_LEAD_SECONDS.max, Math.max(LINE_LEAD_SECONDS.min, length * LINE_LEAD_SHARE)),
  );
  const stretch: FlybyStretch = {
    start: start + lead,
    end: start + length,
    points,
    headings: turned,
    distance: pose.distance,
    pitch: pose.pitch,
    clearance: pose.clearance,
    ramp: Math.min(0.5, LINE_RAMP_SECONDS / (length - lead)),
  };
  const bound = (t: number, index: number): FlybyKnot => ({
    t,
    target: points[index],
    distance: pose.distance,
    heading: turned[index],
    pitch: pose.pitch,
    clearance: pose.clearance,
    rest: true,
  });
  return { stretch, bounds: [bound(stretch.start, 0), bound(stretch.end, count - 1)] };
}

/**
 * How far along its line the camera is, 0–1, at share `p` of the stretch's time: speeding up evenly
 * over the first `ramp` of it, at one speed through the middle, slowing evenly over the last — so
 * it leaves the first knot and reaches the second with no speed at all, as the curve on the other
 * side of each knot does.
 */
export function flybyLineProgress(p: number, ramp: number): number {
  const clamped = Math.min(1, Math.max(0, p));
  const r = Math.min(0.5, Math.max(1e-6, ramp));
  const top = 1 / (1 - r);
  if (clamped < r) {
    return (top * clamped * clamped) / (2 * r);
  }
  if (clamped <= 1 - r) {
    return top * (r / 2 + clamped - r);
  }
  const left = 1 - clamped;
  return 1 - (top * left * left) / (2 * r);
}

/** Catmull–Rom through the points either side, so the camera's course has no corner at any of them. */
function catmullRom(a: number, b: number, c: number, d: number, f: number): number {
  const f2 = f * f;
  const f3 = f2 * f;
  return 0.5 * (2 * b + (c - a) * f + (2 * a - 5 * b + 4 * c - d) * f2 + (3 * b - a - 3 * c + d) * f3);
}

function stretchKnotAt(stretch: FlybyStretch, t: number): FlybyKnot {
  const { points, headings } = stretch;
  const x = flybyLineProgress((t - stretch.start) / (stretch.end - stretch.start), stretch.ramp) * (points.length - 1);
  const i = Math.min(Math.floor(x), points.length - 2);
  const f = x - i;
  const at = (k: number) => points[Math.min(Math.max(k, 0), points.length - 1)];
  const [a, b, c, d] = [at(i - 1), at(i), at(i + 1), at(i + 2)];
  return {
    t,
    target: {
      east: catmullRom(a.east, b.east, c.east, d.east, f),
      north: catmullRom(a.north, b.north, c.north, d.north, f),
      up: catmullRom(a.up, b.up, c.up, d.up, f),
    },
    distance: stretch.distance,
    heading: lerp(headings[i], headings[i + 1], f),
    pitch: stretch.pitch,
    clearance: stretch.clearance,
    rest: false,
  };
}

// ---- the curve through the knots ----

/** The parts of a knot that are flown: target east, north, up; the distance's logarithm; heading; pitch. */
type KnotVector = [number, number, number, number, number, number];

function vectorOf(knot: FlybyKnot): KnotVector {
  return [knot.target.east, knot.target.north, knot.target.up, Math.log(knot.distance), knot.heading, knot.pitch];
}

/**
 * How fast each part changes as the camera passes knot `index`: the average of its rates on either
 * side, held to nothing where the part turns round and to three times the slower side elsewhere.
 */
function rateAt(knots: readonly FlybyKnot[], index: number): KnotVector {
  const knot = knots[index];
  const before = knots[index - 1];
  const after = knots[index + 1];
  const still: KnotVector = [0, 0, 0, 0, 0, 0];
  if (knot.rest || !before || !after) {
    return still;
  }
  const spanBefore = knot.t - before.t;
  const spanAfter = after.t - knot.t;
  if (!(spanBefore > 0) || !(spanAfter > 0)) {
    return still;
  }
  const here = vectorOf(knot);
  const from = vectorOf(before);
  const to = vectorOf(after);
  return here.map((value, i) => {
    const rateBefore = (value - from[i]) / spanBefore;
    const rateAfter = (to[i] - value) / spanAfter;
    if (rateBefore * rateAfter <= 0) {
      return 0;
    }
    const average = (to[i] - from[i]) / (spanBefore + spanAfter);
    const limit = 3 * Math.min(Math.abs(rateBefore), Math.abs(rateAfter));
    return Math.sign(average) * Math.min(Math.abs(average), limit);
  }) as KnotVector;
}

/** Where the camera is at `t`, as a knot of its own. */
export function flybyKnotAt(timeline: FlybyTimeline, t: number): FlybyKnot {
  const { knots } = timeline;
  const clamped = Math.min(Math.max(t, 0), timeline.length);
  // Inside a stretch that follows a line, the line says where the camera is; at its two ends the
  // knots do, and they say the same.
  for (const stretch of timeline.stretches) {
    if (clamped > stretch.start && clamped < stretch.end) {
      return stretchKnotAt(stretch, clamped);
    }
  }
  // The last knot that has begun; the stretch flown is from it to the one after.
  let index = 0;
  for (let i = 0; i < knots.length; i += 1) {
    if (knots[i].t <= clamped) {
      index = i;
    }
  }
  const from = knots[index];
  const to = knots[index + 1];
  if (!to || !(to.t > from.t)) {
    return { ...from, t: clamped };
  }
  const span = to.t - from.t;
  const s = (clamped - from.t) / span;
  const a = vectorOf(from);
  const b = vectorOf(to);
  const rateA = rateAt(knots, index);
  const rateB = rateAt(knots, index + 1);
  // Cubic Hermite: the two knots, and the rates at either end scaled to the stretch's length.
  const s2 = s * s;
  const s3 = s2 * s;
  const h00 = 2 * s3 - 3 * s2 + 1;
  const h10 = s3 - 2 * s2 + s;
  const h01 = -2 * s3 + 3 * s2;
  const h11 = s3 - s2;
  const [east, north, up, logDistance, heading, pitch] = a.map(
    (value, i) => h00 * value + h10 * span * rateA[i] + h01 * b[i] + h11 * span * rateB[i],
  );
  return {
    t: clamped,
    target: { east, north, up },
    distance: Math.exp(logDistance),
    heading,
    pitch: Math.min(MAX_PITCH, Math.max(MIN_PITCH, pitch)),
    // The limit this stretch is flying towards; blending from the one it left is flybyCameraAt's.
    clearance: to.clearance,
    rest: false,
  };
}

/** How far through its stretch of the path moment `t` is, 0–1, and the limits at either end of it. */
function clearanceAt(timeline: FlybyTimeline, t: number): { from: number | null; to: number | null; p: number } {
  const { knots } = timeline;
  let index = 0;
  for (let i = 0; i < knots.length; i += 1) {
    if (knots[i].t <= t) {
      index = i;
    }
  }
  const from = knots[index];
  const to = knots[index + 1];
  if (!to || !(to.t > from.t)) {
    return { from: from.clearance, to: from.clearance, p: 1 };
  }
  const s = (t - from.t) / (to.t - from.t);
  // Eased, so a limit being lifted to go underground starts and ends gently.
  return { from: from.clearance, to: to.clearance, p: s * s * (3 - 2 * s) };
}

/**
 * The camera at `t`: where the eye is and which way it points.
 *
 * `groundHeight` is asked where the eye would stand, and the eye is held above the ground by the
 * limit in force. Between a stretch that has a limit and one that has none the eye is blended from
 * the held height to the free one, so lifting the limit is a descent and not a drop. An eye that
 * was lifted keeps looking at the same point, by tilting further down.
 */
export function flybyCameraAt(
  timeline: FlybyTimeline,
  t: number,
  groundHeight: (longitude: number, latitude: number) => number,
): Scene3DCameraState {
  const clamped = Math.min(Math.max(t, 0), timeline.length);
  const knot = flybyKnotAt(timeline, clamped);
  const target = movedBy(timeline.origin, knot.target);
  const eye = eyeLookingAt(target, knot.heading, knot.pitch, knot.distance);
  const limits = clearanceAt(timeline, clamped);
  let height = eye.height;
  if (limits.from !== null || limits.to !== null) {
    const ground = groundHeight(eye.lon, eye.lat);
    const held = (limit: number | null) => (limit === null ? eye.height : Math.max(eye.height, ground + limit));
    height = lerp(held(limits.from), held(limits.to), limits.p);
  }
  let pitch = knot.pitch;
  if (height !== eye.height) {
    const horizontal = knot.distance * Math.cos((knot.pitch * Math.PI) / 180);
    pitch = (Math.atan2(target.height - height, horizontal) * 180) / Math.PI;
  }
  return {
    longitude: eye.lon,
    latitude: eye.lat,
    height,
    heading: wrapHeading(knot.heading),
    pitch: Math.min(MAX_PITCH, Math.max(-90, pitch)),
    roll: 0,
  };
}

/**
 * The sections that were to follow a line and found none to follow — a cave whose survey is not
 * drawn, or does not join its highest point to its deepest; a file with no line in it — and are
 * flown as plain moves instead.
 */
export function flybyLineFallbacks(
  document: Pick<FlybyDocument, 'sections'>,
  resolved: readonly (FlybyResolvedSubject | undefined)[],
): string[] {
  return document.sections
    .filter((section) => (section.kind === 'descent' || section.kind === 'follow') && (subjectFor(section.subject, resolved)?.route?.length ?? 0) < 2)
    .map((section) => section.id);
}

/** The section being flown at `t`, by its place in the document; the last one once the end is reached. */
export function flybySectionAt(timeline: FlybyTimeline, t: number): number {
  const index = timeline.spans.findIndex((span) => t < span.end);
  return index < 0 ? timeline.spans.length - 1 : index;
}
