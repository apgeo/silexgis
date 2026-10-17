// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The location-protection grid the server snaps a protected position to unless its operator set
 * another — 5 km. The server does not say which grid it uses alongside each position, so this is
 * what a position marked approximate is written for.
 */
export const DEFAULT_PROTECTION_GRID_METRES = 5000;

/** Metres in a degree of latitude, as the server reckons its grid. */
export const METRES_PER_DEGREE = 111_320;

/**
 * The decimal-degree places a protected position is written to: the places whose last step is the
 * largest not coarser than the grid (two, about a kilometre, for 5 km). Every format's protected
 * form is measured against this step, so that no format writes a snapped point finer than decimal
 * degrees would.
 */
export function protectedDecimalPlaces(gridMetres: number = DEFAULT_PROTECTION_GRID_METRES): number {
  return Math.min(5, Math.max(0, Math.ceil(Math.log10(METRES_PER_DEGREE / gridMetres))));
}

/** The largest power of ten in metres not above the grid — the kilometre, for 5 km. */
export function protectedGridStepMetres(gridMetres: number = DEFAULT_PROTECTION_GRID_METRES): number {
  return 10 ** Math.floor(Math.log10(gridMetres));
}
