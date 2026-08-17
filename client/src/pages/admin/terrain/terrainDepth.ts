// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How deep a pyramid may be asked to go, and what each depth means on the ground.
 *
 * The numbers are restated here rather than read from the server, which publishes neither. They
 * are the same bar the server refuses on, and the refusal remains what actually decides: this is
 * here so somebody choosing a depth is told what they are asking for before they ask, instead of
 * after a round trip.
 *
 * The relationship is measured, not arithmetic. Thirty-metre elevation data runs out of detail
 * around level 13, which is the working default; half-metre survey data earns 16 or 17. Past 18
 * the tile count multiplies with nothing in any input to fill the extra levels, and below 6 a
 * single tile covers more ground than any elevation model is asked to describe.
 */
export const MIN_DEPTH = 6;
export const MAX_DEPTH = 18;
export const DEFAULT_DEPTH = 13;

/**
 * The deepest level the coverage this application obtains for itself can honestly carry.
 *
 * Asking for more is not refused and does not fail: the tile maker works out how deep it may go
 * from each raster's own pixel size, so it simply stops advertising levels nothing finer exists
 * for, and says so nowhere. That silence is why a build asking past this with no finer source of
 * its own is warned before it is started.
 */
export const OBTAINED_COVERAGE_DEPTH = 13;

/** Which sentence describes what a depth is asking for. */
export type TerrainDepthBand = 'coarse' | 'coverage' | 'fine' | 'survey';

export function depthBand(depth: number): TerrainDepthBand {
  if (depth <= 11) {
    return 'coarse';
  }
  if (depth <= OBTAINED_COVERAGE_DEPTH) {
    return 'coverage';
  }
  if (depth <= 15) {
    return 'fine';
  }
  return 'survey';
}

/**
 * Whether this depth asks for more than the sources chosen can give.
 *
 * True only when nothing finer than the obtained coverage is on the build: a raster somebody
 * uploaded or a directory they named may be any resolution at all, and guessing at it from here
 * would produce a warning that is wrong as often as it is right.
 */
export function depthExceedsCoverage(depth: number, hasOwnRasters: boolean): boolean {
  return !hasOwnRasters && depth > OBTAINED_COVERAGE_DEPTH;
}
