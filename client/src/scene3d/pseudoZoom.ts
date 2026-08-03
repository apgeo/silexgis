// SPDX-License-Identifier: AGPL-3.0-or-later

// A 3D camera has no zoom level, but every map endpoint the scene needs is keyed by one: the
// entrance layer clusters below an integer zoom, and the centerline overlay switches between the
// stored skeleton and full survey detail at another. This module derives the missing number from
// how far the camera is from the ground, so a 3D view and the 2D map ask the server for the same
// representation whenever they are showing the same amount of ground.
//
// Deliberately dependency-free — no map library, no 3D engine, no framework. Everything a camera
// contributes (its height above ground, its vertical field of view, the pixel height of the
// surface it draws into) is passed in as a plain number, which is also what makes it testable.
//
// The maths is the Web Mercator tile pyramid the 2D map already stands on: 256-pixel tiles over
// an equatorial circumference of 2·pi·6378137 m, halving with every zoom step. The server's
// centerline simplification uses the same pyramid expressed in degrees of longitude,
// 360/(256·2^zoom), which carries no latitude term because a Mercator pixel spans a constant
// longitude everywhere. Ground distance is not constant, though: a pixel covers cos(latitude)
// times as many metres on the ground as it does at the equator, and it is ground metres a camera
// height produces. Both conventions are therefore present below and must not be confused — the
// cos term belongs on ground-sample-distance and nowhere else. That the pair is consistent with
// the 2D map is checkable against the number the detail-zoom default was chosen from: zoom 18 at
// Romanian latitudes is ~0.42 ground metres per pixel, which is the "roughly 0.4 m/px, below
// which survey splays start to carry information" the server's default was set at.

/** Ground metres per pixel at zoom 0 on the equator: 2·pi·6378137 / 256. */
export const EQUATORIAL_METERS_PER_PIXEL_AT_ZOOM_0 = 156543.03392804097;

/** Highest zoom the map endpoints accept; they clamp to 0..24, so producing more is pointless. */
export const MAX_MAP_ZOOM = 24;

/**
 * Where the tile pyramid's square world stops. Mercator is undefined at the poles, and every
 * latitude-dependent figure derived from it has to be clamped here or it runs away to infinity.
 */
export const MERCATOR_MAX_LATITUDE = 85.0511287798066;

/** Ground metres in one degree of latitude on the sphere the tile pyramid is defined over. */
export const METERS_PER_DEGREE_LATITUDE = (EQUATORIAL_METERS_PER_PIXEL_AT_ZOOM_0 * 256) / 360;

export interface CameraView {
  /** Camera height above the ground it is looking at, in metres. Must be > 0. */
  heightMeters: number;
  /** Latitude of the point being looked at, in degrees — the cos term of the projection. */
  latitudeDegrees: number;
  /** Vertical field of view in radians (a perspective frustum's `fovy`). */
  fieldOfViewRadians: number;
  /** Height of the drawing surface in CSS pixels. */
  viewportHeightPixels: number;
}

/**
 * Ground metres covered by one pixel at a given zoom and latitude. This is the quantity the
 * server's rendering thresholds were chosen against, so it is the currency the two sides share.
 */
export function groundSampleDistance(zoom: number, latitudeDegrees: number): number {
  return (
    (EQUATORIAL_METERS_PER_PIXEL_AT_ZOOM_0 * Math.cos(toRadians(clampLatitude(latitudeDegrees)))) /
    Math.pow(2, zoom)
  );
}

/**
 * The zoom at which a 2D map would show this many ground metres per pixel. Fractional on
 * purpose: rounding is the caller's decision, and rounding early would lose the difference
 * between a camera that has only just crossed a threshold and one well past it.
 */
export function zoomForGroundSampleDistance(metersPerPixel: number, latitudeDegrees: number): number {
  if (!(metersPerPixel > 0)) return Number.NaN;
  return Math.log2(
    (EQUATORIAL_METERS_PER_PIXEL_AT_ZOOM_0 * Math.cos(toRadians(clampLatitude(latitudeDegrees)))) /
      metersPerPixel,
  );
}

/**
 * Ground metres per pixel for a camera looking straight down: the frustum spans
 * 2·h·tan(fov/2) metres vertically, shared out over the viewport's pixel height. Off-nadir the
 * figure varies across the screen — nearer ground at the bottom, further at the top — and this
 * returns the value at the point the camera is aimed at, which is the one that should decide
 * what to request.
 */
export function cameraGroundSampleDistance(view: CameraView): number {
  const { heightMeters, fieldOfViewRadians, viewportHeightPixels } = view;
  if (!(heightMeters > 0) || !(fieldOfViewRadians > 0) || !(viewportHeightPixels > 0)) {
    return Number.NaN;
  }
  return (2 * heightMeters * Math.tan(fieldOfViewRadians / 2)) / viewportHeightPixels;
}

/** The fractional map zoom showing the same amount of ground as this camera. */
export function pseudoZoom(view: CameraView): number {
  return zoomForGroundSampleDistance(cameraGroundSampleDistance(view), view.latitudeDegrees);
}

/**
 * The inverse: how high the camera has to sit for the scene to match a given map zoom. Used to
 * enter 3D at the zoom the 2D map was left at, so the handover does not jump.
 */
export function cameraHeightForZoom(
  zoom: number,
  view: Omit<CameraView, 'heightMeters'>,
): number {
  const { latitudeDegrees, fieldOfViewRadians, viewportHeightPixels } = view;
  if (!(fieldOfViewRadians > 0) || !(viewportHeightPixels > 0)) return Number.NaN;
  const metersPerPixel = groundSampleDistance(zoom, latitudeDegrees);
  return (metersPerPixel * viewportHeightPixels) / (2 * Math.tan(fieldOfViewRadians / 2));
}

/**
 * The integer the map endpoints want. Rounds rather than floors, matching what the 2D layers do
 * with the OpenLayers view's zoom, and clamps to the range those endpoints accept — a camera on
 * the ground would otherwise produce a zoom no server threshold is defined for.
 */
export function mapZoomFor(pseudoZoomValue: number): number {
  if (!Number.isFinite(pseudoZoomValue)) return 0;
  return Math.min(MAX_MAP_ZOOM, Math.max(0, Math.round(pseudoZoomValue)));
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}

// cos() would drive the ground sample distance to zero (and the zoom to infinity) as the latitude
// approached a pole. Clamping to the pyramid's own extent keeps every derived figure finite
// without changing any answer inside the area a map covers.
export function clampLatitude(latitudeDegrees: number): number {
  return Math.min(MERCATOR_MAX_LATITUDE, Math.max(-MERCATOR_MAX_LATITUDE, latitudeDegrees));
}
