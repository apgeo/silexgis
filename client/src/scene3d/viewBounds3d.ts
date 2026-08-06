// SPDX-License-Identifier: AGPL-3.0-or-later
import { eyeLookingAt, type Camera3DEye, type Camera3DState } from './camera3d.ts';
import { clampLatitude, METERS_PER_DEGREE_LATITUDE } from './pseudoZoom.ts';
import type { Scene3DBounds } from './scene3dEngine.ts';

// Every map endpoint the scene needs is asked for a longitude/latitude box. A 2D map has one for
// free — its view is a rectangle over the ground — while a 3D camera looks at the world from an
// angle and technically sees ground all the way to the horizon. This module turns a camera into
// the box a loader should ask for, and it is deliberately dependency-free so the policy can be
// read and tested without a graphics context.
//
// The policy, and why it is this one:
//
//   * The box is centred on the ground the middle of the screen is showing, not on the point
//     under the camera. Those differ the moment the view is tilted, and it is what the viewer is
//     looking at that should decide what gets loaded.
//   * Its half-span is half the screen *diagonal* in ground metres, on both axes. Half the width
//     and half the height would be the exact answer for a north-up camera looking straight down;
//     the diagonal is the smallest figure that still covers the screen when the camera is turned
//     to any compass bearing, which it can be at any moment and without a reload.
//   * Nothing enlarges it further for tilt. A steeply tilted camera does show ground beyond this
//     box, near the horizon, and that ground is not loaded until the viewer moves toward it. The
//     alternative — asking for everything out to the limb — would request a whole country's
//     survey geometry to draw a few pixels of it, and the server's own budget would then withhold
//     the cave actually being looked at.

/** What the camera contributes; all of it plain numbers, none of it engine types. */
export interface GroundView {
  /** Ground point the middle of the screen is showing, in degrees. */
  centerLongitude: number;
  centerLatitude: number;
  /** Ground metres one pixel covers at that point. */
  metersPerPixel: number;
  viewportWidthPixels: number;
  viewportHeightPixels: number;
}

/**
 * The longitude/latitude box to request for this view, or undefined when the numbers describing
 * the camera cannot produce one (an unlaid-out canvas, a camera aimed at the sky).
 */
export function viewportBounds(view: GroundView): Scene3DBounds | undefined {
  const { centerLongitude, centerLatitude, metersPerPixel } = view;
  const width = view.viewportWidthPixels;
  const height = view.viewportHeightPixels;
  if (
    !Number.isFinite(centerLongitude) ||
    !Number.isFinite(centerLatitude) ||
    !(metersPerPixel > 0) ||
    !(width > 0) ||
    !(height > 0)
  ) {
    return undefined;
  }

  const halfSpanMeters = (Math.hypot(width, height) / 2) * metersPerPixel;
  const halfLatitudeDegrees = halfSpanMeters / METERS_PER_DEGREE_LATITUDE;
  // A degree of longitude shrinks with the cosine of the latitude, so the same ground distance is
  // more degrees the further from the equator the view sits.
  const cosLatitude = Math.cos(toRadians(clampLatitude(centerLatitude)));
  const halfLongitudeDegrees = halfLatitudeDegrees / Math.max(cosLatitude, MIN_COS_LATITUDE);

  const south = Math.max(-90, centerLatitude - halfLatitudeDegrees);
  const north = Math.min(90, centerLatitude + halfLatitudeDegrees);
  // No antimeridian handling, matching the 2D map: a box that runs off one edge is clamped rather
  // than split, because every endpoint on the other side takes a single west/south/east/north.
  const west = Math.max(-180, centerLongitude - halfLongitudeDegrees);
  const east = Math.min(180, centerLongitude + halfLongitudeDegrees);
  return [west, south, east, north];
}

/**
 * The bbox string the map endpoints take. Five decimals is what the 2D loaders send — about a
 * metre on the ground, far finer than any box that decides which tile of data to fetch — and
 * matching it keeps the two views hitting the same server-side response cache entries.
 */
export function boundsToBbox(bounds: Scene3DBounds): string {
  return bounds.map((n) => n.toFixed(5)).join(',');
}

/** The middle of a box, as a ground point at ellipsoid height. */
export function boundsCenter(bounds: Scene3DBounds): Camera3DEye {
  const [west, south, east, north] = bounds;
  return { lon: (west + east) / 2, lat: (south + north) / 2, height: 0 };
}

/**
 * The shallowest a camera is allowed to be tilted when it is asked to frame a box, in degrees.
 *
 * A camera looking near the horizon is an arbitrary distance from what it is aiming at — the
 * distance goes to infinity as the tilt goes to zero — so "stand back far enough to see this box"
 * has no answer down there. Framing from a shallow angle at all is unusual; framing from one at a
 * plausible distance is what a viewer wants, and this is what makes it computable.
 */
const MINIMUM_FRAMING_PITCH_DEGREES = 5;

/**
 * The camera that shows `bounds` at map zoom `zoom`, *without changing which way the camera is
 * pointing*.
 *
 * This is how the 3D view follows the flat map. The obvious alternative — hand the box to the
 * engine's own "frame this rectangle" — reorients the camera to look straight down at it, so
 * panning the flat map would repeatedly flatten a view the viewer had deliberately tilted. Only
 * one number here needs the engine at all: how high a camera has to be to show a given map zoom,
 * which depends on the frustum and on the size of the drawing surface. It arrives as a function so
 * everything else stays testable without one.
 */
export function cameraFramingBounds(
  bounds: Scene3DBounds,
  zoom: number,
  current: Camera3DState,
  heightForZoom: (zoom: number, latitude: number) => number,
): Camera3DState | undefined {
  const target = boundsCenter(bounds);
  const height = heightForZoom(zoom, target.lat);
  if (!Number.isFinite(height) || height <= 0) {
    return undefined;
  }
  // The camera's height above what it is looking at is what a zoom describes, and it is the
  // vertical leg of the triangle whose hypotenuse is the distance to that point.
  const pitch = Math.min(current.pitch, -MINIMUM_FRAMING_PITCH_DEGREES);
  const distance = height / Math.sin((-pitch * Math.PI) / 180);
  return {
    ...current,
    eye: eyeLookingAt(target, current.heading, pitch, distance),
    pitch,
    target,
  };
}

/** Guards the division at the poles; the clamp above already keeps this from ever biting a map. */
const MIN_COS_LATITUDE = 1e-6;

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
