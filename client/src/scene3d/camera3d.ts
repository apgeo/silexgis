// SPDX-License-Identifier: AGPL-3.0-or-later
import { clampLatitude, METERS_PER_DEGREE_LATITUDE } from './pseudoZoom.ts';
import type {
  Scene3DCamera,
  Scene3DCameraOptions,
  Scene3DCameraState,
  Scene3DPosition,
  Scene3DProjection,
} from './scene3dEngine.ts';

// The camera state this application writes down.
//
// Everything here is degrees and metres. That is the whole point of the module and it is not a
// stylistic preference: a camera position that leaves this application — into a URL somebody
// bookmarks, into a saved view stored on the server, into a link mailed to a colleague — outlives
// the library that drew it. A stored Cartesian triple is a promise that the next renderer will
// model the world as the same ellipsoid in the same axis convention; a stored radian is a promise
// that nobody will ever read the number; a stored projected metre is a promise about a map
// projection that is not even in this file. Longitude, latitude, metres above the ellipsoid and
// compass degrees are the only things about a camera that mean anything on their own, so they are
// the only things persisted, and the conversion to whatever the renderer wants happens at the one
// seam that talks to it.
//
// The live camera type the engine contract uses next door is flat and has no projection, because
// that is all the engine needs to be told to point somewhere. This one adds what has to survive a
// reload: which projection was in use, how wide an orthographic view was, and the ground the view
// was actually about. Converting between them is two functions, both below, and neither of them
// touches a renderer.

/** A point on the globe, as it is written down: degrees, and metres above the WGS84 ellipsoid. */
export interface Camera3DEye {
  lon: number;
  lat: number;
  /** Metres above the ellipsoid. Negative underground, which is where this view earns its keep. */
  height: number;
}

/** Metres east, north and up from some origin — the local frame the presets do their work in. */
export interface Camera3DOffset {
  east: number;
  north: number;
  up: number;
}

/**
 * A camera, written down.
 *
 * `target` is the ground the middle of the screen was showing. It is optional because a camera
 * pointed at the sky has none, and it is worth storing because it is the only part of the state
 * that survives being reopened in a window of a different shape: the eye alone describes where
 * somebody stood, the target describes what they were looking at.
 */
export interface Camera3DState {
  eye: Camera3DEye;
  /** Compass direction the camera faces, degrees: 0 = north, increasing clockwise, [0, 360). */
  heading: number;
  /** Degrees below the horizon: -90 looks straight down, 0 looks at the horizon. */
  pitch: number;
  /**
   * Tilt about the camera's own axis, degrees, folded into (-180, 180] so that a level camera
   * reads as zero rather than as a whole turn. See `wrapRoll`.
   */
  roll: number;
  target?: Camera3DEye;
  projection: Scene3DProjection;
  /** Metres from the middle of an orthographic view to its edge. Absent under perspective. */
  orthoHalfWidth?: number;
}

/**
 * A compass bearing folded into [0, 360).
 *
 * Looking straight down, a renderer can report a heading of exactly one full turn. Comparing that
 * with zero would show a camera that had moved when it had not, which matters here because the
 * comparison decides whether the URL is rewritten and whether a preset still counts as active.
 */
export function wrapHeading(degrees: number): number {
  if (!Number.isFinite(degrees)) {
    return 0;
  }
  const wrapped = degrees % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}

/**
 * A roll folded into (-180, 180].
 *
 * Roll is the one angle that is compared against zero rather than against another angle: a camera
 * is either level or it is not, and everything that reads it — whether a one-press view counts as
 * the current one, whether a restored camera is upright — asks that question. A renderer reports
 * roll as a position in a full turn, so a camera with no roll at all comes back as either nothing
 * or a whole turn depending on which side of zero the arithmetic landed, and a whole turn compared
 * with zero reads as a camera tipped completely over. Folding it around zero is what makes "level"
 * a single answer. Deliberately a different fold from a compass bearing's [0, 360), because a
 * bearing is a direction and a roll is a deviation.
 */
export function wrapRoll(degrees: number): number {
  if (!Number.isFinite(degrees)) {
    return 0;
  }
  const wrapped = ((degrees % 360) + 360) % 360;
  return wrapped > 180 ? wrapped - 360 : wrapped;
}

/** The live camera the engine is asked to adopt. The target and the projection are not its business. */
export function toSceneCamera(state: Camera3DState): Scene3DCameraState {
  return {
    longitude: state.eye.lon,
    latitude: state.eye.lat,
    height: state.eye.height,
    heading: wrapHeading(state.heading),
    pitch: state.pitch,
    roll: state.roll,
  };
}

/** What the engine reports, written down, with the parts it does not carry supplied by the caller. */
export function toCamera3DState(
  camera: Scene3DCameraState,
  extra: {
    target?: Scene3DPosition;
    projection: Scene3DProjection;
    orthoHalfWidth?: number;
  },
): Camera3DState {
  const state: Camera3DState = {
    eye: { lon: camera.longitude, lat: camera.latitude, height: camera.height },
    heading: wrapHeading(camera.heading),
    pitch: camera.pitch,
    roll: wrapRoll(camera.roll),
    projection: extra.projection,
  };
  if (extra.target) {
    state.target = {
      lon: extra.target.longitude,
      lat: extra.target.latitude,
      height: extra.target.height,
    };
  }
  // Only written when it means something. An orthographic half-width recorded against a
  // perspective camera would be restored as a framing nobody asked for.
  if (extra.projection === 'orthographic' && isPositive(extra.orthoHalfWidth)) {
    state.orthoHalfWidth = extra.orthoHalfWidth;
  }
  return state;
}

/** The parts of a scene a camera is read out of; a plain object satisfies it in a test. */
export type Camera3DReader = Pick<
  Scene3DCamera,
  'getCamera' | 'getCameraTarget' | 'getProjection' | 'getOrthoHalfWidth'
>;

/** The parts of a scene a camera is written into. */
export type Camera3DWriter = Pick<Scene3DCamera, 'setCamera' | 'setProjection'>;

/** Everything the scene is showing about its camera, written down. */
export function readCamera3D(engine: Camera3DReader): Camera3DState {
  return toCamera3DState(engine.getCamera(), {
    target: engine.getCameraTarget(),
    projection: engine.getProjection(),
    orthoHalfWidth: engine.getOrthoHalfWidth(),
  });
}

/**
 * Puts a written-down camera back into the scene.
 *
 * The move happens first and the projection second, which matters in exactly one case and matters
 * a lot there: a scene asked to switch to an orthographic projection without being told how wide
 * to be sizes itself from where the camera is standing at that moment. Switching before the move
 * would therefore frame wherever the camera happened to have been left, not what the view being
 * restored was about. A camera written down by this application always carries its width, so this
 * only bites on a document somebody edited — which is exactly the case worth being right about.
 *
 * For the same reason a restore that changes the projection should not be animated: a flight is
 * still in the air when this returns, so the width would again be taken from the wrong place.
 */
export function applyCamera3D(
  engine: Camera3DWriter,
  state: Camera3DState,
  options?: Scene3DCameraOptions,
): void {
  engine.setCamera(toSceneCamera(state), options);
  engine.setProjection(state.projection, state.orthoHalfWidth);
}

/**
 * Reads a camera out of a stored document, or returns null when the document does not hold one.
 *
 * Everything that reaches this function came from somewhere outside the running program — a saved
 * view written by an older build, a URL a person edited by hand, a share link that has been
 * through a mail client. So every field is checked rather than trusted, and a document that fails
 * any check reads as "no camera stored" rather than as an error: a saved view whose camera block
 * is unusable should still open, showing everything else it remembers.
 */
export function normalizeCamera3DState(value: unknown): Camera3DState | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  const eye = normalizeEye(bag.eye);
  if (!eye) {
    return null;
  }
  const heading = finiteNumber(bag.heading);
  const pitch = finiteNumber(bag.pitch);
  if (heading === undefined || pitch === undefined || pitch < -90 || pitch > 90) {
    return null;
  }
  const state: Camera3DState = {
    eye,
    heading: wrapHeading(heading),
    pitch,
    roll: wrapRoll(finiteNumber(bag.roll) ?? 0),
    projection: bag.projection === 'orthographic' ? 'orthographic' : 'perspective',
  };
  const target = normalizeEye(bag.target);
  if (target) {
    state.target = target;
  }
  const halfWidth = finiteNumber(bag.orthoHalfWidth);
  if (state.projection === 'orthographic' && isPositive(halfWidth)) {
    state.orthoHalfWidth = halfWidth;
  }
  return state;
}

/** Straight-line distance in metres, on the local tangent plane — exact enough at cave scale. */
export function metersBetween(from: Camera3DEye, to: Camera3DEye): number {
  const offset = offsetBetween(from, to);
  return Math.hypot(offset.east, offset.north, offset.up);
}

/** Metres east, north and up from `from` to `to`. */
export function offsetBetween(from: Camera3DEye, to: Camera3DEye): Camera3DOffset {
  const north = (to.lat - from.lat) * METERS_PER_DEGREE_LATITUDE;
  const east =
    (to.lon - from.lon) *
    METERS_PER_DEGREE_LATITUDE *
    Math.cos(toRadians(clampLatitude((from.lat + to.lat) / 2)));
  return { east, north, up: to.height - from.height };
}

/** The point that many metres east, north and up from here. */
export function movedBy(origin: Camera3DEye, offset: Camera3DOffset): Camera3DEye {
  const latitude = origin.lat + offset.north / METERS_PER_DEGREE_LATITUDE;
  // The cos term is taken at the origin's latitude rather than at the midpoint of the move: the
  // moves this is used for are a few kilometres at most, and taking it at a latitude that depends
  // on the answer would make the function implicit for no gain anyone could measure.
  const metersPerDegreeLongitude =
    METERS_PER_DEGREE_LATITUDE * Math.cos(toRadians(clampLatitude(origin.lat)));
  const longitude =
    metersPerDegreeLongitude > 0 ? origin.lon + offset.east / metersPerDegreeLongitude : origin.lon;
  return { lon: longitude, lat: latitude, height: origin.height + offset.up };
}

/**
 * Where the eye has to stand to be looking at `target` from `distance` metres away, facing
 * `heading` and tilted `pitch` below the horizon.
 *
 * The camera is displaced from what it is looking at in the direction it is looking *away* from,
 * which is the half of this that is easy to get backwards: a camera facing north stands to the
 * south of its subject.
 */
export function eyeLookingAt(
  target: Camera3DEye,
  heading: number,
  pitch: number,
  distance: number,
): Camera3DEye {
  const pitchRadians = toRadians(pitch);
  const headingRadians = toRadians(wrapHeading(heading));
  const horizontal = distance * Math.cos(pitchRadians);
  return movedBy(target, {
    east: -horizontal * Math.sin(headingRadians),
    north: -horizontal * Math.cos(headingRadians),
    up: -distance * Math.sin(pitchRadians),
  });
}

/**
 * The ground point a camera with no reported target is nevertheless about: the point directly
 * below it, on the ellipsoid.
 *
 * Used when the middle of the screen is showing sky. Framing that as "no pivot at all" would leave
 * the preset buttons dead exactly when a viewer who has lost their bearings most wants them.
 */
export function fallbackTarget(state: Camera3DState): Camera3DEye {
  return { lon: state.eye.lon, lat: state.eye.lat, height: 0 };
}

function normalizeEye(value: unknown): Camera3DEye | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  const lon = finiteNumber(bag.lon);
  const lat = finiteNumber(bag.lat);
  const height = finiteNumber(bag.height);
  if (lon === undefined || lat === undefined || height === undefined) {
    return null;
  }
  if (lon < -180 || lon > 180 || lat < -90 || lat > 90) {
    return null;
  }
  return { lon, lat, height };
}

function finiteNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function isPositive(value: number | undefined): value is number {
  return value !== undefined && value > 0;
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
