// SPDX-License-Identifier: AGPL-3.0-or-later

// Mirrors the server's cluster grid (~64 px cells on a 256 px tile pyramid):
// cell = 360° / 2^zoom / 4. Keep in sync with the map endpoints' aggregation.
export function clusterCellDegrees(zoom: number): number {
  return 360 / Math.pow(2, zoom) / 4;
}

/**
 * The bbox (west,south,east,north) covering a cluster's members: one full cell
 * of margin on each side of the centroid. The margin absorbs protected caves
 * whose grid-snapped coordinates fall just outside the exact cell, at the cost
 * of occasionally listing a close neighbor too.
 */
export function clusterCellBbox(lon: number, lat: number, zoom: number): string {
  const cell = clusterCellDegrees(zoom);
  return [lon - cell, lat - cell, lon + cell, lat + cell].map((n) => n.toFixed(6)).join(',');
}
