// SPDX-License-Identifier: AGPL-3.0-or-later

/** Formats WGS84 coordinates for display: "45.53127°N 25.44721°E". */
export function formatLonLat(lon: number, lat: number, decimals = 5): string {
  const ns = lat >= 0 ? 'N' : 'S';
  const ew = lon >= 0 ? 'E' : 'W';
  return `${Math.abs(lat).toFixed(decimals)}°${ns} ${Math.abs(lon).toFixed(decimals)}°${ew}`;
}

/** Clamps a longitude/latitude pair to valid WGS84 ranges. */
export function clampLonLat(lon: number, lat: number): [number, number] {
  return [Math.min(180, Math.max(-180, lon)), Math.min(90, Math.max(-90, lat))];
}
