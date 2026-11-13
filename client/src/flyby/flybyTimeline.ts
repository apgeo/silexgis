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
import { subjectFor, type FlybyResolvedSubject } from './flybySubjects.ts';

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

export interface FlybyTimeline {
  /** Seconds. */
  length: number;
  /** The point every knot's target is measured from. */
  origin: Camera3DEye;
  knots: FlybyKnot[];
  spans: FlybySpan[];
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
/** A hop's highest point stands at least this share of the way between the two subjects back. */
const HOP_DISTANCE_RATIO = 0.9;
/** A hop looks down at least this steeply at its highest point, so the ground travels under it. */
const HOP_PITCH_DEGREES = -40;
const MIN_PITCH = -89.9;
const MAX_PITCH = 60;

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
    distance: stance.pose.range * subject.radius,
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
  let stance: Stance = { subject: document.start.subject, pose: document.start.pose };
  let t = 0;
  knots.push(knotOf(stance, resolved, origin, t, true));

  document.sections.forEach((section, index) => {
    const length = sectionLength(document, section);
    const previous = knots[knots.length - 1];
    const next: Stance = { subject: section.subject, pose: poseAfter(stance.pose, section.to) };
    const last = index === document.sections.length - 1;
    const end = knotOf(next, resolved, origin, t + length, last || section.rest === true || section.kind === 'hold');

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

  return { length: t, origin, knots, spans };
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

/** The section being flown at `t`, by its place in the document; the last one once the end is reached. */
export function flybySectionAt(timeline: FlybyTimeline, t: number): number {
  const index = timeline.spans.findIndex((span) => t < span.end);
  return index < 0 ? timeline.spans.length - 1 : index;
}
