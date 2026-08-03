// SPDX-License-Identifier: AGPL-3.0-or-later
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

/** Guards the division at the poles; the clamp above already keeps this from ever biting a map. */
const MIN_COS_LATITUDE = 1e-6;

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
