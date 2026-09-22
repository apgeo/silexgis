// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The one place stored fractions meet OpenLayers coordinates.
 *
 * Stored pin payloads are fractions of the drawn picture with the origin at its top-left —
 * the frame `imagelink/regions.ts` owns, shared with every other image region in the
 * system. OpenLayers draws over an extent whose y axis points *up*, so somewhere the y has
 * to flip, and that somewhere is exactly here: everything above this file speaks fractions
 * in the stored frame, everything below it speaks map coordinates, and no second flip may
 * exist anywhere — two flips cancel into markers that look right on a square image and sit
 * mirrored on any other.
 */

/** The picture's natural size in pixels, which is the map's whole coordinate space. */
export interface ImageSize {
  width: number;
  height: number;
}

/** The OL extent the image is drawn into: origin to natural size, y up. */
export function imageExtent(size: ImageSize): [number, number, number, number] {
  return [0, 0, size.width, size.height];
}

/**
 * A stored fraction pair as an OL coordinate on the drawn image — scaled to pixels, with
 * the y axis flipped from the stored top-left origin to the extent's bottom-left one.
 */
export function toMapCoordinate(
  fraction: { x: number; y: number },
  size: ImageSize,
): [number, number] {
  return [fraction.x * size.width, size.height - fraction.y * size.height];
}
