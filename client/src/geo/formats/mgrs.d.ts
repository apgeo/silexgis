// SPDX-License-Identifier: AGPL-3.0-or-later

// The package ships no types. Only what this client calls is declared. Positions are
// [longitude, latitude], and a failed conversion throws a plain string rather than an Error.
declare module 'mgrs' {
  /** A position as an MGRS reference, `accuracy` digits per axis (5 = 1 m … 1 = 10 km). */
  export function forward(lonLat: [number, number], accuracy?: number): string;
  /** The square a reference names, as [west, south, east, north]. */
  export function inverse(reference: string): [number, number, number, number];
  /** The centre of the square a reference names, as [longitude, latitude]. */
  export function toPoint(reference: string): [number, number];
}
