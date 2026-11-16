// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where things go on the drawn stereonet. The server places every pole and counts every density
 * node on a net of radius one (east positive, north positive); this module only scales that to the
 * drawing, whose y axis points down, and groups the density into the bands that are painted.
 */

export const NET_VIEW = 240;
export const NET_CENTRE = NET_VIEW / 2;
export const NET_RADIUS = 100;

export interface NetPoint {
  x: number;
  y: number;
}

/** A point of the unit net in drawing coordinates. North is up, so the y axis is turned over. */
export function netPoint(x: number, y: number): NetPoint {
  return { x: NET_CENTRE + x * NET_RADIUS, y: NET_CENTRE - y * NET_RADIUS };
}

/**
 * The contour band a density value falls in: zero below the first level, then one per interval
 * above it. With Kamb's density in standard deviations and an interval of two, band 1 is 2–4σ,
 * band 2 is 4–6σ, and so on.
 */
export function densityBand(value: number, interval: number): number {
  if (!(interval > 0) || !Number.isFinite(value) || value < interval) return 0;
  return Math.floor(value / interval);
}

/** The contour levels a density reaches, from the first interval up to its maximum. */
export function contourLevels(max: number, interval: number): number[] {
  if (!(interval > 0) || !Number.isFinite(max)) return [];
  const levels: number[] = [];
  for (let level = interval; level <= max + 1e-9; level += interval) levels.push(level);
  return levels;
}

export interface DensityCell {
  x: number;
  y: number;
  size: number;
  band: number;
}

/**
 * One square per density node in band one or above, centred on the node and as wide as the node
 * spacing, so the squares tile the net without gaps. The caller clips them to the net's circle.
 */
export function densityCells(
  density: { size: number; values: (number | null)[]; contourIntervalSigma: number } | null | undefined,
): DensityCell[] {
  if (!density || density.size < 2) return [];
  const step = 2 / (density.size - 1);
  const size = step * NET_RADIUS;
  const cells: DensityCell[] = [];
  for (let row = 0; row < density.size; row++) {
    for (let column = 0; column < density.size; column++) {
      const value = density.values[row * density.size + column];
      if (value === null || value === undefined) continue;
      const band = densityBand(value, density.contourIntervalSigma);
      if (band === 0) continue;
      const centre = netPoint(-1 + column * step, 1 - row * step);
      cells.push({ x: centre.x - size / 2, y: centre.y - size / 2, size, band });
    }
  }
  return cells;
}
