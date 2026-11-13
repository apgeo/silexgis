// SPDX-License-Identifier: AGPL-3.0-or-later
import { METERS_PER_DEGREE_LATITUDE, clampLatitude } from '../scene3d/pseudoZoom.ts';
import { metersBetween, type Camera3DEye } from '../scene3d/camera3d.ts';
import type { Scene3DBounds } from '../scene3d/scene3dEngine.ts';
import type { FlybyDocument, FlybySubject, FlybySubjectRef } from './flybyDocument.ts';
import { trackRoute } from './flybyRoutes.ts';

// Finding a flyby's subjects in the world, at the moment it is played.
//
// A document says "this cave"; a camera needs a point to look at and a size to stand back from.
// Those are worked out here, from what the scene is drawing for the person playing — and, for a
// cave nothing is drawn of yet, from what the server tells that same person about it. Nothing is
// stored: a subject resolved today from a survey of two kilometres resolves next year from one of
// three.

/** A subject, found: the box it fills, as a middle and two sizes. */
export interface FlybyResolvedSubject {
  /** The middle of it; `height` is midway between its top and its bottom, above the ellipsoid. */
  centre: Camera3DEye;
  /** How far the furthest of it is from the middle, in metres: the unit a pose's range is counted in. */
  radius: number;
  /** Half of how tall it is, in metres: the unit a pose's lift is counted in. */
  halfHeight: number;
  /**
   * The highest and the lowest place on it, when it has places of its own: for a cave, the top and
   * the bottom of its drawn survey. Left out for anything that is only a box.
   */
  top?: Camera3DEye;
  bottom?: Camera3DEye;
  /**
   * True when this was worked out from a description rather than from anything drawn — a cave
   * known only by where the server says it is and how long and deep it is said to be.
   */
  coarse: boolean;
  /**
   * A line the camera can follow along it: for a cave, the way down its drawn survey from its
   * highest point to its deepest; for an imported file, its track. Found when the flyby is
   * prepared and never stored in it.
   */
  route?: Camera3DEye[];
}

/** What is drawn of a cave in the view now. */
export interface FlybyCaveInView {
  caveId: string;
  /** The ground box of what is drawn of it. */
  bounds: Scene3DBounds;
  /** The drawn heights of its highest and lowest survey point, when its lines carry heights. */
  top?: number;
  bottom?: number;
  /** Where those two points are. */
  topAt?: { longitude: number; latitude: number; height: number };
  bottomAt?: { longitude: number; latitude: number; height: number };
}

/** What the server says about a cave to the person asking. */
export interface FlybyCaveFacts {
  longitude: number;
  latitude: number;
  /** Metres from its highest to its lowest point, when recorded. */
  depthM?: number;
  /** Metres of surveyed passage, when recorded. */
  lengthM?: number;
}

/** The parts of the application a flyby asks about the world; a test supplies a plain object. */
export interface FlybyWorld {
  /** What the scene draws of a cave now, or undefined when it draws nothing of it. */
  caveInView(caveId: string): FlybyCaveInView | undefined;
  /** Where a cave is and how big, as this reader may know it; undefined when they may not read it. */
  caveFacts(caveId: string): Promise<FlybyCaveFacts | undefined>;
  /** The ground box of a feature, or undefined when it has no extent this reader may see. */
  featureBounds(featureId: string): Promise<Scene3DBounds | undefined>;
  /** Height of the drawn ground at a point, metres above the ellipsoid. */
  groundHeight(longitude: number, latitude: number): number;
  /** The point the view is centred on and how far the eye is from it; undefined when it shows sky. */
  viewTarget(): { target: Camera3DEye; distance: number } | undefined;
  /**
   * The survey lines the scene draws of a cave now, at the heights they are drawn at; lines laid on
   * the ground, which carry no heights, are left out. Left out of a world that cannot say.
   */
  caveLines?(caveId: string): { longitude: number; latitude: number; height: number }[][];
  /**
   * The lines an imported file holds and the box it fills, as this reader may see it; undefined when
   * it cannot be read. Left out of a world that cannot say.
   */
  fileLines?(geofileId: string): Promise<{ bounds: Scene3DBounds; lines: { longitude: number; latitude: number }[][] } | undefined>;
}

/** The smallest subject a camera is asked to circle: a short cave is still stood back from. */
const MIN_RADIUS_METERS = 40;
/**
 * The smallest a subject known only roughly is taken to be — an entrance with no survey drawn, a
 * cave known by its description. Wider than a surveyed one, because the camera has nothing but the
 * ground around a point to show, and from forty metres that is a blur of one map tile.
 */
const MIN_ROUGH_RADIUS_METERS = 150;
/** A subject with no height of its own is still given some, so a lift has something to count in. */
const MIN_HALF_HEIGHT_RATIO = 0.02;
/** How much of the distance from eye to target counts as "what the view shows". */
const VIEW_RADIUS_RATIO = 0.35;
/** How many points along each side the ground is sampled at to find an area's highs and lows. */
const AREA_GROUND_SAMPLES = 5;

function boundsCentre(bounds: Scene3DBounds): { longitude: number; latitude: number } {
  return { longitude: (bounds[0] + bounds[2]) / 2, latitude: (bounds[1] + bounds[3]) / 2 };
}

/** Half the diagonal of a ground box, in metres. */
export function boundsRadius(bounds: Scene3DBounds): number {
  const latitude = clampLatitude((bounds[1] + bounds[3]) / 2);
  const width =
    (bounds[2] - bounds[0]) * METERS_PER_DEGREE_LATITUDE * Math.cos((latitude * Math.PI) / 180);
  const height = (bounds[3] - bounds[1]) * METERS_PER_DEGREE_LATITUDE;
  return Math.hypot(width, height) / 2;
}

/**
 * A subject from its middle, its reach along the ground and half its height. Its radius is the
 * distance to its furthest corner — along the ground AND down — so a cave as deep as it is wide is
 * stood back from far enough for its bottom to stay in the picture.
 */
function sized(centre: Camera3DEye, reach: number, halfHeight: number, coarse: boolean): FlybyResolvedSubject {
  return balled(centre, Math.hypot(reach, halfHeight), halfHeight, coarse);
}

/** A subject from its middle and its radius as already measured, held to the smallest sizes allowed. */
function balled(centre: Camera3DEye, radius: number, halfHeight: number, coarse: boolean): FlybyResolvedSubject {
  const sizedRadius = Math.max(radius, coarse ? MIN_ROUGH_RADIUS_METERS : MIN_RADIUS_METERS);
  return {
    centre,
    radius: sizedRadius,
    halfHeight: Math.max(halfHeight, sizedRadius * MIN_HALF_HEIGHT_RATIO),
    coarse,
  };
}

/** A cave from what is drawn of it: the box its lines fill, or its entrances when it has no lines. */
export function subjectFromCaveInView(
  cave: FlybyCaveInView,
  groundHeight: FlybyWorld['groundHeight'],
): FlybyResolvedSubject {
  const middle = boundsCentre(cave.bounds);
  const ground = groundHeight(middle.longitude, middle.latitude);
  const top = cave.top ?? ground;
  const bottom = cave.bottom ?? top;
  const subject = sized(
    { lon: middle.longitude, lat: middle.latitude, height: (top + bottom) / 2 },
    boundsRadius(cave.bounds),
    (top - bottom) / 2,
    // Without heights the box is a guess at the surface: good enough to fly to, not to dive into.
    cave.top === undefined,
  );
  if (cave.topAt && cave.bottomAt) {
    subject.top = { lon: cave.topAt.longitude, lat: cave.topAt.latitude, height: cave.topAt.height };
    subject.bottom = {
      lon: cave.bottomAt.longitude,
      lat: cave.bottomAt.latitude,
      height: cave.bottomAt.height,
    };
  }
  return subject;
}

/**
 * A cave nothing is drawn of, from its description: a ball around where it is said to be, sized
 * from its length and hung below the ground by its depth. Rough by nature — it exists so that a
 * camera can be sent there to load what is really drawn.
 */
export function subjectFromCaveFacts(
  facts: FlybyCaveFacts,
  groundHeight: FlybyWorld['groundHeight'],
): FlybyResolvedSubject {
  const ground = groundHeight(facts.longitude, facts.latitude);
  const depth = Math.max(0, facts.depthM ?? 0);
  // A cave is rarely a straight line: a quarter of its length is a fair reach for its plan.
  const reach = Math.max(0, facts.lengthM ?? 0) / 4;
  return sized(
    { lon: facts.longitude, lat: facts.latitude, height: ground - depth / 2 },
    Math.min(reach, 3000),
    depth / 2,
    true,
  );
}

/** An area from its ground box: its middle, its half-diagonal, and the ground's own highs and lows. */
export function subjectFromBounds(
  bounds: Scene3DBounds,
  groundHeight: FlybyWorld['groundHeight'],
): FlybyResolvedSubject {
  const middle = boundsCentre(bounds);
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (let row = 0; row < AREA_GROUND_SAMPLES; row += 1) {
    for (let column = 0; column < AREA_GROUND_SAMPLES; column += 1) {
      const longitude = bounds[0] + ((bounds[2] - bounds[0]) * column) / (AREA_GROUND_SAMPLES - 1);
      const latitude = bounds[1] + ((bounds[3] - bounds[1]) * row) / (AREA_GROUND_SAMPLES - 1);
      const height = groundHeight(longitude, latitude);
      low = Math.min(low, height);
      high = Math.max(high, height);
    }
  }
  return sized(
    { lon: middle.longitude, lat: middle.latitude, height: (low + high) / 2 },
    boundsRadius(bounds),
    (high - low) / 2,
    false,
  );
}

/** One subject, found; undefined when this reader's world does not hold it. */
export async function resolveSubject(
  subject: FlybySubject,
  world: FlybyWorld,
): Promise<FlybyResolvedSubject | undefined> {
  switch (subject.kind) {
    case 'cave': {
      const drawn = world.caveInView(subject.caveId);
      if (drawn) {
        return subjectFromCaveInView(drawn, world.groundHeight);
      }
      const facts = await world.caveFacts(subject.caveId);
      return facts ? subjectFromCaveFacts(facts, world.groundHeight) : undefined;
    }
    case 'feature': {
      const bounds = await world.featureBounds(subject.featureId);
      return bounds ? subjectFromBounds(bounds, world.groundHeight) : undefined;
    }
    case 'file': {
      const file = await world.fileLines?.(subject.geofileId);
      if (!file) {
        return undefined;
      }
      // The box it fills, and its longest line on the ground to be followed along.
      const route = trackRoute(file.lines, world.groundHeight);
      return { ...subjectFromBounds(file.bounds, world.groundHeight), ...(route ? { route } : {}) };
    }
    case 'view': {
      const view = world.viewTarget();
      if (!view) {
        return undefined;
      }
      return sized(view.target, view.distance * VIEW_RADIUS_RATIO, 0, false);
    }
    case 'place': {
      if (subject.height !== undefined) {
        // A captured view: the point it looked at and how far the eye stood from it, exactly — a
        // view taken a few metres from a passage wall is not to be stood back from like a cave.
        return {
          centre: { lon: subject.longitude, lat: subject.latitude, height: subject.height },
          radius: subject.radiusM,
          halfHeight: subject.radiusM * MIN_HALF_HEIGHT_RATIO,
          coarse: false,
        };
      }
      const ground = world.groundHeight(subject.longitude, subject.latitude);
      return sized({ lon: subject.longitude, lat: subject.latitude, height: ground }, subject.radiusM, 0, false);
    }
  }
}

/** Every subject taken as one: the ball that holds all of their balls. */
export function subjectsTogether(subjects: readonly FlybyResolvedSubject[]): FlybyResolvedSubject | undefined {
  if (subjects.length === 0) {
    return undefined;
  }
  if (subjects.length === 1) {
    return subjects[0];
  }
  let west = Number.POSITIVE_INFINITY;
  let south = Number.POSITIVE_INFINITY;
  let east = Number.NEGATIVE_INFINITY;
  let north = Number.NEGATIVE_INFINITY;
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const subject of subjects) {
    west = Math.min(west, subject.centre.lon);
    east = Math.max(east, subject.centre.lon);
    south = Math.min(south, subject.centre.lat);
    north = Math.max(north, subject.centre.lat);
    low = Math.min(low, subject.centre.height - subject.halfHeight);
    high = Math.max(high, subject.centre.height + subject.halfHeight);
  }
  const centre: Camera3DEye = { lon: (west + east) / 2, lat: (south + north) / 2, height: (low + high) / 2 };
  // Each one's own radius already reaches its furthest corner, down as well as along the ground.
  const radius = Math.max(...subjects.map((subject) => metersBetween(centre, subject.centre) + subject.radius));
  return balled(centre, radius, (high - low) / 2, subjects.some((subject) => subject.coarse));
}

/** The subject a section or the opening names, out of the ones that were found. */
export function subjectFor(
  ref: FlybySubjectRef,
  resolved: readonly (FlybyResolvedSubject | undefined)[],
): FlybyResolvedSubject | undefined {
  if (ref === 'all') {
    return subjectsTogether(resolved.filter((subject): subject is FlybyResolvedSubject => subject !== undefined));
  }
  return resolved[ref];
}

/**
 * The order to visit subjects in so the camera travels least: from the first, always on to the
 * nearest not yet visited. Not the shortest tour there is — that is a harder problem than a list of
 * a dozen caves deserves — but never the zigzag a list in alphabetical order gives.
 */
export function shortestRoute(subjects: readonly FlybyResolvedSubject[]): number[] {
  if (subjects.length === 0) {
    return [];
  }
  const order = [0];
  const left = new Set(subjects.map((_, index) => index).slice(1));
  while (left.size > 0) {
    const from = subjects[order[order.length - 1]].centre;
    let nearest = -1;
    let nearestDistance = Number.POSITIVE_INFINITY;
    for (const index of left) {
      const distance = metersBetween(from, subjects[index].centre);
      if (distance < nearestDistance) {
        nearestDistance = distance;
        nearest = index;
      }
    }
    order.push(nearest);
    left.delete(nearest);
  }
  return order;
}

/** The subjects of a document in a new order, with every reference to them following. */
export function reorderSubjects(document: FlybyDocument, order: readonly number[]): FlybyDocument {
  const position = new Map(order.map((from, to) => [from, to]));
  const moved = (ref: FlybySubjectRef): FlybySubjectRef => (ref === 'all' ? ref : (position.get(ref) ?? ref));
  return {
    ...document,
    subjects: order.map((index) => document.subjects[index]),
    start: { ...document.start, subject: moved(document.start.subject) },
    sections: document.sections.map((section) => ({ ...section, subject: moved(section.subject) })),
  };
}
