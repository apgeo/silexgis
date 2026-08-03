// SPDX-License-Identifier: AGPL-3.0-or-later
import { clampLatitude, METERS_PER_DEGREE_LATITUDE } from './pseudoZoom.ts';
import type {
  Scene3DCutawayFootprint,
  Scene3DPolyline,
  Scene3DPosition,
} from './scene3dEngine.ts';

// The patch of ground a cutaway view cuts away, worked out from the survey it has to reveal.
//
// Two shapes were measured against each other for this and the result was not the intuitive one.
// A tight outline traced around the actual passages — the shape a contour tracer produces, a few
// hundred vertices following every lobe of the cave — shows the whole survey when the camera looks
// straight down and loses more than half of it as soon as the camera tilts, because a tilted view
// looks *through* the sides of the cut and every notch in the outline becomes a wall in the way.
// One smooth generous ellipse over the same cave keeps the whole survey visible at nadir and at a
// rim-height oblique alike, and costs nothing extra to build. So the outline here is deliberately
// not the cave's shape: it is the simplest convex ring that comfortably contains it.
//
// Nothing in this module knows about the graphics engine, which is what makes the geometry
// checkable arithmetic rather than something only a screenshot can confirm.

/**
 * Points around the ring. Enough that the rim reads as a curve rather than as a polygon at any
 * zoom a cave is looked at, and few enough that rebuilding it while panning is free.
 */
const RING_POINTS = 96;

/**
 * How much wider than the survey the ring is drawn, in half-extents of it.
 *
 * An ellipse whose axes are the survey's own half-extents touches the middle of each side of its
 * box and cuts every corner off, so the number has to be at least the root of two before the ring
 * contains the whole cave at all. This is that, with room on top so no passage ends flush against
 * the wall of the shaft it is being looked at down.
 */
const RING_GENEROSITY = 1.6;

/** Smallest half-width of the ring, so a single short cave still gets an opening to look into. */
const MINIMUM_RING_RADIUS_METERS = 150;

/** How far under the deepest passage the excavation floor sits. */
const FLOOR_CLEARANCE_METERS = 200;

/**
 * How far below the deepest thing on screen a camera may still descend, so a viewer can look up
 * at the bottom of a cave rather than being stopped exactly at it.
 */
const CAMERA_CLEARANCE_METERS = 500;

/**
 * The floor used when nothing has said how deep the data goes: two kilometres below the
 * ellipsoid, which is under the deepest cave anyone has surveyed and still far enough from the
 * centre of the earth to be recoverable by scrolling back out.
 */
export const DEFAULT_CAMERA_FLOOR_METERS = -2000;

/**
 * Shallowest and steepest angles below the horizon the cutaway hand-back is ever set to.
 *
 * The shallow end is measured: however wide and shallow the excavation, by about ten degrees above
 * the ground a viewer is looking at the near rim rather than into the opening. The steep end stops
 * a very deep, very narrow cave from demanding a perfect overhead view before it will show
 * anything — past this angle the difference between what the cutaway and the overlay reveal is not
 * worth taking the mode away for.
 */
const SHALLOWEST_CUTAWAY_PITCH_DEGREES = 10;
const STEEPEST_CUTAWAY_PITCH_DEGREES = 75;

/** The middle of the outline, which is the point the opening is looked into. */
export function footprintCenter(footprint: Scene3DCutawayFootprint): {
  longitude: number;
  latitude: number;
} {
  let longitude = 0;
  let latitude = 0;
  for (const position of footprint.ring) {
    longitude += position.longitude;
    latitude += position.latitude;
  }
  return {
    longitude: longitude / footprint.ring.length,
    latitude: latitude / footprint.ring.length,
  };
}

/**
 * Shallowest camera angle, in degrees below the horizon, from which a cutaway over this footprint
 * still shows the cave rather than the near wall of the shaft it is cut down.
 *
 * A single fixed angle cannot answer this, because how far over an opening a viewer has to be
 * depends on the shape of the opening. The excavation is a vertical shaft: a line of sight that
 * just clears the near rim descends across the opening, and it has to fall the whole depth of the
 * shaft before it reaches the far wall or it never reaches the bottom at all. Crossing a shaft of
 * radius r costs 2r of horizontal travel, so a shaft d deep needs an angle whose tangent is at
 * least d / 2r. A wide shallow cave is legible from almost anywhere; one as deep as it is broad
 * has to be looked at from most of the way overhead, and telling a viewer they are in a cutaway
 * while showing them a lid of rock is worse than quietly handing back to the overlay, which shows
 * the whole survey from every angle.
 *
 * `groundHeight` is the surface the opening is cut into, in metres above the ellipsoid.
 */
export function cutawayPitchLimitDegrees(
  footprint: Scene3DCutawayFootprint,
  groundHeight: number,
): number {
  const center = footprintCenter(footprint);
  const cosLatitude = Math.max(Math.cos((clampLatitude(center.latitude) * Math.PI) / 180), 1e-6);

  let radiusMeters = 0;
  for (const position of footprint.ring) {
    const east =
      (position.longitude - center.longitude) * METERS_PER_DEGREE_LATITUDE * cosLatitude;
    const north = (position.latitude - center.latitude) * METERS_PER_DEGREE_LATITUDE;
    radiusMeters += Math.hypot(east, north);
  }
  radiusMeters /= footprint.ring.length;

  const depthMeters = groundHeight - footprint.floorHeight;
  if (!(radiusMeters > 0) || !(depthMeters > 0)) {
    // Nothing to look into; the measured shallow limit is as good an answer as there is.
    return -SHALLOWEST_CUTAWAY_PITCH_DEGREES;
  }

  const degrees = (Math.atan(depthMeters / (2 * radiusMeters)) * 180) / Math.PI;
  return -Math.min(
    STEEPEST_CUTAWAY_PITCH_DEGREES,
    Math.max(SHALLOWEST_CUTAWAY_PITCH_DEGREES, degrees),
  );
}

/**
 * A ring over everything the given lines occupy, or undefined when they occupy nothing.
 *
 * The ellipse is built in degrees rather than in metres because that is what the ring is consumed
 * as, with the longitude axis widened by 1/cos(latitude) so the shape is a circle on the ground
 * rather than one squashed east-west at Carpathian latitudes.
 */
export function caveFootprint(
  polylines: readonly Scene3DPolyline[],
): Scene3DCutawayFootprint | undefined {
  let west = Number.POSITIVE_INFINITY;
  let east = Number.NEGATIVE_INFINITY;
  let south = Number.POSITIVE_INFINITY;
  let north = Number.NEGATIVE_INFINITY;
  let lowest = Number.POSITIVE_INFINITY;

  for (const line of polylines) {
    for (const position of line.positions) {
      west = Math.min(west, position.longitude);
      east = Math.max(east, position.longitude);
      south = Math.min(south, position.latitude);
      north = Math.max(north, position.latitude);
      lowest = Math.min(lowest, position.height);
    }
  }

  if (!Number.isFinite(west) || !Number.isFinite(south) || !Number.isFinite(lowest)) {
    return undefined;
  }

  const centerLongitude = (west + east) / 2;
  const centerLatitude = (south + north) / 2;
  const cosLatitude = Math.max(Math.cos((clampLatitude(centerLatitude) * Math.PI) / 180), 1e-6);

  const minimumLatitudeRadius = MINIMUM_RING_RADIUS_METERS / METERS_PER_DEGREE_LATITUDE;
  const minimumLongitudeRadius = minimumLatitudeRadius / cosLatitude;

  const longitudeRadius = Math.max(
    ((east - west) / 2) * RING_GENEROSITY,
    minimumLongitudeRadius,
  );
  const latitudeRadius = Math.max(((north - south) / 2) * RING_GENEROSITY, minimumLatitudeRadius);

  const ring: Scene3DPosition[] = [];
  for (let index = 0; index < RING_POINTS; index += 1) {
    const angle = (2 * Math.PI * index) / RING_POINTS;
    ring.push({
      longitude: centerLongitude + longitudeRadius * Math.cos(angle),
      latitude: centerLatitude + latitudeRadius * Math.sin(angle),
      // The cut is a vertical shaft and ignores these; they are zero so the ring is also a usable
      // ground outline for anything else that ever wants one.
      height: 0,
    });
  }

  return { ring, floorHeight: lowest - FLOOR_CLEARANCE_METERS };
}

/**
 * How deep the camera may go with this footprint on screen: under the excavation floor, or the
 * standing default when there is nothing to look into.
 */
export function cameraFloorFor(footprint: Scene3DCutawayFootprint | undefined): number {
  if (!footprint) {
    return DEFAULT_CAMERA_FLOOR_METERS;
  }
  return Math.min(DEFAULT_CAMERA_FLOOR_METERS, footprint.floorHeight - CAMERA_CLEARANCE_METERS);
}
