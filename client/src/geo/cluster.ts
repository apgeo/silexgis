// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The lattice cell a low-zoom entrance cluster was summed over, as the server names it:
 * west, south, east, north in degrees. The lattice is global and the same for every caller, so
 * these bounds disclose nothing; the client never derives them itself, because a copy of the
 * server's lattice arithmetic is a copy that can drift from it.
 */
export type ClusterCell = readonly [west: number, south: number, east: number, north: number];

/** The cell a cluster feature's properties carry, or undefined when it is malformed. */
export function clusterCellOf(value: unknown): ClusterCell | undefined {
  if (!Array.isArray(value) || value.length !== 4) {
    return undefined;
  }
  const numbers = value.map(Number);
  const [west, south, east, north] = numbers;
  if (!numbers.every(Number.isFinite) || !(west < east) || !(south < north)) {
    return undefined;
  }
  return [west, south, east, north];
}

/**
 * The bbox query value for a cluster's members. Membership on the server is decided on the
 * position each caller is shown — the snapped point for an entrance they may not place exactly —
 * which is also what decided the cell, so the cell itself is the whole query: no margin is needed
 * for protected entrances, and none would be correct.
 */
export function clusterCellBbox(cell: ClusterCell): string {
  // Written at full precision: rounding an edge inward could drop a member sitting on it. A cell
  // on the antimeridian or at a pole reaches past the globe, which the server refuses as a window;
  // nothing is stored out there, so the edge is cut to the globe's.
  const [west, south, east, north] = cell;
  return [Math.max(-180, west), Math.max(-90, south), Math.min(180, east), Math.min(90, north)]
    .map((n) => String(n))
    .join(',');
}
