// SPDX-License-Identifier: AGPL-3.0-or-later

/** A build's rectangle, in degrees, in the order the submit request takes them. */
export type TerrainBbox = [west: number, south: number, east: number, north: number];

/**
 * The largest rectangle this application builds in one go, in square degrees.
 *
 * Restated here because the server does not publish it: it refuses a larger one with a stable
 * code and a sentence, and a form that only learned the limit from that refusal would let somebody
 * draw a continent, wait for the round trip and then be told. The number admits a whole country
 * and refuses a continent; if the server's own limit ever moves, this moves with it and the
 * refusal remains the thing that actually decides.
 */
export const MAX_AREA_SQUARE_DEGREES = 25;

/** Degrees of longitude times degrees of latitude — the same measure the server refuses on. */
export function areaSquareDegrees([west, south, east, north]: TerrainBbox): number {
  return Math.abs(east - west) * Math.abs(north - south);
}

/** Whether the rectangle is one the server will accept at all, before it is asked. */
export function areaTooLarge(bbox: TerrainBbox): boolean {
  return areaSquareDegrees(bbox) > MAX_AREA_SQUARE_DEGREES;
}

/** West, south, east, north, at the precision the picker rounds to. */
export function formatBbox([west, south, east, north]: TerrainBbox): string {
  return [west, south, east, north].map((n) => n.toFixed(4)).join(', ');
}
